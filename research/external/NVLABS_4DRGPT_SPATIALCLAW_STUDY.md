# NVIDIA Labs Repo Investigation — 4D-RGPT & SpatialClaw vs. ARMSMITH

**Date:** 2026-06-14
**Scope:** Research-only assessment of two NVlabs repos for relevance to ARMSMITH
(Unity SO-101 6-DOF arm sim; ArticulationBody physics; IK; GA + sensor-policy +
diffusion-policy training; wrist/claw camera + depth + lidar + tactile sensor suite;
MCP/socket bridge; LeRobot deployment). User focus: **computer-vision AI, spatial
understanding, training with randomized/scrambled worlds.**

Both repos were cloned (`--depth 1`) into `/tmp` and inspected directly (README, docs,
configs, model/inference code, license).

---

## TL;DR Recommendations

| Repo | Verdict | One-line reason |
|------|---------|-----------------|
| **SpatialClaw** | **ADAPT-IDEAS (strong) → selectively ADOPT one tool** | A *training-free* spatial-reasoning agent built around exactly the perception primitives ARMSMITH wants (depth/3D reconstruction + promptable segmentation + geometry math). The "Reconstruct → SAM3 → geometry → grasp pose" pipeline is directly inspiring for vision-based grasp selection. Heavy as a whole, but the **Depth-Anything-3 reconstruction tool is adoptable in isolation** over the MCP bridge. |
| **4D-RGPT** | **ADAPT-IDEAS (narrow) / mostly SKIP** | A research VLM for *region-level 4D video QA* (depth/motion-aware question answering). Not a control or grasping model. Out of scope as a deployed model for ARMSMITH, but its **Perceptual 4D Distillation** idea (distill depth/flow knowledge into a model at train-time, free at inference) is a genuinely useful concept if you later train a vision policy. Skip the repo as infra; borrow the idea. |

Both are **NVIDIA Source Code License-NC: non-commercial scientific research only.**
This is fine for ARMSMITH as a research/learning project, **but blocks any commercial
product use** of the code or the released model weights. Flag this early.

---

## 1. SpatialClaw — `github.com/NVlabs/SpatialClaw`

### What it actually is
Paper: *"SpatialClaw: Rethinking the Action Interface for Agentic Spatial Reasoning"*
(Cho et al., NVIDIA + KAIST, 2026). The "Claw" is **not** a robot gripper — it's the name
of the **agent framework**. (Worth knowing given the project name; the relevance is real
but it's a reasoning agent, not a manipulation stack.)

Core thesis: **code is the right action interface for spatial reasoning**. A VLM-backed
agent writes one Python cell per step into a **persistent Jupyter kernel** that is
pre-loaded with:
- input frames (`InputImages`, `Metadata`)
- **perception primitives**: `Reconstruct` (Depth-Anything-3 / Pi3 / MapAnything),
  `SAM3` (promptable segmentation + video tracking)
- **geometry helpers** (`tools.Geometry`)
- drawing/visualization utilities
- an isolated VLM sub-session (`vlm.locate`, `vlm.ask_with_thinking`)
- `ReturnAnswer(...)` to commit

It runs a 5-stage loop (Plan → CodeGen → AST-checked Execute → Feedback → Answer),
training-free, and scores **59.9% avg across 20 spatial-reasoning benchmarks** (+11.2
over the prior best agent), consistent across 6 VLM backbones (Qwen3.5/3.6, Gemma4,
26B–397B). It's an **evaluation/reasoning harness**, not a policy you deploy on hardware.

### Architecture / key components
Three independently-running services (each also a plain Python entry point — SLURM is
optional convenience):
1. **vLLM server** — serves the VLM backbone (OpenAI-compatible API). Can also point at a
   cloud endpoint (NVIDIA/OpenAI/Gemini) via `.env` keys.
2. **GPU perception-tool server** (FastAPI) — runs the heavy CV models:
   - `gpu_models/da3_model.py` — **Depth-Anything-3**: depth + extrinsics + intrinsics →
     **unprojected world-space point maps** `(N,H,W,3)` + camera poses `(N,4,4)` +
     confidence + metric scale. *This is the highest-value file for ARMSMITH.*
   - `gpu_models/sam3_model.py` — **SAM3**: text/point-promptable detection + video
     segmentation/tracking → masks.
   - `pi3_model.py`, `mapanything_model.py` — alternate reconstruction backends.
   - `easyocr.py` — text reading.
3. **Agent** — LangGraph loop + per-sample Jupyter kernel (CPU only).

Geometry toolbox (`tools/geometry_utils.py`, CPU, dependency-light, pure numpy):
`euclidean_distance`, `angle_between_vectors`, `project_point_to_camera(point, c2w, fx,
fy, cx, cy)`, `rotation_matrix_from_vectors`, `transform_points` (SE(3)),
`fit_ground_plane_ransac`, `normalized_to_pixel`. **These are exactly the operations a
grasp-pose selector needs** and are trivially liftable (no GPU, no license-heavy deps in
that file specifically — but see license note below).

Output dataclasses (`gpu_models/types.py`) are clean, torch-free numpy structs
(`DA3ReconstructionOutput` with `points`, `camera_poses`, `confidence`, `_intrinsics`),
designed to cross a process boundary — which is encouraging for a socket/MCP bridge.

### License
**NVIDIA Source Code License-NC** — non-commercial scientific research only (§3.3).
Also pulls third-party submodules (SAM3, Pi3, Depth-Anything-3, map-anything) each with
their **own** licenses (check `tools/third_party/<repo>/LICENSE` before use; SAM3 weights
are **gated on HuggingFace**).

### Dependencies + compute/hardware
Heavy. The full stack expects:
- **Hopper (H100) or newer** for the FP8 model variants used in the paper; A100/L40S need
  AWQ variants. The VLM backbones are 26B–397B params — **not runnable on a typical
  workstation GPU**.
- conda env (agent) + a separate CUDA 12.8 conda env + a `uv` vLLM venv + DeepGEMM build
  for FP8. ~15–30 min setup. langgraph, vLLM nightly, jupyter_client, fastapi, ffmpeg.
- HuggingFace gated access for SAM3 weights; network for model downloads.

**BUT** — the individual perception tools are far lighter than the agent:
- **Depth-Anything-3** alone runs on a single consumer GPU and gives metric depth +
  point maps. This is the piece worth extracting.
- SAM3 (segmentation) likewise runs standalone on one GPU.
You do **not** need the 397B VLM, vLLM, or SLURM to use the depth/seg tools.

### How it concretely integrates with ARMSMITH
ARMSMITH already renders **WristCam (256×256, 80° FOV, near-clip 0.01)** and **EnvCam
(320×240, 78°)** to RenderTextures with **configurable intrinsics that match the real
rig** (per `CAMERA_VISION_SPEC.md`), and exposes `CameraRig.Capture(camId) -> Texture2D`.
That is a near-perfect feeder for SpatialClaw's tools:

**(a) Wrist/claw camera + depth obs we already render.**
ARMSMITH renders depth in-engine (ground truth). SpatialClaw's DA3 *estimates* depth
from RGB. Two concrete uses:
   - **Validate / replace** your estimated-depth path: feed WristCam RGB to DA3, compare
     its `points (H,W,3)` against Unity's ground-truth depth buffer. This is a free
     sim-side accuracy benchmark for "what would the real wrist UVC cam's depth look
     like" — directly serves the sim-to-real crossover plan in your spec.
   - You already have intrinsics in `CameraRigConfig`; DA3 returns its own intrinsics,
     so you can sanity-check unprojection math against your known Unity intrinsics.

**(b) Spatial reasoning for grasp-pose selection.** *This is the strongest fit.*
The SpatialClaw recipe — `Reconstruct` → world point map → `SAM3.detect("the cube")` →
mask → `get_centroid_3d` → `tools.Geometry` (distance, plane fit, rotation-from-vectors)
→ grasp pose — is **exactly** a vision-driven grasp planner. You can replicate this
*idea* in ARMSMITH without the agent: segment the target in WristCam, unproject its mask
centroid to a 3D point in arm-base frame, fit the support plane, and use
`rotation_matrix_from_vectors` to align the gripper approach axis. The geometry file is
copy-pasteable (pure numpy) and `project_point_to_camera` lets you draw the predicted
grasp back into the HUD for debugging.

**(c) Training a vision/spatial model from synthetic Unity images + domain randomization.**
SpatialClaw is training-free, so it doesn't *train* anything — but it's an excellent
**auto-labeler / oracle** for your randomized worlds:
   - Run your domain-randomized Unity scenes, capture WristCam/EnvCam frames, and use
     SAM3 + DA3 to auto-generate (mask, 3D centroid, grasp-axis) labels.
   - Those labels become supervision for your *own* lightweight vision policy (the
     "classical path" YOLO/blob detector you already plan, upgraded to a learned grasp
     predictor) that runs fast on the real arm. SpatialClaw is the heavyweight teacher;
     your deployed net is the lightweight student.

**(d) Pretrained models to run over the MCP/socket bridge.**
Yes — and this is the cleanest adoption path. The **GPU tool server is already a FastAPI
service** returning numpy dataclasses. You can:
   - Stand up *only* the DA3 (and optionally SAM3) GPU server on a machine with one GPU.
   - Have the ARMSMITH MCP/socket bridge POST a WristCam JPEG and receive back
     `{points, camera_poses, confidence, intrinsics}` (DA3) or masks (SAM3).
   - No vLLM, no 397B model, no LangGraph loop needed for this. You're reusing the two
     perception endpoints, not the agent.

### Adoptable vs out of scope
- **Adoptable now:** `geometry_utils.py` (pure numpy, copy it); the DA3 GPU-server
  pattern as a depth/point-cloud inference endpoint behind your bridge; SAM3 as a
  promptable target segmenter; the `Reconstruct→Segment→Geometry→grasp` *pipeline shape*.
- **Adapt-ideas:** the "perception primitives + geometry as composable tools" design —
  mirror it as MCP tools so a future ARMSMITH agent can reason about grasps in code.
- **Out of scope:** the full LangGraph agent, vLLM/FP8/DeepGEMM stack, 26B–397B VLM
  backbones, the 20 QA benchmark loaders, SLURM chain-job managers. Too heavy and aimed
  at benchmark QA, not robot control.

### Recommendation: **ADAPT-IDEAS (strong), selectively ADOPT the DA3 endpoint**
**First step:** Stand up the Depth-Anything-3 model standalone (skip vLLM/SLURM/agent)
on a single GPU, wrap `DA3Model.reconstruct(frames)` in a tiny FastAPI/socket endpoint,
and POST one WristCam RenderTexture grab from ARMSMITH. Compare the returned point map
`(H,W,3)` against Unity's ground-truth depth on the same frame. If the metric depth lines
up, you have a real-camera-equivalent depth source for the sim-to-real loop and a teacher
for grasp-label generation. In parallel, copy `geometry_utils.py` into a research scratch
module and prototype mask-centroid→3D grasp-pose on a single rendered frame.

---

## 2. 4D-RGPT — `github.com/NVlabs/4D-RGPT`

### What it actually is
Paper: *"4D-RGPT: Toward Region-level 4D Understanding via Perceptual Distillation"*
(Yang et al., **CVPR 2026 Highlight**). A specialized **Multimodal LLM (VLM)** for
**region-level 4D video question answering** — i.e., answer questions about depth, motion,
spatial relations, rotation, displacement etc. over a *video*, with region prompting.
It is a **fork of NVlabs/VILA** (inherits NVILA model code). Released artifacts:
`nvidia/4D-RGPT-8B` weights (HF) and the `R4D-Bench` benchmark.

It is a **perception/QA model, not a controller or grasper.** It tells you *"the red cube
moved left and is closer to the camera"*, not *"set joint angles to grasp it."*

### Architecture / key components
- Backbone: **NVILA-Lite-8B** (vision encoder + Qwen2-family LLM).
- Three 4D-RGPT-specific additions, each in a single file (designed to be reused):
  1. **L_LD (latent distillation)** — match a student projector's per-layer features to a
     frozen **L4P** teacher (a depth/flow expert), MSE per token, at train time only.
  2. **L_ED (explicit distillation)** — decode student features through the teacher's task
     heads and match per-task predictions (depth, 2D optical flow, dynamic mask).
  3. **TPE (Timestamp Positional Encoding)** — sinusoidal per-frame timestamp encoding
     added to vision tokens; no learned params (`llava/model/pe/time_pe.py`, ~45 lines,
     self-contained).
- Collectively called **Perceptual 4D Distillation (P4D)**: transfer depth/flow/motion
  perception from a frozen expert into the VLM **at training time with zero added
  inference cost**. Result: +5.3% avg over baselines on 6 3D/4D benchmarks, +4.3% on
  R4D-Bench.

### License
**NVIDIA Source Code License-NC** — non-commercial research only. The released
**4D-RGPT-8B weights** are under the same NC terms. Built on VILA (also NV-licensed).

### Dependencies + compute/hardware
- Pinned, brittle stack: **`transformers==4.46` + `torch==2.3` + `flash_attn
  2.5.6+cu121torch2.3`** (the README documents an ~8pt accuracy drift if you move to
  transformers 5). CUDA 12.1. `deepspeed`, `peft`, `open3d`, `lmms-eval`, `s2wrapper`.
- Training: multi-node SLURM (defaults to 8 nodes), NVIDIA-internal launchers.
- Inference: an 8B VLM — needs a sizable GPU (≈16–24GB+ for the 8B in bf16) and the exact
  pinned env. The README explicitly warns the checkpoint is **not portable** across the
  transformers v4/v5 stacks.

### How it concretely integrates with ARMSMITH
Honestly, weakly as a deployed component — but the *training idea* is valuable.

**(a) Wrist/claw camera + depth obs.** 4D-RGPT consumes RGB *video* and answers
questions; it doesn't produce depth maps or grasp poses you can act on. It would *use*
your wrist video, not enhance your depth obs. Low direct value.

**(b) Spatial reasoning for grasp-pose selection.** It reasons in language about spatial
relations ("is A left of B", "did it move closer"). That's scene understanding, not a
6-DOF grasp pose. You *could* query it ("which object should be picked first?") over the
bridge, but it's an 8B model giving text — far heavier and less actionable than
SpatialClaw's geometry pipeline for grasping. Out of scope for grasp selection.

**(c) Training a vision/spatial model from synthetic Unity images + domain randomization.**
*This is where the value is.* The **P4D distillation idea** maps cleanly onto ARMSMITH's
unique advantage: **in Unity you have free ground-truth depth, optical flow, segmentation,
and object motion.** Instead of distilling from a frozen L4P teacher, you can distill from
*Unity's perfect ground truth* into a compact vision policy:
   - Train your sensor/vision policy with an auxiliary loss that predicts the Unity
     ground-truth depth/flow/dynamic-mask from RGB (exactly L_ED's structure), so the
     deployed RGB-only policy internalizes 4D perception with **zero inference cost** —
     the core P4D selling point. This is a strong, concrete technique for the
     "train a vision model from synthetic Unity images" goal and pairs naturally with
     domain randomization.
   - **TPE** (`time_pe.py`) is a 45-line drop-in if you ever feed multi-frame sequences
     (e.g., a temporal grasp/tracking policy) and want timestamp-aware tokens. Cheap to
     borrow, no license entanglement risk beyond attribution.

**(d) Pretrained models over the MCP/socket bridge.** You *can* run `nvidia/4D-RGPT-8B`
inference behind the bridge (the `eval/nvila.py` entry shows the `generate_content`
call), but it answers QA in text and needs the pinned env + a big GPU. Useful only if you
want a natural-language "scene critic" in the HUD ("the cube is near the edge, approach
from the left"). Niche; not core.

### Adoptable vs out of scope
- **Adopt-ideas:** the **P4D distillation pattern** (predict ground-truth depth/flow/
  motion as auxiliary train-time losses → free 4D perception at inference). Reframed
  with Unity's ground truth as the teacher, this is genuinely applicable.
- **Adopt (tiny):** `time_pe.py` Timestamp Positional Encoding if you go temporal.
- **Out of scope:** the 4D-RGPT-8B model as a deployed ARMSMITH component, the
  transformers-4.46/torch-2.3 pinned stack, multi-node SLURM training, R4D-Bench, the
  whole VILA training harness.

### Recommendation: **ADAPT-IDEAS (narrow) / mostly SKIP the code**
**First step (only when you start training a vision policy):** add an auxiliary
depth-(and optionally flow/mask-)prediction head to your sensor/vision policy and
supervise it with Unity's ground-truth buffers during training, dropping it at inference.
That captures the P4D benefit using assets you already have for free. Don't stand up the
8B model or the pinned env unless you specifically want a language scene-critic.

---

## Cross-cutting notes for ARMSMITH

1. **Licensing gate (act now):** Both repos and the released weights are **NC — research
   only.** If ARMSMITH might ever ship/commercialize, treat SpatialClaw/4D-RGPT/DA3/SAM3
   code & weights as reference-and-prototype only, and plan permissively-licensed
   replacements (e.g., other depth/seg models) for any production path. Document this in
   `research/INDEX.md`.
2. **SpatialClaw >> 4D-RGPT for this project.** One is a spatial-reasoning + perception
   *toolkit* whose components (depth reconstruction, promptable segmentation, geometry)
   are exactly grasp-relevant and partly liftable. The other is a research QA VLM whose
   *idea* (perceptual distillation) is useful but whose code/model is heavy and not
   control-oriented.
3. **The high-leverage combined play:** Use **SpatialClaw's DA3+SAM3+geometry** as an
   offline *teacher/auto-labeler* over your domain-randomized Unity scenes to generate
   grasp supervision, then train a small RGB-only student policy with **4D-RGPT's P4D
   auxiliary-distillation idea** (supervised by Unity ground-truth depth/flow). Deploy the
   small student on the real arm via LeRobot. This unifies both repos around ARMSMITH's
   actual goal: vision-driven grasping that transfers sim→real.
4. **Bridge fit is good:** SpatialClaw's GPU tool server already speaks FastAPI and
   returns numpy dataclasses across a process boundary — a natural match for the existing
   MCP/socket bridge. Start with a single DA3 endpoint.

---

### Files inspected
- SpatialClaw: `README.md`, `docs/architecture.md`, `docs/installation.md`,
  `spatial_agent/gpu_models/{da3_model,sam3_model,types}.py`,
  `spatial_agent/tools/geometry_utils.py`, `spatial_agent/kernel_types/vlm_module.py`,
  config/dataset listing, `LICENSE`.
- 4D-RGPT: `README.md`, `pyproject.toml`, `eval/nvila.py`,
  `llava/model/pe/time_pe.py`, `LICENSE`, directory structure.
- ARMSMITH context: `design/specs/CAMERA_VISION_SPEC.md`,
  `UnityProject/Assets/Scripts/Sensors/`.
