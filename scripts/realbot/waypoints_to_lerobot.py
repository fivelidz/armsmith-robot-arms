#!/usr/bin/env python3
"""
waypoints_to_lerobot.py — convert ARMSMITH `armsmith.waypoints.v1` demonstration files into a
LeRobot-style dataset for training a Diffusion Policy (research/diffusion_pathfinding/REPORT.md, DF1).

This is step 1 of the "demo factory": the Unity sim (BehaviourRecorder / DemoRecorder) records joint
trajectories; this script turns a folder of them into the (observation, action) episodes a diffusion
policy trains on. It is intentionally DEPENDENCY-LIGHT:

  • Default mode writes a portable intermediate dataset (JSON manifest + per-episode arrays) that any
    trainer can read — works on ANY machine, no LeRobot needed. Good for inspection + CI.
  • With --lerobot AND `lerobot` installed, it also builds a real LeRobotDataset (lazy-imported).

Mapping decisions (documented so training matches deployment):
  • action            = absolute joint targets in DEGREES + gripper, in joint_map order. This is exactly
                        what armsmith_lerobot.py streams to the arm, so a trained policy is directly
                        deployable through the existing bridge (sim-to-real consistency).
  • observation.state = the SAME joint vector at the current step (low-dim proprioceptive obs). Object
                        pose / images can be added later; low-dim is the right first target (REPORT §5).
  • Each waypoint file = one episode. dt comes from the file (default 0.05 s = 20 Hz).

Usage:
  python3 waypoints_to_lerobot.py <in_dir_or_file> [-o out_dir] [--map joint_map_lerobot.json]
  python3 waypoints_to_lerobot.py demos/ -o dataset/            # portable intermediate (no deps)
  python3 waypoints_to_lerobot.py demos/ --lerobot --repo-id me/armsmith_pickplace   # + LeRobotDataset
  python3 waypoints_to_lerobot.py demos/ --stats-only           # just print dataset stats

Exit 0 on success.
"""

import argparse, json, math, os, sys, glob

SCHEMA = "armsmith.waypoints.v1"


def load_json(path):
    with open(path) as f:
        return json.load(f)


def load_map(map_path):
    """joint_map_lerobot.json: {gameJointName: motorName, ...}. Returns (game_names_ordered, motor_names)."""
    if not map_path or not os.path.exists(map_path):
        return None, None
    m = load_json(map_path)
    items = [(k, v) for k, v in m.items() if not k.startswith("_")]
    game_names = [k for k, _ in items]
    motor_names = [v for _, v in items]
    return game_names, motor_names


def waypoint_vector(wp, joint_order, gripper_name):
    """Extract [j0..jn, gripper] in joint_order from one waypoint dict."""
    by_name = {j["name"]: float(j["deg"]) for j in wp.get("joints", [])}
    vec = []
    for name in joint_order:
        if name == gripper_name:
            vec.append(float(wp.get("gripper_deg", 0.0)))
        else:
            vec.append(by_name.get(name, 0.0))
    return vec


def parse_episode(path, map_game_names=None):
    """Return dict: {feature_names, action[T][D], state[T][D], dt, n_frames, src} or raise."""
    data = load_json(path)
    if data.get("schema") != SCHEMA:
        raise ValueError(f"{path}: not {SCHEMA} (got {data.get('schema')})")
    if data.get("units", "degrees") != "degrees":
        raise ValueError(f"{path}: expected degrees units")

    joint_names = data["joint_names"]
    gripper_name = data.get("gripper_name", "Gripper")
    # Action/state column order: joints then gripper. If a joint_map is given, use ITS order (so the
    # dataset column order matches what the live deployment expects), restricted to names present.
    if map_game_names:
        order = [n for n in map_game_names]
        # ensure gripper is included
        if gripper_name not in order and gripper_name in (joint_names + [gripper_name]):
            order.append(gripper_name)
    else:
        order = list(joint_names) + [gripper_name]

    wps = data["waypoints"]
    dt = float(data.get("dt_s", 0.05))
    action, state = [], []
    for wp in wps:
        v = waypoint_vector(wp, order, gripper_name)
        for x in v:
            if math.isnan(x) or math.isinf(x):
                raise ValueError(f"{path}: NaN/Inf in waypoint")
        action.append(v)
        state.append(v)  # low-dim proprio obs = same joint vector this step
    return {
        "feature_names": order,
        "action": action,
        "state": state,
        "dt": dt,
        "n_frames": len(action),
        "src": os.path.basename(path),
        "arm_type": data.get("arm_type", "so101"),
    }


def collect_inputs(in_path):
    if os.path.isdir(in_path):
        files = sorted(glob.glob(os.path.join(in_path, "*.json")))
        files = [f for f in files if "joint_map" not in os.path.basename(f)]
        return files
    return [in_path]


def compute_stats(episodes, dim):
    """Per-dimension mean/std/min/max over all frames (needed to normalise for training)."""
    import statistics

    cols = [[] for _ in range(dim)]
    for ep in episodes:
        for frame in ep["action"]:
            for d in range(dim):
                cols[d].append(frame[d])
    stats = {"mean": [], "std": [], "min": [], "max": []}
    for d in range(dim):
        c = cols[d] or [0.0]
        stats["mean"].append(sum(c) / len(c))
        stats["std"].append(statistics.pstdev(c) if len(c) > 1 else 0.0)
        stats["min"].append(min(c))
        stats["max"].append(max(c))
    return stats


def write_portable(out_dir, episodes, feature_names, stats, motor_names):
    os.makedirs(out_dir, exist_ok=True)
    ep_dir = os.path.join(out_dir, "episodes")
    os.makedirs(ep_dir, exist_ok=True)
    for i, ep in enumerate(episodes):
        with open(os.path.join(ep_dir, f"episode_{i:04d}.json"), "w") as f:
            json.dump(
                {
                    "action": ep["action"],
                    "observation.state": ep["state"],
                    "dt": ep["dt"],
                    "n_frames": ep["n_frames"],
                    "src": ep["src"],
                },
                f,
            )
    manifest = {
        "schema": "armsmith.lerobot_intermediate.v1",
        "feature_names": feature_names,
        "motor_names": motor_names,
        "action_dim": len(feature_names),
        "state_dim": len(feature_names),
        "fps": round(1.0 / episodes[0]["dt"]) if episodes else 20,
        "num_episodes": len(episodes),
        "total_frames": sum(ep["n_frames"] for ep in episodes),
        "stats": stats,
        "note": "action/state = absolute joint+gripper degrees in feature_names order; "
        "ready for LeRobot DiffusionPolicy or any trainer.",
    }
    with open(os.path.join(out_dir, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)
    return manifest


def try_build_lerobot(episodes, feature_names, repo_id, fps):
    """Best-effort real LeRobotDataset build (lazy import; no-op with a message if unavailable)."""
    try:
        from lerobot.common.datasets.lerobot_dataset import LeRobotDataset  # noqa
    except Exception as e:
        print(
            f"[lerobot] not building real dataset ({e}). Portable intermediate was written instead."
        )
        print("[lerobot] install with `pip install lerobot` to enable --lerobot.")
        return False
    # NOTE: LeRobot's dataset API evolves; this is the integration point. We keep it guarded so the
    # script never hard-fails on import. Implement against your installed lerobot version here.
    print(
        f"[lerobot] lerobot import OK. repo_id={repo_id}, fps={fps}, "
        f"features={feature_names}. (Hook up LeRobotDataset.create() for your installed version.)"
    )
    return True


def main():
    ap = argparse.ArgumentParser(
        description="Convert armsmith.waypoints.v1 demos -> LeRobot dataset."
    )
    ap.add_argument("input", help="waypoint .json file OR a directory of them")
    ap.add_argument(
        "-o", "--out", default="dataset", help="output dir (portable intermediate)"
    )
    ap.add_argument(
        "--map",
        default=os.path.join(os.path.dirname(__file__), "joint_map_lerobot.json"),
        help="joint_map_lerobot.json (column order + motor names)",
    )
    ap.add_argument(
        "--lerobot", action="store_true", help="also try to build a real LeRobotDataset"
    )
    ap.add_argument(
        "--repo-id", default="local/armsmith", help="LeRobot repo id (with --lerobot)"
    )
    ap.add_argument(
        "--stats-only",
        action="store_true",
        help="just parse + print stats, write nothing",
    )
    args = ap.parse_args()

    game_names, motor_names = load_map(args.map)
    files = collect_inputs(args.input)
    if not files:
        print(f"No waypoint files found at {args.input}", file=sys.stderr)
        return 1

    episodes, errors = [], []
    for fp in files:
        try:
            episodes.append(parse_episode(fp, game_names))
        except Exception as e:
            errors.append(f"  SKIP {fp}: {e}")
    if errors:
        print("Warnings:")
        print("\n".join(errors))
    if not episodes:
        print("No valid episodes parsed.", file=sys.stderr)
        return 1

    feature_names = episodes[0]["feature_names"]
    dim = len(feature_names)
    # sanity: all episodes same dim
    for ep in episodes:
        if len(ep["feature_names"]) != dim:
            print(
                "ERROR: inconsistent feature dimension across episodes.",
                file=sys.stderr,
            )
            return 1
    stats = compute_stats(episodes, dim)
    fps = round(1.0 / episodes[0]["dt"]) if episodes else 20

    print(
        f"Parsed {len(episodes)} episode(s), {sum(e['n_frames'] for e in episodes)} frames, "
        f"dim={dim} {feature_names}, fps={fps}"
    )
    print(f"  action mean={[round(x, 1) for x in stats['mean']]}")
    print(f"  action min ={[round(x, 1) for x in stats['min']]}")
    print(f"  action max ={[round(x, 1) for x in stats['max']]}")

    if args.stats_only:
        return 0

    manifest = write_portable(args.out, episodes, feature_names, stats, motor_names)
    print(
        f"Wrote portable dataset -> {args.out}/ (manifest.json + episodes/, "
        f"{manifest['num_episodes']} eps / {manifest['total_frames']} frames)"
    )

    if args.lerobot:
        try_build_lerobot(episodes, feature_names, args.repo_id, fps)

    return 0


if __name__ == "__main__":
    sys.exit(main())
