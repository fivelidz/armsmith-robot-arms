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

**Open questions to confirm with user later (do not block):**
- Q1: Which real arm is the primary target to port to — reBot B601-DM (Damiao/CAN) or an SO-ARM100
  (Feetech) you may already own from the prior project?
- Q2: "0 auth Claude Code in game" — acceptable to call a local agent process / your existing
  opencode-shared auth, vs. truly bundling a key? (security: never embed secrets in the build).
