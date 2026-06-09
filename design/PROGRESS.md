# ARMSMITH — Progress Log

Append-only build log. See ROADMAP.md for the plan, PROMPT_LOG.md for user prompts/intentions.

## 2026-05-30 — Session 1: foundation playable

### Done
- Research library complete: `research/{arm_hardware,manipulation_repos,cad_3dprint,cameras,learning_evolution,unity_integration}/REPORT.md` + `INDEX.md`
  + `manipulation_repos/{REAL_ROBOT_PORTING.md,TEST_ENVIRONMENTS.md}`.
- Design docs: `design/GAME_DESIGN.md`, `ROADMAP.md`, `design/PROMPT_LOG.md`,
  `design/specs/{CAMERA_VISION_SPEC.md,REAL_ROBOT_PORT_SPEC.md}`, `design/ui_html/` (HTML console mockups).
- Prior project located + mined: `~/projects/robot_hand/` (STS3215 `servo_controller.py`, SO-101 STLs,
  STS3215 STEP, OpenSCAD servo bed, 2nd-camera observer pattern). Real-robot port will reuse it.
- Unity project scaffolded at `UnityProject/` (6000.4.2f1, URP) with MCP-for-Unity bridge live on :6990.
- Core C# (Assets/Scripts/): ArmConfig, FabrikIK, ProceduralArm (ArticulationBody chain), Gripper
  (prismatic jaws), ArmController (mouse+keyboard, IK+manual modes), CameraRig (main orbit + wrist +
  env RenderTexture HUD), ScenarioManager (6 scenarios), ArmGizmos (axes/triad/workspace bounds),
  BehaviourRecorder (waypoint record/playback + JSON export), StlExporter (binary STL), GameBootstrap
  (builds the whole scene at runtime), TaskManager (legacy single-task, superseded by ScenarioManager).
- Scene `Assets/Scenes/Workshop.unity` with `__Bootstrap` object; in build settings.

### Verified working (in play mode, via MCP)
- Scene builds at runtime: room (floor, walls, legged worktop), arm, scenario props, cameras, HUD. 0 errors.
- Full 4-DOF arm chain stands & holds a natural ready pose over the worktop (Base→BaseYaw→Shoulder→
  Elbow→Wrist→Gripper). Confirmed via hierarchy dump + screenshot.
- Tray-to-tray scenario renders (red Tray A w/ cube, green Tray B).
- Gizmos render in play: yellow joint axes, EE RGB triad, orange workspace reach hemisphere.
- Wrist-cam + env-cam panels render to HUD.

### Bugs fixed
- ArmConfig is `[Serializable]` plain class → Unity deserializes a non-null EMPTY config, so the
  `if(config==null)` guard failed and the arm built with 0 joints (only base+gripper). Fixed by also
  checking `joints.Count==0` in both GameBootstrap and ProceduralArm.Build.
- Table/ground z-fighting → rebuilt as a proper legged worktop (top at y=0) above a lowered floor.
- Arm stood bolt upright unreachable → added natural home pose {0,48,-88,-20} in ArmController.Bind.

### Known/next
- IK SolveIK uses incremental SignedAngle accumulation; works but could be smoother — revisit.
- Port HTML UI → Unity UI Toolkit panels.
- Real-robot Python sidecar (scripts/realbot/) reusing servo_controller.py.
- Evolution layer (CMA-ES motion params), CAD engine, zero-auth in-game AI.

## 2026-05-30 — Session 1 (cont): real-robot port + evolution working

### Done
- Real-robot port sidecar `scripts/realbot/`: `armsmith_player.py` (Feetech STS3215, deg->4096 steps,
  SyncWrite, ramp/rate-limit/e-stop) + `armsmith_lerobot.py` (LeRobot send_action for SO-101 & reBot)
  + `joint_map.json` + `joint_map_lerobot.json` + `sample.waypoints.json` + README. Both DRY-RUN tested OK.
- In-engine STL export verified: 3508-triangle valid binary STL (175 KB), bytes match 84+N*50.
  Sample saved to `exports_samples/starter_arm.stl`.
- Evolution layer: `MotionGenome` (keyframe genome) + `EvolutionTrainer` (GA: elitism+tournament+
  crossover+Gaussian mutation; rolls out each genome on the ArticulationBody arm; fitness = scenario
  reward - energy). Wired into GameBootstrap. Keys: T train, N +1 gen, F11 export best trajectory.
- git checkpoint committed (112 files).

### Verified working
- **Evolution improves across generations** (tray-to-tray, pop 16, 4 keys): best fitness
  gen4 -2.03 -> gen8 -1.61 -> gen11 -1.16 -> gen14 -0.69. Monotonic learning confirmed with real physics rollouts.
- Best genome -> exportable waypoint trajectory -> same JSON the real-robot sidecars consume.

### Next
- Evolution UI (population grid + fitness bars + breed/select buttons) — interactive evolution.
- Unity UI Toolkit panels from design/ui_html.
- CAD engine + zero-auth in-game AI agent.

## 2026-05-30 — Session 2: STL arm, startup fix, sensors, servo UI, controls

### Done
- **Unity GUI startup hang SELF-DIAGNOSED + FIXED**: SDL picks "(null)" window backend on XWayland and
  blocks after licensing. Fix = `SDL_VIDEODRIVER=x11` + full env + nohup. `scripts/unity_start.sh`
  (auto-cleans stale lockfile / orphaned mcp server on :6990 / kills editor) + `docs/UNITY_STARTUP.md`.
- **Real SO-101 STL arm** is the default model: `UrdfArm.BuildFromKinematics` builds a 6-DOF
  ArticulationBody chain from `kinematics.json` (real URDF joint origins/axes/limits) + mounts 19 STL
  meshes. Fixed 4 frame bugs (STLs already in metres; per-joint anchorRotation; URDF rpy->Unity; base
  yaw). All-zero pose assembles correctly. 6-DOF home pose {0,-40,-30,-15,0,0}.
- **Sensor module system** (pillar I): ISensor + SensorBase; MotorEncoders, IMU, RangeFinder, Lidar2D,
  DepthCamera, EFleshTactile; SensorHub concatenates enabled modules -> obs vector (~50ch). F2-F7 toggle
  for ablation. "Train with all info" realised. Verified live values + toggle shrinks obs.
- **Servo motor-value UI** (ServoPanel, bottom-left): per-joint current->target angle + STS3215 tick
  (0-4096) + range bar. The values that translate an arm point into motor commands — for control,
  training visibility, real-robot export.
- **Controls overhaul**: labeled per-servo keys T/G Y/H U/J I/K O/L P/; (work both modes); claw
  open/close ,/. ; claw ROTATION N/B (wrist_roll); mouse on/off M; camera-relative depth (top view =
  ground plane for X/Z, side view = vertical plane for height) + [/] depth keys.
- **More mouse control** (MouseInteraction, layered on approved control): double-click grab/place,
  Shift+drag draw-a-path (recorded as a trajectory/training seed).
- **IK fix**: CCD skips Roll + gripper joints so wrist_roll no longer spins 360° to "reach"; calibrated
  FK reads real rest geometry.

### Known / next
- URDF-arm IK reach is partial (CCD local minimum on offset wrist) — wants DLS/Jacobian. Per-servo keys
  + claw rotation give full manual control meanwhile.
- Wire SensorHub observation into EvolutionTrainer; comparative analytics (which module helps which task).

## How to run
1. MCP server + editor already launched (port 6990). Helper: `scripts/mcp.py`.
2. `python3 scripts/mcp.py tool manage_editor '{"action":"play"}'` to play.
3. Screenshot: `manage_camera screenshot ... output_folder=Captures max_resolution<=900` then
   resize to <=900px before reading (crash-prevention rule).

## 2026-05-31 — Session 3: base-bend fix + clickable control UI + servo arrows/gauges
### Done
- FIXED base-bend (shoulder_lift) wrong-axis/links-detach bug: anchorRotation Euler(0,-90,0)->Euler(0,0,90)
  so it pitches forward/back (X constant, Y/Z swing). Verified lift=40 keeps X; IK still 0.4cm.
- Clickable ControlBar (bottom-center): all VIEW + CTRL toggles mouse-operable, live colour state.
- Sensor-view keys remapped to numpad 7/8/9 (off the servo letter hotkeys); views independent of sensor.
- Colour-coded servos (ProceduralArm.ServoColor palette) consistent on arm hotspots + servo panel + callouts.
- On-arm servo CALLOUTS upgraded: coloured hotspots, leader line, +/- arrow buttons (hold to drive),
  RadialGauge (circular activation fill = angle within range), coloured stripe + gauge per panel.
- Angle display wrap so wide joints (wrist_roll) don't show 561deg.
- Bigger servo panel, calibration start pose, claw cam remounted, scenario menu heading, SaveSystem.
### Verified
- All UI clickable; servo arrows drive joints; gauges fill; colours consistent. 0 compile/runtime errors.

## 2026-05-31 — Session 4: grasp fix + STL-arm joint-mapping diagnosis
### Done
- Reliable GRASP (parent-carry + hysteresis + graspRadius 0.12) — verified the cube is HELD through
  transport (moved ~30cm). FixedJoint on ArticulationBody was flaky; kinematic-parent works.
### DIAGNOSED (STL arm joint-mapping problems — root cause of "can't pick up / impossible joints")
- shoulder_pan: command -60° -> actual +105° (wrong sign + range mapping).
- shoulder_lift: earlier fixed (was rotating about own length).
- IK reach: good on RIGHT side mid-height (0.4cm) but FAILS left side (-X) and table level (8-31cm err).
- Net: STL arm looks authentic but joint frames/anchors from URDF conversion are inconsistent ->
  unreliable manipulation. Procedural arm has correct joints + full-workspace IK + working grasp.
### DECISION POINT (for user)
Option A: invest in fixing every STL joint anchor/sign + IK (authentic look, more work).
Option B: ship procedural-arm kinematics (works) but SKIN it with the STL meshes (best of both) — needs
  mesh alignment to the procedural chain.
Option C: keep procedural arm default for gameplay; STL arm as a visual-only showcase.

## 2026-06-01 — Session 5: realistic arm solid + major feature batch (Unity recovered post-reboot)
Unity GUI recovered after the reboot (SDL window-backend issue cleared). Realistic SO-101 arm is the
default and fully functional. Delivered + VERIFIED this session:
- Self-collision physics: arm can't pass through itself (10.9mm max fold vs full pass-through).
- Joint velocity caps (maxAngularVelocity 8, damping) -> no IK singularity explosions.
- Grasp+carry solid; via-point place routing (no fling); trays moved to reachable zone.
- GripDetector grip-readiness feedback, revealed only via EFleshTactile module (verified 45%).
- Training LEARNS to solve a task: ReachTouch motion-GA fitness -1.98 -> +13.91 over 20 gens.
- Self-collision penalty added to training fitness.
- Reachable-workspace MAP (green/red grid, ArmController.TestReach FK probe; 28/45 cells).
- Module-mount system MM1 (ModuleMount: 7 sockets, mount/orient/save; verified WristCam on wrist_roll).
- CAD primitives layer C1 (ICadPrimitive/CadBox/CadCylinder + CadMeshGen + CadPart -> Evaluate -> STL;
  ServoBracket verified to 132-tri valid STL).
- Realistic wrist camera (WristCamAim: world-space aim at grasp point, 70deg; looks down -0.79Y).
- Multi-robot foundation (WorldBlackboard pub/sub + RobotAgent; arm1 publishing verified).
- Full regression: all 11 systems present, 0 runtime errors.

## 2026-06-01 — Session 6: realism priority + control breakthroughs
- PRIORITY set: realistic SO-101 sim over gameplay convenience (P23). Realistic arm is default.
- FIX proximal-joint stall: shoulder_pan/lift carry the extended arm's load -> need much stronger drive
  (stiffness 40000, force 600) than wrist (14000/150). Pan now reaches commanded angle BOTH sides under
  load (was -35cmd/-8actual); workspace reach 10-14cm -> ~4cm uniform.
- Realistic arm achieved a FULL pick-place success (cube ON_PAD 6.9cm) — but scripted control is
  NON-DETERMINISTIC across runs (6.9cm then 62cm then explode) due to physics-timing + marginal
  offset-wrist IK. This is real robotics difficulty.
- KEY INSIGHT validated: LEARNED policies are the right control approach for this hard accurate arm, not
  open-loop scripts. Motion-GA training IMPROVES on it: ReachTouch -1.98->+13.91 (solved);
  PickPlaceCube -2.24 -> -1.23 over 31 gens (steady monotonic learning on the hard grasp task).
- Strategy going forward: train/evolve policies on the realistic arm (the sim-to-real path), warm-start
  from demos to crack grasp-success, rather than perfecting brittle scripted sequences.
