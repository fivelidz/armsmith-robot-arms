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
