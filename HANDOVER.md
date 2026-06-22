# ARMSMITH — Session Handover

> Last updated: 2026-06-14 (end of Session 7g). Read this FIRST when resuming.
> Project: `/home/fivelidz/projects/unity_projects/robot_arms` · Private GitHub repo (all work pushed).

---

## 0. TL;DR — where we are

ARMSMITH is a Unity game/sim where you design, control, evolve, train, and (eventually) deploy a realistic
**Seeed SO-101 / SO-ARM100** 6-DOF robot arm to solve manipulation tasks. Engine: **Unity 6000.4.2f1**, URP,
**ArticulationBody** physics, units = metres. Driven via the **MCP-for-Unity bridge** (TCP port 6990).

**The pick-and-place task WORKS** (reach → grasp → lift, verified). A **13/13 headless regression suite**
guards physics, the task, visualization, training, and the diffusion pipeline. A full **training regimen +
UI** is built (backends, curriculum, reward shaping, conditions, multi-generation viz). Diffusion (planner +
trainable policy + inference server) and in-world path visualization are functional.

**The one persistent friction:** the GUI editor's *background launch from the automation shell* is
unreliable — but **headless `-batchmode` verification works 100%** and is the dependable path. See §6.

---

## 1. The single most important lesson: VERIFY HEADLESS

The interactive Unity GUI on this host (KDE Plasma 6 Wayland + AMD Radeon 8060S/RADV/Mesa 25.3) is flaky to
launch *from the automation shell* (it imports then exits when the launch command returns; `systemctl`/
`systemd-run` are DENIED by the sandbox). **Do not burn time fighting it.** Everything important is verified
headlessly via `-batchmode -nographics -executeMethod` Editor checks. Run:

```bash
cd /home/fivelidz/projects/unity_projects/robot_arms
./scripts/run_checks.sh            # full 7-gate suite (~6-8 min)
./scripts/run_checks.sh quick      # Python/diffusion gates only (fast)
```

Individual gates (each is a `-executeMethod` you can run directly):
```
ArmSmith.EditorTools.PhysxStabilityCheck.RunHeadless   # arm builds + 600 sim steps, no NaN
ArmSmith.EditorTools.HeadlessPickCheck.RunHeadless     # reach+grasp+LIFT end-to-end (the task gate)
ArmSmith.EditorTools.VizSmokeCheck.RunHeadless         # path-viz + diffusion planner data layer
ArmSmith.EditorTools.MotorPhysicsCheck.RunHeadless     # drive tracking, servo rate/ticks, gravity hold
ArmSmith.EditorTools.TrainingSmokeCheck.RunHeadless    # GA + Policy backends LEARN
ArmSmith.EditorTools.DescentCheck.RunHeadless          # FK-vs-physical match diagnostic
ArmSmith.EditorTools.LiveIkCheck.RunHeadless           # live IK-target control loop (known ~7cm residual)
```
Run one directly:
```bash
env DISPLAY=:0 "$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity" \
  -batchmode -nographics -projectPath "$PWD/UnityProject" \
  -executeMethod ArmSmith.EditorTools.HeadlessPickCheck.RunHeadless -quit -logFile - 2>&1 | grep -a "PASSED\|FAILED"
```
A plain compile check (zero CS errors expected):
```bash
env DISPLAY=:0 "$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity" \
  -batchmode -quit -nographics -projectPath "$PWD/UnityProject" -logFile - 2>&1 | grep -a "error CS\|Exiting batchmode"
```

---

## 2. THE big bug that's fixed (don't reintroduce it)

For many sessions the arm "wouldn't descend / IK was bad / task impossible". **Root cause: the IK's
forward-kinematics model was wrong by ~30 cm** vs the real ArticulationBody chain (a buggy inverse-frame
reconstruction in `CalibrateIK`). The IK solved correct-looking angles against a broken model, so the arm
floored ~25 cm high.

**Fix (S7f, in `ArmController.cs`):** rewrote `CalibrateIK` + `ForwardKinematics` with a robust
rest-world-transform formulation — capture each joint's REST world position + REST world twist axis + rest
angle, **sync `targetAngles` to the ACTUAL joint angles before capturing**, then FK walks root→tip applying
each joint's DELTA angle about its chain-carried axis. **FK now matches the physical tip to 0.0 cm.**
`DescentCheck` verifies this ("ARM DESCENDS CORRECTLY"). If FK/physical ever diverge again, that's the
regression to hunt.

Other crash fixes that must stay:
- **PhysX `setupDescTask` segfault** was caused by (a) over-stiff drives on light links → NaN, and (b) the
  arm base spawning *inside* the worktop (both at y=0) → violent depenetration. Fixed by: lower stiffness +
  conditioned inertia + joint friction; `maxDepenetrationVelocity`/velocity caps; `SelfCollision` ignoring
  ALL internal arm pairs (dedicated arm layer); `GameBootstrap.IgnoreArmVsEnvironment` (arm-vs-static).
- **HardResetJoints** must defer the `SetJointPositions` teleport to the next FixedUpdate (inline = PhysX
  crash). It's queued via a pending flag in `ProceduralArm`.
- **Editor launch failures** were a **lapsed Unity license** at one point — re-activated via Hub. The
  "Selected window backend (null)" log line is a red herring.

---

## 3. Architecture map (64 C# scripts under UnityProject/Assets/Scripts/)

**Core arm**
- `ProceduralArm.cs` — the articulation (joints, servos, mass/inertia). `BuildFromKinematics(kinPath)`
  builds the real SO-101 from `Assets/Meshes/SOARM100/kinematics.json` + STL meshes. `SetJointTargets`
  (servo-rate-limited drive), `HardResetJoints` (deferred teleport home), `TickHeld` (headless grasp tick),
  gravity-comp scaffold (currently off — joint friction does the holding).
- `Mesh/UrdfArm.cs` — `BuildFromKinematics` + `ConfigureUrdfRevolute` (drive stiffness/damping/inertia/
  friction; the depenetration + solver-iteration hardening lives here). **Drive config is here.**
- `ServoModel.cs` — STS3215 digital twin (4096 ticks, 360°/s, 1.6 N·m, tick quantize, rate limit).
- `ArmController.cs` — **APPROVED/protected** mouse-follow IK control. Holds `CalibrateIK`,
  `ForwardKinematics` (the FK fix), `IKAnglesFor` (multi-seed elbow-up/down solver), `SolveIK` (live loop),
  `HardHome`, `TickControl` (headless one-step), `TestReach`/`TestReachWith`. Mode enum: IK / Manual.
- `Gripper.cs` — parallel jaws, grasp-assist (`HeldFollow` parents object kinematically), floor guard,
  held-object collision-ignore.
- `SelfCollision.cs` — ignores ALL internal arm collider pairs (layer-based + per-pair); `MaxSelfPenetration`
  is a training metric only.

**Sensors** (`Sensors/`) — `ISensor`/`SensorBase`, `SensorHub` (BuildObservation/SetEnabled), modules:
MotorEncoders, **TaskState** (EE pose + gripper + joint velocities + vector-to-target — key for manip),
IMU, RangeFinder, Lidar2D, DepthCamera, EFleshTactile. `SensorViz`, `GripDetector`.

**Scenarios** — `ScenarioManager.cs`: 7 scenarios, `ComputeReward`, `Reroll`, **out-of-bounds auto-reset**
watchdog, **ScrambleObjects** (size/mass/yaw/color domain randomization scaled by `randomness`).

**Training / evolution** (`Evolution/`)
- `MotionGenome.cs` (keyframe GA), `PolicyGenome.cs` (MLP), `EvolutionTrainer.cs` (both backends + diffusion
  hook), `TrainingConfig.cs` (the shared config struct — backend, difficulty, randomization, reward weights,
  sensor mask, GA params). Trainer has `ShapedFitness`, per-gen history, `ApplyConfig`, `StepOneGeneration`,
  `ResetTraining`, `CaptureBestTrajectory`, `BestToTrajectory`, `SaveBestAsDemo`.

**Visualization** (`Visualization/`)
- `TrajectoryData.cs` (TrajectorySample/Set + ITrajectoryProvider), `PathVisualizer.cs` (GL drawing +
  obstacle rings), `PathProviders.cs` (IK preview, DiffusionPathDemo, DenoisePathDemo),
  `DiffusionMotionPlanner.cs` + `ObstacleField.cs` (MPD-style collision-free multimodal planner),
  `PlannedPathFollower.cs` (plan→motion), `MultiGenViz.cs` (overlay last N generations' best paths).

**Agent / deploy** — `Agent/AgentCommands.cs` (text command grammar + pick/place skills, RobotAgent),
`Agent/DiffusionPolicyClient.cs` (TCP client → drives arm receding-horizon from the Python policy server).

**UI** (`UI/`) — ArmSmithHud, ControlBar, ServoPanel, ServoCallouts, ScenarioMenu, BuilderPanel,
ModuleUsagePanel, CommandConsole, **TrainingPanel (F3)**, **ConditionsPanel (F4)**.

**Editor checks** (`Editor/`) — the 7 headless `*Check.cs` files (see §1). `_Recovery/` has scene backups.

**Bootstrap** — `GameBootstrap.cs` builds the ENTIRE scene at runtime in Play mode (environment → arm →
scenarios → cameras → sensors → trainer → HUD → IgnoreArmVsEnvironment). The Workshop scene
(`Assets/Scenes/Workshop.unity`) just hosts the bootstrap.

**Python** (`scripts/`)
- `scripts/run_checks.sh` — the regression suite. `scripts/realbot/` — LeRobot bridge
  (`armsmith_lerobot.py`), waypoint verify/convert (`verify_waypoints.py`, `waypoints_to_lerobot.py`),
  `joint_map_lerobot.json`. `scripts/diffusion/` — `train_diffusion_policy.py` (torch + lerobot backends,
  VERIFIED real training), `serve_diffusion_policy.py` (DF4 inference server), README.
- `scripts/mcp.py` — drive the live editor over the bridge (when GUI is up).

---

## 4. In-sim controls (when the GUI is running)
- **F3** Training panel (backend selector, curves, start/stop/+1gen). **F4** Conditions panel (difficulty,
  randomization, reward weights, sensor toggles, GA params).
- Path/training viz keys: **3** generations · **4** diffusion-policy · **5** follow-plan · **6** MPD planner
  (+obstacles) · **7** denoise · **8** path-viz toggle · **9** demo-routes.
- Control: mouse-follow IK (M toggle), depth scroll, double-click grab/place, RMB orbit, V HUD, scenario
  buttons (top), T/N train, F11 export best (+ GA demo), Esc reset, Ctrl+S/L save/load.

---

## 5. What WORKS (verified) vs PENDING

**WORKS (headless-verified, 13/13 suite):**
- Realistic SO-101 build; physics stable (no PhysX crash); motor model realistic (servo rate/ticks/hold).
- **Pick-and-place: reach (3.4cm) + grasp (~3cm) + LIFT** (cube to ~0.1-0.2m) via analytic IKAnglesFor path.
- Manual/IK control not regressed; FK matches physical exactly.
- Diffusion: in-sim MPD planner (collision-free multimodal), trainable Diffusion Policy (loss drops),
  inference server (samples action chunks over TCP), Unity client.
- Training regimen: Motion-GA + Sensor-Policy LEARN; config/curriculum/reward-shaping/conditions wired;
  Training+Conditions UI; scrambled-world randomization; multi-generation viz.
- Diffusion data pipeline: GA demo → safety-verify → LeRobot dataset → train → serve.

**KNOWN-LIMITED / PENDING:**
- **Live continuous IK-target loop (mouse-follow)** tracks X/Z well but settles ~7-8cm high on LOW targets:
  the continuous Jacobian re-solve oscillates with the stiff drive. The DISCRETE analytic-hold path (used by
  the pick routine) is accurate. Proper fix = gravity-comp / computed-torque control. NOT a task blocker.
- Large extended joint angles **sag realistically** (honest small-servo physics) — curriculum keeps targets
  in the dependable envelope.
- **Live GUI visual confirmation** of the full scene + UI panels is unconfirmed (launch friction). Code is
  headless-verified; when opened via Hub it should run.

---

## 6. How to launch the GUI (if you must) + recovery
1. Open the project through **Unity Hub** GUI (most reliable): add/open
   `/home/fivelidz/projects/unity_projects/robot_arms/UnityProject` (the INNER folder), press Play, load the
   **Workshop** scene. GameBootstrap builds everything.
2. If launching from CLI, the pattern that *sometimes* works keeps the editor a child of a polling command:
   ```bash
   SDL_VIDEODRIVER=x11 DISPLAY=:0 "$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity" \
     -projectPath "$PWD/UnityProject" -logFile UnityProject/Logs/sim.log </dev/null >/dev/null 2>&1 &
   UPID=$!; for i in $(seq 1 24); do sleep 8; kill -0 $UPID 2>/dev/null||break; \
     ss -tlnp 2>/dev/null | grep -q 6990 && python3 scripts/mcp.py tool execute_code "$(python3 -c 'import json;print(json.dumps({"action":"execute","compiler":"codedom","code":"return \"alive\";"}))')" | grep -q alive && { echo READY; break; }; done; disown $UPID
   ```
   Then drive it: `python3 scripts/mcp.py tool execute_code "$(...)"` with CodeDom C# (fully-qualified
   `UnityEngine.*`, no top-level usings, must `return "...";`). Enter play: `manage_editor '{"action":"play"}'`.
3. If the editor crashes/hangs: `pkill -9 -f "Unity/Hub/Editor/6000.4.2f1"`, `fuser -k 6990/tcp`,
   `rm -f UnityProject/Temp/UnityLockfile`. A bad crash can poison the Wayland session → graphics restart.
4. The bridge can WEDGE after heavy use (returns empty) — restart the editor.
5. NEVER run risky live-physics experiments over the bridge (e.g. IgnoreCollision on a held kinematic body
   mid-step) — one segfaulted the editor. Put such logic in code + verify headless.

---

## 7. Gotchas (the qalcode sandbox + this repo)
- `rm`/deletes inside compound commands are REFUSED — run `rm <path>` standalone (it archives, doesn't
  delete). `systemctl`/`systemd-run`/`sudo` are denied.
- Cloned NVlabs repos in `/tmp` (SpatialClaw/4D-RGPT/roblab) spam the linter with import errors — IGNORE
  them; not our project. (They may have been cleared.)
- Reading images: check dims first, resize to <2000px before Read (large PNGs crash the agent).
- Use `rg`/Glob/Read, not `cat`/`grep`/`find`.

---

## 8. Suggested next steps (priority order)
1. **Live-confirm in GUI** (open via Hub): play Workshop, verify arm reaches/grasps/lifts, F3/F4 panels work,
   keys 3-9 viz, claw camera frames the grasp. Fix anything visual.
2. **Computer-vision / spatial AI** (user-requested; research in `research/external/`):
   - SpatialClaw: stand up a **Depth-Anything-3 + SAM3** GPU tool server; POST the Unity WristCam frame over
     a socket; return grasp pose. Their numpy geometry toolbox is copy-pasteable.
   - 4D-RGPT **P4D distillation**: train an RGB student with depth/flow/seg AUX losses using Unity's free
     ground-truth as teacher.
3. **RoboLab patterns** (user flagged critical): adopt the InferenceClient serving contract + composable
   predicate success-detection (as C# checks) + LeRobot v3.0 export schema → `design/specs/EVAL_AND_LEROBOT_SPEC.md`.
4. **Continuous-IK quality**: implement gravity-compensation / computed-torque so the live mouse-follow
   tracks low targets accurately (removes the ~7cm residual).
5. **Run the real training**: collect demos (F11/recorder) → `waypoints_to_lerobot.py` → `train_diffusion_
   policy.py --backend torch` → `serve_diffusion_policy.py` → key 4 to deploy in-sim.
6. Roadmap also has: 2nd arm + hand-off, ORCA Hand import, CAD evolution, in-game module-placement UI.

---

## 9. Key files to read first when resuming
- `HANDOVER.md` (this file) · `ROADMAP.md` · `STATUS.md` · `design/PROGRESS.md` (session log).
- `design/specs/TRAINING_REGIMEN.md` (the training design).
- `research/diffusion_pathfinding/REPORT.md` + `research/external/NVLABS_*_STUDY.md` (research).
- `scripts/run_checks.sh` (what's verified) · `scripts/diffusion/README.md` (the diffusion pipeline).
- Code entry points: `GameBootstrap.cs`, `ArmController.cs` (FK/IK), `Mesh/UrdfArm.cs` (drives),
  `Evolution/EvolutionTrainer.cs` + `TrainingConfig.cs`.

## 10. User preferences / standing instructions
- Realism > gameplay (sim-to-real fidelity is the priority).
- Keep working autonomously, brainstorm, don't stop; run all verification; use sub-agents for token-heavy
  research. Record intentions in `design/PROMPT_LOG.md`; update STATUS/PROGRESS; commit + push.
- The mouse-follow control in ArmController is APPROVED/protected — additive changes only, don't regress it.
- Don't delete user-approved files; archive before overwrite.
