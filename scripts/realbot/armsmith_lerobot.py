#!/usr/bin/env python3
"""
armsmith_lerobot.py — play an ARMSMITH waypoint JSON via the LeRobot API.

Works for BOTH the Seeed reBot B601-DM (Damiao/CAN) and SO-101 (Feetech) because LeRobot
exposes a uniform `robot.send_action({motor_name: degrees})` interface (see
research/manipulation_repos/REAL_ROBOT_PORTING.md).

joint_map_lerobot.json maps game joint names -> lerobot motor names.

SAFETY: ramp to first pose; LeRobot's max_relative_target clamps per-step delta; Ctrl-C disconnects.

Usage (dry-run default):
  python3 armsmith_lerobot.py traj.waypoints.json
  python3 armsmith_lerobot.py traj.waypoints.json --live --robot so101 --port /dev/ttyACM0
"""

import argparse, json, os, time


def load(path):
    with open(path) as f:
        return json.load(f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trajectory")
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--robot", default="so101", choices=["so101", "rebot_b601_dm"])
    ap.add_argument("--port", default="/dev/ttyACM0")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    traj = load(args.trajectory)
    mmap = load(
        os.path.join(here, "joint_map_lerobot.json")
    )  # game name -> lerobot motor name
    dt = float(traj.get("dt_s", 0.05))
    grip = traj.get("gripper_name", "Gripper")

    def action_for(wp):
        d = {j["name"]: j["deg"] for j in wp["joints"]}
        out = {}
        for n in traj["joint_names"]:
            if n in mmap:
                out[mmap[n]] = float(d.get(n, 0.0))
        if grip in mmap:
            out[mmap[grip]] = float(wp.get("gripper_deg", 0.0))
        return out

    robot = None
    if args.live:
        # Lazy import — lerobot only needed on the real controller.
        from lerobot.common.robot_devices.robots.utils import make_robot  # noqa

        robot = make_robot(args.robot)  # uses lerobot config for the chosen arm
        robot.connect()

    wps = traj["waypoints"]
    print(
        f"Playing {len(wps)} waypoints @ dt={dt}s robot={args.robot} live={args.live}"
    )
    try:
        if wps:
            first = action_for(wps[0])
            print("ramp ->", first)
            if robot:
                robot.send_action(first)
            time.sleep(1.5)
        for wp in wps:
            a = action_for(wp)
            if robot:
                robot.send_action(a)  # LeRobot clamps via max_relative_target
            else:
                print("[dry-run] send_action", a)
            time.sleep(dt)
    except KeyboardInterrupt:
        print("\nE-STOP")
    finally:
        if robot:
            robot.disconnect()
    print("done.")


if __name__ == "__main__":
    main()
