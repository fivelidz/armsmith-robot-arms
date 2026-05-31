# Prompt & Intention Log — ARMSMITH

A persistent record of the user's prompts and the intentions/decisions derived from them.
Append-only. Newest at the bottom. (Per user request: "Record all prompts and intentions.")

---

## P1 — Initial brief (2026-05-29 / session start)
**User prompt (summary):** Use Unity + Unity MCP to make a game with real-world application and good
physics. Players control and design robot arms to solve tasks, via mouse+keyboard, and run operations
where over generations robot arms learn tasks (loading a box, unscrewing a nut). Player helps design
and select behaviour. We've done STL arms + visualisers before. Want camera displays from different
points. Start with simple problems (pick up object, move to location). Explore GitHubs: eflesh
(robotics), AutoCAD/3D-print creation systems, Claude developments. Game must export STL. Controls/UI
intelligently constructed with keyboard+mouse.
**Specs provided:** Arm = Seeed reBot-DevArm (github.com/Seeed-Projects/reBot-DevArm). Wrist cameras =
AliExpress UVC module. Environment camera = Logitech C922 Pro Stream.
**Instructions:** Make a library folder for fully researching this area. Then plan and start the game.

**Derived intentions:**
- I1: Build research library FIRST (done: research/ with 6 reports + INDEX).
- I2: Engine = Unity 6000.4.2f1, URP, ArticulationBody physics. Units = metres (sim-to-real).
- I3: Start with simplified 3-DOF + gripper, pick-and-place task.
- I4: FABRIK IK, mouse+keyboard control.
- I5: Multi-camera HUD (main orbit + wrist + environment).
- I6: STL export + arm-config JSON.
- I7: Evolution/selection layer (phased: manual -> CMA-ES -> morphology GA -> ML-Agents).
- I8: Use MCP for Unity (com.coplaydev.unity-mcp) to drive the editor.

## P2 — Expansion (CAD evolution, in-game AI, training+export, real-robot port)
**User prompt (verbatim):** "I want to be able to evolve and create autocad designs, I want 0 auth of
claude code to also potentially be possible in game. I want you to work on all the features and how the
behaviour could be trained and then also potentially exported. A means to port the commands to a real
robot arm would be good as well."

**Derived intentions:**
- I9: In-game PARAMETRIC CAD — evolve & create AutoCAD-style designs (real geometry, not just numbers).
  Approach: OpenSCAD-WASM / build123d sidecar (research/cad_3dprint). CAD designs are evolvable genomes.
- I10: ZERO-AUTH in-game Claude Code agent — player talks to an AI inside the game to generate/modify
  arms & parts. "0 auth" = no login friction; runs against a local/bundled agent endpoint.
- I11: Behaviour TRAINING + EXPORT — train arm behaviour (waypoints/policy), export the trained policy.
- I12: REAL-ROBOT PORT — emit commands to drive a real reBot/SO-ARM (LeRobot, degrees-based JSON
  waypoints is the chosen primary format — see research/manipulation_repos/REAL_ROBOT_PORTING.md).

## P3 — Cameras feed training + sim-to-real crossover; find prior arm project; keep spec guides
**User prompt (verbatim):** "like the small robot arm control project I did briefly on this computer.
The computer vision camera attached to the ingame arm should also be displayed as well as another
camera position that can then help with the training and realistically cross across to real life to
follow actual performance and adaptations. Keep writing full spec guides for yourself. Record all
prompts and intentions, follow a roadmap."

**Derived intentions:**
- I13: Locate the user's prior small robot-arm control project on this machine; reuse its conventions
  (servo<->angle, calibration, IK) so the game matches what they already built.
- I14: WRIST CV CAMERA (on the in-game arm) is displayed AND is a training input. Plus a SECOND camera
  position (environment/fixed) also displayed and feeding training.
- I15: Cameras must map 1:1 to REAL cameras (wrist UVC + C922) so trained vision policies cross over to
  real life; the game becomes a way to "follow actual performance and adaptations" of the real arm.
  => Camera intrinsics (FOV, resolution, position) are configurable to match the real rig.
- I16: Keep writing full SPEC GUIDES for myself (design/specs/*.md), maintain ROADMAP.md, keep this log.

## P4 — Use real STL, fix claw-in-desk, camera, vision detection
**User prompt (summary):** Generated arm doesn't look good — use the actual STL model for the game.
Claw was stuck in the desk. Liked the system's general appearance. One camera may have been off.
Camera detection of the claw elements and other features should work too.
**Derived intentions:**
- I17: Load the REAL SO-ARM100/SO-101 STL meshes per link for an authentic look (downloaded to
  Assets/Meshes/SOARM100/). Keep ArticulationBody physics; STL meshes are the visual (+ collider) skin.
- I18: Claw must not penetrate the worktop — proper collision physics, not just clamping.
- I19: Fix the off camera (wrist/env framing/target).
- I20: In-game computer-vision detects the claw jaws + objects in the camera feeds (for training).

## P5 — Collision physics for claw-in-desk
**User prompt (summary):** Implement proper collision physics so the claw doesn't go through the desk.
**Derived intentions:**
- I21: Gripper palm + jaws + links have colliders; worktop is solid; drives yield to contact. (done: palm
  collider added, home pose lifted, EE verified at y=0.238 above worktop=0.)

## P6 — Control linked to servos; clear objectives; good physics; don't stop
**User prompt (verbatim):** "Control of arm should be linked to actual servo motors. Objectives for the
training should also be clear and stated. good physics should be present. Continue doing all and test
all fully. do not stop"
**Derived intentions:**
- I22: Every in-game joint command maps to a real servo position (STS3215 4096-tick), rate-limited like
  the real motor — digital twin. (done: ServoModel per joint, bus ticks shown in HUD.)
- I23: Each scenario states an explicit OBJECTIVE + reward spec, shown in UI. (done.)
- I24: Keep good physics; test fully; never stop.

## P7 — Generation/training controls, mouse-follow, comma/period gripper, record→train→export to real servos, text agent, success condition
**User prompt (verbatim):** "Control of generations, initial training etc should work. Control of the
arm should be able to follow the mouse, claw should be able to open and close based off of , and . key
presses. this control should be able to be recorded to be used for training and be able to be exported
so actual robot arms can follow the commands. Think about how the servo motors have to be activated so
that follow the mouse at different positions. Try to consider ways that a text based agent could also
give it commands and create instructions. Even if the successful role is initially very slow,
successive generations can speed this up. The success condition should work."
**Derived intentions:**
- I25: Generation controls must work reliably: start/stop training, step one generation, reset/seed
  population — via keys AND UI, with clear feedback.
- I26: Arm follows the MOUSE: IK target tracks the cursor projected onto the worktop work-plane (not
  only while LMB held) — smooth real-time reaching.
- I27: Claw opens/closes on `,` (open) and `.` (close) keys (in addition to Space toggle).
- I28: Recorded control → usable for training (seed/imitation) AND exportable so real arms follow it.
  Document the servo-activation chain: mouse pos → IK joint angles → servo rate-limit → 4096-tick →
  SyncWritePosEx, at each mouse position.
- I29: Text-based agent command interface: parse text instructions ("move to tray A", "close gripper",
  "go to x y z", "run 10 generations") into arm/training actions; can generate instruction sequences.
  Slow-but-correct first; later generations speed it up (seed evolution from the agent's demo).
- I30: Success condition must actually fire and be visible.

## P8 — Roadmap + prompts, STL arm, claw-not-through-table
**User prompt (summary):** Add all these points to the roadmap, save all prompts. Continue development.
Keep brainstorming and giving out best solutions. Claw should not go through the table. Arm should
follow the STL designs.
**Derived intentions:**
- I31: All P4-P8 points recorded here + added to ROADMAP.md.
- I32: Reaffirm: claw must not pass through the table (collision); arm visuals follow the STL designs.

## P9 — Positive feedback: mouse control
**User prompt (verbatim):** "Yo fantastic mouse control and following that was excellent"
**Note:** The cursor->work-plane->CCD-IK mouse-follow (I26) is confirmed good by the user. KEEP this
behaviour and feel. CCD IK (replacing the weak FABRIK-angle-reconstruction) reaches within ~3 cm and
stays above the table. Do not regress it.
**Still to fix (observed in test):** grasp mechanics — jaws swipe/knock the cube instead of holding it;
gripper EE dips to y=-0.02 on descend; close only reaches ~0.29. Fix jaw closing force/width + approach.

## P10 — PRESERVE the mouse control (explicit)
**User prompt (verbatim):** "But save that mouse control, that seems like it could be definitely helpful"
**HARD CONSTRAINT:** The cursor->work-plane->CCD-IK mouse-follow control (ArmController.HandleIKInput +
SolveIK CCD + mouseFollow) is APPROVED and PROTECTED. Per ~/CLAUDE.md rules, approved features must not
be removed or regressed. Any future change must keep this behaviour intact. If replacing, keep a copy.

## P11 — Jaw orientation/closing wrong, collision still off, arm should match real STL
**User prompt (summary):** Claw worked on one test but orientation wasn't correct — jaws moved "out and
in unnaturally" and never closed on the box. Collision physics still not right. Arm doesn't match the
real model or controls as it should.
**Diagnosis:** leftJaw anchorRotation=(0,0,90) -> prismatic axis ended up VERTICAL, so jaws slide up/down
instead of opening sideways. Jaws never straddle the cube. Fix prismatic axis to gripper-local X.
**Derived intentions:**
- I33: Fix gripper prismatic jaws to open/close along local X (horizontal), straddling the object;
  fingers offset along X; closed = clamp on object via friction. Verify with a real grasp test.
- I34: Make collision authoritative (jaws+cube+table) — continuous collision on cube, solid contacts.
- I35: Load the real SO-ARM100 STL meshes per link so the arm LOOKS like the real model (I17).

## P12 — Sensor module system + train with all info + more mouse control
**User prompt (verbatim):** "An idea for going forward is to also train with all the information
available. I want to be able to create add on modules so we can see performance with different
information. such as IMU instead of positioning from motors, lidar, single point range finding lidar,
depth camera, eflesh sensor etc. This will let players and people know what modules might help the best
for different tasks. For now generative training should be done with all the information. I'd like to do
more mouse control and see how that translates as that was working well. Save this stuff to the roadmap
and have it elements to consider and keep going for everything on the to do list and test."
**Derived intentions:**
- I36: SENSOR MODULE SYSTEM — pluggable add-on sensors the player attaches to the arm/scene. Each sensor
  produces an observation vector. Players toggle modules to compare task performance with different
  information. Catalogue:
    * MotorEncoders (joint angles from servos — the baseline/default)
    * IMU (orientation + accel/gyro at a link, INSTEAD of motor positioning)
    * Lidar2D (planar scan of ranges around the workspace)
    * RangeFinder (single-point ToF distance from the gripper, "1-point lidar")
    * DepthCamera (RGB-D from the wrist cam — depth per pixel / downsampled grid)
    * EFleshTactile (contact/force at the gripper fingers — magnetic tactile, see eflesh research)
    * (future) ForceTorque wrist, ProximityIR
- I37: Each module = a component implementing a common ISensor interface (Observe() -> float[] + a
  human-readable channel list), discoverable + toggleable. An "observation builder" concatenates the
  enabled modules' outputs into the training observation vector.
- I38: For NOW, generative/evolutionary training uses ALL enabled sensor info (full observation). Later:
  ablation mode (train with subsets) to rank which modules help which task -> "module advisor".
- I39: Comparative analytics — show per-task which sensor set gives best fitness (the player/dev insight
  the user wants: "what modules help best for different tasks").
- I40: MORE MOUSE CONTROL — the cursor->work-plane->CCD-IK control is the winner; extend it (e.g.
  click-to-grab, drag objects, draw a path the arm follows, scrub depth) and observe how it translates
  into recorded trajectories / training seeds.

**Open questions to confirm with user later (do not block):**
- Q1: Which real arm is the primary target to port to — reBot B601-DM (Damiao/CAN) or an SO-ARM100
  (Feetech) you may already own from the prior project?
- Q2: "0 auth Claude Code in game" — acceptable to call a local agent process / your existing
  opencode-shared auth, vs. truly bundling a key? (security: never embed secrets in the build).

## P13 — Private repo, multi-robot ecosystem, module-usage panels, record training, real-world fidelity
**User prompt (summary):** Make a PRIVATE GitHub repo for the whole game. Add to roadmap: inclusion of
other open-source robot systems (ORCA Hand / orcahand and ALL open-source robotic systems). For now it's
on Unity — run/setup in Unity and test all elements. All module outputs should have a PANEL DISPLAY and
a NOTICE of whether they are actually being used / factored in when training. Be able to RECORD initial
training actions (e.g. picking up an object and putting it in a tray). Add to roadmap: MULTIPLE robot
arms with multiple modules that can COMMUNICATE with each other. Explain the IK reach issue. Want the
simulated environment to be like the real world.
**Derived intentions:**
- I41: Create a PRIVATE GitHub repo and push all game code/docs.
- I42: ROADMAP - integrate other open-source robot systems: ORCA Hand (open dexterous hand), plus a
  general open-source robot catalogue (SO-ARM100/101, reBot, LeRobot arms, Koch, Mobile ALOHA, OpenArm,
  Dummy-Robot, eFlesh tactile, etc). Each = importable URDF/STL + joint map.
- I43: Module-output PANELS: every sensor module shows live output values AND a clear "USED IN TRAINING:
  yes/no" indicator (is this module's data actually in the current observation vector?).
- I44: RECORD initial training demonstrations - capture a hand-driven pick-and-place-into-tray run as a
  labelled demo usable to seed/bootstrap training (imitation seed).
- I45: ROADMAP - MULTIPLE robot arms, each with multiple modules, that COMMUNICATE (shared world state /
  message bus / coordinated tasks; e.g. hand-off an object between two arms).
- I46: Real-world fidelity - tighten sim to match reality (servo speed/torque, sensor noise, latency,
  friction). IK reach issue = CCD local-minimum on offset wrist -> implement DLS/Jacobian IK.

## P14 — Waypoint sequences, multi-object tray scenario, scenario menu, builder UI, UI polish
**User prompt (summary):** Continue all elements + tests. Consider the UI panels. From previous prompts:
disable live movement but PLAY the arm between points. Save points in a SEQUENCE so recorded controls
can be adjusted. Create a scenario where objects must be placed into a tray. Create a MENU of different
scenarios. Create the robot-arm BUILDER UI with the different modules + ability to see training/generations.
**Derived intentions:**
- I47: Waypoint SEQUENCE editor: capture the current pose as a keyframe; build an ordered list; PLAY the
  arm smoothly between keyframes (not live). Adjust/insert/delete keyframes. Export sequence -> waypoints
  JSON (real-robot) + as a training/demo seed.
- I48: Multi-object "place into tray" scenario: several cubes scattered -> must all end inside a target tray.
- I49: Scenario MENU UI: list selectable scenarios with name + objective; click to load.
- I50: Robot-arm BUILDER UI: pick arm model + attach sensor modules (toggle), live arm stats, AND a
  training/generations view (start/step training, gen counter, best fitness, population/fitness display).
- I51: UI polish: clean panel layout, legible, consistent dark console theme.

## P15 — Clickable control buttons, fix base-bend joint, color-coded clickable servos
**User prompt (summary):** Want clickable BUTTONS for all view toggles + controls, in relevant areas,
incl. ARROWS for controlling servos beyond hotkeys. The view-toggle keys (L, ', Shift+L) overlap servo
hotkeys — fix. The BASE bend joint should bend forward/back but instead the whole system rotates on the
WRONG axis and the link connections visibly DETACH. Want servo motors COLOUR-CODED + clickable while on
the arm, better labels, and a circular-infill "activation" display per servo for better control + feedback.
Continue all tasks, test yourself, record prompts, update ROADMAP.
**Derived intentions:**
- I52: FIX base-bend joint (shoulder_lift): should pitch fwd/back; currently wrong axis + links detach.
- I53: Clickable on-screen BUTTONS for every view toggle + control, docked in relevant areas.
- I54: On-arm SERVO ARROWS: +/- buttons per joint floating at the joint to drive it (beyond hotkeys).
- I55: Remap sensor-view keys off the servo hotkeys (L/'/Shift+L clash with O/L, P/;).
- I56: Colour-code each servo (consistent colour on arm + panels + callouts); clickable on the arm.
- I57: Per-servo CIRCULAR activation gauge (radial infill = angle within range / load) for feedback.

## P16 — Text-to-task, training/generative strategy, randomized scenarios, multi-robot
**User prompt (summary):** Do all three: (a) live text-command input box, (b) run training to solve a
task, (c) multi-arm / ORCA Hand. Want to know how TEXT->TASK COMPLETION is best solved and how the
training + generative learning should best play out so robot commands solve scenarios well. Scenarios
should have varied elements (objects in different/random grid locations). Future: robots interacting
with other robots.
**Derived intentions:**
- I58: Write a STRATEGY doc: the layered text->task->control->training pipeline + how generative learning
  should solve scenarios (the "best" approach).
- I59: Live in-game TEXT COMMAND input box (type -> AgentCommands executes).
- I60: Run training that actually SOLVES a task (warm-start from demos/scripted solver -> evolve faster).
- I61: Randomized scenario variation (object positions on a random grid; difficulty knob).
- I62: Multi-arm foundation + ORCA Hand catalogue; future robot-robot interaction tasks.

## P17 — Solve scenarios, fix grasp, training-systems view, sensor panel buttons, colour 3D servo regions, camera controls
**User prompt (summary):** Create solutions to the scenarios; want to SEE the different training systems
(one of the EARLIER ones seemed to work). Sensor-modules panel: missing view-toggle buttons + improve it.
Servo-motor BUTTONS missing. When asking to colour-code servos, meant colour the RESPECTIVE servo-motor
REGIONS on the 3D MODEL too. Camera CONTROLS should be shown; Alt+mouse-drag for camera. Mouse-click to
select next transition (IK target) location.
**Derived intentions:**
- I63: FIX grasp — EE (IK aim point) must sit between the jaws so closing grips the object (currently
  EE is ~10cm from the jaw midpoint -> grasp always fails). Then scenarios are solvable.
- I64: Working scenario SOLUTIONS (scripted skills + the motion-genome GA that worked earlier).
- I65: Show the different TRAINING SYSTEMS (motion GA vs policy net) + pick which; surface the one that worked.
- I66: Sensor-modules panel: add per-module view-toggle buttons (lidar/range/depth) + enable toggles; improve layout.
- I67: On-arm servo +/- BUTTONS visible (callouts) — ensure they show.
- I68: Colour the SERVO REGIONS on the 3D arm model itself (tint each link/servo body its servo colour).
- I69: Camera controls shown on-screen; Alt+LMB drag = orbit (also keep RMB).
- I70: Click in the world to set the next IK target / transition location.

## P18 — Revert to working procedural arm; fix self-collision/desk-collision; colour 3D motors
**User prompt (summary):** Arm is colliding THROUGH ITSELF (should be selected against — could damage
itself). The FIRST initial play (before training/URDF work) was working BEST. Impossible joints again.
Arm moves THROUGH THE DESK (should be impossible). Colour-code motors on the 3D model.
**Derived intentions / DECISION:**
- I71: REVERT default to the PROCEDURAL arm (clean joints + IK + grasp that worked). Keep URDF/STL arm as
  an OPT-IN that still needs joint-frame work; stop forcing it as default.
- I72: Ensure SELF-COLLISION is enabled between non-adjacent arm links (and penalised in fitness).
- I73: Ensure the arm CANNOT pass through the desk (solid worktop collision vs all arm colliders).
- I74: Self-collision + desk-collision penalty in the training fitness (selected against).
- I75: Colour-code the servo-motor REGIONS on the 3D model (tint each link/joint body its servo colour).

## P19 — Player-placeable & orientable sensor/camera modules (mount on robot parts)
**User prompt (summary):** For the modules and camera, their placement and direction should be settable
by the PLAYER. A way to DROP them onto different robot parts and ORIENT them a particular way.
**Derived intentions:**
- I76: Module MOUNTING system: each sensor/camera is a draggable module the player drops onto a robot
  link/part; it parents to that link.
- I77: ORIENT the mounted module (rotation handles / gizmo) so it faces a chosen direction.
- I78: The mounted placement+orientation feeds the sensor (e.g. wrist cam pose, lidar origin/direction)
  AND is saved with the arm config + exported for the real rig (so sim matches reality).
- I79: A "mount points" UI: highlight valid robot parts; show current modules + their parent link + pose.

## P20 — Option B: procedural kinematics + STL skin; proper collision; grip-detection feedback
**User prompt (summary):** Go with Option B (procedural arm kinematics that works, SKINNED with the SO-101
STL meshes). Proper collision mechanics. Make objects easier to pick up via kinetic/grip DETECTION +
feedback. Reveal this to the player IF they have that (tactile/grip) module on.
**Derived intentions:**
- I80: Skin the working PROCEDURAL arm chain with the SO-101 STL meshes (align meshes to procedural links).
  Keep procedural joints/IK/grasp; STL is the visual layer.
- I81: Proper COLLISION: arm links vs desk (no pass-through), and self-collision avoidance/penalty.
- I82: GRIP DETECTION feedback: when the gripper is near/aligned to a graspable object, detect it and give
  feedback (highlight / readout) so objects are easier to pick up; auto/assisted grip when in range.
- I83: This grip/proximity feedback is REVEALED to the player only if the EFleshTactile (grip) module is
  attached/enabled — ties into the sensor-module system.

## P21 — STL skin made model worse; revert to working 4-DOF procedural arm
**User prompt (summary):** The STL-skinned model is much worse — connections look wrong, base plate not
fastened to the table, servo motors appear incorrectly transparent. Much worse than before.
**DECISION:** Revert to the 4-DOF PROCEDURAL arm that worked well (clean connections, good movement +
positioning) + restore its tuned ready pose {0,40,-78,-5}. STL skin shelved (looked worse, not better).
The procedural arm is the shipping default. STL/SO-101 look remains a future task done PROPERLY (full
URDF kinematics) rather than skinning, OR not at all if the clean arm is preferred.
