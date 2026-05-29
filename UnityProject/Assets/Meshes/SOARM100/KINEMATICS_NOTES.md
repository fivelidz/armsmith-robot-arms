# SO-ARM100 / SO-101 Follower — Kinematics Notes for Unity

**Source URDF:** `so101_new_calib.urdf`  
**Source MJCF:** `so101_new_calib.xml`  
**Repo:** https://github.com/TheRobotStudio/SO-ARM100  
**Data file:** `kinematics.json` (same folder)

---

## 1. The 7-Link Kinematic Chain

```
base_link ──[shoulder_pan]──► shoulder_link
             revolute Z      ±110°, z+62.4mm up

shoulder_link ──[shoulder_lift]──► upper_arm_link
               revolute Z         ±100°, offset [-30.4,-18.3,-54.2]mm

upper_arm_link ──[elbow_flex]──► lower_arm_link
                revolute Z        ±96.8°, offset [-112.6,-28.0,0]mm
                (5° cal. offset applied to limits)

lower_arm_link ──[wrist_flex]──► wrist_link
                revolute Z        ±95°, offset [-134.9,+5.2,0]mm

wrist_link ──[wrist_roll]──► gripper_link
             revolute Z      -157.2° / +162.8°, offset [0,-61.1,+18.1]mm
             (asymmetric — hardware stop geometry + 2.79° cal. offset)

gripper_link ──[gripper]──► moving_jaw_so101_v1_link
              revolute Z    -10° / +100°  (open/close jaw)
              offset [+20.2,+18.8,-23.4]mm

gripper_link ──[gripper_frame_joint FIXED]──► gripper_frame_link (TCP)
              offset [-7.9,-0.2,-98.1]mm, rpy=[0,π,0]
```

**Total link count:** 8 (7 rigid links + 1 TCP dummy)  
**DOF:** 6 actuated revolute joints  
**End-effector:** `gripper_frame_link` (zero-mass, no mesh; 98.1mm below gripper_link)

---

## 2. Total Reach

Summing the primary segment offsets:

| Segment | Length |
|---------|--------|
| base → shoulder_pan pivot | 62.4 mm (vertical) |
| shoulder pivot → shoulder_lift pivot | ~55 mm diagonal |
| shoulder_lift → elbow pivot | 112.6 mm |
| elbow → wrist_flex pivot | 134.9 mm |
| wrist → gripper (wrist_roll pivot) | ~63 mm |
| gripper → TCP | 98.1 mm |
| **Rough max reach (arm fully extended)** | **~345 mm / 0.345 m** |

This is a desktop manipulation arm — typical working radius ≈ 200–300 mm from base.

---

## 3. Coordinate Frame Conventions

### URDF/MJCF (source)
- **Right-hand coordinate system**
- **Z-up** (Z is vertical)
- **Metres** as the length unit
- Joint origins are relative to their **parent link's frame**
- Mesh origins (`origin xyz` in URDF / `pos` in MJCF) are relative to the **link frame**

### Unity (target)
- **Left-hand coordinate system**
- **Y-up** (Y is vertical)
- **Metres** as the length unit (STLs are in mm → must scale ×0.001)
- `ArticulationBody` transforms are relative to parent body

### Conversion required

| URDF axis | Unity axis |
|-----------|-----------|
| +X        | +X        |
| +Y        | +Z        |
| +Z        | +Y        |

**Vector transform:** `urdf(x, y, z)` → `unity(x, z, y)`  
**Rotation (RPY → Unity Euler):** negate Y and Z, then swap Y↔Z  

Concretely, for joint `origin_xyz_m: [x, y, z]`:
```csharp
// C# — URDF → Unity local position
Vector3 unityPos = new Vector3((float)x, (float)z, (float)y);

// For joint axis [ax, ay, az]:
Vector3 unityAxis = new Vector3((float)ax, (float)az, (float)ay);
```

For RPY rotation → Unity Quaternion, use the standard URDF importer approach:
```csharp
// URDF RPY (roll, pitch, yaw) → Unity Quaternion
// Apply in order: Rz(yaw) * Ry(pitch) * Rx(roll) in URDF right-hand frame,
// then convert handedness.
Quaternion q = Quaternion.Euler(
    Mathf.Rad2Deg * (float)roll,   // x rotation stays
    Mathf.Rad2Deg * (float)yaw,    // y←z
    Mathf.Rad2Deg * (float)pitch   // z←y
) * Quaternion.Euler(0, 180, 0);  // flip handedness
// (exact formula depends on your importer — use Unity's URDF Importer package
//  com.unity.robotics.urdf-importer for a tested reference implementation)
```

**Simplest approach:** Import using [Unity Robotics URDF Importer](https://github.com/Unity-Technologies/URDF-Importer) which handles all conversions automatically from `so101_new_calib.urdf`.

---

## 4. STL Scale

The STL files in this folder were exported from OnShape in **millimetres**.  
Unity imports STL files with a default scale of 1 unit = 1 mm, which is **wrong for physics**.

**Required action on Unity import:**
- Set Model Import Scale Factor = **0.001** in the STL Inspector, OR
- Apply a `transform.localScale = Vector3.one * 0.001f` on each mesh GameObject.

> Already done if using the simulation-set STLs that were downloaded alongside this file.  
> Verify: `base_so101_v2.stl` bounding box should be approximately 65×65×75 mm in the CAD;
> after import and scaling it should show as ~0.065×0.065×0.075 m in Unity.

---

## 5. Mesh-to-Link Assignments (Quick Reference)

| Unity ArticulationBody | Primary STL (sim set) | Secondary STLs |
|------------------------|-----------------------|----------------|
| `base_link` | `base_so101_v2.stl` | `base_motor_holder_so101_v1.stl`, `sts3215_03a_v1.stl`, `waveshare_mounting_plate_so101_v2.stl` |
| `shoulder_link` | `rotation_pitch_so101_v1.stl` | `motor_holder_so101_base_v1.stl`, `sts3215_03a_v1.stl` |
| `upper_arm_link` | `upper_arm_so101_v1.stl` | `sts3215_03a_v1.stl` |
| `lower_arm_link` | `under_arm_so101_v1.stl` | `motor_holder_so101_wrist_v1.stl`, `sts3215_03a_v1.stl` |
| `wrist_link` | `wrist_roll_pitch_so101_v2.stl` | `sts3215_03a_no_horn_v1.stl` |
| `gripper_link` | `wrist_roll_follower_so101_v1.stl` | `sts3215_03a_v1.stl` |
| `moving_jaw_link` | `moving_jaw_so101_v1.stl` | — |
| `gripper_frame_link` | *(no mesh — TCP only)* | — |

The `sts3215_03a_v1.stl` servo mesh is reused 5× (motors 1–3, 5–6).  
The `sts3215_03a_no_horn_v1.stl` (no horn variant) is used once at wrist_link (motor 4).

---

## 6. Joint Axis Summary

All joints use **local Z-axis** `[0, 0, 1]` as their rotation axis (in URDF joint frame).  
In Unity `ArticulationBody` terms, the joint frame is set by the URDF origin RPY, so after
coordinate conversion the effective axis direction will differ per joint — trust the
`origin_rpy_*` values from `kinematics.json` to orient the ArticulationBody correctly.

| Joint | Servo ID | Axis (URDF local) | Limits (deg) | Role |
|-------|----------|-------------------|--------------|------|
| shoulder_pan | 1 | Z | ±110° | Base yaw |
| shoulder_lift | 2 | Z | ±100° | Shoulder pitch |
| elbow_flex | 3 | Z | ±96.8° | Elbow pitch |
| wrist_flex | 4 | Z | ±95° | Wrist pitch |
| wrist_roll | 5 | Z | −157.2° / +162.8° | Wrist roll |
| gripper | 6 | Z | −10° / +100° | Jaw open/close |

---

## 7. Calibration Notes

The `so101_new_calib` variant (vs `so101_old_calib`) has:
- A ~2.79° pitch offset on `wrist_roll` joint origin (accounts for servo horn alignment)
- A 5° offset applied to `elbow_flex` joint limits
- These are physical calibration corrections — **do not zero them out** in simulation

---

## 8. Recommended Unity Import Workflow

1. **Use URDF Importer package** (`com.unity.robotics.urdf-importer`) — drag in `so101_new_calib.urdf`
   with mesh path pointing to this `SOARM100/` folder (rename to `assets/` or adjust URDF paths).
2. **Or build manually** using `kinematics.json`:
   - Create an `ArticulationBody` hierarchy: base → shoulder → upper_arm → lower_arm → wrist → gripper → moving_jaw
   - For each joint: set `localPosition` from `origin_xyz_m` (apply axis swap), set `anchorRotation` from `origin_rpy_*`
   - Assign mesh GameObjects as children of each ArticulationBody, using `mesh_xyz_m` offsets
   - Set `ArticulationBody.jointType = ArticulationJointType.RevoluteJoint` for all 6 DOF joints
   - Set `ArticulationBody.xDrive.lowerLimit / upperLimit` from `limit_deg` values

---

*Generated 2026-05-30 from TheRobotStudio/SO-ARM100 main branch*
