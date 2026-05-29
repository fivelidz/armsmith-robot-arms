# Porting Arm Trajectories from Simulation to Real Robots

**Date:** 2026-05-30  
**Target hardware:** Seeed reBot-DevArm B601-DM (Damiao CAN motors), B601-RS (Robstride motors), SO-ARM100/SO-101 (Feetech STS3215 servos)  
**Primary framework:** HuggingFace LeRobot v0.5.x  
**Sources:** huggingface/lerobot main branch (verified against actual source code), TheRobotStudio/SO-ARM100, ROS2 trajectory_msgs docs, Feetech STS/SMS control table specification

---

## Quick-Reference Summary (5 bullets)

1. **LeRobot is already the common denominator** — `RebotB601Follower`, `SO100Follower`, and `SO101Follower` are all first-class robot classes in the LeRobot `src/lerobot/robots/` tree. They share an identical Python interface (`connect()` → `get_observation()` ↔ `send_action()`), so a game exporting the right format drives any of them without changing control logic.

2. **The universal action unit is degrees** — LeRobot normalises both the Feetech STS3215 (4096-tick encoder) and the Damiao CAN position feedback to floating-point degrees. Your game trajectory must emit a dict of `{joint_name: float_degrees}` at each timestep; everything below is plumbing to get that dict onto the wire.

3. **Two completely different transports** — The SO-ARM100/101 talks TTL serial at 1 Mbit/s (Feetech SCServo SDK, USB dongle, daisy-chained); the reBot B601-DM talks CAN bus via the `motorbridge` Python package (`send_pos_vel()` → radians under the hood, but the LeRobot wrapper accepts/returns degrees). The game does not need to know which transport is in use.

4. **The recommended export format is annotated JSON waypoints** — A plain JSON file of `{t_s, joints: {name: deg}, gripper: deg}` timestamped at ≥20 Hz is the simplest, most portable, human-readable format. It converts trivially to a LeRobot episode, a ROS2 `JointTrajectory`, or a raw servo write loop.

5. **Safety is non-negotiable at the sim→real boundary** — Always: clamp each joint to its hardware soft-limit before sending, cap `max_relative_target` (max Δ degrees per tick, e.g. 10°), enforce a wall-clock sleep between waypoints, and implement keyboard/button e-stop. LeRobot provides `ensure_safe_goal_position()` and per-joint clipping in every follower class.

---

## Recommended Primary Export Format

> **Use annotated JSON waypoints** (Schema C1, defined in Section 5a).  
> It is the single format that:  
> - is trivially written from Unity/Godot/Blender  
> - is human-inspectable and version-controllable  
> - converts in < 50 lines of Python to LeRobot `send_action()` calls, a LeRobot Parquet episode, or a ROS2 `JointTrajectory` YAML  
> - carries enough metadata (joint names, arm type, dt) that the replay script can validate it before moving anything physical

---

## Table of Contents

1. [LeRobot Control API](#1-lerobot-control-api)
2. [Feetech STS3215 Servo Protocol](#2-feetech-sts3215-servo-protocol)
3. [Damiao / Robstride CAN Motors (reBot B601)](#3-damiao--robstride-can-motors-rebot-b601)
4. [ROS2 / MoveIt2 Path](#4-ros2--moveit2-path)
5. [Export Formats from the Game](#5-export-formats-from-the-game)
6. [Safety: Sim→Real Checklist](#6-safety-simreal-checklist)

---

## 1. LeRobot Control API

### 1.1 Architecture Overview

LeRobot (v0.5+) provides a hardware-agnostic `Robot` abstract base class in  
`src/lerobot/robots/robot.py`. Each supported arm ships as a concrete subclass under its own subdirectory.

```
src/lerobot/robots/
├── robot.py                      # abstract base
├── config.py                     # RobotConfig dataclass registry
├── so_follower/
│   ├── config_so_follower.py     # SOFollowerRobotConfig
│   └── so_follower.py            # SO100Follower / SO101Follower
├── rebot_b601_follower/
│   ├── config_rebot_b601_follower.py
│   └── rebot_b601_follower.py    # RebotB601Follower
└── ...
```

The `Robot` interface contract is five methods:

| Method | Purpose |
|--------|---------|
| `connect(calibrate=True)` | Open port, verify calibration, enable torque |
| `disconnect()` | Graceful shutdown, disable torque |
| `get_observation() → dict[str, float]` | Read present joint positions (and camera frames) |
| `send_action(action: dict) → dict` | Write goal positions; returns what was actually sent |
| `is_connected` | Property: bool |

### 1.2 Motor Bus Abstraction (`FeetechMotorsBus`)

For Feetech/Dynamixel motors LeRobot uses `SerialMotorsBus`, a pyserial-backed class that wraps the SCServo SDK / Dynamixel SDK. Key methods used by follower arms:

```python
bus.sync_read("Present_Position")   # → {motor_name: normalised_value}
bus.sync_write("Goal_Position", goal_dict)  # goal_dict: {name: normalised_value}
bus.write("P_Coefficient", motor, 16)       # single-register config write
```

`sync_write` is **fire-and-forget** (no status packet expected) — fast, appropriate for control loops.  
`write` requests a response packet — slower, used during `configure()`.

### 1.3 Motor Definition and Calibration

A motor on the bus is declared with `Motor(id, model, norm_mode)`:

```python
from lerobot.motors import Motor, MotorNormMode
from lerobot.motors.feetech import FeetechMotorsBus

bus = FeetechMotorsBus(
    port="/dev/ttyUSB0",          # Waveshare board appears here on Linux
    motors={
        "shoulder_pan":  Motor(1, "sts3215", MotorNormMode.DEGREES),
        "shoulder_lift": Motor(2, "sts3215", MotorNormMode.DEGREES),
        "elbow_flex":    Motor(3, "sts3215", MotorNormMode.DEGREES),
        "wrist_flex":    Motor(4, "sts3215", MotorNormMode.DEGREES),
        "wrist_roll":    Motor(5, "sts3215", MotorNormMode.DEGREES),
        "gripper":       Motor(6, "sts3215", MotorNormMode.RANGE_0_100),
    },
    calibration=loaded_calib_dict,
)
```

**`MotorNormMode` options:**

| Mode | Description | Typical use |
|------|-------------|-------------|
| `DEGREES` | Ticks → degrees centred on calibrated mid-point | Body joints |
| `RANGE_M100_100` | Ticks → −100…+100 percentage of full range | Legacy mode |
| `RANGE_0_100` | Ticks → 0…100 | Gripper (open/close percentage) |

**Calibration file** (`~/.cache/lerobot/calibration/<robot_id>.json`):

```json
{
  "shoulder_pan":  {"id": 1, "drive_mode": 0, "homing_offset": 2047, "range_min": 100, "range_max": 3995},
  "shoulder_lift": {"id": 2, "drive_mode": 0, "homing_offset": 2047, "range_min": 200, "range_max": 3900},
  ...
}
```

`homing_offset` is written to register `0x1F` (address 31, 2 bytes) of each motor during calibration. It shifts the encoder zero so that 2047 ticks = physical zero position. `range_min` / `range_max` define the permitted tick range (soft limits).

### 1.4 Action / Observation Format

`action_features` (what `send_action` expects):

```python
{
    "shoulder_pan.pos":  float,   # degrees (DEGREES mode) or -100..100 (RANGE mode)
    "shoulder_lift.pos": float,
    "elbow_flex.pos":    float,
    "wrist_flex.pos":    float,
    "wrist_roll.pos":    float,
    "gripper.pos":       float,   # 0..100 (open=100, closed=0)
}
```

LeRobot stores datasets as Parquet + MP4. In a stored episode the `action` column is a 1-D float tensor of shape `(N_joints,)` where joints are in the dict's key order. The observation `state` tensor is the same layout.

### 1.5 Sending a Sequence of Joint Targets — Minimal Python Example

```python
#!/usr/bin/env python3
"""
Replay a list of joint-angle waypoints on an SO-101 follower arm via LeRobot.

Waypoints format: list of dicts, each {joint_name: float_degrees, ...}
"""
import time
import json
from lerobot.robots.so_follower import SO101Follower
from lerobot.robots.so_follower.config_so_follower import SOFollowerRobotConfig

# ── Configuration ────────────────────────────────────────────────────────────
PORT          = "/dev/ttyUSB0"
WAYPOINT_FILE = "trajectory.json"
STEP_HZ       = 30           # playback frequency
MAX_DELTA_DEG = 10.0         # safety: max Δ per joint per step (degrees)

# ── Load waypoints from game export ──────────────────────────────────────────
with open(WAYPOINT_FILE) as f:
    data = json.load(f)
waypoints = data["waypoints"]   # list of {t_s, joints: {name: deg}, gripper: deg}

# ── Initialise robot ─────────────────────────────────────────────────────────
cfg = SOFollowerRobotConfig(
    port=PORT,
    max_relative_target=MAX_DELTA_DEG,   # LeRobot will clamp each Δ automatically
    use_degrees=True,
)
robot = SO101Follower(cfg)
robot.connect(calibrate=False)   # assumes calibration file already exists

# ── Safety: move slowly to first waypoint ─────────────────────────────────────
print("Homing to first waypoint…")
first_wp = waypoints[0]["joints"]
first_wp["gripper"] = waypoints[0].get("gripper", 0.0)
# ramp over 2 seconds
obs = robot.get_observation()
for alpha in [i / 20 for i in range(1, 21)]:
    interp = {
        f"{k}.pos": obs[f"{k}.pos"] + alpha * (first_wp[k] - obs[f"{k}.pos"])
        for k in first_wp
    }
    robot.send_action(interp)
    time.sleep(0.1)

# ── Replay loop ───────────────────────────────────────────────────────────────
dt = 1.0 / STEP_HZ
t0 = time.perf_counter()
wp_idx = 0
try:
    while wp_idx < len(waypoints):
        t_now = time.perf_counter() - t0
        wp = waypoints[wp_idx]
        if t_now >= wp["t_s"]:
            action = {f"{k}.pos": v for k, v in wp["joints"].items()}
            action["gripper.pos"] = wp.get("gripper", 0.0)
            robot.send_action(action)
            wp_idx += 1
        time.sleep(dt)
except KeyboardInterrupt:
    print("E-STOP triggered!")
finally:
    robot.disconnect()    # disables torque, closes port
    print("Arm disconnected safely.")
```

**Key takeaways from the actual LeRobot source:**
- `send_action()` in `SOFollower` calls `bus.sync_write("Goal_Position", goal_pos)` after stripping `.pos` suffixes.
- `max_relative_target` triggers `ensure_safe_goal_position()` which reads the current position and clips each Δ to the configured magnitude — this is the built-in velocity limiter.
- The gripper motor uses `MotorNormMode.RANGE_0_100`; values outside 0–100 are clamped internally.

---

## 2. Feetech STS3215 Servo Protocol

### 2.1 Physical / Electrical Layer

| Property | Value |
|----------|-------|
| Bus type | Half-duplex TTL serial (1-wire, RS-485 signal levels on some boards) |
| Default baud rate | 1,000,000 bps (1 Mbps) |
| Topology | Daisy-chain (up to 253 devices per port on protocol 0) |
| Connector | 3-pin JST-XH: GND / VCC (5–8.4V) / DATA |
| Controller board | Waveshare Serial Bus Servo Driver (USB-C → USB-serial bridge) |

### 2.2 Encoder / Position Range

| Property | Value |
|----------|-------|
| Encoder resolution | **4096 ticks per revolution** (12-bit absolute encoder) |
| Physical range | 0° – 360° (continuous encoder, but physical range depends on joint hardware stops) |
| Tick → degrees | `degrees = ticks / 4096 * 360` → 1 tick ≈ 0.0879° |
| Degrees → tick | `tick = round(degrees / 360 * 4096)` |
| Goal_Position register | Address `0x2A` (42), 2 bytes (little-endian with sign-magnitude bit 15) |
| Present_Position register | Address `0x38` (56), 2 bytes, read-only |
| Mid-point tick | `2047` (after calibration via `homing_offset`) |

**Sign-magnitude encoding:** The STS3215 uses bit 15 as a sign bit (not two's complement) for signed registers like `Present_Position` and `Goal_Position`. LeRobot's `encode_sign_magnitude()` / `decode_sign_magnitude()` handle this automatically.

**Degree ↔ tick conversion (practical):**

```python
RESOLUTION = 4096  # ticks per revolution

def deg_to_tick(degrees: float, homing_offset: int = 0, range_min: int = 0, range_max: int = 4095) -> int:
    """Convert calibrated degrees to raw STS3215 tick value.
    
    LeRobot's _unnormalize() does this internally when normalize=True.
    Use this function if bypassing LeRobot and writing directly via SCServo SDK.
    """
    mid = (range_min + range_max) / 2          # calibrated zero tick
    tick = int(degrees / 360 * RESOLUTION + mid)
    return max(range_min, min(range_max, tick))  # clamp to safe range

def tick_to_deg(tick: int, homing_offset: int = 0, range_min: int = 0, range_max: int = 4095) -> float:
    mid = (range_min + range_max) / 2
    return (tick - mid) / RESOLUTION * 360.0
```

### 2.3 SCServo SDK (used by LeRobot internally)

LeRobot wraps `scservo_sdk` (PyPI: `feetech-servo-sdk`). If you need to write directly without the LeRobot layer:

```python
import scservo_sdk as scs

port_handler   = scs.PortHandler("/dev/ttyUSB0")
packet_handler = scs.PacketHandler(0)   # protocol 0 for STS series

port_handler.openPort()
port_handler.setBaudRate(1_000_000)

MOTOR_ID       = 1
GOAL_POS_ADDR  = 42    # 0x2A
GOAL_POS_LEN   = 2

# Write 2047 (centre position, ~180°) to motor 1
packet_handler.writeTxRx(port_handler, MOTOR_ID, GOAL_POS_ADDR, GOAL_POS_LEN,
                          [scs.SCS_LOBYTE(2047), scs.SCS_HIBYTE(2047)])
```

For multi-motor synchronous writes use `GroupSyncWrite` (which is what `bus.sync_write()` calls internally). This sends a single broadcast packet updating all motors simultaneously, eliminating serial round-trip latency.

### 2.4 Control Table Summary (STS3215)

| Register name | Addr | Len | R/W | Notes |
|---------------|------|-----|-----|-------|
| `ID` | 5 | 1 | R/W | 1–253, set once at setup |
| `Baud_Rate` | 6 | 1 | R/W | 0=1M, 1=500k, 4=115200 |
| `Min_Position_Limit` | 9 | 2 | R/W | Soft lower tick limit |
| `Max_Position_Limit` | 11 | 2 | R/W | Soft upper tick limit |
| `Homing_Offset` | 31 | 2 | R/W | Encoder zero offset |
| `Operating_Mode` | 33 | 1 | R/W | 0=Position, 1=Velocity, 2=PWM, 3=Step |
| `Torque_Enable` | 40 | 1 | R/W | 1=enabled, 0=disabled |
| `Goal_Position` | 42 | 2 | R/W | Target tick |
| `Goal_Velocity` | 46 | 2 | R/W | Max speed (0=unlimited) |
| `Present_Position` | 56 | 2 | R | Current tick |

---

## 3. Damiao / Robstride CAN Motors (reBot B601)

### 3.1 Hardware Configuration

The **reBot B601-DM** has 6 DOF + gripper driven by **Damiao** BLDC motors:

| Joint | Motor model |
|-------|-------------|
| shoulder_pan, shoulder_lift, elbow_flex | DM-J4340P (high-torque) |
| wrist_flex, wrist_yaw, wrist_roll, gripper | DM-J4310 |

The **B601-RS** variant uses **Robstride** motors with equivalent CAN-based control. The LeRobot integration layer (`motorbridge` package) abstracts the differences.

### 3.2 CAN Bus Layer

| Property | Value |
|----------|-------|
| Physical bus | CAN 2.0B, 1 Mbit/s |
| Adapter options | (a) Damiao serial bridge (USB↔CAN dongle, baud 921600), (b) SocketCAN (slcan, PCAN, embedded host) |
| Motor IDs | Each motor has a send CAN ID and a receive CAN ID (e.g. `0x01`/`0x11`) |
| Max motors per bus | Theoretically 127, practically limited by termination and timing |

### 3.3 Operating Modes

Damiao motors support three primary closed-loop modes:

| Mode | LeRobot name | Description | Use case |
|------|-------------|-------------|----------|
| Position+Velocity | `MotorBridgeMode.POS_VEL` | Position target + velocity feed-forward | Body joints (shoulder, elbow, wrist) |
| Force+Position | `MotorBridgeMode.FORCE_POS` | Position target with torque cap | Gripper (prevents overloading) |
| MIT mode | (raw CAN) | Simultaneous position/velocity/torque control | Advanced teleoperation |

LeRobot uses `POS_VEL` for all joints except the gripper (which uses `FORCE_POS` with `gripper_torque_ratio=0.1`).

### 3.4 The `motorbridge` Python Package

`motorbridge` is a Seeed/community Python library that wraps the Damiao CAN protocol. It is installed as an optional LeRobot extra:

```bash
pip install lerobot[rebot]
```

Key API used in `RebotB601Follower`:

```python
from motorbridge import Controller as MotorBridgeController, Mode as MotorBridgeMode

# Initialise (Damiao serial bridge adapter)
bus = MotorBridgeController.from_dm_serial(
    serial_port="/dev/ttyACM0",
    baud=921600,
)

# Add motors (send_id, recv_id, model_string)
motor = bus.add_damiao_motor(0x01, 0x11, "4340P")

# Enable and set mode
bus.enable_all()
motor.ensure_mode(MotorBridgeMode.POS_VEL)

# Send position target (RADIANS internally!)
import math
pos_rad = math.radians(45.0)   # convert from degrees
vel_rad = math.radians(150.0)  # velocity limit in rad/s
motor.send_pos_vel(pos_rad, vel_rad)

# Read feedback
motor.request_feedback()
bus.poll_feedback_once()
state = motor.get_state()
position_deg = math.degrees(state.pos)
```

**Important:** `motorbridge`/Damiao internally uses **radians**. LeRobot's `RebotB601Follower.send_action()` accepts/returns **degrees** and converts via `math.radians()` / `math.degrees()` before/after calling `motor.send_pos_vel()`.

### 3.5 Sending Joint Targets to the B601-DM

```python
from lerobot.robots.rebot_b601_follower import RebotB601Follower
from lerobot.robots.rebot_b601_follower.config_rebot_b601_follower import RebotB601FollowerRobotConfig

cfg = RebotB601FollowerRobotConfig(
    port="/dev/ttyACM0",
    can_adapter="damiao",
    dm_serial_baud=921600,
    max_relative_target=10.0,         # degrees per step safety cap
    pos_vel_velocity=[150.0] * 7,     # deg/s for each joint
    gripper_torque_ratio=0.1,
)
robot = RebotB601Follower(cfg)
robot.connect(calibrate=False)

# Action dict — same format as SO-101
action = {
    "shoulder_pan.pos":  0.0,
    "shoulder_lift.pos": -30.0,
    "elbow_flex.pos":    -60.0,
    "wrist_flex.pos":    20.0,
    "wrist_yaw.pos":     0.0,
    "wrist_roll.pos":    0.0,
    "gripper.pos":       -10.0,  # degrees (gripper range: -270 to 0)
}
sent = robot.send_action(action)
```

Soft joint limits (from `config_rebot_b601_follower.py`, verified from source):

| Joint | Min (°) | Max (°) |
|-------|---------|---------|
| shoulder_pan | −145 | +145 |
| shoulder_lift | −170 | +1 |
| elbow_flex | −200 | +1 |
| wrist_flex | −80 | +90 |
| wrist_yaw | −90 | +90 |
| wrist_roll | −90 | +90 |
| gripper | −270 | 0 |

### 3.6 Robstride Motors (B601-RS)

Robstride motors use a similar CAN-based MIT-mode protocol. The `motorbridge` package abstracts them under the same API. From a game export perspective the control interface is identical — the LeRobot follower class handles the motor-specific framing. The key difference: Robstride uses a slightly different CAN frame format and motor model strings (e.g. `"RS03"`, `"RS02"`), but these are internal to `motorbridge`.

---

## 4. ROS2 / MoveIt2 Path

### 4.1 Overview

The reBot ships with a ROS2 Humble/Iron controller stack. MoveIt2 uses the `FollowJointTrajectory` action server (`control_msgs/action/FollowJointTrajectory`) to execute planned paths. This is the standard ROS2 arm control interface.

### 4.2 `JointTrajectory` Message Structure

```
trajectory_msgs/msg/JointTrajectory
├── std_msgs/Header header
│   └── builtin_interfaces/Time stamp
├── string[] joint_names           # ordered list matching points columns
└── trajectory_msgs/JointTrajectoryPoint[] points
    └── (per waypoint):
        ├── float64[] positions    # joint positions (radians, ROS2 convention)
        ├── float64[] velocities   # (optional) desired velocities at point
        ├── float64[] accelerations # (optional)
        ├── float64[] effort       # (optional) torque
        └── builtin_interfaces/Duration time_from_start
            ├── int32 sec
            └── uint32 nanosec
```

**Critical:** ROS2 uses **radians** throughout. Always convert from game degrees before publishing.

### 4.3 Sending a Trajectory from Python (ROS2)

```python
#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from control_msgs.action import FollowJointTrajectory
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from builtin_interfaces.msg import Duration
import math, json

JOINT_NAMES = [
    "shoulder_pan_joint", "shoulder_lift_joint", "elbow_flex_joint",
    "wrist_flex_joint", "wrist_yaw_joint", "wrist_roll_joint", "gripper_joint"
]

class TrajPlayer(Node):
    def __init__(self, waypoints):
        super().__init__("traj_player")
        self._client = ActionClient(
            self, FollowJointTrajectory,
            "/rebot_arm_controller/follow_joint_trajectory"
        )
        self.waypoints = waypoints

    def send(self):
        goal = FollowJointTrajectory.Goal()
        traj = JointTrajectory()
        traj.joint_names = JOINT_NAMES

        for wp in self.waypoints:
            pt = JointTrajectoryPoint()
            pt.positions = [math.radians(wp["joints"][j]) for j in JOINT_NAMES]
            t_s = wp["t_s"]
            pt.time_from_start = Duration(
                sec=int(t_s),
                nanosec=int((t_s % 1) * 1e9)
            )
            traj.points.append(pt)

        goal.trajectory = traj
        self._client.wait_for_server()
        future = self._client.send_goal_async(goal)
        rclpy.spin_until_future_complete(self, future)

def main():
    rclpy.init()
    with open("trajectory.json") as f:
        waypoints = json.load(f)["waypoints"]
    node = TrajPlayer(waypoints)
    node.send()
    rclpy.shutdown()
```

### 4.4 MoveIt2 Integration

For more complex trajectories (collision avoidance, IK-planned), use MoveIt2:

```python
from moveit.planning import MoveItPy
from moveit.core.robot_state import RobotState

moveit = MoveItPy(node_name="moveit_py")
arm    = moveit.get_planning_component("rebot_arm")  # group name from SRDF

# Plan to a named pose
arm.set_start_state_to_current_state()
arm.set_goal_state(configuration_name="home")
plan = arm.plan()
if plan:
    moveit.execute(plan.trajectory, controllers=[])
```

### 4.5 ROS2 vs LeRobot — When to Use Which

| Scenario | Recommendation |
|----------|----------------|
| Open-loop waypoint playback from game | LeRobot (simpler, no ROS2 overhead) |
| Real-time teleoperation | LeRobot |
| Collision-aware planned paths (MoveIt2) | ROS2 + `FollowJointTrajectory` |
| Integration with broader ROS2 ecosystem | ROS2 |
| Training imitation learning policies | LeRobot (native dataset format) |

---

## 5. Export Formats from the Game

All three formats store joint angles **in degrees** (floating point). The game should export in the coordinate frame of the real robot's URDF/calibration (not Unity's left-handed Y-up frame — apply a transform layer).

### 5a. Recommended: Annotated JSON Waypoints

```json
{
  "schema_version": 1,
  "arm_type": "so101_follower",
  "joint_names": ["shoulder_pan", "shoulder_lift", "elbow_flex",
                  "wrist_flex", "wrist_roll"],
  "dt_s": 0.05,
  "total_duration_s": 4.2,
  "waypoints": [
    {
      "t_s": 0.00,
      "joints": {
        "shoulder_pan":  0.0,
        "shoulder_lift": -10.5,
        "elbow_flex":    -45.2,
        "wrist_flex":    12.3,
        "wrist_roll":    0.0
      },
      "gripper": 85.0
    },
    {
      "t_s": 0.05,
      "joints": {
        "shoulder_pan":  1.2,
        "shoulder_lift": -11.0,
        "elbow_flex":    -46.0,
        "wrist_flex":    13.1,
        "wrist_roll":    0.5
      },
      "gripper": 84.5
    }
  ]
}
```

**Schema notes:**
- `arm_type` must match a valid LeRobot `config_class.name` string (e.g. `"so101_follower"`, `"so100_follower"`, `"rebot_b601_follower"`)
- `dt_s` is informational; the replay script uses `t_s` timestamps for timing
- `gripper` is a separate float: degrees for reBot B601 (`-270..0`), 0..100 percentage for SO-101
- Joint order in `joint_names` does not need to match the dict key order; use names
- For reBot B601 add `"wrist_yaw"` to `joint_names` and the `joints` dict

**Replay script (minimal, ~30 lines):**

```python
import time, json, math
from lerobot.robots.so_follower import SO101Follower
from lerobot.robots.so_follower.config_so_follower import SOFollowerRobotConfig

data = json.load(open("trajectory.json"))
cfg  = SOFollowerRobotConfig(port="/dev/ttyUSB0", max_relative_target=10.0, use_degrees=True)
robot = SO101Follower(cfg)
robot.connect(calibrate=False)

try:
    t0 = time.perf_counter()
    for wp in data["waypoints"]:
        while time.perf_counter() - t0 < wp["t_s"]:
            time.sleep(0.002)
        action = {f"{k}.pos": v for k, v in wp["joints"].items()}
        action["gripper.pos"] = wp.get("gripper", 0.0)
        robot.send_action(action)
finally:
    robot.disconnect()
```

---

### 5b. LeRobot-Compatible Episode

LeRobot datasets store episodes as **Parquet files** (state + action columns) plus optional **MP4 video** (cameras). You can write a minimal episode from the JSON waypoints:

```python
import pandas as pd
import numpy as np
import json

JOINT_COLS = [
    "observation.state.shoulder_pan.pos",
    "observation.state.shoulder_lift.pos",
    "observation.state.elbow_flex.pos",
    "observation.state.wrist_flex.pos",
    "observation.state.wrist_roll.pos",
    "observation.state.gripper.pos",
]
ACTION_COLS = [c.replace("observation.state.", "action.") for c in JOINT_COLS]

with open("trajectory.json") as f:
    data = json.load(f)

rows = []
for i, wp in enumerate(data["waypoints"]):
    j = wp["joints"]
    vals = [
        j["shoulder_pan"], j["shoulder_lift"], j["elbow_flex"],
        j["wrist_flex"], j["wrist_roll"], wp.get("gripper", 0.0)
    ]
    row = {
        "episode_index":   0,
        "frame_index":     i,
        "timestamp":       wp["t_s"],
        "index":           i,
    }
    for col, v in zip(JOINT_COLS, vals):
        row[col] = v
    for col, v in zip(ACTION_COLS, vals):   # action = target position at this step
        row[col] = v
    rows.append(row)

df = pd.DataFrame(rows)
df.to_parquet("episode_000000.parquet", index=False)
print(f"Wrote {len(df)} frames to episode_000000.parquet")
```

**Parquet schema (per row):**

| Column | Type | Description |
|--------|------|-------------|
| `episode_index` | int64 | Episode number (0-indexed) |
| `frame_index` | int64 | Frame within episode |
| `timestamp` | float64 | Seconds from episode start |
| `index` | int64 | Global frame index across all episodes |
| `observation.state.<joint>.pos` | float32 | Present position in degrees |
| `action.<joint>.pos` | float32 | Goal position in degrees |
| `observation.images.cam_wrist` | list[uint8] | (Optional) JPEG bytes or video frame index |

A valid LeRobot dataset also needs a `meta/info.json` (dataset statistics, feature specs) — see `LeRobotDataset` documentation. For playback-only purposes the Parquet file alone is sufficient.

---

### 5c. ROS2 JointTrajectory YAML / Bag

**YAML format** (for `ros2 bag play` or direct loading):

```yaml
# trajectory.yaml — ROS2 JointTrajectory message
header:
  stamp: {sec: 0, nanosec: 0}
  frame_id: ''
joint_names:
  - shoulder_pan_joint
  - shoulder_lift_joint
  - elbow_flex_joint
  - wrist_flex_joint
  - wrist_roll_joint
  - gripper_joint
points:
  - positions: [0.0000, -0.1833, -0.7889, 0.2147, 0.0000, 1.4835]   # radians!
    velocities: []
    accelerations: []
    effort: []
    time_from_start: {sec: 0, nanosec: 0}
  - positions: [0.0209, -0.1920, -0.8029, 0.2286, 0.0087, 1.4748]
    velocities: []
    accelerations: []
    effort: []
    time_from_start: {sec: 0, nanosec: 50000000}   # 50 ms = 20 Hz
```

**Important:** ROS2 uses **radians**. Convert from your JSON degrees: `rad = deg * π / 180`.

**Converting JSON → YAML with Python:**

```python
import json, math, yaml

with open("trajectory.json") as f:
    data = json.load(f)

JOINT_MAP = {
    "shoulder_pan":  "shoulder_pan_joint",
    "shoulder_lift": "shoulder_lift_joint",
    "elbow_flex":    "elbow_flex_joint",
    "wrist_flex":    "wrist_flex_joint",
    "wrist_roll":    "wrist_roll_joint",
}
JOINT_ORDER = list(JOINT_MAP.values())

msg = {
    "header": {"stamp": {"sec": 0, "nanosec": 0}, "frame_id": ""},
    "joint_names": JOINT_ORDER,
    "points": []
}

for wp in data["waypoints"]:
    t = wp["t_s"]
    pos_rad = [math.radians(wp["joints"][j]) for j in JOINT_MAP]
    msg["points"].append({
        "positions": pos_rad,
        "velocities": [], "accelerations": [], "effort": [],
        "time_from_start": {"sec": int(t), "nanosec": int((t % 1) * 1e9)}
    })

with open("trajectory.yaml", "w") as f:
    yaml.dump(msg, f, default_flow_style=None, sort_keys=False)
```

**ROS2 bag:** Record to bag format using `rosbag2_py` or `ros2 bag record /joint_trajectory_controller/joint_trajectory`. For game export just write the YAML and load with `rclpy` at runtime; bags add unnecessary complexity for offline trajectories.

---

## 6. Safety: Sim→Real Checklist

The biggest risks when going from simulation to real hardware are:
1. **Joint singularities and collisions** — the sim rarely models real joint limits faithfully
2. **Velocity shocks** — a large position jump sent in one step can over-current motors and trip protection
3. **Lost communication** — a dropped serial/CAN packet leaves the arm mid-motion

### 6.1 Joint-Limit Clamping

Always clamp before sending. LeRobot does this automatically when `calibration` is set:

```python
# Manual clamp (if bypassing LeRobot):
JOINT_LIMITS_DEG = {
    # SO-101 (soft limits, smaller than physical stops)
    "shoulder_pan":  (-165, 165),
    "shoulder_lift": (-90,  90),
    "elbow_flex":    (-90,  90),
    "wrist_flex":    (-90,  90),
    "wrist_roll":    (-180, 180),
    "gripper":       (0,    100),   # % open
    # reBot B601-DM
    # (see table in Section 3.5)
}

def clamp_action(action: dict) -> dict:
    clamped = {}
    for joint, val in action.items():
        name = joint.removesuffix(".pos")
        lo, hi = JOINT_LIMITS_DEG.get(name, (-360, 360))
        clamped[joint] = max(lo, min(hi, val))
    return clamped
```

### 6.2 Velocity / Rate Limiting

**Option A — `max_relative_target` in LeRobot config:**
```python
cfg = SOFollowerRobotConfig(max_relative_target=10.0)  # max 10° per control step
```
This activates `ensure_safe_goal_position()` which reads current position and clips Δ per joint.

**Option B — explicit rate limiting in replay loop:**
```python
MAX_DEG_PER_SEC = 60.0    # conservative for learning/playback

def rate_limited_action(goal, current, dt_s):
    max_delta = MAX_DEG_PER_SEC * dt_s
    return {
        k: current[k] + max(-max_delta, min(max_delta, goal[k] - current[k]))
        for k in goal
    }
```

**Recommended values (conservative for first test):**

| Arm | Max Δ per step @ 30 Hz | Max deg/s |
|-----|------------------------|-----------|
| SO-101 (STS3215) | 3° | 90 °/s |
| reBot B601-DM | 5° | 150 °/s |
| reBot B601-RS | 5° | 150 °/s |

### 6.3 Smooth Homing to First Waypoint

**Never teleport to the first waypoint from an unknown arm pose.** Always read present position and ramp over ≥ 1–2 seconds:

```python
obs = robot.get_observation()
present = {k.removesuffix(".pos"): v for k, v in obs.items() if k.endswith(".pos")}
first   = waypoints[0]["joints"]
RAMP_STEPS = 40

for i in range(1, RAMP_STEPS + 1):
    alpha = i / RAMP_STEPS
    action = {f"{k}.pos": present[k] + alpha * (first[k] - present[k]) for k in first}
    robot.send_action(action)
    time.sleep(1 / 20)   # 20 Hz ramp
```

### 6.4 Emergency Stop

**Keyboard e-stop** (insert in replay loop):

```python
import threading, sys

_estop = threading.Event()

def _watch_estop():
    input("Press ENTER at any time to E-STOP\n")
    _estop.set()

threading.Thread(target=_watch_estop, daemon=True).start()

# In replay loop:
if _estop.is_set():
    robot.disconnect()   # disables torque
    sys.exit(1)
```

**Hardware e-stop** — wire a normally-closed pushbutton in series with the power supply to the motor controller board (Waveshare board or Damiao serial bridge). This is the only reliable hardware-level stop.

### 6.5 Torque / Current Limits

For the STS3215 the following registers limit torque (written once during `configure()`):

```python
bus.write("Max_Torque_Limit",   "gripper", 500)   # 50% of 1000 max
bus.write("Protection_Current", "gripper", 250)   # 50% of 500 mA max
bus.write("Overload_Torque",    "gripper", 25)    # 25% when overloaded
```

For Damiao motors, `gripper_torque_ratio=0.1` in the config limits `FORCE_POS` current to 10% of rated.

### 6.6 Communication Watchdog

If using a background thread to stream actions, implement a watchdog:

```python
import time

WATCHDOG_TIMEOUT_S = 0.5   # if no new action in 0.5s, disable torque

last_send = time.time()

def watchdog_loop(robot):
    while True:
        if time.time() - last_send > WATCHDOG_TIMEOUT_S:
            robot.disconnect()
            print("WATCHDOG: torque disabled (no heartbeat)")
            break
        time.sleep(0.05)
```

### 6.7 Summary Safety Checklist

- [ ] **Calibration file exists** before `connect(calibrate=False)` is called
- [ ] **Homing ramp** executed before any trajectory playback
- [ ] **`max_relative_target`** set in config (≤ 10° per step for first run)
- [ ] **Joint limits** defined and enforced in game export and replay script
- [ ] **E-stop** wired and tested (keyboard and/or hardware button)
- [ ] **Low velocity** on first run — use ≤ 50% of nominal speed
- [ ] **Clear workspace** — no cables, objects, or people within arm reach
- [ ] **Test on lowest-risk joint first** — usually shoulder_pan (least loaded)
- [ ] **Log all sent actions** to a file for post-mortem debugging
- [ ] **Motor temperatures** monitored for long runs (`bus.read("Present_Temperature", motor)`)

---

## Appendix A: Joint Name Mapping (Game → Robot)

Unity typically exports joints with names like `Joint1`, `joint_0`, or bone names. Map to LeRobot names before sending:

```python
UNITY_TO_LEROBOT_SO101 = {
    "Base":          "shoulder_pan",
    "Shoulder":      "shoulder_lift",
    "Elbow":         "elbow_flex",
    "WristPitch":    "wrist_flex",
    "WristRoll":     "wrist_roll",
    "Gripper":       "gripper",
}

def remap_joints(unity_joints: dict) -> dict:
    return {UNITY_TO_LEROBOT_SO101[k]: v for k, v in unity_joints.items()
            if k in UNITY_TO_LEROBOT_SO101}
```

Also apply coordinate frame correction if your Unity simulation uses a different convention (e.g. negate shoulder_lift if sim uses Z-up vs real arm's Y-up).

---

## Appendix B: Quick Install

```bash
# SO-101 (Feetech)
pip install lerobot[feetech]

# reBot B601-DM (Damiao CAN)
pip install lerobot[rebot]

# ROS2 trajectory publishing (alongside lerobot)
pip install rclpy  # provided by your ROS2 distro, not PyPI

# Verify installed robot types
lerobot-info
```

---

*Report generated: 2026-05-30. Source-verified against huggingface/lerobot main branch (v0.5.x), commit history up to May 2026.*
