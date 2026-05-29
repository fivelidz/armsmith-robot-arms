# Arm Hardware Research — reBot-DevArm (Seeed) & Reference Arms

> Source: https://github.com/Seeed-Projects/reBot-DevArm (fetched 2026-05-30)
> License: Hardware CERN-OHL-W-2.0, Software Apache-2.0 (fully commercial-OK as of 2026-05-11)

## reBot-DevArm — the reference arm for this game

Two structurally-identical variants differing only in motor brand:
- **reBot Arm B601-DM** — Damiao motors (further along: STEP files + BOM released, ROS2, Pinocchio, LeRobot, depth-camera grasping demo all complete)
- **reBot Arm B601-RS** — Robstride motors (in progress)

### Hardware specs (B601-DM)

| Parameter | Value |
|---|---|
| Degrees of freedom | **6 DOF + 1 gripper** |
| Max reach | **650 mm** |
| Recommended payload | **1.5 kg** (continuous <1.5 kg within 70% of reach) |
| Weight | ~4.5 kg |
| Repeatability | **< 0.2 mm** |
| Supply voltage | DC 24 V |
| Ecosystems | ROS1, ROS2, LeRobot, Pinocchio, Isaac Sim, Python SDK |

### Kinematic model for the game
A 6-DOF arm is a serial chain of revolute joints. For an SO-ARM/reBot-style desktop arm the standard layout is:
1. **Base yaw** (rotate about vertical Z)
2. **Shoulder pitch** (lift)
3. **Elbow pitch**
4. **Forearm roll** (wrist rotate)
5. **Wrist pitch**
6. **Wrist roll** / gripper rotate
7. **Gripper** open/close (prismatic-ish, modelled as 2 mirrored finger joints)

For the game's **starter (simple) arm** we begin with a **3-DOF planar reach + 1 gripper** (base yaw, shoulder pitch, elbow pitch, gripper) so the pick-and-place task is tractable, then unlock the full 6-DOF reBot model as a later tier. This matches the user's "start simple" requirement.

### What's reusable from the repo
- **STEP 3D files** for all structural parts (B601-DM) → can be converted to STL/OBJ and imported to Unity as visual meshes for an authentic-looking arm. (CERN-OHL-W means we must retain copyright + license notice + mark modifications.)
- **BOM** down to every screw — useful flavour/specs for in-game "part cards."
- **Camera mount STEP files**: UVC32 mount, Intel D435i mount, D405/Gemini305 mount, Gemini2 mount → directly relevant to the wrist-camera feature.
- **Soft gripper finger** (TPU 95A) STEP — a "soft gripper" upgrade option.
- **Pinocchio integration** (FK/IK + gravity compensation) — reference for our IK math: https://github.com/vectorBH6/reBotArm_control_py
- **LeRobot integration** — confirms the modern imitation-learning path (matches manipulation_repos research).

### Inspiration lineage (all open source, good references)
- **SO-ARM100** (TheRobotStudio) — the canonical low-cost 6-DOF printable arm; per-part STL available → our actual in-game starter mesh source.
- Mobile ALOHA, Dummy-Robot, OpenArm, I2RT, TRLC-DK1.

## Mapping into the game

| Real hardware fact | Game mechanic |
|---|---|
| 6 DOF + gripper | Joint config the player assembles; each joint = a draggable/keyable axis |
| 650 mm reach, 1.5 kg payload | Workspace bounds + payload limit → fail if object too heavy / out of reach |
| < 0.2 mm repeatability | "Precision" stat that affects scoring on placement accuracy |
| STEP/STL parts | Importable authentic meshes; player can swap link lengths (morphology evolution) |
| Servo torque limits | Per-joint torque budget → energy/effort penalty in fitness function |
| Wrist + env cameras | Multi-camera display panels (see cameras/REPORT.md) |
| Pinocchio FK/IK | Our Unity IK solver (FABRIK recommended) mirrors this |
| LeRobot / Isaac Sim | The "training/evolution" layer the player drives |

### Sim-to-real angle (the "real world application")
Because the reBot/SO-ARM dimensions, joint limits and servo torques are published, an arm a player tunes/evolves in-game can be exported (STL + a joint-config JSON) and, in principle, printed and built with FEETECH/Damiao servos. The game's coordinate units are **metres** to keep this 1:1 with the real arm.
