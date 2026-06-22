# ARMSMITH — STATUS & HANDOVER

> **Read this first.** Single source of truth for where development is up to. Update at the end of
> every work session. Newest status at the top of each section.
>
> **NEW SESSION? Read `HANDOVER.md` first** — full state, architecture, how-to-run, what works/pending,
> gotchas, and next steps. (Headless suite: `./scripts/run_checks.sh` — currently 16/16.)

## SESSION 2026-06-22f (latest) — UI polish + playtest verification (every element checked live)
- **Visual polish (kept colours + font)**: `UiTheme.Btn` is now a real button — taller (28px min), padded,
  rounded, faint accent-tint fill + **hover state** (PointerEnter/Leave brighten); new `BtnPrimary` (solid
  accent fill, dark text) for the one main action per screen; `SetActive` gives a clear filled/outline state.
  `Panel` has a thicker 3px accent edge + clipped corners; `PanelHeader` gains an accent tick before the
  title; `Badge` is now a filled pill; new `CardEl` hoverable content card (left accent bar) used by the
  scenario grid. Scenario LAUNCH buttons are solid + difficulty-coloured (green/teal/orange).
- **Manual-control UX**: Dashboard shows a "⚙ TRAINING IS DRIVING THE ARM" banner with a "✋ Take Manual
  Control" primary button (stops training + switches to Manual) whenever a run is active — no silent fight.
- **Bug fix**: `RecordGeneration` mean/best now ignore -Infinity/NaN genomes, so POP MEAN no longer shows
  -Infinity mid/after a generation (verified live: mean 12.03).
- **Playtested every element LIVE via the bridge** and confirmed each does what it states:
  mode switch (IK↔Manual), gripper close/open + GRASPED chip, mouse-follow active state, training run
  (gen feedback best/mean/success update; success ACHIEVED ✓), take-manual-control (stops + switches),
  presets (Robust→pop32/DR0.8/SensorPolicy), sensor mask (obs 68→27 when IMU+Lidar off), scenario load,
  catalogue generate, URDF convert, servo torque saturation, conditions save/load round-trip, Build joint
  limit edit applies, module advisor recommends. Suite still 16/16.

## SESSION 2026-06-22e — CONDITIONS PERSISTENCE (all training conditions now save/restore)
- **SaveState v2**: now persists the ENTIRE `TrainingConfig` (reward weights + per-term enables, domain-
  randomization ranges, termination/success params, curriculum difficulty, GA hyperparameters, sensor mask)
  + global settings (usePredicateSuccess, SensorRealism enabled/noise/latency, sim speed, policy mode).
  Previously NONE of the training conditions were saved — only arm/sequence/scenario/sensor-flags/zero-pose.
- **Auto-save**: conditions auto-save on Apply, every 30s, and on quit/pause; **auto-load on Start**
  (conditionsOnly — restores conditions/settings WITHOUT yanking the scene to a different scenario).
- **UI**: Training view gains a "Conditions Persistence" section (APPLY + SAVE / RELOAD + status); module
  loadout toggles and Options "Apply + Save" autosave immediately. Wired saveSystem into UiManager.
- **Tested**: new headless `ConditionsPersistenceCheck` (35 assertions — save→mutate→load→verify every field
  + conditionsOnly keeps scenario). Suite 15→16/16. LIVE-verified: wReach 2.22 / difficulty 0.66 round-trip
  in the running editor; autosave.save.json on disk (schema v2, full config). GUI relaunched + running.

## SESSION 2026-06-22d — UI/UX overhaul from industry research (Isaac/Foxglove/Onshape/W&B)
- Researched leading robotics-sim/training tools and applied concrete patterns. Suite still 15/15; UI check 32.
- **Training Conditions (Isaac Lab RewardsCfg/EventsCfg/TerminationsCfg patterns)**: presets (Quick GA /
  Robust / Sim-to-real / Reach-debug); per-term reward TABLE (toggle + weight slider, 7 terms); domain-
  randomization as named ranges (spawn ±, yaw ±, mass ×, friction ×) scaled by a DR master; termination vs
  success editor (timeout, success-hold, advance-@-success, terminate-on-OOB, EV1 predicate toggle).
- **Live Dashboard (W&B/TensorBoard pattern)**: metric TILES with Painter2D SPARKLINES (best/mean/success
  history) + a curriculum STEPPER (L0→L4, current highlighted, auto-advance flashes).
- **Controls**: big clickable MODE pill in the nav (◀ IK/MANUAL ▶) — kills mode confusion; Dashboard joint
  telemetry upgraded to GAUGES (angle-within-limit, amber/red near limits) + a grasp/contact STATUS CHIP;
  Record/Auto-solve/STL/Waypoints buttons now wired to real actions.
- **Module add menu (Modules view)**: mounted-loadout list (eye-toggle per module) + module CATALOG cards
  (mount/enable/disable, advisor hints) + obs-channel & mass BUDGET readout; mount-socket list.
- **Creation menu (Build view)**: parametric joint/link editor (live limit edit per joint) + CREATIONS
  LIBRARY gallery (best-of-gen cards with fitness/success + ▶ Replay) backed by EvolutionStore.
- **Widgets (UiTheme)**: Gauge/SetGauge, Sparkline, MetricTile, StatusChip, DualRange, SemColor (Foxglove
  semantic colour grammar: green ok / amber near-limit / red over).
- Nav now: Menu · Dashboard · Build · Modules · Catalogue · Training · Options · Help (8 views).
- TrainingConfig: per-term enable flags, DR ranges, termination params, ApplyPreset(4). Help view updated.

## SESSION 2026-06-22c — catalogue · URDF import · servo torque · sensor realism · advisor · sensor-only
- **J2 Robot catalogue** (`Catalogue/RobotCatalogue.cs`): registry of importable robots (SO-101 + parametric
  3/5/6-DOF generated arms). `GenerateParametricKinematics` writes a valid kinematics JSON; `ResolveKinematicsPath`
  generates on demand — all loadable by the existing `BuildFromKinematics`. New **Catalogue view** in the UI.
- **J3 URDF importer** (`Catalogue/UrdfImporter.cs`): parses standard URDF XML → the kinematics schema
  (links/joints, rad→deg, continuous→wide revolute, fixed preserved) → registers a catalogue entry. "Scan
  import folder" button reads persistentDataPath/Import/*.urdf. Verified: a 2-DOF URDF converts + BUILDS.
- **F-r1 servo torque saturation** (`ServoModel`): datasheet speed/torque curve — `AvailableTorque(speed)`
  falls linearly stall→no-load; `SaturateTorque` / `IsTorqueSaturated` for honest sag/slip + training penalties.
- **F-r2 sensor realism** (`SensorRealism` + `SensorBase.ObserveNoisy`): global noise (relative+absolute
  Gaussian) + latency (frame ring buffer); `SensorHub.BuildObservation` uses it. Toggle in the Training view.
- **S10 module advisor** (`Evolution/ModuleAdvisor.cs`): records (task, sensor-set)→best success/fitness across
  ablation runs; recommends the best set (tie-break: fewer channels). Trainer feeds it each generation; shown
  in the Training view ("best so far for TrayToTray: …").
- **SP1 sensor-only teleop** (`UI/SensorOnlyMode.cs`, Shift+S): blacks out the god-view and surfaces every
  enabled sensor module's live channels — the exact information budget a policy gets (human-vs-policy compare).
- **Tested**: new `ElementsCheck` (24 assertions) + UI check now covers the Catalogue view; **suite 14→15/15**.
  LIVE-verified in the GUI: Catalogue view + Sensor-Only mode screenshotted working (real SO-101 STL arm renders).

## SESSION 2026-06-22b — UNIFIED UI TOOLKIT INTERFACE (built · incorporated · live-verified)
- **New interface system** (`UI/UiTheme.cs` + `UI/UiManager.cs` + `UI/UiManager.Views.cs`): a single
  UI Toolkit overlay in the robotics-console design language (from design/ui_html/) with a top NAV BAR,
  live STATUS BAR (sim/arm/task/IK/mode/gen/fps), and 5 switchable VIEWS:
  - **Menu** — ARM·SMITH splash + Navigate + all 7 scenario cards (difficulty dots + Launch).
  - **Dashboard** — Driver/Teleop (mode/gripper/EE/grip + record/auto-solve), live Joint Telemetry
    (6 colour-coded joints), Task & Export (objective/reward/success bar + STL/Waypoints + safety).
  - **Training** — intelligence pipeline (text→plan→skill→control→physics), live dashboard (backend/gen/
    best/mean/success + fitness curve via Painter2D), observation-composition toggles (live obs-channel count).
  - **Options** — sim speed/solver/gravity, mouse-follow, UI scale, randomness/difficulty/curriculum/
    EV1-predicate toggle/GA params, "Apply to trainer". Sliders two-way bound to live values.
  - **Help** — full controls reference by category (conveys everything to the user).
- **Runtime, no asset-authoring**: `UiTheme.GetPanelSettings()` builds a PanelSettings in code + loads the
  runtime theme + shared USS from `Resources/UI/` (copied ArmSmithTheme.tss + ArmSmithUI.uss).
- **Incorporated**: GameBootstrap builds it behind **F1** (additive). When the overlay is up it HIDES the
  legacy uGUI HUD so they don't overlap; F1 again restores the legacy panels.
- **Revived the orphaned UXML**: fixed the unsupported `:last-child` USS selector; ArmSmithHud/ArmSmithUI.uxml
  remain valid (covered by the check).
- **Tested**: new headless gate `UiToolkitCheck` (26 assertions: theme/PanelSettings/USS + nav + all 5 views
  build + per-frame refreshers run + legacy UXML declares its named elements). **Suite 13/13 -> 14/14.**
  Also LIVE-verified in the GUI: all 5 views screenshotted rendering correctly; F1 legacy/new swap clean.

## SESSION 2026-06-22 — EV1 predicates · reactive expert · multi-robot bus (suite 13/13)
- **EV1 composable success predicates** (RoboLab-style): `Evaluation/Predicates.cs` (NearXZ, Near,
  EeReaches, BelowHeight, AboveAligned, AtRest, Grasping + And/Or/Not/ForAll combinators, each with a
  signed `Margin()` for shaped reward) + `Evaluation/TaskEvaluator.cs` mapping all 7 scenarios to a
  declarative predicate tree. `ScenarioManager.usePredicateSuccess` routes success through it (off by
  default to preserve approved reward shaping); `PredicateDescription()` gives the English breakdown.
  New gate `PredicateEvalCheck` (18 assertions). Tolerances copied verbatim — behaviour preserved.
- **EV4**: wrote `design/specs/EVAL_AND_LEROBOT_SPEC.md` (EV1 predicates + EV2 InferenceClient serving
  contract + EV3 LeRobot v3.0 export authority).
- **SortIntoTray generalisation FIXED**: `Evolution/ScriptedExpert.cs` — one reactive Cartesian plan
  source that reads CURRENT object positions, so warm-start + auto-solve track ANY scatter. Trainer's
  `BuildPickPlaceDemo` now delegates to it (removed duplicated per-scenario plan code). New gate
  `ReactiveExpertCheck` delivers 2/3 RANDOMLY-scattered cubes into the tray (was the known limitation).
- **Pillar K multi-robot** (K1/K2/K3): extended `WorldBlackboard` with a transient EVENT bus
  (RobotEvent pub/sub) + NearestOther/WouldCollide/MustYield coordination helpers + K3 hand-off
  protocol on `RobotAgent` (OfferHandoff/AcceptHandoff/ShouldYield). `MultiRobot/MultiRobotManager.cs`
  spawns N real SO-101 arms at offset bases facing a shared workspace, all on one blackboard. New gate
  `MultiRobotCheck` (16 assertions: state/events/claims/2-arm spawn).
- **Headless suite now 13/13** (added 3e Predicate, 3f Reactive expert, 3g Multi-robot to run_checks.sh).

## SESSION 2026-06-19 — the system PERFORMS THE TASK across all scenarios
- **GA solves the task by default**: warm-start from an IK pick-place demo + GA refine. Best fitness
  ~14-18 (task complete + success bonus). Verified live via the F7 Generations panel.
- **Scenario-aware warm-start — ALL 7 scenarios now solve at 100%**: ReachTouch, PickPlaceCube,
  PushToZone, TrayToTray, DropInBin, StackTwo, SortIntoTray (multi-object, 18-key demo). Was only
  TrayToTray before; each non-tray task scored 0% because the demo was hardcoded to S_Cube->tray.
- **Honest success metric**: lastSuccessRate now reflects the BEST genome of the generation (added
  MotionGenome.succeeded), fixing the 100/0/100 flicker.
- **Persistence**: best-of-gen "creations" saved to persistentDataPath/Evolution/creations.json each
  gen; resumable checkpoint (population+history) saved on demand AND auto-saved every 5 gens; both
  load on Init. ReplayCreation() replays a saved best in-scene.
- **Wrist kinematics verified vs real SO-101** (so101_new_calib.urdf): wrist_flex ±95deg pitches the
  tip up/down; wrist_roll [-157.2,162.8] rolls the gripper about the forearm axis — both track exactly.
- **Wrist cam FOV 80deg + nearClip 0.01m** to match the real UVC module (CAMERA_VISION_SPEC).
- **New CI gate**: TrainingTaskSuccessCheck (pick-place moves the cube to target). Suite now 10/10.
- Known limitation: SortIntoTray demo uses fixed cube positions; under per-rollout RandomSpot it solves
  the eval but won't generalise to arbitrary scatter without re-solving IK per reset (future work).
- Commits: 8efeb88 (task gate) · 88a8659 (scenario-aware) · f927514 (multi-obj sort) · 324f5a7 (auto-ckpt).

## SESSION 2026-06-16 — major progress
- **Launch FINALLY fixed**: native Wayland (`SDL_VIDEODRIVER=wayland`) — no more XWayland poison / graphics restarts. See HOW TO RUN + tooling gotcha below.
- **UI overhaul**: CanvasScaler configured (fonts legible at 2560×1440), panels de-overlapped (Training F3 / Conditions F4 default hidden), F-key clashes fixed (sensor toggles -> Shift+F2..F7, Verification -> F6), BuilderPanel internal overlap fixed.
- **Gripper 'exploding' / holding-physics bug FIXED**: the fixed jaw was a near-zero-mass LOCKED ArticulationBody that diverged in the PhysX solver (flew to y=-100m, gripper came apart). Now a plain rigid collider — stable 20s+. This was the real "holding physics seems wrong".
- **Wrist camera fixed**: was stuck top-down (got the same transform for tip+body); now derives a grasp basis from the JAW transforms and looks OUT, framing both jaws.
- **Realistic friction grasp** (opt-in `Gripper.realisticGrasp`): force-limited dynamic follower, slips/drops on weak grip. STS3215 servo speed corrected to 270°/s.
- **GENERATIONS & CREATIONS UI (F7)** + persistence: `EvolutionStore` saves best-of-gen creations + resumable checkpoints to `persistentDataPath/Evolution/`; trainer auto-captures creations, `ReplayCreation()` replays a saved best in-scene; panel shows fitness curves, creations list (with Replay), population grid (click to lock survivors). `wOob` reward weight now actually penalises knocking the object off. Verified live: ran 3 gens, 3 creations saved to disk, replay works.
- **CV grasp-geometry toolbox** (`scripts/vision/`, 24 tests) — CPU side of the DA3+SAM3 pipeline; GPU confirmed usable via ROCm.
- Commits: ee42d94 (UI) · 24a70c0 (grasp+servo) · d3a1404 (CV) · ff79e63 (wayland) · f8ba007 (builder) · 250795d (cam/claw/launch) · 0ec5d77 (generations UI) · e4e70a8 (gripper explosion fix).

Repo: https://github.com/fivelidz/armsmith-robot-arms (private)
Engine: Unity 6000.4.2f1, URP, ArticulationBody physics. Units = metres. Arm = real SO-101 STL.

## HOW TO RUN (every session)
```bash
cd /home/fivelidz/projects/unity_projects/robot_arms
./scripts/unity_start.sh        # staged render strategy (WAYLAND -> vulkan -> gamescope -> xwayland) + waits for bridge :6990
# Force a single render mode if needed: RENDER_MODE=wayland ./scripts/unity_start.sh  (or vulkan|gamescope|xwayland|auto)
# 2026-06-16 REAL FIX: native WAYLAND mode (SDL_VIDEODRIVER=wayland) is now tried FIRST and is the working
# default. Unity gets a Wayland-native surface + OpenGL, so there is NO shared XWayland surface to corrupt
# -> a crash/kill can no longer poison later launches (the thing that forced graphics-session restarts).
# The previous vulkan/gamescope modes never actually engaged here (gamescope died on execv(unityhub.desktop)),
# so every launch fell through to fragile xwayland — THAT is why "the fix" kept failing. See KNOWN TOOLING GOTCHA.
# NOTE: "Selected window backend: (null)" in the log is a RED HERRING — Unity continues past it on wayland.
python3 scripts/mcp.py tool manage_editor '{"action":"play"}'      # play
python3 scripts/mcp.py tool manage_editor '{"action":"stop"}'      # stop
python3 scripts/mcp.py tool refresh_unity '{}'                     # recompile after code edits
python3 scripts/mcp.py console 10                                  # read console
# Screenshot (ALWAYS resize before reading — >2000px crashes the agent):
python3 scripts/mcp.py tool manage_camera '{"action":"screenshot","screenshot_file_name":"s.png","max_resolution":880,"capture_source":"game_view","include_image":false,"output_folder":"Captures"}'
python3 -c "from PIL import Image; im=Image.open('UnityProject/Captures/s.png'); im.thumbnail((880,880)); im.save('/tmp/s.png')"   # then read /tmp/s.png
# Run editor C# (CodeDom: fully-qualified UnityEngine.* names, must end with `return "...";`):
python3 scripts/mcp.py tool execute_code "$(python3 -c 'import json,sys;print(json.dumps({"action":"execute","compiler":"codedom","code":sys.stdin.read()}))' <<<"<c#>")"
```
Gotchas: demo recording needs `Time.timeScale>0`. After adding a NEW C# type referenced cross-file,
if you get phantom "name does not exist" errors, restart the editor (clears stale incremental cache).
Full troubleshooting: `docs/UNITY_STARTUP.md`.

## CONTROLS (current)
- Mouse-follow IK: arm follows cursor on work-plane. `M` toggle on/off. Depth: scroll or `[`/`]`.
- Per-servo direct keys: T/G Y/H U/J I/K O/L P/; (joints 0..5). Claw open/close `,`/`.`. Claw rotate `N`/`B`.
- Camera: RMB orbit, MMB pan, Ctrl+scroll zoom. `V` toggle cam HUD, `B` bounds, `X` axes.
- Scenarios `[`/`]`... (NOTE: conflicts with depth keys — see KNOWN ISSUES). Reset Esc.
- Calibrate to zero: `Z`/`Home`. Pause/resume (move-on-resume): `Enter`. Manual speed: `Shift+,`/`Shift+.`.
- Sequence editor: `K` capture pose, `J` play between keyframes, `Shift+Backspace` delete last.
- Builder panel: `Shift+G` toggle. Scenario menu: top-center clickable + `Shift+F1` toggle.
- Servo callouts: `\` then click a joint -> leader-line panel of its command/output.
- Train: `T` start/stop, `N` +1 gen, `F8` policy(sensor) vs motion mode, `F11` export best.
- Sensors toggle: `F2`-`F7`. Module panel `F12`. STL export `F9`, waypoints `F10`.
- Demo record: `Backspace`. Agent demo: `F1`. Sim speed: `+`/`-`/`0`.

## PILLARS & STATE
| Pillar | What | State |
|---|---|---|
| A Physics arm | SO-101 STL, ArticulationBody, servo twin | WORKS |
| B Control | mouse-follow + per-servo keys + claw | WORKS (IK reach = see issues) |
| C Cameras | main + wrist + env RenderTexture HUD | WORKS (wrist cam orientation bug) |
| D Tasks | 6 scenarios incl tray-to-tray | WORKS |
| E Evolution | motion GA + closed-loop sensor policy | WORKS |
| F CAD/STL export | binary STL + waypoints | WORKS |
| G In-game AI | text command console + skill grammar (pick/place/sort/reach) | WORKS |
| H Real-robot port | Feetech + LeRobot sidecars | WORKS (dry-run) |
| I Sensors | 6 modules + hub + usage panel | WORKS |
| J Open-source catalogue | ORCA Hand studied+cloned; SO-101 done; import path documented | PARTIAL |
| K Multi-robot comms | N arms communicating | TODO |
| L Diffusion control | research done; viz layer + LeRobot demo-converter built; planner TODO | PARTIAL (S7d) |
| M Path visualization | in-world trajectory drawing (multimodal, denoise, IK, executed trail) | WORKS (S7d, code) |

## KNOWN ISSUES (priority order)
1. [FIXED] IK reaches the position indicator on the real SO-101 to ~0.2-0.3 cm. Root cause was FK using
   the simplified config axis enum instead of the real ArticulationBody twist axis -> bad FK -> bad IK.
   Now: capture real twist axis in CalibrateIK + DLS/Jacobian solver (Solve3x3). Verified across workspace.
2. [FIXED] Wrist (claw) camera now LookAt forward along gripper reach (faces the work, not backward).
3. [FIXED] UI legible: top-left HUD on dark panel, 17px bold + outline; servo/module panels larger font.
4. [FIXED] Fly-around: WASD (camera-relative) + Q/E up-down flies the green indicator -> claw follows via IK.
5. [OPEN] Scenario cycle keys `[`/`]` clash with depth keys -> remap (low priority).

## FILE MAP (where things live)
- `UnityProject/Assets/Scripts/`
  - `ArmController.cs` — APPROVED mouse control + IK (CalibrateIK/SolveIK/ForwardKinematics). Protected.
  - `ProceduralArm.cs` (+ `Mesh/UrdfArm.cs`) — arm build (procedural OR real SO-101 from kinematics.json).
  - `Mesh/StlImporter.cs`, `Mesh/StlArmSkin.cs` — runtime STL load + skin.
  - `Gripper.cs`, `ServoModel.cs` — gripper + STS3215 digital twin.
  - `CameraRig.cs` — main/wrist/env cameras + orbit.
  - `ScenarioManager.cs` — tasks + reward + objectives.
  - `Sensors/` — ISensor, Sensors.cs (6 modules), SensorHub.cs.
  - `Evolution/` — MotionGenome, PolicyGenome, EvolutionTrainer.
  - `Agent/AgentCommands.cs` — text command interface.
  - `Export/` — StlExporter, BehaviourRecorder (waypoints), DemoRecorder (imitation demos).
  - `UI/` — ServoPanel (motor values), ModuleUsagePanel (sensor usage), ArmSmithHud (UI Toolkit, unused).
  - `MouseInteraction.cs` — dbl-click grab/place, draw-path.
  - `GameBootstrap.cs` — builds the whole scene + HUD at runtime.
- `UnityProject/Assets/Meshes/SOARM100/` — STL meshes + kinematics.json + notes.
- `scripts/` — mcp.py (MCP caller), unity_start.sh, realbot/ (real-arm sidecars).
- `design/` — GAME_DESIGN.md, ROADMAP.md, PROMPT_LOG.md, PROGRESS.md, specs/, ui_html/.
- `research/` — the research library (6 reports + index).

## NEXT ACTIONS (live)
- [x] DLS/Jacobian IK; wrist cam; legible UI; fly-around; calibrate/speed/pause.
- [x] Clickable ControlBar + on-arm servo arrows + colour-coded servos + radial gauges.
- [x] Base-bend (shoulder_lift) axis fix.
- [x] Live text-command console + skill grammar (pick/place/sort/reach resolve live objects). VERIFIED.
- [x] Randomized scenarios + reset-eval training + success-rate metric.
- [x] UI windows (HTML): menu, options, training overview. ORCA Hand studied+cloned.
- [x] CRACKED pick-place non-determinism (S7): root causes = SelfCollision ignore-pairs dropped after
      MeshCollider cooking (jammed low reach), articulation corruption at extreme poses, held-cube
      collision feedback. FIXES: SelfCollision continuous re-assert; IK safety envelope; HardHome/
      HardResetJoints recovery; Gripper held-cube collision-ignore + floor guard; graded drive tiers.
      Catastrophic 44cm jams eliminated; grasp reliable; multi-trial pick repeatable.
- [ ] RESUME (needs fresh graphics session): run final post-fix multi-trial pick-place verification
      (lift-from-grasp) now that held-cube collision-ignore is in. Target >=4/5 success.
- [ ] Warm-start policy population from recorded demos -> train to actual SUCCESS on a task.
- [ ] Build the HTML windows as in-game Unity windows (menu/options/training).
- [ ] Pillar J: import ORCA Hand via URDF (catalogue) ; Pillar K: 2nd arm + comms + hand-off.

## S7d MILESTONES (diffusion + viz + PhysX crash fixed)
- PhysX articulation crash (setupDescTask segfault) — ROOT-CAUSED (over-stiff drives on light links +
  first-step depenetration of overlapping STL) and FIXED. Proven via headless Editor/PhysxStabilityCheck
  (builds arm + 600 Simulate steps, fails on NaN): PASSED 3/3. Run it as a regression gate:
  `Unity -batchmode -nographics -executeMethod ArmSmith.EditorTools.PhysxStabilityCheck.RunHeadless -quit`
- Editor launch failures were a LAPSED UNITY LICENSE (re-activate via Hub; open the inner UnityProject/
  folder). The "Selected window backend (null)" line is a red herring.
- Diffusion: research/diffusion_pathfinding/REPORT.md; Visualization/ module (PathVisualizer +
  TrajectoryData + PathProviders, toggle keys 8/9/0); DF1 scripts/realbot/waypoints_to_lerobot.py.

## KNOWN TOOLING GOTCHA — *ACTUALLY* FIXED 2026-06-16 (native Wayland)
- THE REAL FIX: launch Unity on **native Wayland** (`SDL_VIDEODRIVER=wayland`), now the first/default mode
  in `unity_start.sh`. Unity uses a Wayland-native surface + OpenGL; there is no shared XWayland surface to
  corrupt, so crashing/killing the editor can NOT poison later launches. Verified: bridge up, scene renders,
  survives repeated restarts. `RENDER_MODE=wayland ./scripts/unity_start.sh` to force it.
- WHY THE OLD "FIX" KEPT FAILING: the staged vulkan->gamescope->xwayland strategy never engaged its safe
  modes on this box — vulkan dies, and gamescope dies because Unity (failing to grab a window inside it)
  tries to relaunch via Hub and hits `execv(unityhub.desktop): Permission denied`. So every launch fell
  through to plain `xwayland` (SDL_VIDEODRIVER=x11), the fragile shared-XWayland path a hard pkill poisons.
- "Selected window backend: (null)" is a RED HERRING on wayland mode — Unity logs it then continues,
  creates the GL device, loads MCP, starts the bridge.

## (historical) KNOWN TOOLING GOTCHA (S7) — partially mitigated, see above for the real fix
- SYMPTOM: after a Unity-6 editor GUI crash, the next launch hangs at "Selected window backend: (null)"
  and only a full graphics-session restart cleared it.
- ROOT CAUSE: KDE Plasma 6.5 *Wayland* + AMD Radeon 8060S/RADV/Mesa 25.3. Unity renders OpenGL via
  GLX -> XWayland. A hard crash leaves Unity's XWayland/GLX surface un-released, poisoning the SHARED
  XWayland for all later launches. A session restart works only because it respawns XWayland. (Plasma X11
  session is NOT available — kwin_x11 was removed in Plasma 6.5.)
- FIX (in scripts/unity_start.sh): staged render strategy, default order vulkan -> gamescope -> xwayland.
    * vulkan   : -force-vulkan on XWayland (Vulkan WSI skips the brittle GLX surface; RADV is solid here).
    * gamescope: run Unity inside a nested `gamescope` micro-compositor with a Vulkan-backed surface,
      ISOLATED from the desktop XWayland — so a Unity crash takes down only gamescope, not the session.
      This is the one that should END the restart cycle. (gamescope v3.16.19 verified: selects RADV,
      sets up Vulkan + nested wayland server.)
- Headless `-batchmode -quit -nographics` still works for compile checks regardless.
- Still: don't drive risky live-physics experiments (e.g. IgnoreCollision on a held kinematic body) from
  the bridge — do them in code. One such experiment segfaulted the editor this session (that triggered the
  XWayland poisoning above). The fix moved that logic safely into Gripper.TryGrab.
