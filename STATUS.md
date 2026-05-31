# ARMSMITH — STATUS & HANDOVER

> **Read this first.** Single source of truth for where development is up to. Update at the end of
> every work session. Newest status at the top of each section.

Repo: https://github.com/fivelidz/armsmith-robot-arms (private)
Engine: Unity 6000.4.2f1, URP, ArticulationBody physics. Units = metres. Arm = real SO-101 STL.

## HOW TO RUN (every session)
```bash
cd /home/fivelidz/projects/unity_projects/robot_arms
./scripts/unity_start.sh        # launches editor (SDL x11 fix) + waits for MCP bridge :6990
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
- [ ] Warm-start policy population from recorded demos -> train to actual SUCCESS on a task.
- [ ] Build the HTML windows as in-game Unity windows (menu/options/training).
- [ ] Pillar J: import ORCA Hand via URDF (catalogue) ; Pillar K: 2nd arm + comms + hand-off.
