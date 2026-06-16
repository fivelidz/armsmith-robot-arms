# Robot Arms Game — Research Library Index

Created 2026-05-30. All research consolidated here before building the game.

## Reports
| File | Topic | Key takeaway |
|---|---|---|
| `arm_hardware/REPORT.md` | reBot-DevArm (Seeed) + SO-ARM100 | 6-DOF+gripper, 650 mm reach, 1.5 kg payload, <0.2 mm repeat. STEP/STL parts open-source (CERN-OHL-W). Start game with simplified 3-DOF+gripper. Units = metres for sim-to-real. |
| `arm_hardware/STS3215_SERVO_MODEL.md` | **Feetech STS3215 deep dive (digital-twin)** | 4096 ticks/rev (0.088°), stall 30 kg·cm@12V / 16.5 kg·cm@6V (7.4V kit), rated 10 kg·cm, 0.222 s/60°. Internal law = **P-dominant PID + deadband + torque cap** (regs P/D/I @21/22/23). Servos **sag under load** (proportional hold). Official SO-101 MuJoCo: kp=998.22, kv=2.731, force ±2.94 N·m, ±0.5° backlash, **assumes servo P=16**. **kp ∝ servo P-gain.** Unity drive: stiffness=kp×0.0175 (N·m/deg), damping=kv×0.0175, forceLimit=stall. Tunes `ServoModel.cs` + `UrdfArm.cs`. |
| `manipulation_repos/REPORT.md` | eFlesh, LeRobot, SO-ARM100, Gymnasium-Robotics, IK libs | FetchPickAndPlace defines the task spec (4-d action, 25-d obs, sparse reward @5cm). eFlesh = tactile upgrade. IK upgrade tree CCD→FABRIK→DLS. |
| `cad_3dprint/REPORT.md` | CADAM, OpenSCAD-WASM, CadQuery/build123d, STL I/O, URDF-Importer | Binary STL export from Unity Mesh is ~50 lines C#. OpenSCAD-WASM = in-game text-to-CAD. URDF-Importer loads robots. |
| `learning_evolution/REPORT.md` | IK, NEAT, CMA-ES, Karl Sims morphology, ML-Agents | Use FABRIK for IK. Phased: manual → CMA-ES motion tuning → morphology GA w/ player selection → optional ML-Agents PPO. |
| `cameras/REPORT.md` | Wrist UVC cam + Logitech C922 env cam | Multi-RenderTexture HUD panels: main orbit cam + wrist cam (~80° FOV) + env cam (~78° FOV). URP. |
| `unity_integration/REPORT.md` | MCP for Unity bridge | Pkg `com.coplaydev.unity-mcp` (CoplayDev). 43 tools. Editor 6000.4.2f1. Template project: GoblinFortDefense. |

## External repos (vendored, computer-vision / spatial)
| File | Topic | Key takeaway |
|---|---|---|
| `external/NVLABS_4DRGPT_SPATIALCLAW_STUDY.md` | NVlabs **4D-RGPT** + **SpatialClaw** study | SpatialClaw = training-free spatial-reasoning agent over DA3 depth + SAM3 seg + numpy geometry → adoptable grasp-perception pipeline. 4D-RGPT = region-level 4D-video QA VLM; borrow its Perceptual-4D-Distillation idea, skip the 8B model. Both NVIDIA-NC (research only). |
| `external/NVLABS_UTILIZATION_AND_SCENARIOS.md` | How to USE the above in ARMSMITH | 6 scenarios S1–S6. Highest-leverage: S1 DA3 depth endpoint over MCP → S3 auto-label domain-randomized worlds → S4 train RGB-only grasp policy via P4D distillation (Unity GT as teacher) → deploy via LeRobot. |
| `external/ORCAHAND_STUDY.md` | ORCA dexterous hand | (prior study) |
| `external/4D-RGPT/`, `external/SpatialClaw/` | Vendored source (code+docs tracked; demo media gitignored) | Working clones live in `~/projects/github_repos/`. Perception submodules fetched: Depth-Anything-3, SAM3, Pi3, L4P. |

## The chosen tech stack
- **Engine:** Unity 6000.4.2f1, URP, built-in PhysX (ArticulationBody for the arm — accurate jointed physics).
- **Control:** FABRIK IK + per-joint drive; mouse drags an end-effector target, keyboard nudges joints.
- **Tasks:** start = pick a block, move it to a target zone (FetchPickAndPlace-style, sparse + dense reward).
- **Learning layer:** Phase 1 manual; Phase 2 CMA-ES motion-param evolution; Phase 3 morphology GA with player selection (interactive evolution).
- **Export:** binary STL exporter from arm meshes + joint-config JSON (sim-to-real).
- **Cameras:** main orbit + wrist + environment RenderTexture panels.
- **AI bridge:** MCP for Unity to let agents build/inspect the scene.

## License notes
- reBot/SO-ARM hardware = CERN-OHL-W-2.0 → if we ship their exact meshes, keep copyright + license + mark modifications. Safer: use our OWN procedurally-generated parametric arm meshes (link cylinders + joint spheres) for the game, and treat reBot dimensions as *reference specs only*. This sidesteps redistribution obligations and makes morphology evolution trivial. **Decision: game uses procedural meshes parameterised by reBot-like dimensions.**
- **4D-RGPT / SpatialClaw + DA3/SAM3/Pi3/L4P = NVIDIA Source-Code-License-NC (and gated SAM3 weights)** → **non-commercial research use only.** Fine for this research project. If ARMSMITH ever goes commercial, treat their code + model weights as prototype-only and swap in permissively-licensed depth/seg models for any shipped product.
