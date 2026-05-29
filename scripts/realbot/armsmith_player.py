#!/usr/bin/env python3
"""
armsmith_player.py — play an ARMSMITH waypoint JSON on a REAL Feetech STS3215 arm.

Reuses the conventions from the prior project ~/projects/robot_hand/python/servo_controller.py:
  degrees_to_steps(deg) = round(deg / 360 * 4096); 1 Mbit/s TTL bus; SyncWritePosEx for sync moves.

Input: a file written by Unity BehaviourRecorder (schema "armsmith.waypoints.v1"):
{
  "arm_type": "so101", "units": "degrees", "dt_s": 0.05,
  "joint_names": ["BaseYaw","Shoulder","Elbow","Wrist"], "gripper_name": "Gripper",
  "waypoints": [ {"t_s":0.0,"joints":[{"name":"BaseYaw","deg":0},...],"gripper_deg":30}, ... ]
}

Joint -> servo mapping + calibration come from joint_map.json (next to this file).

SAFETY (see design/specs/REAL_ROBOT_PORT_SPEC.md):
  - load joint_map before connect; torque-enable; ramp to first pose over RAMP_S seconds
  - clamp every commanded angle to per-joint soft limits
  - cap per-step delta (MAX_STEP_DEG); global keyboard Ctrl-C e-stop disables torque
  - --dry-run prints commands without opening a port (default ON for safety)

Usage:
  python3 armsmith_player.py traj.waypoints.json --port /dev/ttyUSB0          # dry-run
  python3 armsmith_player.py traj.waypoints.json --port /dev/ttyUSB0 --live    # actually move
"""

import argparse, json, os, sys, time

STEPS_PER_REV = 4096
RAMP_S = 1.5
MAX_STEP_DEG = 12.0  # max change per control tick (rate limit)
DEFAULT_SPEED = 600
DEFAULT_ACC = 30


def deg_to_steps(deg: float) -> int:
    return int(round(deg / 360.0 * STEPS_PER_REV))


def load_json(path):
    with open(path) as f:
        return json.load(f)


def load_joint_map(here):
    p = os.path.join(here, "joint_map.json")
    if not os.path.exists(p):
        raise SystemExit(
            f"Missing joint_map.json at {p} (defines joint->servo id + limits + offset)"
        )
    return load_json(p)


class FeetechBus:
    """Thin wrapper over scservo_sdk, mirrors robot_hand/servo_controller.py."""

    def __init__(self, port, baud=1_000_000, dry=True):
        self.port, self.baud, self.dry = port, baud, dry
        self._sms = self._ph = None

    def open(self):
        if self.dry:
            print(f"[dry-run] would open {self.port} @ {self.baud}")
            return
        from scservo_sdk import sms_sts, PortHandler  # ftservo-python-sdk

        self._ph = PortHandler(self.port)
        self._sms = sms_sts(self._ph)
        if not self._ph.openPort():
            raise SystemExit(f"cannot open {self.port}")
        self._ph.setBaudRate(self.baud)

    def torque(self, ids, on=True):
        if self.dry:
            print(f"[dry-run] torque {'ON' if on else 'OFF'} ids={ids}")
            return
        for sid in ids:
            self._sms.write1ByteTxRx(sid, 40, 1 if on else 0)

    def sync_write(self, ids, steps, speed=DEFAULT_SPEED, acc=DEFAULT_ACC):
        if self.dry:
            print("[dry-run] sync ->", dict(zip(ids, steps)))
            return
        self._sms.SyncWritePosEx(ids, steps, [speed] * len(ids), [acc] * len(ids))

    def close(self):
        if not self.dry and self._ph:
            self._ph.closePort()


def joints_to_dict(wp):
    return {j["name"]: j["deg"] for j in wp["joints"]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trajectory")
    ap.add_argument("--port", default="/dev/ttyUSB0")
    ap.add_argument(
        "--live",
        action="store_true",
        help="actually drive the arm (default is dry-run)",
    )
    ap.add_argument("--speed", type=int, default=DEFAULT_SPEED)
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    traj = load_json(args.trajectory)
    jmap = load_joint_map(
        here
    )  # { "BaseYaw": {"id":1,"min":-180,"max":180,"offset":0,"invert":false}, ... }

    dt = float(traj.get("dt_s", 0.05))
    grip_name = traj.get("gripper_name", "Gripper")
    bus = FeetechBus(args.port, dry=not args.live)
    bus.open()

    ids = [jmap[n]["id"] for n in traj["joint_names"] if n in jmap]
    if grip_name in jmap:
        ids.append(jmap[grip_name]["id"])
    bus.torque(ids, True)

    def clamp(name, deg):
        m = jmap[name]
        deg = max(m["min"], min(m["max"], deg))
        if m.get("invert"):
            deg = -deg
        return deg + m.get("offset", 0.0)

    def steps_for(wp):
        out_ids, out_steps = [], []
        for n in traj["joint_names"]:
            if n not in jmap:
                continue
            out_ids.append(jmap[n]["id"])
            out_steps.append(deg_to_steps(clamp(n, joints_to_dict(wp).get(n, 0.0))))
        if grip_name in jmap:
            out_ids.append(jmap[grip_name]["id"])
            out_steps.append(deg_to_steps(clamp(grip_name, wp.get("gripper_deg", 0.0))))
        return out_ids, out_steps

    wps = traj["waypoints"]
    if not wps:
        raise SystemExit("trajectory has no waypoints")

    # Ramp to first pose slowly.
    print(f"Ramping to start pose over {RAMP_S}s ...")
    fids, fsteps = steps_for(wps[0])
    bus.sync_write(fids, fsteps, speed=200)
    time.sleep(RAMP_S)

    print(f"Playing {len(wps)} waypoints at dt={dt}s (live={args.live}) ...")
    try:
        prev = None
        for wp in wps:
            wids, wsteps = steps_for(wp)
            # rate-limit: clamp step delta
            if prev is not None:
                wsteps = [
                    int(
                        max(
                            p - deg_to_steps(MAX_STEP_DEG),
                            min(p + deg_to_steps(MAX_STEP_DEG), s),
                        )
                    )
                    for p, s in zip(prev, wsteps)
                ]
            bus.sync_write(wids, wsteps, speed=args.speed)
            prev = wsteps
            time.sleep(dt)
    except KeyboardInterrupt:
        print("\nE-STOP: disabling torque")
    finally:
        bus.torque(ids, False)
        bus.close()
    print("done.")


if __name__ == "__main__":
    main()
