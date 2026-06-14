# NVIDIA Labs Repos → ARMSMITH: Utilization & Scenarios

**Date:** 2026-06-14
**Companion to:** `NVLABS_4DRGPT_SPATIALCLAW_STUDY.md` (the what/architecture/license study).
**This doc:** *how* to actually use the now-vendored repos in ARMSMITH, with concrete
scenarios. **Research project — non-commercial use is fine** (both repos are NVIDIA
Source-Code-License-NC; see study doc §License gate).

---

## Where the code now lives

| Location | Purpose | `.git`? | Contents |
|---|---|---|---|
| `~/projects/github_repos/4D-RGPT/`, `…/SpatialClaw/` | **Working clones** — run, experiment, `git pull` to update | ✅ kept | Full repos + key perception submodules fetched (DA3, SAM3, Pi3, L4P) |
| `research/external/4D-RGPT/`, `…/SpatialClaw/` | **Vendored snapshot** in the ARMSMITH repo (reference, tracked in git) | ❌ stripped | Code + docs tracked; heavy demo media (`*.mp4/*.gif/*.npz`, weights) gitignored |

Perception models fetched into both `SpatialClaw/tools/third_party/`:
**Depth-Anything-3, SAM3, Pi3** (+ `map-anything` placeholder), and into
`4D-RGPT/third_party/`: **L4P** (the depth/flow teacher for distillation).

> To run anything heavy, work out of `~/projects/github_repos/` (it has `.git` and the
> full media). The copy under `research/external/` is the citable reference snapshot.

---

## The mental model: which piece does what

ARMSMITH is a **control + simulation** stack. These repos are **perception + spatial
reasoning**. They slot in at three distinct layers, from lightest to heaviest:

```
        ARMSMITH (Unity: arm, physics, IK, GA, cameras, sensors, MCP bridge)
                                   │
   ┌───────────────────────────────┼───────────────────────────────────────┐
   │ LAYER 1: Perception model      │ LAYER 2: Reasoning recipe              │ LAYER 3: Training idea
   │ (a model you RUN)              │ (a pipeline you COPY)                  │ (a loss you ADD)
   ├───────────────────────────────┼────────────────────────────────────────┼──────────────────────
   │ Depth-Anything-3  →  metric    │ SpatialClaw's                          │ 4D-RGPT's P4D:
   │   depth + point map + pose     │   Reconstruct→SAM3→Geometry→grasp      │   distill GT depth/flow
   │ SAM3  →  promptable masks      │   (copy the SHAPE, not the 397B VLM)   │   into an RGB-only policy
   │   ("the red cube")             │   + geometry_utils.py (pure numpy)     │   (free at inference)
   └───────────────────────────────┴────────────────────────────────────────┴──────────────────────
```

- **Layer 1 — run a model.** DA3 / SAM3 are standalone, single-GPU, and the most directly
  useful. Serve them behind the MCP/socket bridge; ARMSMITH POSTs a camera frame, gets
  back depth/points/masks.
- **Layer 2 — copy a recipe.** SpatialClaw's `Reconstruct → segment → geometry → answer`
  loop *is* a vision grasp planner. Copy the pipeline shape and the pure-numpy
  `geometry_utils.py`; **skip** the 26B–397B VLM and LangGraph agent.
- **Layer 3 — borrow a training trick.** 4D-RGPT's Perceptual 4D Distillation: predict
  depth/optical-flow/motion as *auxiliary losses at train time*, free at inference.
  Reframe with **Unity's free ground-truth buffers as the teacher** (no L4P needed).

Everything else (vLLM serving of giant models, SLURM chain-jobs, the 20 QA benchmarks,
the 4D-RGPT-8B VLM as a deployed component) is **out of scope** for ARMSMITH.

---

## SCENARIOS

Ordered by effort/payoff. S1–S2 are near-term and high-value; S3–S5 are the research arc;
S6 is speculative.

### S1 — DA3 depth endpoint behind the MCP bridge *(quick win, do first)*

**Goal:** give ARMSMITH a "what would a real wrist camera's depth look like" signal, and a
sanity check on the sim-to-real camera math.

**Setup:**
1. In `~/projects/github_repos/`, install Depth-Anything-3 in its own venv (single GPU,
   no vLLM/SLURM). It exposes `depth_anything_3.api.DepthAnything3(...).inference(images)`
   → `Prediction` with `depth`, `intrinsics`, `extrinsics`, `conf` (see
   `SpatialClaw/spatial_agent/gpu_models/da3_model.py` for the exact call + the
   depth→world-point unprojection, ~lines 107–220; that file is a ready-made wrapper).
2. Wrap it in a tiny FastAPI/socket endpoint: `POST {jpeg}` → `{depth[H,W],
   points[H,W,3], intrinsics, pose, conf}` (numpy, matching `DA3ReconstructionOutput`).
3. From ARMSMITH's MCP/socket bridge, grab `CameraRig.Capture("wrist")` (you already have
   this), send the JPEG, receive the point map.

**Use it for:**
- **Validation:** Unity gives you *ground-truth* depth for the same frame. Overlay
  DA3-estimated depth vs GT → quantify the gap. This is your sim-to-real depth-fidelity
  metric, for free, on every rendered frame.
- **Intrinsics check:** DA3 returns its own intrinsics; compare to your
  `CameraRigConfig` (80° FOV, 256×256) to confirm unprojection is consistent.

**Effort:** ~1 day. **Payoff:** real-camera-equivalent depth + a sim-to-real metric.
**Risk:** low. DA3 is the lightest, most self-contained model here.

---

### S2 — Vision grasp-pose from a single wrist frame *(copy the recipe)*

**Goal:** select a 6-DOF grasp pose from the wrist camera using SpatialClaw's reasoning
recipe — **without** running its VLM agent.

**Pipeline (copied shape):**
```
WristCam RGB ──► SAM3.detect("the cube")  ──► mask
            └──► DA3.reconstruct(frame)    ──► points[H,W,3] (arm-base frame)
mask + points ──► centroid_3d  (mean of points under mask, conf-weighted)
points        ──► tools.Geometry.fit_ground_plane_ransac  ──► support normal
approach axis ──► tools.Geometry.rotation_matrix_from_vectors(gripper_z, -normal)
                 ──► grasp pose (position = centroid + offset, orientation = R)
              ──► hand to ARMSMITH IK (FABRIK) as the end-effector target
```

**What to copy:** `SpatialClaw/spatial_agent/tools/geometry_utils.py` — **pure numpy, no
GPU, no license-heavy deps** (`euclidean_distance`, `project_point_to_camera`,
`rotation_matrix_from_vectors`, `transform_points`, `fit_ground_plane_ransac`). Drop it
into a research scratch module (`scripts/vision/` say). `project_point_to_camera` also
lets you **draw the predicted grasp back into the Unity HUD** for debugging.

**What to skip:** the LangGraph agent, the persistent Jupyter kernel, the 397B VLM. You're
borrowing the *composition*, not the orchestration.

**Effort:** ~2–4 days (needs S1's DA3 endpoint + a SAM3 endpoint). **Payoff:** a real
vision-driven grasp planner you can compare against your IK/scripted grasps.
**Risk:** medium — SAM3 weights are HF-gated; DA3 metric scale needs calibrating to
Unity's metre units (you already use metres for sim-to-real, which helps).

---

### S3 — Auto-label randomized worlds (SpatialClaw as an offline oracle)

**Goal:** turn your **domain-randomized / scrambled** Unity scenes into a labeled grasp
dataset, using SpatialClaw's perception stack as a teacher.

**Why it fits your interest:** you want "training with randomized/scrambled worlds." In
Unity you can randomize lighting, textures, colours, clutter, object poses, camera jitter
(your spec already calls for this). For each randomized frame:
- run SAM3 + DA3 → `(mask, 3D centroid, grasp axis, support plane)`,
- store `(WristCam RGB, EnvCam RGB, label)` rows.

Now you have a **synthetic grasp dataset** with two label sources you can cross-check:
1. **Unity ground truth** (perfect — you own the scene graph), and
2. **SpatialClaw's estimate** (what a real-camera perception stack would infer).

Where they disagree on a randomized scene tells you *which randomizations break
perception* — i.e. it directly measures sim-to-real robustness of the vision path.

**Effort:** ~1 week (depends on S1+S2). **Payoff:** a reusable labeled dataset + a
robustness diagnostic for domain randomization. **Risk:** medium; throughput-bound
(DA3/SAM3 are not real-time on big batches — run offline).

---

### S4 — Train a compact RGB-only grasp policy with P4D distillation *(the 4D-RGPT idea)*

**Goal:** a small, fast vision network that runs on the real arm (via LeRobot) yet has
"4D" perception baked in — **trained on S3's dataset**, using 4D-RGPT's distillation trick
but with **Unity ground truth as the teacher** (no L4P, no 8B VLM).

**The trick (P4D, reframed):** train your grasp network on RGB, but add **auxiliary
heads** that predict Unity's free ground-truth buffers:
- depth (you render it),
- optical flow / object motion (you know object velocities),
- a dynamic/object mask (you own the segmentation).

These auxiliary losses force the RGB encoder to internalize geometry/motion. **At
inference you drop the heads** — the deployed policy is RGB-only with zero added cost.
That's exactly 4D-RGPT's selling point ("transfer 4D representation… without additional
inference cost"), but supervised by Unity instead of a frozen expert. Read
`4D-RGPT/llava/model/language_model/llava_llama.py:283-396` (`perception_distillation`)
and `--ed_weights "depth=0.1,flow_2d_backward=0.001,dyn_mask=0.01"` for the loss-weighting
recipe — the natural-scale-mismatch warning (flow dominates if equal-weighted) applies to
your version too.

**Optional drop-in:** `4D-RGPT/llava/model/pe/time_pe.py` — a 45-line Timestamp Positional
Encoding if you make the policy temporal (multi-frame grasp/tracking).

**Effort:** ~2–3 weeks (real training loop). **Payoff:** a deployable, sim-to-real,
vision-driven grasp policy with strong geometry priors. **Risk:** higher — this is real ML
work, and is the natural fit for the "sensor-policy / diffusion-policy training" pillar.

---

### S5 — Fold vision into the GA / diffusion-policy training loops

**Goal:** connect the vision stack to ARMSMITH's existing learning layers.

- **GA / CMA-ES:** use the DA3/SAM3 "cube centred & grasp-ready in wrist view" signal as a
  **vision-derived reward/fitness term** (your camera spec already suggests
  vision-centring reward). Evolve morphologies/controllers that present the target well to
  the wrist cam.
- **Diffusion policy:** your `scripts/diffusion/` trains action sequences. Feed the
  S4 RGB-encoder features (or DA3 point maps) as the **observation conditioning** for the
  diffusion policy, instead of/in addition to joint state. SpatialClaw's `points[H,W,3]`
  in arm-base frame is a clean conditioning signal.

**Effort:** integrates with existing scripts; ~1–2 weeks per loop. **Payoff:** vision-
conditioned evolution and diffusion. **Risk:** medium; depends on S1–S4 maturity.

---

### S6 — Natural-language scene critic (speculative, optional)

**Goal:** a HUD "scene critic" that answers spatial questions about the workspace.

Run `nvidia/4D-RGPT-8B` (HF weights, NC license) behind the bridge via
`4D-RGPT/eval/nvila.py`'s `generate_content` path. Ask: *"which object should be picked
first?"*, *"is the cube near the table edge?"* Output is **text**, not a pose — useful as a
demo/debug overlay or a high-level task selector, **not** for low-level control.

**Effort:** medium (pinned `transformers==4.46`/`torch==2.3` env + a big GPU). **Payoff:**
low/niche. **Risk:** brittle env, heavy. **Recommendation: only if you specifically want a
language layer.** Otherwise skip.

---

## Recommended sequence

```
S1 (DA3 depth endpoint)  ──►  S2 (vision grasp recipe)  ──►  S3 (auto-label random worlds)
                                                               │
                                                               ▼
                                              S4 (P4D-distilled RGB grasp policy)
                                                               │
                                                               ▼
                                              S5 (feed GA + diffusion loops)   [S6 optional, anytime]
```

**Single highest-leverage play:** S1→S3→S4 — use SpatialClaw (DA3+SAM3+geometry) as the
**offline teacher/auto-labeler** over domain-randomized Unity scenes, then train a small
RGB-only student with 4D-RGPT's **P4D distillation** (Unity GT as teacher), and deploy the
student on the real arm via LeRobot. This unifies both NVIDIA repos around ARMSMITH's
actual goal: **sim→real, vision-driven grasping** — and it leans directly into your stated
interest in computer-vision AI, spatial understanding, and randomized-world training.

---

## Guardrails

- **License (NC):** research/learning use only. If ARMSMITH ever heads commercial, treat
  4D-RGPT / SpatialClaw / DA3 / SAM3 code + weights as prototype-only and plan permissive
  replacements for any production grasp/depth model. SAM3 weights are HF-gated.
- **Compute reality:** DA3 and SAM3 are single-GPU and the realistic things to run. The
  26B–397B VLMs, FP8/DeepGEMM, vLLM serving, and SLURM chains are **not** needed for any
  scenario above except S6.
- **Repo hygiene:** heavy demo media under `research/external/**` is gitignored; the
  working clones with full media live in `~/projects/github_repos/`. Re-fetch submodules
  there with `git submodule update --init --recursive` if you need more models
  (map-anything is currently a placeholder).
