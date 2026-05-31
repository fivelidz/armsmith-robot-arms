# Spec Guide — Player-Placeable & Orientable Sensor/Camera Modules

Implements P19 (I76-I79): the player drops sensor/camera modules onto robot parts and orients them.
Built on the extensible sensor system (ISensor/SensorHub) + PlacementVerifier + SaveSystem.

## Goal
Modules (wrist cam, env cam, IMU, lidar, rangefinder, depth, eFlesh tactile, future ones) are not fixed
— the player attaches them to a chosen robot part at a chosen pose. The mount pose then DRIVES that
module's sensing (camera viewpoint, lidar origin/direction, IMU link) AND is saved + exported so the
SIM matches the REAL rig 1:1.

## Core data: MountPoint + ModuleInstance
- **`MountPoint`** — a valid attachment site on a robot part (a named socket on a link with a default
  pose + allowed module types). Parts expose a list of MountPoints (e.g. gripper has "wrist_cam_mount",
  each link has a "surface" mount). Extensible: new parts/CAD parts declare their own MountPoints.
- **`ModuleInstance`** — a placed module: { moduleType, mountPoint (or free transform on a link),
  localPosition, localRotation }. Parents to the link transform. Serializable -> saved with the arm.

```
RobotPart ──exposes──► MountPoint[]      ModuleCatalog ──► ModuleInstance (placed)
                           ▲                                   │ parents to link, sets pose
                           └──── PlacementVerifier checks ◄─────┘  drives the ISensor
```

## Placement interaction (drag-drop + orient)
1. **Pick** a module from the catalog (Builder panel) — a ghost preview attaches to the cursor.
2. **Hover** over the arm: valid MountPoints highlight (green); the ghost snaps to the nearest one.
   Free-placement mode lets you stick it anywhere on a link surface (raycast hit -> local pose).
3. **Drop** to place -> creates a ModuleInstance parented to that link at that pose.
4. **Orient**: a rotation gizmo (or keys) spins the module about its mount normal / tilts it. For a
   camera, a thin frustum preview shows where it will look so you aim it deliberately.
5. **Verify**: PlacementVerifier's ModuleMountRule (+ new rules) confirm it's on a valid part, not
   intersecting, facing a sensible direction.

## Mount pose drives the sensor (the important link)
Each ISensor reads its pose from its ModuleInstance instead of a hardcoded transform:
- **WristCam / EnvCam**: camera transform = the mount transform (position + look direction).
- **Lidar2D / RangeFinder**: ray origin + forward = the mount transform.
- **IMU**: the link it's mounted on.
- **DepthCamera**: the camera it's mounted to.
So moving/orienting a module physically changes what it senses — and that's exactly the information the
policy gets, so where you mount matters for task performance (ties into "which module helps which task").

## Sim ↔ real fidelity (why pose is saved + exported)
- Mount pose is saved in the arm config (SaveSystem) and exported in the rig config JSON
  (cameras + sensors). On the REAL arm you mount the physical sensor at the SAME link + pose (printed
  mount bracket — which CAD can generate!). So a policy trained in sim transfers.
- A CAD MountPoint can auto-generate a printable bracket (CAD_SPEC) for that exact pose -> real mount.

## Extensibility
- New module type: add to ModuleCatalog + an ISensor impl. No changes elsewhere.
- New part / CAD part: declare its MountPoints. No changes elsewhere.
- New validity rule (e.g. "camera must not be occluded by a link"): add an IPlacementRule.

## Milestones
- MM1: MountPoint + ModuleInstance data + serialization; parts declare default MountPoints.
- MM2: Drag-drop placement (catalog -> ghost -> snap to MountPoint / free on surface).
- MM3: Orientation gizmo + camera frustum preview.
- MM4: Sensors read pose from their ModuleInstance (wrist/env cam, lidar, rangefinder first).
- MM5: Save/export mount poses; PlacementVerifier mount rules.
- MM6: CAD-generated printable bracket per MountPoint (sim->real).
