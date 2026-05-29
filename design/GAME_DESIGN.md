# ARMSMITH — Game Design Document

> Working title: **ARMSMITH** (design, control, and evolve robot arms to solve real tasks)
> Engine: Unity 6000.4.2f1 / URP / PhysX ArticulationBody. Units = metres (sim-to-real).

## 1. Pitch
A sandbox where players **design**, **control**, and **evolve** robot arms to solve manipulation
tasks. You hand-drive an arm with mouse + keyboard, then hand the design to an evolutionary loop
that improves both the arm's *behaviour* and *morphology* across generations — you act as the
breeder, selecting which arms survive. Designs export as printable STL + a joint-config so a winning
arm could be built for real (modelled on the open-source Seeed reBot-DevArm / SO-ARM100).

## 2. Core loop
1. **Design** — assemble an arm from segments (base, links, joints, gripper). Set link lengths, joint limits.
2. **Drive** — control it by hand (mouse drags an IK target, keyboard nudges joints / gripper).
3. **Task** — attempt the active task (pick block → place in target zone). Score = success + accuracy + speed − energy.
4. **Evolve** — spawn a population of variants; they auto-attempt the task; you **select** the best to breed the next generation.
5. **Export** — save the winning arm as STL + JSON config.

## 3. First task (keep it simple — the user asked for this)
**T1 — Pick & Place:** a coloured cube sits on a table; move it onto a marked target pad.
- Success: cube center within target radius and at rest.
- Dense reward: −(gripper→cube dist) before grasp; −(cube→target dist) after grasp.
- Sparse reward: +1 on success.
- Failure: cube knocked off table, timeout, or arm self-collision over budget.

Later tasks: stack two cubes, unscrew a nut (rotational), load a box into a slot.

## 4. The arm (physics)
- Built from Unity **ArticulationBody** chain (proper reduced-coordinate joint physics, stable, no drift).
- Starter arm = **3 revolute joints + gripper** (base-yaw, shoulder-pitch, elbow-pitch, 2 finger joints).
- Upgrade tier = full **6-DOF reBot layout**.
- Each joint: target drive (stiffness/damping), limits, max torque (→ energy cost).
- Meshes: **procedural** (cylinders for links, spheres at joints, box fingers) so morphology = just numbers. Authentic look later via optional imported reBot meshes.

## 5. Control scheme (mouse + keyboard)
**Mouse**
- LMB drag on the **IK target gizmo** → moves end-effector goal in a plane; arm solves via FABRIK.
- Scroll → move IK target along camera-forward (depth).
- RMB drag → orbit main camera. MMB drag → pan. Scroll(+Ctrl) → zoom.
- LMB on a joint handle (Manual mode) → rotate that joint directly.

**Keyboard**
- `1..6` select active joint (manual mode); `Q/E` rotate selected joint −/+.
- `Space` toggle gripper open/close.
- `Tab` toggle Manual ↔ IK mode.
- `W/A/S/D` (+`R/F` up/down) nudge IK target in world axes.
- `C` cycle camera view (main / wrist / env / top). `V` toggle multi-cam HUD.
- `G` grab record (start/stop a demonstration). `P` play back demonstration.
- `Enter` run task attempt; `Esc` reset task.
- `[`/`]` previous/next task.

## 6. Cameras (multi-view console)
- Main orbit camera (primary).
- Wrist camera (RenderTexture HUD, parented to gripper, ~80° FOV).
- Environment camera (RenderTexture HUD, fixed front-overhead, ~78° FOV — C922 analogue).
- Top-down alignment camera (toggle).
HUD = corner `RawImage` panels, toggleable/enlargeable.

## 7. UI (intelligently constructed)
- **Left dock — Designer:** sliders for each link length, joint limit, gripper width; add/remove joint; arm stats (reach, payload, DOF, precision).
- **Bottom bar — Driver:** mode toggle, gripper button, current joint readouts (angles, torque), task timer + score.
- **Right dock — Evolution:** population grid (thumbnails), fitness bars, "breed selected", generation counter, lineage view.
- **Top bar:** task selector, camera toggles, export STL button, play/pause sim.
- Built with Unity UI Toolkit / uGUI (uGUI present in template).

## 8. Learning / evolution layer (phased)
- **Phase 1 (manual):** FABRIK IK + player hand-driving. Ships first. *(this milestone)*
- **Phase 2:** CMA-ES over a motion-primitive parameter vector (waypoint timings/positions). First auto-improvement loop.
- **Phase 3:** Morphology GA — genome = [link lengths, joint limits, gripper type]. Headless batch attempts; **player selects survivors** (interactive evolution). Lineage tree.
- **Phase 4 (optional):** Unity ML-Agents PPO on a chosen morphology ("unlock RL").
- Fitness: success(+big) + placement accuracy + −time + −energy(Σ|torque·Δθ|) + −collisions.

## 9. Export (STL + config)
- Binary STL writer from combined arm Mesh (Unity Y-up LH → STL Z-up RH conversion, winding flip).
- `arm_config.json`: per-joint {type, axis, limits, link_length, drive} + gripper spec → the sim-to-real recipe.
- Future: OpenSCAD-WASM / build123d sidecar for high-fidelity printable parts (research/cad_3dprint).

## 10. Milestone plan
- **M0 (now):** Unity project scaffold + MCP bridge online.
- **M1:** Table + cube + target scene; 3-DOF ArticulationBody arm prefab (procedural meshes).
- **M2:** FABRIK IK + mouse/keyboard driver; gripper grasp via trigger+joint.
- **M3:** Pick-and-place task scoring + reset; multi-camera HUD.
- **M4:** Designer UI (link/joint sliders) regenerates arm live.
- **M5:** STL + config export.
- **M6:** CMA-ES evolution loop + population/selection UI.

## 11. Naming / scene
- Scene: `Assets/Scenes/Workshop.unity`
- Root objects: `Environment` (table, lights), `Arm` (ArticulationBody chain), `Task` (cube, target), `Cameras`, `UI`, `GameManager`.
