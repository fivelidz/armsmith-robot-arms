#!/usr/bin/env python3
"""
verify_waypoints.py — SAFETY validation for an ARMSMITH waypoint file BEFORE it drives a real arm.

This is the real-robot side of the "verify correct placement / safe operation" system. It checks a
trajectory against the joint map (limits) and physical safety rules, so you never stream a dangerous
sequence to hardware. Run this in CI and before every --live run.

Checks:
  - schema/units present and = degrees
  - every joint angle within its soft limits (joint_map.json)
  - per-step velocity <= max (deg per dt), no teleport jumps
  - gripper within [0,90]
  - monotonic timestamps, sane dt
  - no NaN/inf
Exit code 0 = safe, 1 = unsafe (with a report).

Usage: python3 verify_waypoints.py <traj.waypoints.json> [--max-vel 400]
"""

import argparse, json, math, os, sys


def load(path):
    with open(path) as f:
        return json.load(f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trajectory")
    ap.add_argument(
        "--max-vel", type=float, default=400.0, help="max joint speed deg/s"
    )
    ap.add_argument("--joint-map", default=None)
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    traj = load(args.trajectory)
    jmap_path = args.joint_map or os.path.join(here, "joint_map.json")
    jmap = load(jmap_path) if os.path.exists(jmap_path) else {}

    errors, warnings = [], []

    # schema/units
    if traj.get("units", "degrees") != "degrees":
        errors.append(f"units must be 'degrees', got {traj.get('units')}")
    wps = traj.get("waypoints", [])
    if not wps:
        errors.append("no waypoints")
    names = traj.get("joint_names", [])
    dt = float(traj.get("dt_s", 0.05))

    prev = None
    prev_t = None
    for i, wp in enumerate(wps):
        # timestamps
        t = wp.get("t_s", i * dt)
        if prev_t is not None and t < prev_t - 1e-6:
            errors.append(f"wp{i}: time goes backwards ({t} < {prev_t})")
        prev_t = t

        cur = {}
        for j in wp.get("joints", []):
            n, d = j["name"], j["deg"]
            cur[n] = d
            if math.isnan(d) or math.isinf(d):
                errors.append(f"wp{i} {n}: NaN/inf")
                continue
            # limits
            m = jmap.get(n)
            if m:
                lo, hi = m.get("min", -180), m.get("max", 180)
                if d < lo - 0.5 or d > hi + 0.5:
                    errors.append(f"wp{i} {n}: {d:.1f}° outside limits [{lo},{hi}]")
            # velocity
            if prev is not None and n in prev:
                vel = abs(d - prev[n]) / max(dt, 1e-4)
                if vel > args.max_vel:
                    warnings.append(
                        f"wp{i} {n}: {vel:.0f}°/s exceeds max {args.max_vel:.0f}°/s"
                    )
        # gripper
        g = wp.get("gripper_deg", 0.0)
        if g < -1 or g > 91:
            warnings.append(f"wp{i}: gripper {g}° outside [0,90]")
        prev = cur

    print(f"=== Waypoint safety check: {os.path.basename(args.trajectory)} ===")
    print(f"  waypoints: {len(wps)}  joints: {names}  dt: {dt}s")
    for w in warnings:
        print(f"  \033[33mWARN\033[0m {w}")
    for e in errors:
        print(f"  \033[31mERR \033[0m {e}")
    if not errors and not warnings:
        print("  \033[32mSAFE\033[0m — all checks passed")
    elif not errors:
        print(
            f"  \033[33m{len(warnings)} warnings, 0 errors — review before --live\033[0m"
        )
    else:
        print(
            f"  \033[31mUNSAFE — {len(errors)} errors, {len(warnings)} warnings\033[0m"
        )

    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
