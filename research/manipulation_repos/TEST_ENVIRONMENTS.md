# Test Environments for Robot Arm Manipulation — Research Report

> Compiled: 2026-05-30  
> Target: Unity 6, URP, ArticulationBody physics  
> Purpose: Reference for building simulation test scenes in a game where players design/train robot arms for pick-and-place tasks

---

## 6-Bullet Summary

1. **Standard workspace dimensions are tightly converged across frameworks**: robosuite, Gymnasium-Robotics (Fetch), and ManiSkill all use a table roughly **0.8 m × 0.8 m × 0.05 m** (surface slab) at a height of **0.42–0.80 m** from ground. Object spawn regions are kept to a **±0.15 m** radius patch centred in front of the robot to stay comfortably within workspace reach.

2. **Domain randomisation (DR) is a first-class citizen**, not an afterthought: every production framework randomises object XY-spawn within a fixed box, object Z-rotation (full 360°), goal position, and optionally lighting intensity, texture, and object mass/friction. Isaac Lab additionally randomises controller gains and motor dead-zones. DR is what transforms a brittle sim-policy into one that transfers to real hardware.

3. **The tray-to-tray scenario is the canonical "medium difficulty" pick-and-place test**: two bins/trays (~0.15 m × 0.12 m × 0.04 m) sit side-by-side on the table; an object spawns in Tray A; the agent must lift, translate, and deposit it in Tray B. Success threshold is object centroid within **±0.03–0.05 m** of the target tray centre AND object resting on tray floor (Z-height ≤ tray\_wall\_height + object\_half\_height + 0.01 m tolerance).

4. **Camera layout follows a 3-viewport standard in real-system consoles** (Mobile ALOHA, LeRobot, Isaac): a large primary 3rd-person view, a small wrist/gripper-cam inset (bottom-right), and a small overhead/bird's-eye cam inset (top-right or left panel). This arrangement covers workspace blind spots and provides the agent's pixel observations directly.

5. **Joint-state UI conventions are well-established**: a horizontal strip of labelled angle/torque readouts per joint (colour-coded by joint index), a gripper-state indicator (open ↔ closed with width in mm), and an end-effector pose panel (XYZ + RPY or quaternion). Isaac Lab / robosuite additionally overlay live trajectory ghosts (translucent grey arm at goal pose) and workspace-boundary meshes.

6. **For the Unity game**, the eight scenarios below form a natural difficulty ladder buildable entirely with primitive shapes (cubes, cylinders, spheres, trays). The easiest (reach-touch) can be solved by a 2-DOF arm; the hardest (sort-by-color) requires planning over multiple picks. All share the same table, robot, and camera layout, making scene construction modular.

---

## Part 1 — Standard Manipulation Test Scenes

### 1.1 Convergence Across Frameworks

The following table summarises the physical workspace layout used by the five major frameworks:

| Framework | Table Size (L × W × H slab) | Table Height (floor→surface) | Object Spawn Region | Goal Marker |
|---|---|---|---|---|
| **Gymnasium-Robotics / Fetch (MuJoCo)** | ~1.25 m × 0.75 m (robot on end) | 0.42 m | ±0.15 m XY from gripper home | Red/green translucent sphere, 0.05 m radius |
| **robosuite (MuJoCo)** | 0.8 m × 0.8 m × 0.05 m | 0.80 m | Uniform within table surface | No persistent marker; success checked programmatically |
| **robosuite BinsArena** | Two bins: 0.39 m × 0.49 m × 0.82 m total | 0.82 m | Inside bin bounds | Bin walls act as implicit constraint |
| **ManiSkill (PhysX GPU)** | Standard tabletop, ~0.6–1.0 m visible | ~0.6 m | ±0.10 m XY square | Green translucent sphere |
| **Isaac Lab (PhysX)** | ~0.8 m × 0.8 m | ~0.80 m | ±0.15 m XY | Coloured sphere at goal, 0.025 m radius |
| **RLBench (CoppeliaSim)** | Franka table-mount, ~0.5 m reach radius | ~0.78 m | Task-specific waypoints | Red target ball or zone on surface |

**Key takeaway:** a Unity test bench should use a table **0.8 m × 0.8 m**, surface at **0.80 m** above floor (matching robosuite standard and typical lab bench height), with a spawn zone of **±0.12 m XY** centred at the robot's nominal reaching point (~0.5 m in front of the arm base, midway along the table).

### 1.2 Anatomy of a Good Repeatable Test Scene

A production-grade test scene has these invariant components:

```
┌────────────────────────────────────────────────────────┐
│  [Overhead Cam]                                        │
│                                                        │
│   ┌──────────────────────────────────────────────┐     │
│   │           TABLE SURFACE (0.8 × 0.8 m)        │     │
│   │   [Spawn Zone A]        [Goal Zone / Tray B]  │     │
│   │   ±0.12 m XY            ±0.12 m XY           │     │
│   │         •object                ○target        │     │
│   └──────────────────────────────────────────────┘     │
│                                                        │
│   ARM BASE                                             │
│   (0.0, 0.0, 0.80 m, fixed)                            │
│                                                        │
│  [Front Cam 45°]            [Wrist Cam]                │
└────────────────────────────────────────────────────────┘
```

**Required elements:**

| Element | What It Does | Implementation Notes |
|---|---|---|
| **Ground plane + table** | Physics foundation | Collider with friction ~0.6–1.0 |
| **Spawn bounds box** (trigger volume) | Hard boundary for randomisation | Invisible box, 0.24 × 0.24 m XY, centred on table |
| **Goal zone marker** | Visual target for human observer | Translucent disc or sphere; driven by `TargetPose` component |
| **Object** | The manipulandum | Rigidbody + collider; mass 50–200 g |
| **Workspace boundary mesh** | Shows arm reach limit | Translucent hemisphere or cylinder, radius = max arm reach |
| **Reset logic** | Reproducibility | On episode end: respawn object at new sampled XY, zero all joint velocities |
| **Success checker** | Episode termination | Distance from object centroid to goal < threshold, velocity < 0.2 m/s |
| **Episode timer** | Enforces time budget | Default 50–200 steps @ 25 Hz = 2–8 seconds real-time |

### 1.3 Domain Randomisation

Every mainstream framework applies at least these randomisation tiers:

**Tier 1 — Always applied (every episode reset):**
- Object XY spawn: uniform in ±0.12 m square
- Object Z-rotation: uniform [0, 2π]
- Goal XY position: uniform in ±0.12 m square (if free goal)

**Tier 2 — Common for sim-to-real work:**
- Object colour/texture: uniform sample from a texture atlas or HSV range
- Lighting: intensity × [0.7, 1.3], colour temperature shift ±500 K
- Table friction: ×[0.8, 1.2] of nominal
- Object mass: ×[0.8, 1.2] of nominal

**Tier 3 — Advanced (Isaac Lab FORGE, RLBench visual DR):**
- Controller gains: ±20%
- Camera pose: ±2° angular noise, ±5 mm position noise
- Background textures: random from DistractorDB

**Unity implementation approach:** a `SceneRandomiser` MonoBehaviour reads a `DomainRandomisationConfig` ScriptableObject and modifies `PhysicsMaterial.dynamicFriction`, material colour, and `Light.intensity` values each time `EpisodeManager.ResetEpisode()` is called.

---

## Part 2 — Tray-to-Tray Transfer Scenario

### 2.1 Setup

This is the recommended **first scenario** for the game, and maps directly onto the robosuite `PickAndPlace` and `BinsArena` conventions.

**Scene layout (top-down view):**

```
         ← 0.80 m table width →

    Y+
    │   [ARM BASE]
    │         (0.0, 0.0, 0.80 m)
    │
    │   ┌──────────────────────────────────┐  table surface
    │   │                                  │
    │   │  ┌──────────┐  ┌──────────┐      │
    │   │  │  TRAY A  │  │  TRAY B  │      │
    │   │  │  (red)   │  │ (green)  │      │
    │   │  │ 0.15×0.12│  │ 0.15×0.12│      │
    │   │  │  h=0.04  │  │  h=0.04  │      │
    │   │  └──────────┘  └──────────┘      │
    │   │                                  │
    │   └──────────────────────────────────┘
    └──────────────────────────────────────── X+
```

### 2.2 Concrete Specification

```
TRAY-TO-TRAY TRANSFER — CANONICAL SPEC
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
TABLE
  Size:          0.80 m × 0.80 m × 0.05 m (L × W × H slab)
  Surface Z:     0.825 m  (floor → top of table surface)
  Friction:      0.8 (nominal, randomised ×[0.8, 1.2])

ROBOT BASE
  Position:      (0.0, 0.0, 0.825 m)  — mounted flush on table
  OR floor-mount: base at (0.0, 0.0, 0.0), arm reaches across

TRAY A  (source — red)
  Size:          0.150 m (X) × 0.120 m (Y) × 0.040 m (Z walls)
  Inner floor Z: 0.825 m  (sits on table surface)
  Centre:        (−0.20 m, +0.45 m, 0.825 m) in world space
  Wall thickness: 0.008 m
  Material:      Red, matte, friction 0.5

TRAY B  (target — green)
  Size:          0.150 m (X) × 0.120 m (Y) × 0.040 m (Z walls)
  Inner floor Z: 0.825 m
  Centre:        (+0.20 m, +0.45 m, 0.825 m) in world space
  Material:      Green semi-transparent, friction 0.5

OBJECT
  Type:          Cube, 0.040 m × 0.040 m × 0.040 m
  Mass:          0.100 kg
  Friction:      0.6
  Spawn location: Random within Tray A inner bounds
    X:  [−0.20 − 0.045, −0.20 + 0.045]  = [−0.245, −0.155] m
    Y:  [+0.45 − 0.040, +0.45 + 0.040]  = [+0.410, +0.490] m
    Z:  0.825 m + 0.020 m (object half-height) = 0.845 m
  Spawn rotation: Random Z-rotation ∈ [0, 2π]

SUCCESS CRITERIA  (all must hold simultaneously for 1 step)
  1. Object centroid XY distance to Tray B centre < 0.05 m
  2. Object Z position ≤ (tray_floor_Z + wall_height + obj_half + 0.012 m)
       = 0.825 + 0.040 + 0.020 + 0.012 = 0.897 m   (resting, not flying)
  3. Object Z position ≥ (tray_floor_Z + obj_half − 0.005 m)
       = 0.840 m  (must be inside tray, not just above it)
  4. Object linear speed < 0.05 m/s  (settled)

FAILURE CRITERIA  (any one triggers episode end)
  1. Object leaves table surface  (Z < 0.60 m  — fell off edge)
  2. Episode timer exceeds 200 steps @ 25 Hz  (8 seconds)
  3. Collision force on arm link > 20 N  (optional safety limit)

RESET LOGIC
  1. Set all ArticulationBody joint velocities to 0
  2. Set all joint positions to home pose
  3. Sample new object spawn within Tray A inner bounds
  4. Place object at sampled position, random Z-rotation
  5. Apply domain randomisation (DR tier 1 always, tier 2 if enabled)
  6. Advance physics 5 frames before unfreezing agent

REWARD (dense, recommended for training)
  Each step:
    r_reach   = 0.25 × tanh(3 / ||EE_pos − obj_pos||)    # approach reward
    r_lift    = 0.25 × clamp(obj_Z − 0.845, 0, 0.10) / 0.10  # lift reward
    r_carry   = 0.25 × tanh(3 / ||obj_pos_XY − trayB_XY||)   # proximity to goal
    r_success = 1.0 if success_criteria_met else 0.0
  Total step reward = r_reach + r_lift + r_carry + r_success
  Note: remove r_reach once object is grasped to avoid reward hacking

CAMERAS  (see Part 4 for UI layout)
  front_cam:     pos=(0.0, −0.50, 1.30), aim=(0.0, 0.45, 0.85), FOV=55°
  overhead_cam:  pos=(0.0, 0.45, 1.80), aim=(0.0, 0.45, 0.83), FOV=60°
  wrist_cam:     attached to gripper base, aim along −Z of gripper frame, FOV=80°
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 2.3 Visual Markers

- **Tray A**: opaque red walls; a subtle pulsing red ring on the inner floor at episode start, dims once object is picked up
- **Tray B**: translucent green walls (alpha 0.5) so wrist cam can see through; a persistent green circular indicator (radius 0.04 m) on the inner floor
- **Object**: bright yellow cube, sufficient contrast against both tray colours
- **Success flash**: both tray walls briefly turn white + particle burst when success condition fires

---

## Part 3 — Scenario Difficulty Ladder (8 Scenarios)

All scenarios share the same base scene (table, robot, 3-camera layout). They are buildable with Unity primitive shapes and trigger volumes.

---

### Scenario 1 — Reach & Touch ⭐ (Difficulty: 1/8)

**Description:** Move end-effector to touch a stationary sphere. No grasp needed.

| Property | Value |
|---|---|
| **Objects** | 1 × sphere (r=0.04 m, static Trigger) |
| **Spawn region** | ±0.12 m XY on table, fixed Z = table_surface + r |
| **Goal spawn** | Same as object spawn |
| **Success condition** | Distance(EE_tip, sphere_centre) < 0.05 m for 1 step |
| **Episode length** | 50 steps @ 25 Hz (2 sec) |
| **Reward** | Dense: −||EE − sphere|| per step; +1.0 on success |
| **Difficulty factors** | No grasp, no object dynamics, large tolerance |
| **Why easy** | Only requires reaching IK, no contact needed |

**Reward shaping detail:**
```
r = 1.0 − tanh(5 × distance)   # smooth approach signal in [0, 1]
```

---

### Scenario 2 — Push to Zone ⭐⭐ (Difficulty: 2/8)

**Description:** Push a cube (no grasp) until it crosses into a flat circular target zone.

| Property | Value |
|---|---|
| **Objects** | 1 × cube (0.04 m), 1 × flat disc zone (decal, r=0.08 m) |
| **Cube spawn** | Random ±0.10 m XY |
| **Zone spawn** | Fixed at cube_start_XY + [0.20, 0] to force leftward push |
| **Success condition** | Cube XY centroid within 0.08 m of zone centre; cube still on table |
| **Episode length** | 80 steps |
| **Reward** | Dense: −||cube_XY − zone_XY|| per step |
| **Difficulty factors** | Must plan push direction; cube can veer off course |

---

### Scenario 3 — Lift Cube ⭐⭐ (Difficulty: 2/8)

**Description:** Grasp a cube and lift it above a height threshold. No specific placement goal.

| Property | Value |
|---|---|
| **Objects** | 1 × cube (0.04 m, 0.10 kg) |
| **Spawn region** | ±0.10 m XY, random Z-rotation |
| **Success condition** | Cube Z > table_surface + 0.10 m AND robot static AND cube not grasped (release ok) |
| **Episode length** | 50 steps |
| **Reward** | Stage-gated: +0.3 contact bonus, +0.5 × lift_height / 0.10, +1.0 on success |
| **Difficulty factors** | Must close gripper around object (grasp planning); gravity wants to pull cube back |

**ManiSkill reference:** `PickCube-v1` / `LiftCube-v1` — identical spec.

---

### Scenario 4 — Pick and Place ⭐⭐⭐ (Difficulty: 4/8)

**Description:** Grasp the cube and move it to a target sphere marker anywhere on the table surface or above it.

| Property | Value |
|---|---|
| **Objects** | 1 × cube (0.04 m), 1 × goal marker sphere (translucent, r=0.03 m) |
| **Cube spawn** | Random ±0.12 m XY from gripper home |
| **Goal spawn** | Random ±0.12 m XY; Z randomly in [table_surface, table_surface + 0.30 m] to allow aerial goals |
| **Success condition** | Cube centroid within 0.025 m of goal; robot static (joints < 0.2 rad/s) |
| **Episode length** | 100 steps |
| **Reward** | `r = -||cube - goal||` (dense) or sparse ±1 |
| **Difficulty factors** | Variable goal height requires full 3D arm control; small 0.025 m tolerance |

**Gymnasium-Robotics reference:** `FetchPickAndPlace-v3` — identical spec. Sparse threshold = 0.05 m.

---

### Scenario 5 — Tray-to-Tray Transfer ⭐⭐⭐ (Difficulty: 5/8)

*See Part 2 for the full spec.*

**Summary:** Pick cube from Tray A (red), deposit in Tray B (green). Success = cube resting in tray B, settled.

| Key differentiator from Scenario 4 |
|---|
| Destination is a **constrained volume** (tray walls), not an open-space goal marker |
| Requires correct approach angle so arm doesn't knock the tray |
| Harder grasping geometry (picking from inside a shallow tray) |

---

### Scenario 6 — Stack 2 Cubes ⭐⭐⭐⭐ (Difficulty: 6/8)

**Description:** Pick Cube A and stack it on top of Cube B (which remains on the table).

| Property | Value |
|---|---|
| **Objects** | 2 × cubes (0.04 m each, different colours: red + green) |
| **Cube B** | Fixed or lightly placed at table centre ±0.05 m XY |
| **Cube A** | Random spawn ≥ 0.10 m from Cube B |
| **Success condition** | Cube A centre Z within 0.005 m of (cubeB_Z + cube_side = 0.04+0.04=0.08 m above table); Cube A static; Cube A NOT grasped |
| **Episode length** | 100 steps |
| **Reward** | Stage-gated: reach A (+0.2), grasp A (+0.3), lift (+0.2), align XY (+0.2), place (+1.0) |
| **Difficulty factors** | Precise XY alignment (4 cm tolerance); release must be gentle; Cube A must not knock Cube B |

**ManiSkill reference:** `StackCube-v1` — same tolerances.

---

### Scenario 7 — Drop in Bin ⭐⭐⭐⭐ (Difficulty: 6/8)

**Description:** Pick up a sphere from the table surface and drop it into a deep bin (cup/container). The bin has a narrow opening.

| Property | Value |
|---|---|
| **Objects** | 1 × sphere (r=0.025 m), 1 × cylindrical bin (inner r=0.055 m, depth=0.08 m) |
| **Sphere spawn** | Random ±0.10 m XY |
| **Bin position** | Fixed at table centre (slight randomisation ±0.03 m optional) |
| **Success condition** | Sphere centroid within bin horizontal bounds AND sphere Z < bin_rim_Z − 0.015 m AND sphere settled |
| **Episode length** | 120 steps |
| **Reward** | −||sphere_XY − bin_XY|| + lift bonus + containment bonus |
| **Difficulty factors** | Must approach bin opening correctly to avoid sphere bouncing out; narrow 3 cm margin |

---

### Scenario 8 — Sort by Color ⭐⭐⭐⭐⭐ (Difficulty: 8/8)

**Description:** Three cubes (red, green, blue) scattered on the table; three matching-colour target zones on the other side of the table. Each cube must end in its matching zone.

| Property | Value |
|---|---|
| **Objects** | 3 × cubes (0.04 m each), 3 × flat disc markers (r=0.07 m, colour-matched) |
| **Cube spawn** | Random ±0.10 m XY on one half of table; no collisions at spawn |
| **Zone positions** | Fixed or lightly randomised ±0.05 m on other half of table |
| **Success condition** | All 3 cubes: centroid within 0.06 m of matching zone, all settled simultaneously |
| **Episode length** | 300 steps |
| **Reward** | Partial credit: +0.33 per cube correctly placed and settled |
| **Difficulty factors** | Multi-step planning (3 sub-tasks); cubes can be knocked out of zones; agent must remember state; object recognition by colour |
| **Optional harder variant** | Randomise cube colours each episode so agent must read colour at runtime |

---

### Difficulty Summary Table

| # | Scenario | Primitives | Grasp? | Placement Constraint | Approx. Steps | Difficulty |
|---|---|---|---|---|---|---|
| 1 | Reach & Touch | 1 sphere | No | None (open space) | 50 | ⭐ |
| 2 | Push to Zone | 1 cube + 1 disc | No | Flat disc zone | 80 | ⭐⭐ |
| 3 | Lift Cube | 1 cube | Yes | Height threshold only | 50 | ⭐⭐ |
| 4 | Pick and Place | 1 cube + 1 marker | Yes | 0.025 m sphere (3D) | 100 | ⭐⭐⭐ |
| 5 | Tray-to-Tray | 1 cube + 2 trays | Yes | Tray volume (constrained) | 200 | ⭐⭐⭐ |
| 6 | Stack 2 Cubes | 2 cubes | Yes | On top of other cube | 100 | ⭐⭐⭐⭐ |
| 7 | Drop in Bin | 1 sphere + 1 cylinder | Yes | Inside bin opening | 120 | ⭐⭐⭐⭐ |
| 8 | Sort by Color | 3 cubes + 3 discs | Yes (×3) | 3 colour-matched zones | 300 | ⭐⭐⭐⭐⭐ |

---

## Part 4 — Displaying the Arm and Cameras: UI/Console Layout

### 4.1 The 3-Viewport Standard

Every production teleoperation and training console (Mobile ALOHA, LeRobot teleoperation dashboard, Isaac Lab viewer, robosuite mjviewer) converges on the same 3-panel camera arrangement:

```
┌─────────────────────────────────────────────┬───────────────────┐
│                                             │   OVERHEAD CAM    │
│                                             │   (bird's eye)    │
│                                             │   256 × 256 px    │
│         PRIMARY (SCENE) VIEW                ├───────────────────┤
│         (main 3D viewport)                  │   WRIST CAM       │
│         fills ~60% of total width           │   (gripper-eye)   │
│                                             │   256 × 256 px    │
│                                             │                   │
├──────────────────────────┬──────────────────┴───────────────────┤
│  JOINT STATE READOUT     │  END-EFFECTOR POSE PANEL             │
│  J1: ████░░  45.2°       │  X: +0.512 m   Roll:  +12.3°        │
│  J2: ██░░░░  −22.1°      │  Y: +0.384 m   Pitch: −45.0°        │
│  J3: ███░░░  +67.8°      │  Z: +0.923 m   Yaw:   +0.2°         │
│  ...                     │  Gripper: 0.031 m (OPEN 62%)         │
│  Torques shown if avail  │  Speed: 0.14 m/s                     │
└──────────────────────────┴──────────────────────────────────────┘
```

### 4.2 Camera Placement Conventions

**Primary scene camera (front/side view):**
- Position: ~0.5–0.7 m in front of robot base, elevated 45–60° above horizontal
- Focal target: table surface centre (aim point = scene origin at table height)
- FOV: 55–65° — wide enough to see whole table, narrow enough for depth cues
- robosuite uses `"frontview"` camera at `pos=(1.6, 0, 1.45)` aimed at `(0, 0, 0.65)`

**Overhead / bird's-eye camera:**
- Position: directly above workspace, 1.2–1.8 m above table surface
- Orientation: straight down (orthographic or narrow FOV perspective ~30°)
- Isaac Lab and RLBench use this as a secondary policy-observation camera
- LeRobot real-robot setups typically mount this camera on a gantry above the table

**Wrist camera:**
- Attached to the last link before the gripper, aimed along gripper approach axis
- FOV: 70–90° (wide-angle, close range)
- Resolution: typically 84 × 84 (training) or 256 × 256 (display)
- In the UI: shown at 256 × 256, bottom-right inset, with a crosshair overlay and gripper jaw edges highlighted

**Mobile ALOHA / LeRobot layout specifics:**
- Left panel: two camera feeds stacked vertically (wrist left, wrist right for bimanual, or wrist + overhead for single-arm)
- Right panel: primary front-view camera
- Below cameras: joint angles displayed as a strip of real-time line graphs (1 trace per joint), refreshed at control frequency
- Separate gripper state indicator: colour bar (green=open, red=closed) with numeric opening width in mm

### 4.3 Joint State Readout Panel

```
JOINT STATE PANEL (reference design from Isaac Lab / robosuite)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Joint  | Angle (°) | Vel (°/s) | Torque (Nm) | Limit bar
───────┼───────────┼───────────┼─────────────┼──────────────
J1 (base yaw)  | +45.2  | +1.2  | +0.3 Nm | [████░░░░░]
J2 (shoulder)  | −22.1  | −0.4  | −1.2 Nm | [░░░██░░░░]
J3 (elbow)     | +67.8  |  0.0  | +0.8 Nm | [░░░░████░]
J4 (forearm)   | −12.3  | +0.1  | −0.1 Nm | [░░░███░░░]
J5 (wrist P)   | +34.1  | +0.3  | +0.2 Nm | [░░░░░██░░]
J6 (wrist R)   | −89.0  | −0.2  | +0.0 Nm | [████░░░░░]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Gripper L: 0.031 m  |  Gripper R: 0.031 m  |  STATE: OPEN
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Colour convention used across frameworks:
- Joint within ±70% of limit: **green**
- Joint within 70–90% of limit: **amber**  
- Joint past 90% of limit: **red** (flash if exceeding limit)

### 4.4 Trajectory and Ghost Overlays

**Target/ghost pose visualisation (Isaac Lab, RLBench, MuJoCo viewer):**
- A semi-transparent copy of the arm mesh rendered at the **goal end-effector pose** (target ghost). Alpha ≈ 0.25–0.35, tinted light blue.
- In IK-controlled systems: shows the *desired* end-effector frame as a small RGB-axis triad (X=red, Y=green, Z=blue arrows, 0.05 m length)
- A thin white line from current EE to target EE shows the planning displacement

**Trajectory trace:**
- After each completed motion, a sequence of semi-transparent spheres (breadcrumb trail) shows the last N=50 EE positions. Fades from bright to transparent with age.
- Colour encodes time: recent positions = yellow/white, old positions = dark grey

**Contact/force visualisation:**
- At contact points, a red arrow pointing outward from the contact normal, scaled by force magnitude. 0.01 m per 1 N is typical.
- Isaac Lab renders contact forces as magenta vectors at each contacted ArticulationBody link

**Workspace boundary mesh:**
- A semi-transparent hemisphere or cylinder centred on arm base, radius = max arm reach (typically 0.6–0.85 m for a 6-DOF arm)
- Red tint on the near face (close to table), grey/blue tint on far face
- Rendered with backface culling off so it's visible from inside and outside

---

## Part 5 — Visual Conventions for Robot Arms

### 5.1 Joint Axis Gizmos

Standard convention (matches RViz, Isaac Lab, and Unity's own Articulation debug tools):
- Each revolute joint: a **ring** around the rotation axis in the joint's local Z colour
- Each prismatic joint: a **double-headed arrow** along the slide axis
- Axis colours (ROS/Unity standard):
  - **X axis** = Red
  - **Y axis** = Green  
  - **Z axis** = Blue
- Joint ring: a thin torus (major r = half the link length, minor r = 0.005 m), coloured by joint index (rainbow: J1=red, J2=orange, J3=yellow, J4=green, J5=cyan, J6=violet)

### 5.2 Link Coloring

Two schools of thought, both used in production:

**Scheme A — Material-based (photorealistic, robosuite/Isaac Lab style):**
- Links coloured as real robot materials: grey aluminium links, black motor housings
- Fingertips: bright contrasting colour (orange/yellow) for visibility
- This is what the player sees during gameplay

**Scheme B — Debug/index coloring (diagnostic mode):**
- Each link gets a distinct hue from a palette; index 0 = root/dark grey, increasing brightness
- Used in editor/debug overlays, can be toggled with a keyboard shortcut

### 5.3 End-Effector Frame Display

Always rendered when in debug mode:
```
EE frame triad at the gripper tip:
  X (red arrow, 0.05 m):   gripper approach lateral
  Y (green arrow, 0.05 m): gripper approach lateral  
  Z (blue arrow, 0.05 m):  gripper approach axis (into object)
  
  + small label "EE" in white text, always faces camera
```

In addition, a **gripper opening cone** — two thin lines showing the jaw spread direction — makes it obvious when the gripper is open vs closed. Distance between cone endpoints = gripper_opening_width.

### 5.4 Target Pose Ghost

```
Target ghost spec:
  Mesh:        Same arm mesh as live robot
  Material:    Unlit, colour = (0.4, 0.7, 1.0, 0.25 alpha)  — light blue
  Rendering:   Always on top (no depth write) OR depth-tested (configurable)
  Update rate: Updated every planning tick (~5 Hz) not every physics frame
  EE highlight: The target ghost's gripper is rendered at full alpha=0.8
```

The ghost should **not** respond to physics — it is a pure visual indicator of where the planner wants the arm to be, driven by the target position data from the scenario.

### 5.5 Workspace Bounds Visualization

```
Workspace boundary mesh spec:
  Type:         Hemisphere (radius = arm_reach, flat face down)
  Position:     Arm base position
  Material:     (0.9, 0.5, 0.2, 0.07 alpha), double-sided, no shadow
  Inner torus:  Thin torus at r = min_reach (if arm has a min reach, e.g. 0.1 m)
  Grid lines:   Optional 8-sector angular grid lines, alpha 0.15
```

**Reachability heat map (advanced):** A precomputed texture on the workspace hemisphere surface that shows dense sampling of reachable IK solutions. Green = highly reachable, yellow = marginal, dark = unreachable. This is baked at design time per arm configuration.

---

## Part 6 — Implementation Notes for Unity 6 / URP / ArticulationBody

### 6.1 ArticulationBody Physics Notes

- ArticulationBody joints map cleanly to the Fetch/Panda model: `ArticulationJointType.RevoluteJoint` for all rotational joints
- Set `ArticulationBody.matchAnchors = true` for all links; joint limits via `ArticulationReducedSpace`
- For gripper: use two `PrismaticJoint` ArticulationBodies with symmetric limits ±0.04 m
- Solver iterations: minimum 10 for stable grasping (Unity default 6 is often not enough)
- Fixed physics timestep: 0.004 s (250 Hz) → apply control actions every 10 steps = 25 Hz, matching Fetch standard

### 6.2 Scene Hierarchy Suggestion

```
SceneRoot
├── Environment
│   ├── Floor (MeshCollider, no ArticulationBody)
│   ├── Table (MeshCollider, Rigidbody kinematic)
│   │   ├── TrayA (Rigidbody kinematic)
│   │   └── TrayB (Rigidbody kinematic)
│   └── Lights (3-point lighting rig)
├── Robot
│   ├── Base (ArticulationBody, isRoot=true)
│   ├── Link1 ... Link6
│   └── Gripper
│       ├── FingerL (ArticulationBody, PrismaticJoint)
│       └── FingerR (ArticulationBody, PrismaticJoint)
├── Objects
│   └── ManipulandumCube (Rigidbody, 0.1 kg, BoxCollider 0.04 m)
├── Cameras
│   ├── FrontCamera
│   ├── OverheadCamera
│   └── WristCamera (child of Gripper transform)
├── Markers
│   ├── GoalMarkerTrayB (visual only, no collider)
│   └── WorkspaceBoundsMesh (visual only)
└── UI
    ├── CameraViewport (URP RenderTexture → RawImage)
    ├── JointStatePanel
    └── EpisodeInfoPanel
```

### 6.3 Camera Rendering in URP

- Use `UniversalAdditionalCameraData` with `CameraRenderType.Overlay` for the wrist and overhead inset cameras
- Or use separate `RenderTexture` assets (256×256, RGB24) for each camera and display via `UI.RawImage`
- The wrist camera should set `Camera.nearClipPlane = 0.01 m` (close-up objects)
- Use `LayerMask` to exclude UI elements from the robot simulation cameras

### 6.4 Domain Randomisation in Unity

```csharp
// Minimal DR MonoBehaviour example
public class EpisodeRandomiser : MonoBehaviour {
    [Range(0f, 1f)] public float drStrength = 1.0f;
    
    void ResetEpisode() {
        // Tier 1: object placement
        var spawnX = Random.Range(-0.12f, 0.12f);
        var spawnY = Random.Range(-0.12f, 0.12f);
        manipulandum.transform.localPosition = new Vector3(spawnX, 0.02f, spawnY);
        manipulandum.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        
        // Tier 2: lighting (scaled by drStrength)
        mainLight.intensity = 1.0f + drStrength * Random.Range(-0.3f, 0.3f);
        
        // Tier 2: friction
        var mat = tablePhysicsMaterial;
        mat.dynamicFriction = 0.8f * (1f + drStrength * Random.Range(-0.2f, 0.2f));
    }
}
```

---

## Sources and References

| Framework | Version consulted | Key URLs |
|---|---|---|
| Gymnasium-Robotics / Fetch | v1.x | robotics.farama.org/envs/fetch/ |
| robosuite | v1.5 | robosuite.ai/docs |
| ManiSkill | v3 | maniskill.readthedocs.io |
| Isaac Lab | v3.x (2026) | isaac-sim.github.io/IsaacLab |
| RLBench | v1.2 | github.com/stepjam/RLBench |
| LeRobot cameras | 2025 | huggingface.co/docs/lerobot |
| Plappert et al. 2018 | arXiv:1802.09464 | Multi-Goal RL (Fetch environments paper) |

---

*Report written for the robot_arms Unity game project — fivelidz/superlocal — 2026-05-30*
