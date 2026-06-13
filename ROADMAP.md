# ARMSMITH — Master Roadmap

Living document. Tracks phases, milestones, and status. See `design/PROMPT_LOG.md` for the
prompts/intentions behind each item, and `design/specs/` for detailed spec guides.

## Vision (one line)
A Unity game where you **design, control, evolve, and train** robot arms (and their CAD parts) to
solve manipulation tasks — with multi-camera vision that maps 1:1 to a real arm, so trained
behaviours and designs **export and cross over to real hardware**.

## Pillars
- **A. Physics arm** — ArticulationBody, FABRIK IK, gripper, metres units. (reBot/SO-ARM reference)
- **B. Control** — mouse+keyboard driving; manual + IK modes; demonstration record/playback.
- **C. Cameras & vision** — main orbit + wrist CV cam + environment cam; feeds training; matches real rig.
- **D. Tasks & scoring** — pick-and-place first; reward shaping; reset.
- **E. Evolution/training** — manual -> CMA-ES motion -> morphology GA (player selection) -> ML-Agents.
- **F. CAD** — in-game parametric/AutoCAD-style design; evolvable; STL export.
- **G. In-game AI** — zero-auth Claude Code agent that designs arms/parts from natural language.
- **H. Real-robot port** — degrees-based JSON waypoints -> LeRobot / reuse prior `servo_controller.py`.

## Prior assets to reuse (found on this machine)
- `~/projects/robot_hand/python/servo_controller.py` — STS3215 bus control (4096 steps/360°, SyncWrite).
- `~/projects/robot_hand/python/finger_angles.py` + `robot_observer.py` — vision->command + 2nd-camera observer.
- `~/projects/robot_hand/stl/so_arm100_*` + `ST3215.step` — real arm meshes/dims.
- `~/projects/robot_hand/hardware/openscad/servo_bed_ST3215.scad` — parametric CAD seed.
- `~/projects/MASTER_PROJECTS/_research_may_downloads/CADAM/` — OpenSCAD-WASM text-to-CAD reference.

## Milestones & status
| ID | Milestone | Pillars | Status |
|----|-----------|---------|--------|
| M0 | Research library + design docs | — | DONE |
| M0.5 | Unity project scaffold + MCP bridge online | — | IN PROGRESS |
| M1 | Workshop scene: table, cube, target; 3-DOF ArticulationBody arm (procedural) | A,D | IN PROGRESS (scripts written) |
| M2 | FABRIK IK + mouse/keyboard driver + gripper grasp | A,B | scripts next |
| M3 | Multi-camera HUD (main+wrist+env), RenderTextures | C | scripts next |
| M4 | Pick-and-place TaskManager: scoring, reset, reward export | D | scripts next |
| M5 | Designer UI (live arm regen) + STL export + arm-config JSON | A,F | next |
| M6 | Demonstration record/playback + behaviour (waypoint) export | B,E,H | next |
| M7 | Real-robot port bridge: JSON waypoints -> servo_controller.py / LeRobot | H | next |
| M8 | Evolution loop: CMA-ES motion params + population/selection UI | E | next |
| M9 | Morphology GA (link lengths/joint types/gripper) w/ interactive selection | E,F | later |
| M10 | CAD engine: OpenSCAD/build123d sidecar; CAD genome; evolve parts | F | later |
| M11 | Zero-auth in-game Claude Code agent (design from prompt) | G | later |
| M12 | Vision policy training (ML-Agents) using wrist+env cams; sim-to-real eval | C,E | later |
| M13 | Real-arm telemetry import: 2nd cam follows actual performance/adaptations | C,H | later |

## Active requirements backlog (from P4-P8, newest priorities)
- [ ] R1: Load real SO-ARM100/SO-101 STL meshes per link (Assets/Meshes/SOARM100/) — authentic look. (I17,I32)
- [x] R2: Claw must not penetrate worktop — collision physics. (I18,I21,I32) — palm collider + lifted home pose; EE y=0.238 verified.
- [ ] R3: Fix off camera (wrist/env framing). (I19)
- [ ] R4: In-game CV detects claw jaws + objects in camera feeds, feeds training. (I20)
- [x] R5: Servo digital twin — joint cmd → STS3215 4096-tick, rate-limited; bus ticks in HUD. (I22)
- [x] R6: Explicit per-scenario OBJECTIVE + reward spec in UI. (I23)
- [ ] R7: Generation controls reliable: train/stop/step/seed via keys + UI w/ feedback. (I25)
- [ ] R8: Arm follows the MOUSE in real time (cursor→work-plane→IK). (I26)
- [ ] R9: Gripper open on `,`, close on `.` (plus Space toggle). (I27)
- [ ] R10: Record control → train (seed/imitation) + export to real servos; document servo activation chain. (I28)
- [ ] R11: Text-agent command interface (parse instructions → actions; generate sequences; seed evolution). (I29)
- [ ] R12: Success condition fires + is visible; verify each scenario. (I30)

## Sensor module system (P12 — big new pillar "I: Sensors")
Goal: pluggable add-on sensors so players compare task performance with different information, and
training uses all available info. Modules implement a common ISensor interface; an ObservationBuilder
concatenates enabled modules into the training observation.
- [ ] S1: ISensor interface { string[] Channels; float[] Observe(); string Name } + SensorHub registry.
- [ ] S2: MotorEncoderSensor — joint angles + servo ticks (baseline; already have via arm.GetJointAngles).
- [ ] S3: ImuSensor — orientation (quat->euler), angular vel, linear accel at a chosen link (alt to encoders).
- [ ] S4: RangeFinderSensor — single Raycast from gripper -Y / forward -> distance (1-point ToF lidar).
- [ ] S5: Lidar2DSensor — N raycasts in a fan/ring -> range array (planar lidar scan).
- [ ] S6: DepthCameraSensor — read wrist-cam depth (downsampled NxN range grid from camera).
- [ ] S7: EFleshTactileSensor — per-finger contact normal force + contact point (tactile/grasp feedback).
- [ ] S8: ObservationBuilder — concatenate enabled sensors -> obs vector; expose to EvolutionTrainer.
- [ ] S9: HUD/UI toggles for each module; show live channel readouts.
- [ ] S10: Comparative analytics — per-task best-performing sensor set ("module advisor"); ablation runs.
- [ ] S11: Train generatively with ALL enabled sensor info (now); subset ablation later.
- [ ] S12: Sensors as physical add-on visuals on the arm (so attaching a module is visible).

## More mouse control (P12 / I40) — extend the approved control
- [ ] M-ctrl1: click-to-grab (click an object -> arm moves to it + closes gripper).
- [ ] M-ctrl2: drag an object to a location (pick + place by mouse).
- [ ] M-ctrl3: draw-a-path (hold + drag -> arm follows the traced path; record as trajectory/training seed).
- [ ] M-ctrl4: depth scrub feel — refine scroll depth; observe how mouse motion translates to recorded waypoints.

### Servo activation chain (R10 design note)
mouse screen pos → ray → worktop plane hit (work-plane) → IK target → FABRIK → per-joint angle (deg)
→ ServoModel.RateLimit (max deg/s like real motor) → AngleToTick (deg→0..4095) → drive.target (sim)
AND, for real arm: same tick → SyncWritePosEx(id, tick, speed, acc) over the 1 Mbit bus.
So at every mouse position the servos receive a concrete tick target; recording = logging those ticks/angles at dt.

## Working order (immediate)
1. Finish M0.5: write remaining core scripts, build scene via MCP, get arm standing & solving IK.
2. M2 control + M3 cameras + M4 task = first playable loop.
3. M5 STL/JSON export (satisfies "must export STL").
4. M6/M7 behaviour + real-robot port (satisfies "train then export" + "port to real arm").
5. Then E/F/G evolution, CAD, in-game AI.

## Spec guides (design/specs/)
- `CAMERA_VISION_SPEC.md` — camera rig, RenderTextures, real-rig matching, vision-for-training.
- `REAL_ROBOT_PORT_SPEC.md` — export schema + bridge to servo_controller.py / LeRobot.
- `CAD_SPEC.md` — in-game parametric CAD + evolution (TODO).
- `INGAME_AI_SPEC.md` — zero-auth Claude Code agent design (TODO).
- `TRAINING_SPEC.md` — evolution + RL training and policy export (TODO).

## NEW PILLARS (P13)

### Pillar J: Open-source robot catalogue (importable systems)
Goal: support many open-source robots, not just SO-101. Each = URDF/MJCF + meshes + joint map.
- [ ] J1: ORCA Hand (open dexterous hand) - import + control as a gripper/hand module.
- [ ] J2: Robot catalogue registry: SO-ARM100/101, Seeed reBot, Koch, Mobile ALOHA, OpenArm,
      Dummy-Robot, LeRobot-supported arms. Each loadable via BuildFromKinematics(<json>).
- [ ] J3: Generic URDF importer path (drop a URDF + meshes -> playable arm).
- [ ] J4: eFlesh tactile as a real hardware module (already emulated; map to real sensor later).

### Pillar K: Multi-robot + communication
Goal: multiple arms, each with multiple modules, that communicate and coordinate.
- [ ] K1: Spawn N arms in one scene; per-arm controllers/sensors/servo panels.
- [ ] K2: Shared world state / message bus (arms publish pose+intent, subscribe to others).
- [ ] K3: Coordinated tasks: object hand-off between two arms; collaborative pick-place; do-not-collide.
- [ ] K4: Multi-agent training (each arm a policy; cooperative/competitive fitness).

### Module-usage transparency (P13 / I43)
- [ ] U1: Per-module panel showing live outputs (have ServoPanel for motors; add a SensorPanel).
- [ ] U2: Each module shows "USED IN TRAINING: yes/no" = is it in the current observation vector?
- [ ] U3: Observation composition view: which channels feed the policy this generation.

### Record initial training demonstrations (P13 / I44)
- [ ] D1: Record a hand-driven pick-place-into-tray run (already have BehaviourRecorder waypoints).
- [ ] D2: Save as a labelled DEMO (task + sensor stream + actions) for imitation seeding.
- [ ] D3: Seed the policy/evolution population from recorded demos (warm-start training).

### Real-world fidelity (P13 / I46)
- [ ] F-r1: Servo speed/torque limits per STS3215 datasheet (have rate-limit; add torque saturation).
- [ ] F-r2: Sensor noise + latency models (IMU drift, lidar noise, camera lag) toggle.
- [ ] F-r3: Friction/mass calibration to match real cube/tray.
- [ ] F-r4: DLS/Jacobian IK so the real offset-wrist arm reaches like the physical robot.

### IK reach issue (explanation, for the record)
CCD (cyclic coordinate descent) rotates one joint at a time toward the target. The real SO-101 wrist has
an OFFSET TCP (gripper tip ~11 cm off the joint axes + 90deg twist), so single-joint rotations swing the
tip non-intuitively; CCD oscillates and settles in a local minimum ~20 cm short. Procedural arm had an
on-axis tip so CCD was exact. Fix = Damped Least Squares (Jacobian) IK: solve all joints together via the
Jacobian with damping near singularities -> correct reaching for offset wrists (industry standard).

## UI control overhaul (P15)
- [ ] U4: Clickable buttons for all view toggles (lidar/range/depth/bounds/axes/cam-HUD/callouts).
- [ ] U5: Clickable buttons for mode/gripper/calibrate/pause/speed/train.
- [ ] U6: Per-servo +/- ARROW buttons floating at each joint (drive without hotkeys).
- [ ] U7: Colour-code servos consistently (arm hotspot + servo panel + callouts).
- [ ] U8: Per-servo circular activation gauge (radial fill = angle-in-range).
- [ ] U9: Remap sensor-view keys off servo hotkeys.
- [x] BUG: base-bend (shoulder_lift) wrong axis + links detach -> FIXED (see PROGRESS).

## Sensor-only play mode (P17 idea)
- [ ] SP1: A mode where the human player controls the arm using ONLY the sensor-module information
      (wrist cam + lidar + rangefinder + depth + tactile) — NO god-view of true object positions.
      The main 3rd-person view is hidden/dimmed; you operate from the sensor panels like a real teleop.
      This is also the exact information budget a trained policy gets -> direct human-vs-policy comparison.

## MASTER "elements to add/include" list (living checklist)
Bugs/blockers:
- [~] Grasp: EE now ~2cm from jaw midpoint (was 9.8). Verify objects are actually gripped + lifted.
- [ ] IK reach occasionally degrades after mode switches -> ensure recalibrate on play + on arm rebuild.
Scenario solutions:
- [ ] Reliable scripted pick-and-place that SUCCEEDS (cube ends in tray) end-to-end.
- [ ] Per-scenario scripted solver (sort, stack, drop-in-bin).
Training/learning:
- [ ] Show BOTH training systems (motion GA + policy net); surface the earlier motion-GA that worked.
- [ ] Warm-start policy/motion population from a successful scripted demo.
- [ ] Train-to-success demo on tray-to-tray; success-rate curve.
UI:
- [ ] Sensor-modules panel: per-module enable + view-toggle BUTTONS; better layout.
- [ ] On-arm servo +/- buttons visible (callouts) + reliable.
- [ ] Colour the SERVO REGIONS on the 3D arm model (tint each link/servo body).
- [ ] Camera-controls help shown on-screen.
- [ ] HTML windows (menu/options/training) as real in-game Unity windows.
Control:
- [ ] Alt+LMB drag = camera orbit (keep RMB too).
- [ ] Click in world to set next IK target / transition location.
- [ ] Sensor-only play mode (SP1).
Catalogue/multi-robot:
- [ ] Import ORCA Hand (URDF) as a catalogue arm.
- [ ] 2nd arm + shared world state + object hand-off.

## Session-4 deliverables (verifiable, no-Unity-needed work done while editor env broken)
- [x] Placement-VERIFICATION system (Verification/): IPlacementRule + PlacementVerifier + 5 rules + panel. Extensible foundation for CAD + modules.
- [x] Real-robot waypoint SAFETY verifier (scripts/realbot/verify_waypoints.py) — tested (passes good, rejects bad).
- [x] CAD spec (design/specs/CAD_SPEC.md) — extensible parametric-CAD architecture + milestones C1-C6.
- [x] Module-mount spec (design/specs/MODULE_MOUNT_SPEC.md) — player-placeable/orientable modules; pose drives sensing; sim->real; milestones MM1-MM6.
- [ ] BLOCKED on Unity editor display: verify in GUI, finish grip-detection feedback wiring, build CAD/module systems.

## Diffusion-model control & in-world data visualization (P-DIFFUSION, added S7d)
Direction (user): diffusion models are a more powerful way to DIRECT the arms than scripted IK, and
DRAWING the paths / showing more data visually in the world is explicitly wanted. Full research in
`research/diffusion_pathfinding/REPORT.md`. Adopt incrementally; keep IK as substrate + baseline.

### Visualization (do first — pure C#, verifiable headless, no stable-editor dependency)
- [ ] PV1: `Visualization/PathVisualizer.cs` — draw trajectories in the 3D world: planned path (line
      ribbon), executed path (different colour), waypoint spheres, start/goal markers. LineRenderer-based.
- [ ] PV2: MULTIMODAL paths — draw several candidate trajectories at once (the left-vs-right routes a
      diffusion planner produces), colour/alpha by cost or probability. This is the core "show the data".
- [ ] PV3: DENOISING animation — visualize a path refining from noisy -> smooth over diffusion steps
      (great explainer of the diffusion concept; data-driven later, scriptable now).
- [ ] PV4: cost/reachability FIELD overlay (tie into WorkspaceMap green/red grid); obstacle SDF heatmap.
- [ ] PV5: `Visualization/TrajectoryData.cs` + `ITrajectoryProvider` — one data model so IK paths,
      GA-evolved motions, AND diffusion paths all feed the SAME visualizer. Decouples viz from source.

### Diffusion pipeline (Python/LeRobot trains; Unity = data source + deployment target)
- [ ] DF1: `armsmith.waypoints.v1 -> LeRobotDataset` converter (Python). Demo-factory step 1.
- [ ] DF2: repurpose EvolutionTrainer successful rollouts AS demonstrations (GA = demo generator).
- [ ] DF3: train a low-dim joint-space Diffusion Policy (LeRobot `--policy.type=diffusion`) on one task
      (reach-and-grasp placed cube); obs = joint state + object pose; action = joint-deg chunks.
- [ ] DF4: inference server (DiffusionPolicy.from_pretrained) -> MCP/socket -> Unity SetTargets, receding
      horizon. Benchmark robustness (move object / perturb) vs open-loop waypoint playback.
- [ ] DF5: MPD-style DIFFUSION MOTION PLANNER in joint space — collision-free multimodal trajectories
      using Unity collision queries as guidance cost; outputs drop into the existing waypoint/export +
      PathVisualizer. (Directly matches the "diffusion pathfinding" interest.)
- [ ] DF6: branch options — DP3 (synthetic point clouds from Unity depth) / EquiDiff (sample-eff) /
      Consistency Policy (real-time). Sim-to-real via existing LeRobot bridge (armsmith_lerobot.py).

### PhysX articulation stability (P-PHYSX, S7d — blocker for live verification)
- [x] Root-caused: setupDescTask segfault = over-stiff drives on light links + violent first-step
      depenetration of overlapping SO-101 STL meshes (AMD/Mesa/kernel-6.18 + Unity6).
- [x] Fixes: solver iters 24->10; stiffness ~5x lower + higher damping; lock fixed_jaw; per-body
      maxDepenetrationVelocity/maxLinearVelocity caps + per-body solver iters; NaN/velocity watchdog
      auto-rehome; self-collision GATED OFF for first ~40 settle frames then enabled.
- [ ] Verify the settle-gating eliminates the non-deterministic first-step crash across many play cycles.
- [x] NOTE: editor launch failures were a LAPSED UNITY LICENSE (re-activated via Hub), not graphics.
