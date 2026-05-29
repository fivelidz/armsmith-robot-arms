# Spec Guide — Real-Robot Porting

Implements intentions I11, I12, I13 (PROMPT_LOG.md). Pillar H. See
research/manipulation_repos/REAL_ROBOT_PORTING.md for the deep protocol research.

## Goal
Take a behaviour produced in-game (hand-driven demo, evolved motion, or trained policy) and run it on a
REAL arm — primarily a Feetech-STS3215 arm (we have working control code from the prior `robot_hand`
project) and/or the Seeed reBot-DevArm via LeRobot.

## Primary export format — annotated JSON waypoints (chosen)
One file, lossless, drives everything downstream:
```json
{
  "arm_type": "so101",                 // or "rebot_b601_dm"
  "schema": "armsmith.waypoints.v1",
  "units": "degrees",
  "joint_names": ["BaseYaw","Shoulder","Elbow","Wrist"],
  "gripper_name": "Gripper",
  "dt_s": 0.05,
  "home": { "BaseYaw":0, "Shoulder":0, "Elbow":0, "Wrist":0, "Gripper":0 },
  "limits_deg": { "BaseYaw":[-180,180], "Shoulder":[-100,100], "Elbow":[-135,135], "Wrist":[-110,110] },
  "waypoints": [
    { "t_s": 0.00, "joints": {"BaseYaw":0,"Shoulder":-10,"Elbow":40,"Wrist":-30}, "gripper_deg": 30 },
    { "t_s": 0.05, "joints": {"BaseYaw":2,"Shoulder":-12,"Elbow":44,"Wrist":-32}, "gripper_deg": 30 }
  ]
}
```
The game writes this from: (a) demonstration recordings, (b) evolved/optimised trajectories, (c) policy rollouts.

## Bridge layers (game -> hardware)
Two supported back-ends; the JSON above feeds both.

### Back-end 1: reuse prior `robot_hand/python/servo_controller.py` (Feetech STS3215)
- Conversion: `steps = round(deg/360*4096)`; clamp to per-joint `SERVO_LIMITS`.
- `SyncWritePosEx(ids, positions, speeds, accs)` for simultaneous joint moves.
- Torque-enable on connect; ramp to first waypoint over >=1 s; cap step delta (e.g. <=10 deg/tick).
- A thin `armsmith_player.py` reads our JSON, maps joint_names->servo IDs, streams waypoints at `dt_s`.

### Back-end 2: LeRobot (reBot B601-DM or SO-101)
- LeRobot speaks **degrees** via `robot.send_action({joint_name: deg})`.
- `armsmith_lerobot.py`: load JSON -> for each waypoint at dt -> `send_action(...)` with
  `ensure_safe_goal_position` / `max_relative_target` for safety.
- Works for both arms (Feetech serial OR Damiao CAN) transparently.

## Files to create (Python sidecar, in scripts/realbot/)
- `armsmith_player.py` — JSON waypoints -> Feetech via scservo_sdk (wraps prior servo_controller.py).
- `armsmith_lerobot.py` — JSON waypoints -> LeRobot send_action.
- `joint_map.json` — maps game joint_names -> {servo_id | lerobot_motor_name}, plus calibration.
- `safety.py` — rate-limit, soft-limit clamp, e-stop hook.

## Safety (mandatory)
- Calibration/joint-map loaded before connect.
- Ramp to first pose slowly; per-step max delta; global velocity cap.
- Soft-limit clamp to hardware ranges; keyboard + hardware e-stop; log every sent command.

## Reverse port (telemetry in) — M13
- `armsmith_recorder.py` reads real joint feedback (Feetech `read2ByteTxRx` pos / LeRobot get_observation)
  + camera frames, writes the SAME waypoints schema with an extra `measured` block.
- Game imports it: replays on the in-game arm (EnvCam "follows actual performance"), overlays
  commanded vs measured to visualise drift/adaptation -> feeds domain-adaptation retraining.

## In-Unity side
- `Assets/Scripts/Export/WaypointExporter.cs` — serialises a recorded/optimised trajectory to the JSON above.
- `Assets/Scripts/Export/BehaviourRecorder.cs` — samples joint targets at dt during play -> trajectory.
- Export button in UI writes `<armName>_<task>_<timestamp>.waypoints.json` to a `Exports/` folder.
```
