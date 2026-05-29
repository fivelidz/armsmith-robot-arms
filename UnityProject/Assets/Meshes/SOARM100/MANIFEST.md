# SO-ARM100 / SO-101 Mesh Manifest

**Source repo:** https://github.com/TheRobotStudio/SO-ARM100  
**License:** Apache-2.0 — attribution required (see bottom of this file)  
**Downloaded:** 2026-05-30  
**Total files:** 26 STL meshes  
**Arm variant:** SO-101 Follower (6-DOF, STS3215 servo-based)

---

## Two mesh sets included

| Set | Folder prefix | Format | Notes |
|-----|--------------|--------|-------|
| **Simulation** | `*_v1.stl` / `*_v2.stl` (lowercase) | Binary STL | From `Simulation/SO101/assets/` — purpose-built for sim/URDF use; finer tessellation, higher poly count |
| **Individual** | `*_SO101.stl` (mixed case) | Binary STL | From `STL/SO101/Individual/` — exact print files, slightly lower poly |

For Unity rigging **prefer the Simulation set** (lowercase filenames). Use Individual set as fallback or for visual cross-check.

---

## Simulation Set (`Simulation/SO101/assets/`)

| File | Size (bytes) | Robot Link / Role | Source URL |
|------|-------------|-------------------|------------|
| `base_so101_v2.stl` | 471,584 | **Link 0 – Base** (static mount, bolts to table/frame) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/base_so101_v2.stl |
| `base_motor_holder_so101_v1.stl` | 1,877,084 | **Link 0b – Base Motor Holder** (houses servo 1 inside base) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/base_motor_holder_so101_v1.stl |
| `motor_holder_so101_base_v1.stl` | 1,129,384 | **Link 0c – Motor Holder (Base variant)** (secondary mount bracket) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/motor_holder_so101_base_v1.stl |
| `motor_holder_so101_wrist_v1.stl` | 1,052,184 | **Link 4b – Motor Holder (Wrist variant)** (bracket at wrist joint) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/motor_holder_so101_wrist_v1.stl |
| `rotation_pitch_so101_v1.stl` | 883,684 | **Link 1 – Shoulder / Rotation-Pitch** (first rotating joint above base) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/rotation_pitch_so101_v1.stl |
| `upper_arm_so101_v1.stl` | 1,303,484 | **Link 2 – Upper Arm / Humerus** (long segment from shoulder to elbow) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/upper_arm_so101_v1.stl |
| `under_arm_so101_v1.stl` | 1,975,884 | **Link 3 – Forearm / Under-Arm** (elbow to wrist, carries wrist servo) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/under_arm_so101_v1.stl |
| `wrist_roll_pitch_so101_v2.stl` | 2,699,784 | **Link 4 – Wrist Pitch+Roll combined** (dual-axis wrist block) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/wrist_roll_pitch_so101_v2.stl |
| `wrist_roll_follower_so101_v1.stl` | 1,439,884 | **Link 5 – Wrist Roll (Follower-specific)** (final roll DOF, follower variant without trigger) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/wrist_roll_follower_so101_v1.stl |
| `moving_jaw_so101_v1.stl` | 1,413,584 | **Link 6 – Moving Jaw / Gripper finger** (actuated jaw, end-effector) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/moving_jaw_so101_v1.stl |
| `waveshare_mounting_plate_so101_v2.stl` | 62,784 | **Accessory – WaveShare Mounting Plate** (controller board mount, attached to base) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/waveshare_mounting_plate_so101_v2.stl |
| `sts3215_03a_v1.stl` | 954,084 | **Servo – STS3215 with horn** (Feetech STS3215 servo body, reused at each joint) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/sts3215_03a_v1.stl |
| `sts3215_03a_no_horn_v1.stl` | 865,884 | **Servo – STS3215 without horn** (same servo, hornless version for assembly variation) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/Simulation/SO101/assets/sts3215_03a_no_horn_v1.stl |

---

## Individual Set (`STL/SO101/Individual/`) — Follower Links Only

Leader-only parts (Handle, Trigger) are intentionally excluded.

| File | Size (bytes) | Robot Link / Role | Source URL |
|------|-------------|-------------------|------------|
| `Base_SO101.stl` | 477,984 | **Link 0 – Base** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Base_SO101.stl |
| `Base_motor_holder_SO101.stl` | 340,884 | **Link 0b – Base Motor Holder** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Base_motor_holder_SO101.stl |
| `Motor_holder_SO101_Base.stl` | 292,784 | **Link 0c – Motor Holder (Base)** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Motor_holder_SO101_Base.stl |
| `Motor_holder_SO101_Wrist.stl` | 400,584 | **Link 4b – Motor Holder (Wrist)** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Motor_holder_SO101_Wrist.stl |
| `Rotation_Pitch_SO101.stl` | 334,884 | **Link 1 – Shoulder / Rotation-Pitch** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Rotation_Pitch_SO101.stl |
| `Upper_arm_SO101.stl` | 401,984 | **Link 2 – Upper Arm** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Upper_arm_SO101.stl |
| `Under_arm_SO101.stl` | 525,484 | **Link 3 – Forearm / Under-Arm** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Under_arm_SO101.stl |
| `Wrist_Roll_Pitch_SO101.stl` | 851,884 | **Link 4 – Wrist Pitch+Roll** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Wrist_Roll_Pitch_SO101.stl |
| `Wrist_Roll_SO101.stl` | 307,984 | **Link 4c – Wrist Roll (shared)** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Wrist_Roll_SO101.stl |
| `Wrist_Roll_Follower_SO101.stl` | 602,884 | **Link 5 – Wrist Roll Follower** (no trigger cutout) | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Wrist_Roll_Follower_SO101.stl |
| `Moving_Jaw_SO101.stl` | 563,084 | **Link 6 – Moving Jaw / Gripper** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Moving_Jaw_SO101.stl |
| `Seeedstudio_Mounting_Plate_SO101.stl` | 97,684 | **Accessory – Seeedstudio Mounting Plate** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/Seeedstudio_Mounting_Plate_SO101.stl |
| `WaveShare_Mounting_Plate_SO101.stl` | 37,684 | **Accessory – WaveShare Mounting Plate** | https://raw.githubusercontent.com/TheRobotStudio/SO-ARM100/main/STL/SO101/Individual/WaveShare_Mounting_Plate_SO101.stl |

---

## Joint Map — SO-101 Follower (6 DOF)

```
Joint 0  Base rotation (yaw)          → base_so101_v2.stl / Base_SO101.stl
Joint 1  Shoulder pitch               → rotation_pitch_so101_v1.stl / Rotation_Pitch_SO101.stl  
Joint 2  Elbow pitch                  → upper_arm_so101_v1.stl / Upper_arm_SO101.stl
Joint 3  Forearm pitch                → under_arm_so101_v1.stl / Under_arm_SO101.stl
Joint 4  Wrist pitch + roll (combined)→ wrist_roll_pitch_so101_v2.stl / Wrist_Roll_Pitch_SO101.stl
Joint 5  Wrist roll (follower)        → wrist_roll_follower_so101_v1.stl / Wrist_Roll_Follower_SO101.stl
EEF      Gripper (moving jaw)         → moving_jaw_so101_v1.stl / Moving_Jaw_SO101.stl
Servo    STS3215 (×6, at each joint)  → sts3215_03a_v1.stl / sts3215_03a_no_horn_v1.stl
```

---

## Notes for Unity Import

1. **Units:** STL files from this repo are in **millimetres**. In Unity (metres), scale all meshes by `0.001` on import, or set the model scale factor to `0.001` in the Inspector.
2. **Coordinate system:** STL uses right-hand Z-up; Unity is left-hand Y-up. Use the `RiggedArm` import preset or rotate root by `(-90, 0, 0)` on the parent GameObject.
3. **Simulation set recommended** for rigging — higher poly count gives better visual fidelity. Individual set useful for collider meshes (convex decomposition).
4. **Static jaw:** The SO-101 follower does **not** have a separate `Fixed_Jaw` STL in the Individual set (unlike SO-100). The static jaw is integrated into the `wrist_roll_follower` or `wrist_roll_pitch` body. Use `Simulation/SO100/assets/Fixed_Jaw.stl` as a reference if needed — it is a separate part in the SO-100 family.

---

## License & Attribution

```
Copyright 2024 The Robot Studio

Licensed under the Apache License, Version 2.0 (the "License");
you may not use these files except in compliance with the License.
You may obtain a copy of the License at:
  http://www.apache.org/licenses/LICENSE-2.0

Source: https://github.com/TheRobotStudio/SO-ARM100
```

**Required attribution in any product, publication, or derivative work:**  
> "SO-ARM100/SO-101 robot arm meshes © The Robot Studio, Apache-2.0, https://github.com/TheRobotStudio/SO-ARM100"
