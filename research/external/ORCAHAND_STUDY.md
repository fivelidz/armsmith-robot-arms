# ORCA Hand Study Report
> Written: 2026-05-31  
> Author: Claude Code (autonomous study session)  
> For: ARMSMITH game (Unity 6 / ArticulationBody)

---

## 1. Repo Overview

| Repo | URL | Stars | License | Description |
|------|-----|-------|---------|-------------|
| `orcahand_description` | https://github.com/orcahand/orcahand_description | 352 ★ | MIT | **The model** — MJCF + URDF for v1 and v2, all STL meshes |
| `orca_core` | https://github.com/orcahand/orca_core | 508 ★ | MIT | **The SDK** — Python controller, calibration, motor abstraction |
| `orca_sim` | https://github.com/orcahand/orca_sim | 49 ★ | — | **The sim** — MuJoCo Gymnasium environments |
| `faive_gym_oss` | https://github.com/orcahand/faive_gym_oss | 7 ★ | MIT | **RL training** — IsaacGym environment (earlier Faive Hand, same team) |
| `orca_retargeter` | https://github.com/orcahand/orca_retargeter | 10 ★ | — | **Teleoperation** — hand retargeting |
| `rwr_system` | https://github.com/orcahand/rwr_system | 8 ★ | — | Teleop, data collection, inference |
| `dd_system` | https://github.com/orcahand/dd_system | 2 ★ | — | ROS-based teleoperation control |
| `orca-gym` | https://github.com/orcahand/orca-gym | 10 ★ | — | Additional sim environments |
| `openarm` | https://github.com/orcahand/openarm | 3 ★ | — | Companion arm (bimanual setup) |

**Paper:** [arXiv 2504.04259](https://arxiv.org/abs/2504.04259) — "ORCA: An Open-Source, Reliable, Cost-Effective, Anthropomorphic Robotic Hand for Uninterrupted Dexterous Task Learning" (ETH Zurich Soft Robotics Lab, 2025)

**Cloned locally to:**
- `research/external/orcahand/orcahand_description/`
- `research/external/orcahand/orca_core/`
- `research/external/orcahand/orca_sim/`

---

## 2. Simulation System

### 2.1 Engine: MuJoCo
The primary simulation engine is **MuJoCo** (via the `mujoco` Python package). There is a secondary older Isaac Gym environment (`faive_gym_oss`) from the same ETH Zurich team for the predecessor "Faive Hand." The current ORCA hand uses only MuJoCo.

No PyBullet, no Genesis, no Isaac Sim (though a third-party community example `WangZY233/UR_OrcaHand_Pickandplace` runs in Isaac Sim).

### 2.2 Gymnasium Wrapper
`orca_sim` wraps MuJoCo models in the [Gymnasium](https://gymnasium.farama.org/) API (`gym.Env`). Key file: `src/orca_sim/envs.py`.

```
BaseOrcaHandEnv
├── OrcaHandLeft          (single left hand)
├── OrcaHandRight         (single right hand)
├── OrcaHandCombined      (bimanual)
├── OrcaHandLeftExtended  (with camera tower, U2D2 board, fans)
├── OrcaHandRightExtended
└── OrcaHandCombinedExtended
```
Plus task-level subclass in `task_envs.py`:
```
OrcaHandRightCubeOrientation  ← hand + free-floating cube, red-face alignment task
```

### 2.3 Scene Composition (XML include chain)
Scenes are built by XML `<include>` composition:

```
scenes/v2/scene_right_cube_orientation.xml
  ├── models/v2/assets/scene.xml         ← lights, floor, camera, skybox
  ├── models/v2/assets/options.xml       ← compiler settings, default classes, materials
  └── models/v2/mjcf/orcahand_right.mjcf ← asset declarations + actuators + contact excludes
        └── worldbody: <include orcahand_right_body.xml>  ← kinematic tree of bodies/joints
```

This modular XML pattern allows swapping the scene environment without touching the hand model.

### 2.4 Physics Configuration
From `models/v2/assets/options.xml`:
- `<compiler angle="radian" eulerseq="XYZ"/>` — matches URDF `rpy` convention
- `mesh scale="0.001 0.001 0.001"` — STL meshes are in millimetres, scaled to metres
- Default joint: `type="hinge"`, `damping="0.1"`, `armature="0.001"`, `frictionloss="0.001"`, `margin="0.01"`
- Default actuator: `<position kp="2.0" forcerange="-1 1" ctrllimited="true"/>`
- Contact: `condim="3"` (normal + 2 friction), `friction="1 0.005 0.001"` (slip + torsion)

### 2.5 Actuation Model — Position Control (Not Tendon)
**Critical finding:** The MJCF does **not** use MuJoCo `<tendon>` elements. Despite the ORCA hand being physically tendon-driven on the real robot, the simulation uses direct **joint-space position actuators**:

```xml
<actuator>
  <position name="right_wrist_actuator"   joint="right_wrist"  ctrlrange="-1.134 0.611"/>
  <position name="right_i-mcp_actuator"   joint="right_i-mcp"  ctrlrange="-0.436 1.745"/>
  <position name="right_i-pip_actuator"   joint="right_i-pip"  ctrlrange="-0.262 1.867"/>
  ... (17 total, one per DOF)
</actuator>
```

The real tendon routing (17 Dynamixel/Feetech servos → joints via tendons) is abstracted away. The `joint_to_motor_ratios_dict` in `calibration.yaml` captures the effective gear ratio that gets discovered during hardware calibration. The sim simply commands joint angles directly.

### 2.6 Collision Geometry — Dual Mesh Approach
Every body has **two sets of geometries**:
1. **Visual** (`group="2"`, `contype="0"` — non-colliding): high-res STL with full detail
2. **Collision** (`group="1"`, `contype="1"`, `condim="3"`): separate, decimated STL meshes

There is also a **skin collision** geometry for each finger segment (e.g. `left_collision_index_ip_skin.stl`) that captures the soft padded exterior contact surface separately from the bone/structural mesh.

Contact pairs between adjacent bodies in the same finger are excluded with `<exclude>` blocks to avoid self-collision false positives.

### 2.7 Task Environment (Cube Orientation)
`OrcaHandRightCubeOrientation` (`task_envs.py`) adds:
- A free-floating 36mm cube with a red face (`freejoint`)
- Observation: hand qpos + qvel + cube pose + cube velocity
- Reward: alignment of red face toward "up" direction
- Randomization: initial cube orientation, xy jitter
- Reset options: nominal (deterministic) or randomized

---

## 3. DOF / Joint / Kinematic Details

### 3.1 DOF Count
**17 actuated DOF per hand** (confirmed from `config.yaml` and MJCF actuator block).

### 3.2 Joint Breakdown (v2 Right Hand)
| Group | Joint Name | ROM (degrees) | Notes |
|-------|-----------|---------------|-------|
| Wrist | `wrist` | −65 to +35 | Pitch/flex |
| Thumb | `thumb_cmc` | −45 to +33 | Carpometacarpal |
| | `thumb_abd` | −18 to +55 | Abduction (oblique axis) |
| | `thumb_mcp` | −25 to +100 | MCP flex |
| | `thumb_dip` | −15 to +107 | IP flex |
| Index | `index_abd` | −30 to +25 | Abduction (Z axis) |
| | `index_mcp` | −25 to +100 | MCP flex |
| | `index_pip` | −15 to +107 | PIP flex |
| Middle | `middle_abd` | −27 to +27 | Abduction |
| | `middle_mcp` | −25 to +100 | |
| | `middle_pip` | −15 to +107 | |
| Ring | `ring_abd` | −27 to +27 | |
| | `ring_mcp` | −25 to +100 | |
| | `ring_pip` | −15 to +107 | |
| Pinky | `pinky_abd` | −30 to +30 | |
| | `pinky_mcp` | −25 to +100 | |
| | `pinky_pip` | −15 to +107 | |

**Note:** DIP joints are implicit — coupled to PIP via 1:1 tendon coupling on the real hardware, not separately actuated. The MJCF does not expose a DIP DOF (the DP body is a child of IP with a fixed attachment).

### 3.3 Kinematic Tree (v2)
```
ForeArmStructure (base, fixed)
└── TopTower (wrist)
    └── Carpals (palm)
        ├── T-TP (thumb proximal plate)
        │   ├── R-T-AP (thumb abductor)
        │   │   ├── T-PP (thumb PP) [joint: t-cmc + t-abd]
        │   │   │   ├── T-DP [joint: t-mcp]
        │   │   │   │   └── (tip) [joint: t-pip]
        ├── I-AP (index abductor plate) [joint: i-abd]
        │   └── I-PP [joint: i-mcp]
        │       └── I-FingerTip [joint: i-pip]
        ├── M-AP [joint: m-abd] → M-PP → M-FingerTip
        ├── (Ring) R-AP [joint: r-abd] → R-PP → R-FingerTip  
        └── (Pinky) P-AP [joint: p-abd] → P-PP → P-FingerTip
```

### 3.4 Mesh Statistics
- v1: Separate `visual_*` and `collision_*` STL per phalanx, plus `_skin` variants
- v2: 42 STL files per hand (left or right), mix of visual and collision
- Mesh faces: main tower ~15,000 (visual), ~7,500 (collision); fingers ~500 each
- All meshes in **millimetres** (scaled ×0.001 in MJCF)
- URDF versions provided alongside MJCF for ROS/Isaac compatibility

### 3.5 Hardware Actuation Details
From `orca_core`:
- **Motors:** Dynamixel (v1: 3 Mbaud, v2: 1 Mbaud) OR Feetech (1 Mbaud)
- **Control mode:** `current_based_position` (default) — PD position control with current limiting
- **Max current:** 300 mA default
- **Calibration:** stall-detection — each joint is driven flex→extend at calibration current until stall is detected; the motor position at stall defines the hard limit. Gear ratios (`joint_to_motor_ratios`) are computed from commanded vs. observed motion.
- **Tensioning:** Physical procedure (ratchet spool winding) required after every assembly/disassembly since tendons stretch and can go slack

---

## 4. SDK / Control API

### 4.1 Core abstraction layer (`orca_core`)
```python
from orca_core import OrcaHand
hand = OrcaHand()
hand.connect()
hand.init_joints()          # calibrates if needed, moves to neutral

# Command joint positions (radians):
hand.set_joint_positions({"index_mcp": 0.8, "index_pip": 1.2})

# Or full OrcaJointPositions object:
from orca_core import OrcaJointPositions
pos = OrcaJointPositions({"wrist": -0.3, "thumb_mcp": 0.5, ...})
hand.set_joint_positions(pos, num_steps=50, step_size=0.01)  # interpolated

hand.get_joint_position()   # → OrcaJointPositions
hand.set_neutral_position()
hand.disconnect()
```

### 4.2 Interpolation
`BaseHand._linear_waypoints_to()` generates linear interpolated waypoints between current and target pose. `step_size` is the sleep duration between waypoints — effectively a soft trajectory planner.

### 4.3 Touch Variant (`OrcaHandTouch`)
`OrcaHandTouch` extends `OrcaHand` with a Paxini tactile sensor array on the fingertips, providing per-taxel force readings via `get_tactile_forces()` / `get_tactile_taxels()`.

### 4.4 Simulation API (`orca_sim`)
```python
from orca_sim import OrcaHandRight
env = OrcaHandRight(render_mode="human", version="v2")
obs, info = env.reset()
# obs = np.concatenate([qpos, qvel])   → shape (34,) for single hand
# action_space = Box(17,) with joint ctrlrange limits
obs, reward, terminated, truncated, info = env.step(action)
env.close()
```
`frame_skip=5` by default — each `step()` call advances MuJoCo by 5 physics substeps.

---

## 5. Lessons for ARMSMITH (8 Concrete Takeaways)

### Lesson 1 — MJCF is the right format; URDF exists too
ORCA ships **both** MJCF and URDF (`orcahand_description/v2/models/mjcf/` and `.../urdf/`). The URDF is auto-generated from the same Fusion360 model. For ARMSMITH's Unity pipeline, the **URDF is the correct entry point**: Unity's URDF Importer (`com.unity.robotics.urdf-importer`) can directly load `.urdf` files and reconstruct ArticulationBody chains. Files: `v2/models/urdf/orcahand_right.urdf`.

**Action:** Add ORCA Hand v2 to ARMSMITH's arm catalogue using `orcahand_description/v2/models/urdf/orcahand_right.urdf` as input to Unity's URDF importer. The 42 STL meshes in `v2/models/assets/right/` become mesh colliders + MeshFilter renderers. Scale factor: `0.001` (mm→m, matches Unity's import scale).

### Lesson 2 — Dual-mesh collision strategy (adopt this)
ORCA separates **visual** meshes (high-poly, `contype=0`) from **collision** meshes (decimated, `contype=1`, with a separate "skin" layer for soft contact). In Unity terms: one `MeshFilter` for rendering, a separate simplified `MeshCollider` for physics. Currently ARMSMITH uses procedural geometry for both, which is fine for simple arms — but when importing a dexterous hand, you want:
- Visual: full STL
- Collision: decimated STL (or convex hull approximation via Unity's mesh collider "convex" flag)
- Soft contact zones: small capsule/box approximations at fingertip pads

**Action:** When building `BuildFromKinematics()` for imported arms, create a `collision/` prefab variant with convex-hull colliders from the decimated STL files.

### Lesson 3 — Position actuators in sim, tendon coupling in hardware (don't model tendons in Unity)
ORCA's sim takes the pragmatic approach of commanding joint angles directly even though the real hardware is tendon-driven. The `joint_to_motor_ratios` calibration step maps the real nonlinear tendon transmission to a scalar, which is "good enough" for sim-to-real transfer for position control.

**For ARMSMITH:** ArticulationBody joints driven with `ArticulationDrive` (target position + stiffness/damping) is **exactly analogous** to ORCA's MuJoCo `<position kp="2.0">` actuator. No tendon simulation needed at game scale.

The PIP/DIP coupling (curling a finger flexes both phalanges together) can be achieved in Unity with a simple constraint: `DIP_angle = k × PIP_angle` (k ≈ 0.67 based on ORCA's joint ROMs). This gives the biological appearance without simulating actual tendons.

### Lesson 4 — Joint ROM table is directly usable
ORCA's `config.yaml` provides exact joint ROMs in degrees, immediately usable as `ArticulationBody.xDrive.lowerLimit`/`upperLimit`:

| Joint | Unity ArticulationBody limit |
|-------|------------------------------|
| MCP flex | [−25°, +100°] |
| PIP flex | [−15°, +107°] |
| Abduction | [±27°–30°] |
| Thumb CMC | [−45°, +33°] |
| Thumb abduction | [−18°, +55°] |
| Wrist | [−65°, +35°] |

**Action:** These numbers feed directly into ARMSMITH's `ArmConfig.cs` joint limit fields.

### Lesson 5 — Scene composition via XML include (use Unity Prefab nesting)
ORCA composes scenes from modular XML includes: `scene.xml` (environment) + `options.xml` (physics defaults) + `hand_model.mjcf` (kinematics). In Unity this maps cleanly to **Prefab nesting**:
- `Workshop_Environment.prefab` (table, lights, floor)
- `OrcaHand_Right.prefab` (ArticulationBody chain, imported from URDF)
- `Task_CubeOrientation.prefab` (free cube + target indicator)

The ORCA `scene_right_cube_orientation.xml` is a perfect blueprint for ARMSMITH's "Task" layer.

### Lesson 6 — Calibration procedure → ARMSMITH's "Setup" minigame
ORCA's `calibrate()` routine drives each joint to its flex/extend stall limits to auto-discover the hardware ROM. This is a compelling **gameplay mechanic** for ARMSMITH: before a run, the player "calibrates" their designed arm by driving each joint to its limit (auto or manual), confirming the physical build matches the spec. Errors (e.g. a joint that jams early) feed into a fitness penalty.

The 16-step calibration sequence (pair flex/extend for each joint group) could be presented as an animated setup sequence with pass/fail indicators.

### Lesson 7 — Gymnasium API maps to ARMSMITH's Evolution layer
`orca_sim`'s Gymnasium-style `reset()/step(action)` loop is structurally identical to how ARMSMITH's EvolutionTrainer should work in headless batch mode:

```csharp
// ARMSMITH equivalent of orca_sim's Gymnasium API:
void Reset() { /* reset arm + task */ }
float[] Step(float[] action) { /* apply joint targets, step physics, return obs */ }
float GetReward() { /* pick-place fitness */ }
```

The ORCA `OrcaHandRightCubeOrientation.reset()` with `nominal_reset_options()` vs `randomized_reset_options()` is exactly the **deterministic vs. randomized reset** pattern ARMSMITH needs for fair fitness comparisons within a generation.

**Action:** Implement `ITaskEnvironment` interface in ARMSMITH with `Reset(bool randomize)` and `Step(JointAction)` → `(Observation, Reward, Done)`.

### Lesson 8 — Tendon "tensioning" as a maintenance mechanic (sim-to-real insight)
The physical ORCA hand requires regular tendon tensioning (ratchet spool procedure, documented in `orca_core/docs/.../initial-tensioning-and-calibration.md`). This is a major sim-to-real gap: the sim has perfect stiffness, but the real hand's tendons stretch, creating slack.

**ARMSMITH implication:** When implementing the "real robot port" (REAL_ROBOT_PORT_SPEC.md), model tendon slack as a **degradation parameter** that increases over time and requires a calibration/tensioning task to reset. This adds authenticity and a maintenance loop to the gameplay.

---

## 6. Integration Path: ORCA Hand into ARMSMITH

### Can we load the ORCA Hand as an ARMSMITH arm variant?
**Yes.** Here is the conversion path:

#### Step 1 — URDF import (Unity Editor)
```
Package: com.unity.robotics.urdf-importer (0.5.2+)
File: orcahand_description/v2/models/urdf/orcahand_right.urdf
Scale: 0.001 (mm→m)
Mesh path: resolve relative to urdf → v2/models/assets/right/*.stl
```
The URDF importer creates an ArticulationBody hierarchy matching the kinematic tree. Each `<joint>` becomes an `ArticulationBody` with the appropriate DOF (revolute hinge).

#### Step 2 — ArmConfig.cs population
After import, populate `ArmConfig` from the URDF joint data:
- 17 joints → 17 `ArticulationBody` components
- Joint limits from `<limit lower=... upper=...>` URDF attributes (in radians)
- Drive stiffness/damping: start with kp=2.0 / kd=0.5 (matching MJCF defaults)

#### Step 3 — Gripper / end-effector mapping
ORCA has no binary gripper — it's a full dexterous hand. For ARMSMITH's pick-and-place task:
- "Close" = drive all finger MCPs to ~80°, PIPs to ~100°, abductions to 0°
- "Open" = neutral position from `config.yaml`

This can be implemented as `GripperMode.Dexterous` in `Gripper.cs`, replacing the simple jaw-gripper with named pose interpolation.

#### Step 4 — STL meshes for visual rendering
The 42 STL files (left or right) drop directly into `Assets/Meshes/OrcaHand/v2/right/`.
Unity's `StlImporter.cs` (already in the project) can import them at runtime or Editor-time.
The dual mesh approach (visual + collision STL) maps to:
- `MeshFilter` + `MeshRenderer`: visual STL
- `MeshCollider` (convex): collision STL

#### Step 5 — Catalogue entry
Add to `ProceduralArm.cs` / `BuilderPanel.cs` a new arm type `ArmType.OrcaHand_Right` that:
1. Loads the imported URDF prefab
2. Registers its 17 joint `ArticulationBody` refs
3. Exposes the finger group controls in the Designer panel

#### Step 6 — Export compatibility
STL export from ARMSMITH already works for solid meshes. For the ORCA hand:
- The `orcahand_description` STLs are the source-of-truth meshes
- `SaveSystem.cs` should record the `ArmType.OrcaHand_Right` + calibration YAML as the export descriptor
- MJCF/URDF path for real-robot targeting: already exists in the repo

### Estimated effort
| Task | Estimate |
|------|----------|
| URDF import + ArticulationBody chain setup | 2–3 hours |
| ArmConfig population from URDF | 1 hour |
| Gripper.cs dexterous mode | 2 hours |
| STL mesh wiring + materials | 1–2 hours |
| Designer UI: per-finger control groups | 3–4 hours |
| **Total** | **~10 hours** |

---

## 7. Key Differences: ORCA Sim vs. ARMSMITH

| Aspect | ORCA (MuJoCo) | ARMSMITH (Unity / PhysX) |
|--------|--------------|--------------------------|
| Physics engine | MuJoCo (reduced-coordinate) | PhysX ArticulationBody (featherstone) |
| Contact model | `condim=3`, friction coefficients | PhysX contact with PhysicsMaterial |
| Actuator | `<position kp=2.0>` — pure proportional | ArticulationDrive with stiffness + damping |
| Tendon sim | Not modelled | Not needed |
| RL framework | Gymnasium + custom rewards | Unity ML-Agents (Phase 4) |
| Scene format | XML includes | Unity Prefab + SceneManager |
| Observation space | `[qpos, qvel]` = 34D (single hand) | Custom sensor hub via `SensorHub.cs` |
| Reward shaping | Task-specific (`task_envs.py`) | `TaskManager.cs` scoring |
| Real-robot bridge | `orca_core` SDK (Python) | `scripts/realbot/armsmith_lerobot.py` |

Both engines use the **same fundamental abstraction**: position targets → joint angles → mesh poses → contact forces. The key parameter to match for sim-to-real is the stiffness/damping ratio (PhysX ArticulationDrive `stiffness` ≈ MuJoCo `kp`, ArticulationDrive `damping` ≈ MuJoCo `damping`).

---

## 8. Useful File References (local)

```
orcahand_description/v2/models/urdf/orcahand_right.urdf        ← import into Unity
orcahand_description/v2/models/mjcf/orcahand_right.mjcf        ← actuator / contact reference
orcahand_description/v2/models/mjcf/orcahand_right_body.xml    ← 17 joint definitions + limits
orcahand_description/v2/models/assets/right/*.stl              ← 42 STL meshes
orca_core/orca_core/models/v2/orcahand_right/config.yaml       ← ROM + motor map + calibration seq
orca_sim/src/orca_sim/envs.py                                  ← Gymnasium sim loop
orca_sim/src/orca_sim/task_envs.py                             ← Cube orientation task env
orca_sim/src/orca_sim/scenes/v2/scene_right_cube_orientation.xml  ← scene XML blueprint
orca_sim/src/orca_sim/models/v2/assets/options.xml             ← physics defaults
orca_core/orca_core/base_hand.py                               ← high-level joint API
orca_core/orca_core/hardware_hand.py                           ← motor + calibration + tension
orca_core/orca_core/calibration.py                             ← calibration data model
orca_core/docs/pages/getting-started-docs/                     ← calibration/tensioning docs
```

---

## 9. Summary

The ORCA Hand is a **17-DOF tendon-driven anthropomorphic robotic hand** from ETH Zurich's Soft Robotics Lab (2025), MIT-licensed, specifically designed for affordable, repairable, research-grade dexterous manipulation. Its simulation ecosystem is built entirely on **MuJoCo + Gymnasium**, with:

- **MJCF/URDF models** (both provided) ready for Unity URDF Importer
- **Direct position control** in sim (no tendon modelling), calibrated gear ratios for real hardware
- **Dual collision mesh** strategy (visual + decimated collision STLs)
- **Modular XML scene composition** (direct parallel to Unity Prefab nesting)
- **Gymnasium API** that maps 1:1 to ARMSMITH's planned `ITaskEnvironment` interface
- A **16-step automated calibration** procedure that could be ARMSMITH's setup minigame

The ORCA hand's models are drop-in additions to ARMSMITH's arm catalogue via the URDF import path, and its kinematics/ROMs provide ground-truth numbers for ARMSMITH's dexterous gripper evolution experiments.
