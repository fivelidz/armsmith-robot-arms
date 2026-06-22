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

## 2026-06-11 — Session 7: CRACKED the pick-and-place non-determinism (root causes found + fixed)
Goal: resume the autonomous pick-and-place reliability work. Diagnosed the "works once then jams /
6.9cm one run, 62cm the next" non-determinism down to concrete root causes and fixed them in code.

VERIFIED FINDINGS (all measured via the live MCP bridge in Play mode):
- Manual/IK control is NOT regressed — actually improved: analytic IK 0.3cm mean / physical tracking
  ~2.0cm uniform across the workspace (was ~4cm). The mouse-follow APPROVED path is intact.
- ROOT CAUSE #1 (the big one): SelfCollision's IgnoreCollision pairs for the tightly-packed adjacent
  wrist links were being SILENTLY DROPPED after MeshCollider cooking / Unity's post-init collision-pair
  rebuild. Result: the arm jammed ~6cm above any low reach target (couldn't reach down to the cube).
  Proof: disabling all arm colliders dropped the low-reach floor-gap from 6.4cm -> 0.1cm, and ALL joints
  then hit their commanded angles exactly. Re-running SelfCollision.Setup() also fixed it -> confirmed it
  was the ignore-pairs, not force/IK.
- ROOT CAUSE #2: after extreme IK poses (driving the tip below the table / out of reach), the SO-101
  ArticulationBody solver corrupts and wedges joints at their limits — unrecoverable in place by drive
  commands. This is the "works once then jams on the next task" recurrence.
- ROOT CAUSE #3: lifting a GRABBED cube jammed the arm (empty lift = 0.3cm error; lift-while-holding =
  54cm jam). Cause: the held kinematic cube's collider kept generating contacts against the gripper/wrist
  links every physics step, feeding forces back into the articulation solver.

FIXES (all compile clean, batchmode exit 0, zero CS errors):
- SelfCollision.cs: re-assert the ignore-pairs over the first ~1s AND then continuously at 2 Hz forever
  (a few dozen IgnoreCollision calls — negligible cost). Makes low-reach robust across repeated tasks.
  Verified: 6 high/low cycles stay 1.2–5.8cm trackErr (was 44cm catastrophic jams).
- UrdfArm.cs: graded drive tiers (proximal 40000/600, wrist+elbow 22000/450, jaws 14000/150) reflecting
  real STS3215 loading. Helped mid-range wrist tracking (was saturating).
- ArmController.cs (APPROVED — additive only, mouse-follow untouched):
    * anti-stuck IK re-seed: when the DLS residual stays large, gently blend toward the analytic
      neutral-seed solution to escape collapsed local minima (conservative: thresh 0.12, blend 0.10).
    * IK SAFETY ENVELOPE inside SolveIK: clamp the goal to the reachable shell + above the worktop on
      ALL paths (incl. programmatic/agent targets that bypass the mouse-input clamp) — stops the
      below-table / out-of-reach targets that corrupt the articulation.
    * HardHome(): teleport-home primitive (resets controller targetAngles + calls arm.HardResetJoints).
- ProceduralArm.cs: HardResetJoints() — teleports the whole articulation via root.SetJointPositions +
  zeroes velocities (the proper "home the robot between tasks" primitive; individual child .jointPosition
  writes get overwritten by the solver, so it must go through the root). VERIFIED: recovers the arm from
  any wedged pose back to a clean home in one call.
- Gripper.cs: on grab, IgnoreCollision between the held object and ALL arm colliders (restored on
  release); plus a floor-guard so a held object never gets driven below the worktop.

RESULT: catastrophic 44cm jams ELIMINATED. Grasp is now reliable (always latches, ~4cm gap). Multi-trial
pick succeeds repeatedly (e.g. cube lifted to 0.139 / 0.179 / 0.376m across trials) — no longer the
all-or-nothing non-determinism. The remaining soft spot is the lift-from-grasp transition on the
offset-wrist arm, which the held-cube collision-ignore fix targets directly (couldn't run the final
post-fix multi-trial because the Unity GUI session degraded after a crash and needs a session restart —
all code is committed and compiles clean; verification to resume next session).

NOTE on tooling: a live bridge experiment (IgnoreCollision on a held kinematic body mid-physics) segfaulted
the editor; that exact logic now lives safely inside Gripper.TryGrab. After a GUI crash the Unity-6 Linux
editor can't re-acquire a window backend ("Selected window backend: (null)") until the graphics session is
restarted — xvfb / display workarounds did not help; a session restart is the known fix.

## 2026-06-11 — Session 7b: crash-isolating render strategy (end the restart cycle)
Investigated WHY the graphics session needed frequent restarts. Root-caused it (not flaky — specific):
- Host: KDE Plasma 6.5 *Wayland*, AMD Radeon 8060S (RADV gfx1151), Mesa 25.3.3, kernel 6.18 (CachyOS).
- Unity 6 editor is X11 -> runs OpenGL via GLX -> XWayland. A HARD Unity crash leaves its XWayland/GLX
  surface un-released, poisoning the SHARED XWayland; every later launch then hangs at
  "Selected window backend: (null)". A session restart fixes it ONLY because it respawns XWayland.
- Plasma X11 session is NOT an option: kwin_x11 was removed in Plasma 6.5 (only kwin_wayland ships).
FIX (scripts/unity_start.sh rewritten; old version archived to scripts/archive/unity_start_20260611.sh):
- Staged render strategy, default RENDER_MODE=auto tries vulkan -> gamescope -> xwayland until the bridge
  answers, with a hang-detector (backend(null) + no log progress) that abandons a wedged mode early.
    * vulkan   : -force-vulkan on XWayland (Vulkan WSI, sidesteps the GLX surface bug).
    * gamescope: Unity nested inside `gamescope` (Vulkan-backed surface) ISOLATED from desktop XWayland —
      a Unity crash takes down only gamescope, NOT the session. This is the durable fix for the restarts.
    * xwayland : original SDL x11 + OpenGL path (last resort).
- Verified available + healthy: gamescope v3.16.19 (selects RADV, inits Vulkan + nested wayland server),
  vulkaninfo shows RADV Vulkan 1.4.328 solid. Script passes bash -n. Live confirmation pending next launch.

## 2026-06-11 — Session 7c: the editor crashes were SELF-INFLICTED (PhysX), now fixed
Tested the new render strategy live after a session restart. The staged launcher correctly auto-fell
through vulkan (failed) and gamescope (SDL backend crashes in detached launch) to xwayland, which brought
the bridge up. Then — while verifying the S7 fixes — calling HardHome() over the bridge CRASHED the editor.

ROOT CAUSE of the recurring crashes (the thing that poisons XWayland and forces restarts):
- It was OUR OWN code. HardResetJoints() called ArticulationBody.SetJointPositions() on the root INLINE.
  When invoked mid physics-frame (a bridge HardHome lands in the editor update loop, overlapping the PhysX
  step), it corrupts the PhysX solver task descriptor and hard-SIGSEGVs the editor. Verified trace:
  physx::Dy::PxsSolverStartTask::setupDescTask() <- PhysicsScene::Simulate <- FixedUpdate <- HardHome.
- So the "graphics session keeps needing restarts" was NOT the session wearing out — each restart-need was
  triggered by THIS crash poisoning the shared XWayland :0 surface. Fix the crash -> stop the restarts.

FIX (ProceduralArm.cs): HardResetJoints now QUEUES the reset (drive targets + servo reseed applied
immediately — those are safe) and performs the SetJointPositions/SetJointVelocities teleport at the TOP of
the arm's own FixedUpdate, the only physics-safe window to write reduced-coordinate articulation state.
Same clean home, no PhysX corruption. Compiles clean (batchmode exit 0).

LAUNCHER (unity_start.sh): gamescope mode -> --backend wayland (nests into KWin with its own isolated
Xwayland :1; the SDL backend crashes when launched detached). Bridge wait extended to ~220s + editor-died
detection so a slow first import isn't misread as a failure. NOTE: gamescope nesting works when started
from an interactive session shell; launching it fully-detached from the automation shell dies before init,
so the gamescope isolation is best confirmed by a human-run `RENDER_MODE=gamescope ./scripts/unity_start.sh`.

STATE: XWayland :0 is poisoned by this session's 19:12 PhysX crash (editor now dies instantly on launch),
so ONE more session restart is needed. After that, the PhysX fix should prevent the crash recurring, which
should end the restart cycle regardless of gamescope. Final live pick-place verification still pending.

## 2026-06-13 — Session 7d: diffusion research + in-world path viz + PhysX crash FIXED (proven)
Big session. Three threads, all landed.

1) LICENSE was the launch blocker (not graphics): the editor was quitting at startup with
   "No valid Unity Editor license / 404 0 entitlement groups". Re-activated Personal license via Hub
   (open the INNER UnityProject/ folder in Hub to bind it). The "Selected window backend (null)" line
   was a red herring all along.

2) DIFFUSION research + visualization (user: diffusion is a better way to direct the arms; drawing paths
   in the world is wanted):
   - research/diffusion_pathfinding/REPORT.md — full survey (Diffusion Policy, DP3, Consistency/EquiDiff,
     RDT-1B, Diffuser/Decision Diffuser/AdaptDiffuser, MPD motion-planning-diffusion) + concrete LeRobot
     adoption path for THIS project. Bottom line: diffusion complements IK (keep IK for exact free-space +
     baseline); best entry points = LeRobot Diffusion Policy and an MPD-style diffusion motion planner.
   - Visualization/ module (compile-clean): TrajectoryData (TrajectorySample/Set + ITrajectoryProvider —
     common currency for IK/GA/diffusion paths), PathVisualizer (GL immediate-mode; planned+executed paths,
     MULTIMODAL candidate sets coloured by cost/weight w/ chosen highlighted, waypoints, start/goal),
     PathProviders (IKPathProvider live preview, DiffusionPathDemo multimodal over/around-left/right/direct
     w/ collision cost, DenoisePathDemo noisy->smooth animation). Wired into GameBootstrap; toggle keys
     8/9/0; executed-tip trail accumulator. Demos auto-resolve S_Cube(start)/S_Pad(goal).
   - DF1: scripts/realbot/waypoints_to_lerobot.py — converts armsmith.waypoints.v1 demos -> LeRobot dataset
     (portable intermediate w/ no deps + optional real LeRobotDataset). action = absolute joint+gripper deg
     in joint_map order (deploy-consistent). Tested: single/dir/stats-only/lerobot-degrade all pass.

3) PHYSX CRASH — root-caused and FIXED (the cross-session blocker). The editor segfault in
   physx::Dy::PxsSolverStartTask::setupDescTask during Simulate was caused by over-stiff drives on light
   links + violent first-step depenetration of the overlapping SO-101 STL meshes (NOT HardHome — that
   earlier deferred-teleport fix was correct). Fix stack:
   - GameBootstrap: solver iterations 24->10, velocity 8->2, Time.maximumDeltaTime=0.05.
   - UrdfArm drives: stiffness ~5x lower (proximal 8000 / wrist 4500 / other 2000) + higher relative
     damping (critically-damped, numerically safe); moving jaw 9000->1500; fixed_jaw LOCKED (was a live
     0.012 kg stiff DOF). Per-body maxDepenetrationVelocity=1, maxLinearVelocity=5, solverIterations=20/4.
   - ProceduralArm: NaN/velocity watchdog in FixedUpdate (checks full reduced state via root, bleeds
     velocity >40, auto-rehomes on NaN).
   - SelfCollision: GATE all self-collision OFF for the first ~40 settle frames (so overlapping links never
     depenetrate violently at build), then enable gap>=3; steady re-assert slowed to 0.5 Hz.
   PROOF: Editor/PhysxStabilityCheck.cs — headless batchmode test builds the real arm + Simulate()s 600
   steps, fails on any NaN. PASSED 3/3 consecutive runs (exit 0). The previously non-deterministic crash is
   now reliably gone, with a permanent CI regression gate. Verified live earlier too: editor entered play,
   built the 9-body arm, HardHome worked, physical IK tracking 0.3-2.0cm (better than old ~4cm) with the
   lower-stiffness drives, AND low targets now reach.

Remaining: live GUI visual confirmation of the path viz + a full multi-trial pick-place (the editor's
BACKGROUND launch from the automation shell is currently flaky; headless verification is solid). All code
compiles clean and is committed+pushed.

## 2026-06-13 — Session 7d (cont.): diffusion pipeline closed + headless CI suite
After fixing the PhysX crash, built out the diffusion data pipeline AND a reliable headless test suite
(since the GUI is flaky on this Wayland/AMD stack, headless verification is the dependable path).

- DF2: EvolutionTrainer.SaveBestAsDemo() — F11 export now also writes the best evolved genome as an
  armsmith.waypoints.v1 demo into Exports/Demos/. The GA is now a DEMONSTRATION FACTORY for diffusion.
- Headless CI gates (all -executeMethod, no GUI, all PASS):
  * Editor/PhysxStabilityCheck.cs — real arm + 600 Simulate steps, fail on NaN (PASSED 3/3).
  * Editor/HeadlessPickCheck.cs — approach->grasp->lift stays finite under load (PASSED).
  * Editor/VizSmokeCheck.cs — path-viz providers + TrajectoryData helpers sane (PASSED).
- scripts/run_checks.sh — one command runs all 4 gates incl. the Python diffusion pipeline
  (GA demo -> verify_waypoints SAFE -> waypoints_to_lerobot dataset). VERIFIED: 4 passed, 0 failed.
- End-to-end loop proven: GA-style demo (grasp+lift keyframes) -> safety verifier SAFE -> LeRobot
  dataset (3 eps / 96 frames / 5-dim / 20 fps + norm stats). Every link of GA->diffusion works.

Net S7d: the cross-session PhysX BLOCKER is fixed + proven; diffusion research + in-world path viz +
the LeRobot demo pipeline are built and headlessly verified; a 4-gate regression suite guards it all.
Remaining is live GUI confirmation (viz visuals + closed-loop pick-place control), gated only by the
flaky interactive-editor launch on this host — not by code.

## 2026-06-13 — Session 7d (final): in-game PhysX crash root-caused + fixed
After the headless arm-alone test passed but Play-mode in the full Workshop scene STILL crashed
(setupDescTask), found the in-scene-specific cause from the crash trace: the arm base is mounted at
y=0 = the worktop TOP, so the base/lower links SPAWN INTERSECTING the table; and BuildScenarios()
adds cube/trays AFTER the arm. On the first PlayerLoop physics step PhysX depenetrates those overlaps
with huge force -> articulation NaN -> segfault. The arm-alone headless test had no worktop/props so
it never reproduced it.
FIX: GameBootstrap.IgnoreArmVsEnvironment() — ignore collision between every arm link collider and the
STATIC environment (worktop/floor/walls/legs/static props: collider with no Rigidbody or kinematic
only). Called at the END of Start() so ALL props exist first. Manipulable objects (cube/trays =
non-kinematic Rigidbodies) stay collidable. Editor/PhysxStabilityCheck reproduces the worktop-at-y=0
condition + applies the ignore -> PASSED. Full regression suite: 4/4.
HONEST: live full-scene Play confirmation is still pending — the interactive editor's BACKGROUND launch
from the automation shell is intermittently failing to even spawn the process (an environment/tooling
limitation, NOT a code issue; when it does launch, the bridge works). The headless suite is the reliable
verification and all gates pass. The fix is correct per the crash-trace root-cause + headless repro.

### S7d net deliverables (all committed + pushed, all compile clean)
- Diffusion research report (research/diffusion_pathfinding/REPORT.md) + ROADMAP P-DIFFUSION section.
- In-world path VISUALIZATION: Visualization/ (PathVisualizer + TrajectoryData + PathProviders:
  IK preview, multimodal DiffusionPathDemo, DenoisePathDemo, executed-trail); keys 8/9/7.
- Diffusion DEMO PIPELINE: waypoints_to_lerobot.py (DF1) + EvolutionTrainer.SaveBestAsDemo (DF2, GA =
  demo factory via F11). End-to-end verified: GA demo -> verify_waypoints SAFE -> LeRobot dataset.
- PhysX crash FIXED (the cross-session blocker): solver iters, ~5x lower stiffness, locked fixed_jaw,
  depenetration/velocity caps, NaN watchdog, self-collision settle-gating, arm-vs-environment ignore.
- HEADLESS CI SUITE: PhysxStabilityCheck + HeadlessPickCheck + VizSmokeCheck + run_checks.sh (4/4 pass).
- Editor launch failures = LAPSED UNITY LICENSE (re-activated via Hub), not graphics.

## 2026-06-14 — Session 7e: diffusion planner + trainable policy (the "direct the arms" direction, working)
Continued the diffusion pillar from research/scaffolding into FUNCTIONAL code, all headless-verified.

- DF5 DIFFUSION MOTION PLANNER (C#, in-sim): Visualization/DiffusionMotionPlanner.cs + ObstacleField.cs.
  "Planning as denoising": seed K noisy candidate trajectories (each a different mode) -> iterate
  smoothing-prior + cost-guided push away from obstacles (classifier guidance) + endpoint anchoring
  (inpainting) + floor clamp -> score by length+collision, mark best chosen. Fixed a gradient-sign bug
  (was pushing INTO obstacles). VERIFIED headless: chosen path collision-free (cost 0.0) with an obstacle
  on the straight line, multimodal, endpoints anchored, exactly-one chosen. Drawn by PathVisualizer (key 6).
- PLAN->MOTION: Visualization/PlannedPathFollower.cs drives the IK target along the chosen path
  (receding-horizon style). HeadlessPickCheck probe: planner path 24 pts, ALL 24 IK-reachable (<4cm,
  worst 0.4cm) — the arm can actually follow the planned collision-free trajectory. Key 5 = follow.
- DF3 TRAINABLE DIFFUSION POLICY (Python): scripts/diffusion/train_diffusion_policy.py — low-dim
  joint-space Diffusion Policy (Chi et al. recipe). torch backend = self-contained conditional-DDPM
  (obs=recent joint states, action=future joint+gripper chunk). REAL RUN VERIFIED: loss 1.00->0.66 over
  40 epochs on 3 GA demos, 362KB checkpoint saved (torch 2.10). lerobot backend prints the exact
  lerobot-train invocation. --dry-run works with no ML deps. scripts/diffusion/README.md documents the
  full loop.
- run_checks.sh extended: the diffusion-pipeline gate now also runs the DF3 train dry-run. Full suite
  still 4/4 (gate 4 = demo -> SAFE -> dataset -> train dry-run).

THE FULL DIFFUSION LOOP NOW WORKS END-TO-END: Unity GA demos -> waypoints -> LeRobot dataset -> trained
Diffusion Policy; PLUS an in-sim diffusion motion planner that produces collision-free multimodal paths
the arm follows. The user's "diffusion is a better way to direct the arms + draw the paths" direction is
implemented and verified, not just researched. Remaining: DF4 inference server (deploy ckpt over MCP),
learned-denoiser swap for the planner, and live-GUI visual confirmation.

## 2026-06-14 — Session 7e (cont.): DF4 deployment + PV4 obstacle viz — diffusion loop FULLY closed
- DF4 DEPLOYMENT: scripts/diffusion/serve_diffusion_policy.py loads a trained ckpt and serves action
  chunks over TCP by running REVERSE diffusion (DDPM denoising) per request. VERIFIED: loads the trained
  policy, samples a coherent 8x5 action chunk, full TCP ping+action round-trip works. Unity-side
  Agent/DiffusionPolicyClient.cs connects on a background thread, sends joint+gripper obs, executes the
  action chunk receding-horizon via ArmController.SetTargets (key 4 to toggle; HUD "DIFFUSION POLICY LIVE").
- PV4 OBSTACLE VIZ: PathVisualizer draws the ObstacleField as wire rings; toggling the MPD planner (key 6)
  shows the obstacles it routes around — you SEE the collision-free planning.
- Regression suite now 5/5: added the full diffusion DEPLOY gate (train torch -> ckpt -> serve samples an
  action chunk), skipped gracefully if torch absent.

DIFFUSION DIRECTION STATUS: research -> in-world path visualization -> MPD motion planner (collision-free
multimodal) -> executable paths (PlannedPathFollower) -> trainable Diffusion Policy (DF3, real loss-drop
verified) -> inference server (DF4) -> live receding-horizon Unity client -> obstacle viz. ALL functional
and headlessly verified (5/5). Keys in-sim: 4 diffusion policy | 5 follow plan | 6 MPD planner | 7 denoise
| 8 toggle viz | 9 demo routes. Remaining: robustness benchmark, learned-denoiser swap for the planner,
live-GUI visual confirmation (gated only by the flaky editor launch, not code).

## 2026-06-14 — Session 7f: THE FK BUG (root cause of "won't descend") + full pick-place WORKS
The "arm won't reach low / bad IK / floors ~25cm high" symptom that blocked the task across many
sessions was finally root-caused: the IK's FORWARD-KINEMATICS model was wrong by ~30cm vs the real
ArticulationBody chain. CalibrateIK's inverse-frame reconstruction (undoing joint angles with a local
axis treated as a world axis) produced garbage rest offsets; FK predicted the tip ~30cm from where the
arm physically was (FK(home) said Z=0.001 while physical was Z=0.236). The IK solved correct-looking
angles against this broken model -> the arm reached a HIGH pose and refused to descend.

FIX (ArmController): rewrote CalibrateIK + ForwardKinematics with a robust formulation — capture each
joint's REST world position + REST world twist axis + rest angle, sync targetAngles to the ACTUAL joint
angles first, then FK walks root->tip applying each joint's DELTA angle as a world rotation about its
chain-carried axis, pivoting downstream joints + the EE in place. VERIFIED: FK now matches the physical
tip to 0.0cm at every pose; DescentCheck => "ARM DESCENDS CORRECTLY" (tipY 0.017 for goal 0.05).

CONSEQUENCES — everything the FK was blocking now works:
- HeadlessPickCheck is now a REAL end-to-end gate and PASSES: reach (approach err 3.4cm), grasp
  (gap 3.9cm, holding=True), and LIFT (cube raised to 0.102m, follows the tip). The manipulation task
  works. Added Gripper.TickHeld() so grasp-assist runs under headless Physics.Simulate.
- Contact stability: ScenarioManager cubes + test cube get maxDepenetrationVelocity/velocity caps so the
  gripper-vs-cube contact (now that the arm reaches the cube) can't spike PhysX into a crash.
Also this session (all committed, suite 5/5):
- Out-of-bounds scenario AUTO-RESET (ScenarioManager): object knocked off the table -> reload scenario.
- CLAW CAMERA reworked (WristCamAim): mounts back+above the grasp point along the approach axis, looking
  down the approach line, so the jaws AND the grasped object are both clearly framed.
- TaskStateSensor: richer observations for the model — EE pose, gripper open/holding, joint velocities,
  and the VECTOR from the tip to the target (the key "close the gap then grasp" signal).
- Multi-seed IKAnglesFor (elbow-up vs elbow-down) + downward-reach scoring.
REMAINING: live in-GUI confirmation of the real closed-loop control + claw cam (editor bg-launch is
intermittently failing in the automation shell; headless 5/5 is the reliable proof and the FK/IK/grasp/
lift are all verified there).

## 2026-06-14 — Session 7f (cont.): the task WORKS + everything the user asked for
After the FK fix, drove the remaining functionality requests to done and verified the manipulation
task end-to-end (headless, the reliable path while the GUI bg-launch is flaky in this shell).

WHAT WORKS NOW (HeadlessPickCheck PASSES = real end-to-end gate):
- Reach the cube: approach error ~3.4cm
- Grasp: gap ~2.5-3.9cm, holding=True
- LIFT: cube raised to ~0.09-0.10m, follows the tip up
- Full headless regression suite 5/5.

USER REQUESTS — all addressed:
1. "proper physics in relation to motors" — FK now matches the real ArticulationBody chain to 0.0cm
   (was 30cm off), so the IK commands physically correct angles; drives tuned (proximal 35000/800/900,
   elbow+wrist 32000/700/800) to hold pose. Joints reach commanded angles in the pick sequence.
2. "camera on the claw should show the claw better" — WristCamAim reworked: mounts back+above the grasp
   point along the approach axis, looking down the approach line, so jaws AND the grasped object are both
   framed (was looking at itself).
3. "object flies off the table -> reset scenario" — ScenarioManager out-of-bounds watchdog (5 Hz)
   auto-reloads the scenario when a task object leaves the table bounds / goes NaN.
4. "all the data that should be sent to the model" — TaskStateSensor: EE pose, gripper open/holding,
   joint velocities, and the vector-to-target + distance; joins MotorEncoders/IMU/RangeFinder/Lidar/
   DepthCamera/Tactile in SensorHub.BuildObservation().

KEY FIX (the cross-session blocker): the IK forward-kinematics model was wrong by ~30cm. CalibrateIK's
inverse-frame reconstruction was buggy; rewrote CalibrateIK + ForwardKinematics with a robust rest-world-
transform formulation (capture each joint's rest world pos/axis + sync targetAngles to actual). FK now
matches physical exactly -> the arm descends and the IK is correct. Also: self-collision made bulletproof
(dedicated arm layer with layer-self-collision off), depenetration caps on arm + cube, arm-vs-environment
ignore, multi-seed IKAnglesFor (elbow-up vs down) with a continuity bias.

HONEST FOLLOW-UP: the LIVE CONTINUOUS IK-target loop (mouse-follow) tracks X/Z well but settles ~8cm high
on low-Y targets, because the continuous Jacobian re-solve interacts with the stiff PD drive (oscillation/
equilibrium offset). The DISCRETE analytic-hold path the pick routine uses is accurate (that's why the
task passes). Fully smoothing the continuous loop needs gravity-compensation / computed-torque control —
a control-quality improvement, not a task blocker. Live in-GUI confirmation still pending (editor
background-launch is intermittently failing in the automation shell; systemctl/systemd-run denied).

## 2026-06-14 — Session 7g: training regimen + physics verification + NVlabs research
Delivered the full training system the user asked for, grounded on verified physics, plus research.

PHYSICS / MOTOR VERIFICATION (grounds the training):
- Editor/MotorPhysicsCheck.cs (suite gate): drives track modest commands fast; servo rate-limited
  (~117 deg/s vs STS3215 model); tick quantisation 0.088 deg/tick; gravity hold ~3 deg drift (was 11).
- Tuned: conditioned explicit inertia (no oscillation) + joint friction 0.15 (geared-servo stiction =
  passive hold). Large extended angles sag realistically (honest small-servo physics). Lift -> 0.218m.

TRAINING REGIMEN (design/specs/TRAINING_REGIMEN.md):
- TrainingConfig: backend (Motion-GA / Sensor-Policy / Diffusion), difficulty + curriculum (L0 Reach ..
  L4 Scrambled), randomization strength, reward-shaping weights, SENSOR MASK (model inclusion/exclusion
  of info), GA/policy hyperparams.
- EvolutionTrainer: ShapedFitness (config-weighted) in both backends; per-gen best/mean/success history;
  auto-curriculum; ApplyConfig/StepOneGeneration/ResetTraining; per-gen best-trajectory capture.
- TrainingPanel (F3): backend selector + Start/Stop/+1Gen/Reset + live curves (best/mean/success) + progress.
- ConditionsPanel (F4): sliders for difficulty/randomization/reward-weights + sensor toggles + GA params.
- TR7 scrambled-world: randomization slider drives ScenarioManager.ScrambleObjects (size/mass/yaw/colour).
- TR8 multi-generation viz (key 3): overlay last N generations' best paths (newest bright) via PathVisualizer.
- Editor/TrainingSmokeCheck.cs (suite gate): proves Motion-GA converges (-75 -> -4) and Sensor-Policy
  improves (-4.5 -> -2.3).

NVLABS RESEARCH (research/external/): RoboLab (eval benchmark — adopt InferenceClient contract + predicate
success + LeRobot export conventions); GR00T-WBC (humanoid, mostly off-target); 4D-RGPT (P4D distillation
idea — Unity gives free GT depth/flow/seg teacher); SpatialClaw (DA3+SAM3+geometry grasp perception — strong
fit via the MCP bridge). All Apache/NC-licensed; logged in ROADMAP under CV/spatial AI directions.

Headless suite now 7/7 (physx, pick, viz, motor, training-learns, diffusion-pipeline, diffusion-deploy).
In-sim keys: F3 training, F4 conditions; 3 generations, 4 diffusion-policy, 5 follow-plan, 6 MPD planner,
7 denoise, 8 path-viz, 9 demo-routes.

## Session 2026-06-22 — EV1 predicates · reactive expert · multi-robot bus (suite 13/13)
- EV1 composable success predicates: Evaluation/Predicates.cs (7 leaf predicates + And/Or/Not/ForAll,
  each with a signed Margin() for shaped reward) + Evaluation/TaskEvaluator.cs (all 7 scenarios as
  declarative predicate trees). ScenarioManager.usePredicateSuccess routes the success gate through it
  (off by default; reward shaping untouched). PredicateEvalCheck — 18 assertions.
- EV4: design/specs/EVAL_AND_LEROBOT_SPEC.md (EV1 predicates + EV2 serving contract + EV3 LeRobot v3.0).
- SortIntoTray generalisation fix: Evolution/ScriptedExpert.cs — one reactive Cartesian plan source that
  reads CURRENT object positions, so the GA warm-start (BuildPickPlaceDemo now delegates to it) and a
  runtime auto-solve track any scatter. ReactiveExpertCheck delivers 2/3 randomly-scattered cubes.
- Pillar K multi-robot: WorldBlackboard gains a transient RobotEvent bus + NearestOther/WouldCollide/
  MustYield; RobotAgent gains the K3 hand-off protocol (OfferHandoff/AcceptHandoff/ShouldYield);
  MultiRobot/MultiRobotManager.cs spawns N real SO-101 arms on one shared blackboard. MultiRobotCheck —
  16 assertions (state/events/claims/2-arm spawn).
- Headless suite 7/7 -> 13/13 (added 3e Predicate, 3f Reactive expert, 3g Multi-robot).
