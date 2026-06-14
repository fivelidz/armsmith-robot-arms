# NVIDIA Labs Repos Study: RoboLab + GR00T-WholeBodyControl

> Research for ARMSMITH (Unity SO-101 6-DOF sim+training). Investigated 2026-06-14. RESEARCH ONLY.

## TL;DR / Bottom Line
- **RoboLab (the "critical" one) is NOT a training framework — it's an *evaluation benchmark*** built on
  Isaac Sim 5.0 / Isaac Lab 2.2.0. It will not train policies. Its value to ARMSMITH is **architectural
  patterns + data-format conventions**, not drop-in code.
- **GR00T-WholeBodyControl is humanoid whole-body control** (legged locomotion + bimanual on a Unitree
  G1). Largely **off-target** for a 6-DOF tabletop arm. Borrow a couple of teleop/data ideas; SKIP the
  locomotion RL core.
- **Both Apache-2.0 on code** (GR00T *weights* are NVIDIA Open Model License — irrelevant unless we use
  their checkpoints).
- **Neither runs in Unity.** Both are hard-wired to Isaac Sim/Lab + RTX GPUs (RoboLab recommends 48 GB+
  VRAM; GR00T training wants 64+ GPUs). Reuse their **interfaces/conventions/task-design**, not the sim.

---

## Repo 1 — NVlabs/RoboLab  ⭐ (flagged critical)

**What it is:** task-based EVALUATION BENCHMARK for manipulation policies (RSS 2026). RoboLab-120 = 120
new manipulation tasks (pick-place, stacking, rearrangement, reorientation, tool use, counting,
spatial/semantic reasoning) each with language instructions (default/vague/specific) + automated success
detection via composable predicates. You bring a trained policy, it scores it.

**Key components:**
- `robolab/tasks/` — 120 task defs (scene USD + instruction + predicate success + subtask checkpoints +
  difficulty).
- **Server-client policy architecture** — your model runs as a standalone server; RoboLab connects via a
  lightweight `InferenceClient` ABC: `_extract_observation → _pack_request → _query_server →
  _unpack_response (action chunk)`. Backends: pi0_family (Pi0.5/OpenPI), gr00t, cosmos3, dreamzero.
- Embodiment-agnostic ("bring your own robot"); multi-env parallel eval; AI scene/task-gen Claude skills;
  results dashboard (episode-video replay + cross-experiment compare).
- **`scripts/convert_to_lerobot.py`** — RoboLab HDF5 → LeRobot v3.0 (field mapping authority).

**License:** Apache 2.0 (code). ✅
**Compute:** Isaac Sim 5.0 + Isaac Lab 2.2.0, Python 3.11, Ubuntu 22.04+, RTX 48 GB+ VRAM, ~8 GB assets,
~30 GPU-h/100 tasks. Heavyweight Isaac stack — cannot run in Unity.

**Relevance — ADOPT (re-implement in C#/Unity):**
- **Server-client policy interface** — mirror the `InferenceClient` contract for ARMSMITH's diffusion/
  sensor-policy serving (matches our `serve_diffusion_policy.py` + MCP bridge). Highest-value pattern.
- **Composable-predicate success detection** — task = scene + instruction + predicate success + subtask
  checkpoints. Re-implement predicates (`object_in_container`, `object_upright`, `object_left_of`, ...) as
  C# checks for ARMSMITH eval + GA fitness. The single most reusable idea.
- **LeRobot v3.0 export conventions** — align `waypoints_to_lerobot.py`/`joint_map_lerobot.json` with the
  exact field mapping in `convert_to_lerobot.py`.
- **DROID joint-pos obs/action packing** — reference for SO-101 policy I/O serialization.

**ADAPT-IDEAS:** task taxonomy/difficulty labels = curriculum design; three-tier language instructions;
dashboard = fits our GA/evolution viz; AI scene/task-gen skills = portable to our CAD/scene tooling.
**SKIP:** Isaac Sim/Lab sim, USD assets, HDR backgrounds, 48 GB eval pipeline.

**First step:** read `/tmp/roblab/robolab/eval/base_client.py` (InferenceClient ABC),
`.claude/skills/robolab-taskgen/references/predicates.md`, and `scripts/convert_to_lerobot.py`; mirror
into a future `design/specs/EVAL_AND_LEROBOT_SPEC.md` defining our policy-serving contract + LeRobot
export schema.

---

## Repo 2 — NVlabs/GR00T-WholeBodyControl

**What it is:** NVIDIA GEAR humanoid whole-body control (Unitree G1). Hosts: decoupled WBC (RL legs + IK
arms, used in GR00T N1.5/N1.6 VLA), GEAR-SONIC (humanoid behavior foundation model via large-scale motion
tracking of human mocap), MotionBricks (real-time latent generative motion). "Whole-body" = legs+torso+
two arms with balance — NOT "whole-arm".

**License:** code Apache 2.0 ✅; weights NVIDIA Open Model License ⚠️ (irrelevant for arm work).
**Compute:** Isaac Lab 2.3.2; train SONIC wants **64+ GPUs** (num_envs=4096); MuJoCo for demos; VR/PICO
teleop hardware; C++ deploy on real G1; Git-LFS multi-GB (shallow clone timed out).

**Relevance — mostly off-target for a 6-DOF arm.**
- ADAPT-IDEAS: the teleop→data-collection→fine-tune→deploy loop shape (same as our LeRobot path); motion-
  tracking as a scalable training task (train one policy to follow a library of trajectories — useful if
  we generate an arm-trajectory library from GA/CAD); ZMQ deploy + motor-error monitoring patterns for the
  REAL_ROBOT_PORT path; decoupled RL+IK ("don't make RL do what IK already solves").
- SKIP: PPO locomotion/balance, human-mocap retargeting, 64-GPU training, VR teleop hardware, MotionBricks,
  C++ humanoid deploy, Isaac Lab.

**First step:** don't clone (multi-GB LFS). Read hosted docs only
(`nvlabs.github.io/GR00T-WholeBodyControl/tutorials/data_collection.html` and `.../vla_workflow.html`) for
workflow shape; treat as inspiration, not code reuse.

---

## Cross-cutting takeaways
1. Neither is a drop-in (Isaac/RTX-bound; Unity can't host them). Wins = patterns/contracts/conventions.
2. RoboLab is the genuinely useful one — as an **eval/benchmark design reference + LeRobot data-format
   authority**, not a trainer. Top artifacts: InferenceClient contract, predicate success detection,
   convert_to_lerobot.py.
3. Licensing clean (Apache-2.0 code; only GR00T weights restricted).
4. Recommended next action: extract RoboLab's `base_client.py` + `predicates.md` + `convert_to_lerobot.py`
   into a `design/specs/EVAL_AND_LEROBOT_SPEC.md` (policy-serving contract + LeRobot v3.0 export schema).
   Drop GR00T-WBC beyond skimming its data-collection docs.

### Method notes
RoboLab shallow-cloned to /tmp/roblab (Apache-2.0). GR00T-WBC clone timed out (LFS monorepo); used webfetch
of README + repo page. No ARMSMITH files modified.
