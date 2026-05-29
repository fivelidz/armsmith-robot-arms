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

## How to run
1. MCP server + editor already launched (port 6990). Helper: `scripts/mcp.py`.
2. `python3 scripts/mcp.py tool manage_editor '{"action":"play"}'` to play.
3. Screenshot: `manage_camera screenshot ... output_folder=Captures max_resolution<=900` then
   resize to <=900px before reading (crash-prevention rule).
