# ARMSMITH → Real Robot bridge

Plays a behaviour recorded/trained in the game on a **real** robot arm. Implements Pillar H
(see ../../ROADMAP.md and ../../design/specs/REAL_ROBOT_PORT_SPEC.md).

## Pipeline
```
Unity (BehaviourRecorder, press G to record / F10 to export)
   -> <arm>_<timestamp>.waypoints.json   (schema "armsmith.waypoints.v1", degrees)
       -> armsmith_player.py    (Feetech STS3215, reuses ~/projects/robot_hand conventions)
       -> armsmith_lerobot.py   (LeRobot: SO-101 Feetech OR reBot B601-DM Damiao/CAN)
```

## Files
- `armsmith_player.py` — direct Feetech STS3215 bus driver (scservo_sdk / ftservo-python-sdk).
  deg→steps = round(deg/360*4096). SyncWritePosEx. Torque enable, ramp, rate-limit, Ctrl-C e-stop.
- `armsmith_lerobot.py` — LeRobot `send_action({motor: deg})`. Works for both supported arms.
- `joint_map.json` — game joint name → Feetech servo id + soft limits + offset/invert.
- `joint_map_lerobot.json` — game joint name → LeRobot motor name.
- `sample.waypoints.json` — example trajectory for dry-run testing.

## Usage
Dry-run is the DEFAULT (prints commands, opens no port). Add `--live` to actually move.
```bash
# Feetech (SO-101 style)
python3 armsmith_player.py sample.waypoints.json                 # dry-run
python3 armsmith_player.py traj.json --port /dev/ttyUSB0 --live  # real arm

# LeRobot (SO-101 or reBot)
python3 armsmith_lerobot.py traj.json                            # dry-run
python3 armsmith_lerobot.py traj.json --robot rebot_b601_dm --live
```

## Dependencies (only for --live)
- Feetech path: `pip install ftservo-python-sdk` (provides `scservo_sdk`).
- LeRobot path: `pip install lerobot` + arm calibration (see LeRobot docs / Seeed reBot wiki).
These are intentionally lazy-imported so dry-run works on any machine.

## Safety checklist (do before --live)
1. Edit `joint_map.json` ids/limits to match YOUR arm; calibrate (use robot_hand/scripts/calibrate_servos.py as reference).
2. Clear workspace, hand on power switch.
3. Dry-run first; confirm step values look sane.
4. First `--live` run at low speed; watch the ramp to start pose.
5. Ctrl-C = e-stop (disables torque).
```


## Diffusion demo-factory: waypoints -> LeRobot dataset (DF1)
Convert recorded ARMSMITH demos into a training dataset for a Diffusion Policy
(see research/diffusion_pathfinding/REPORT.md).
```bash
# portable intermediate (no deps; works anywhere) — manifest.json + episodes/
python3 waypoints_to_lerobot.py demos/ -o dataset/
# just inspect stats
python3 waypoints_to_lerobot.py traj.waypoints.json --stats-only
# also build a real LeRobotDataset (needs `pip install lerobot`)
python3 waypoints_to_lerobot.py demos/ --lerobot --repo-id you/armsmith_pickplace
```
- action      = absolute joint+gripper DEGREES in joint_map_lerobot.json order (= what
  armsmith_lerobot.py streams, so a trained policy deploys through the existing bridge).
- observation.state = same joint vector (low-dim proprio; add object pose / images later).
- Each waypoint file = one episode; dt/fps taken from the file (0.05 s = 20 Hz).
