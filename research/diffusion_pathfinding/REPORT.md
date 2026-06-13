# Diffusion Models for Robot-Arm Control & Pathfinding — Research Report

> Compiled 2026-06-13 (Session 7d). Scope: state of "Diffusion Policy" and trajectory-diffusion
> methods (2022–2025), assessed for the ARMSMITH project — a Unity SO-101/SO-ARM100 6-DOF arm sim
> that currently uses Jacobian/DLS IK + a genetic-algorithm policy trainer, records
> `armsmith.waypoints.v1` demos, and has a LeRobot bridge. RESEARCH ONLY.

---

## 1. What Diffusion Policy Is

**Core reference:** Chi et al., *"Diffusion Policy: Visuomotor Policy Learning via Action Diffusion"* —
RSS 2023, extended IJRR 2024. https://arxiv.org/abs/2303.04137 ·
https://diffusion-policy.cs.columbia.edu/ · code: https://github.com/columbia-ai-robotics/diffusion_policy

Diffusion Policy reframes the policy not as a function `π(a|o)` that *regresses* one action, but as a
**conditional denoising diffusion process over actions**. At inference you start from Gaussian noise in
action space and iteratively denoise it — conditioned on the current observation — into a coherent action
chunk. Same DDPM machinery as image generators, but the generated object is a chunk of robot actions.

Training learns a noise-prediction network ε_θ(a_k, k, o) ≈ the noise added to a clean action a⁰ at
diffusion step k. This is equivalent to learning the **score** ∇ log p(a|o). Inference runs the reverse
(denoising) process to sample from p(a|o).

### vs plain behavior cloning (BC)
| Aspect | Plain BC (MSE/Gaussian) | Diffusion Policy |
|---|---|---|
| Output | one action vector | a *sample* from a learned distribution |
| Multimodality | averages modes → invalid "in-between" actions | commits to one coherent mode per rollout |
| Training | regression/contrastive, can be unstable | simple denoising MSE on noise — very stable |
| Action | usually single step | naturally an **action chunk** (sequence) |

Diffusion gets the expressiveness of energy-based/implicit models (multimodal, implicit) but trains with a
plain MSE denoising loss — no contrastive negative-sampling instability (the reason it beat Implicit BC).

---

## 2. Why It's Better for Manipulation

- **(a) Multimodal demos are first-class.** Human teleop is messy (left vs right around an obstacle). MSE
  BC averages them → drives into the obstacle. Diffusion captures all modes, picks one coherently.
- **(b) Action chunking + receding horizon = temporal consistency.** Predict T_p≈8–16 future actions,
  execute only the first T_a≈4–8, then re-plan (MPC-style). Gives: smooth/no-jitter trajectories, less
  compounding error than single-step BC, and reactivity to perturbations.
- **(c) Smooth 6-DOF motion near kinematic limits** (learned from real motion vs IK branch-flipping).
- **(d) Closed-loop visual feedback** — copes with moved objects, distractors, pushes.

### Problems with scripted IK / open-loop it solves
- No analytic *task* model needed (only robot kinematics) — behavior is learned, not hand-authored.
- Avoids IK branch-flipping / singularity stalls at the behavior level.
- Fixes open-loop fragility: a recorded waypoint trajectory (what `BehaviourRecorder` produces) fails if
  the object moves 2 cm; diffusion closes the loop on observations.
- Handles contact-rich/deformable tasks (pour, wipe, fold) that have no clean IK.

---

## 3. Key Papers & Variants

**Foundational (action diffusion)**
- **Diffusion Policy** — Chi et al., RSS 2023/IJRR 2024 — origin; +46.9% avg over prior SOTA across 12
  tasks; receding-horizon chunking; 1D temporal U-Net & time-series transformer denoisers.
  https://arxiv.org/abs/2303.04137

**Faster inference**
- **Consistency Policy** — Prasad et al., RSS 2024 — distills Diffusion Policy into a consistency model
  → 1-to-few-step denoising (≈order-of-magnitude faster), keeps competitive success.
  https://arxiv.org/abs/2405.07503 · https://consistency-policy.github.io/
- (DDIM / **DPM-Solver** give few-step deterministic sampling without distillation — common drop-in.)

**Better observations / 3D**
- **3D Diffusion Policy (DP3)** — Ze et al., RSS 2024 — conditions on a compact 3D point cloud; works
  with as few as **10 demos** on 67/72 sim tasks; 85% on 4 real tasks with 40 demos; strong
  generalization; fewer safety violations. "Simple DP3" trims the U-Net for 2× speed.
  https://arxiv.org/abs/2403.03954 · https://3d-diffusion-policy.github.io/

**Sample efficiency via symmetry**
- **Equivariant Diffusion Policy (EquiDiff)** — Wang et al., CoRL 2024 — bakes SO(2) symmetry of 6-DOF
  control into the denoiser; +21.9% over baseline DP, especially low-data (<60 real demos).
  https://arxiv.org/abs/2407.01812 · https://equidiff.github.io/

**Large / foundation diffusion robotics**
- **RDT-1B** — Liu et al., 2024 — 1.2B-param diffusion transformer, pretrained on 46 datasets/1M+
  episodes, bimanual ALOHA; unified action space; ~381 actions/s. Diffusion scales to foundation size.
  https://arxiv.org/abs/2410.07864 · https://rdt-robotics.github.io/rdt-robotics/
- (Adjacent: **Octo** transformer generalist w/ diffusion action head; **π0/pi-zero** uses flow-matching
  — diffusion's close cousin. Diffusion/flow action heads are now default for large robot models.)

**Trajectory diffusion for planning / RL (the "pathfinding" lineage — see §4)**
- **Diffuser** — Janner, Du et al., ICML 2022 — seminal "planning as denoising": diffuse over whole
  state-action trajectories, bias sampling with reward/cost gradients (classifier guidance) or condition
  on goals (inpainting). https://arxiv.org/abs/2205.09991 · https://diffusion-planning.github.io/
- **Decision Diffuser** — Ajay et al., ICLR 2023 Oral — return/constraint/skill-conditioned trajectory
  diffusion via classifier-free guidance; composes constraints/skills at test time.
  https://arxiv.org/abs/2211.15657
- **AdaptDiffuser** — ICML 2023 — self-evolving planner; reward-gradient guidance generates synthetic
  expert data for unseen tasks. https://arxiv.org/abs/2302.01877
- **Hierarchical Diffuser** — Chen et al., 2024 — jumpy subgoal diffusion + low-level diffuser for
  long-horizon. https://arxiv.org/abs/2401.02644

**Motion-planning-specific diffusion**
- **Motion Planning Diffusion (MPD)** — Carvalho et al., IROS 2023 — learns a diffusion prior over
  trajectory distributions, samples posterior conditioned on start/goal while **cost-guiding** toward
  collision-free; demonstrated on planar robots and 7-DOF Panda incl. **obstacles unseen in training**;
  multimodal paths. THE most direct "diffusion as motion planner" reference.
  https://sites.google.com/view/mp-diffusion · https://github.com/jacarvalho/mpd-public

---

## 4. Diffusion for Pathfinding / Motion Planning (the user's stated interest)

### Reframe: trajectory optimization *as* diffusion
Classical planners search config space:
- **RRT\*, PRM** (sampling-based): probabilistically complete, asymptotically optimal, but jerky paths
  needing post-smoothing; replanning from scratch is costly.
- **CHOMP/STOMP/TrajOpt** (optimization-based): descend a smoothness+collision cost from an initial
  trajectory; prone to local minima depending on initialization.

Diffusion planners (Diffuser, MPD) treat a **whole trajectory** τ=[s₀,a₀,…,s_H] as the object to be
**generated by denoising**. A prior learned from successful plans/demos encodes smoothness+feasibility, so
denoising noise → a smooth in-distribution trajectory with little post-smoothing, and captures
**multimodal** routes (left vs right) which RRT/optimization give one-at-a-time.

### Imposing obstacle / goal constraints (both from guided image generation)
1. **Cost-guided sampling (classifier guidance):** at each denoising step nudge the sample by the gradient
   of a cost — signed-distance-field collision cost, joint-limit cost, EE-orientation cost:
   `τ_{k-1} ← denoise(τ_k) + α·∇_τ(−Cost(τ))`. Biases toward collision-free/goal-reaching at test time,
   no retraining; works for obstacles never seen in training (MPD).
2. **Inpainting / conditioning (hard constraints):** clamp τ[0]=start and τ[H]=goal every denoising step
   (like fixing known pixels in image inpainting). **Classifier-free guidance** (Decision Diffuser) trains
   conditioned on the goal/return directly.

### Trade-offs vs classical
- **Pros:** learned smoothness (less post-processing), native multimodality, flexible test-time cost/goal
  composition, fast amortized sampling once trained, learns task-specific motion "style."
- **Cons:** no completeness/optimality *guarantees*, needs a dataset of good trajectories, can hallucinate
  slightly-infeasible segments (keep a final feasibility/collision check), many-step latency unless
  distilled (Consistency) or few-step sampled.

**For ARMSMITH:** an **MPD-style diffusion motion planner is the best first match** to the existing
IK+waypoint pipeline — it stays in joint/config space (already logged), outputs waypoint trajectories
(existing export format), and can use Unity physics collision queries as the guidance cost.

---

## 5. Practical Implementation Requirements

**Data:** demonstration episodes = time series of (obs, action). DP image-based ~50–200 demos/task;
**DP3 ~10–40**; **EquiDiff <60**. ARMSMITH's `BehaviourRecorder` already emits the right artifact:
time-stamped joint-angle + gripper trajectories at dt=0.05 s (20 Hz) in `armsmith.waypoints.v1`. Store
per-dim normalization stats with the checkpoint (LeRobot does this via `dataset.meta.stats`).

**Observation space:** low-dim state (joint angles, EE pose, object pose) — easiest, fastest, ideal first
target (sim has ground truth). Images — needs a visual encoder (ResNet-18+GroupNorm or small ViT).
Point clouds (DP3) — best sample-efficiency/generalization; **Unity can render synthetic depth/point
clouds for free** (a real advantage).

**Action representation:** DP favors **action chunks** (predict T_p≈8–16, execute T_a≈4–8). Natural choice
here = **absolute joint targets in degrees** (what recorder logs and what LeRobot `send_action({motor:deg})`
consumes). EE-pose actions also feasible since DLS IK exists.

**Architecture:** denoiser = **1D temporal U-Net** (robust default) or **transformer** (longer horizons).
Conditioning via FiLM/cross-attention + timestep embedding. Sampler: DDPM (~50–100 steps, quality) →
DDIM/DPM-Solver (~5–20, faster) → Consistency (1-few step, real-time).

**Compute/latency:** training a low-dim or single-cam DP = hours-to-a-day on one GPU (RTX 3090/4090).
Inference DDPM ~tens of ms–>100 ms; with action chunking at 20 Hz you only re-plan ~2.5×/s — comfortable.

---

## 6. Realistic Unity Integration

ARMSMITH is well-positioned: (a) `BehaviourRecorder`/`DemoRecorder` already emit `armsmith.waypoints.v1`;
(b) `EvolutionTrainer` (external-optimizer mindset in place); (c) working **LeRobot bridge**
(`scripts/realbot/armsmith_lerobot.py`, `joint_map_lerobot.json`) for SO-101; (d) MCP bridge for control.

### Architecture: Python (PyTorch/LeRobot) trains; Unity = data source + deployment target
```
UNITY (C#): ArticulationBody arm + DLS-IK + Gripper + Cameras/Depth
  BehaviourRecorder ─► armsmith.waypoints.v1 JSON (+obs/images)   [COLLECT]
       ▲ joint targets in (SetTargets)        │ export
       │                                       ▼ files / HF dataset
PYTHON (PyTorch):
  Converter: armsmith.waypoints.v1 ─► LeRobotDataset
  Train: lerobot-train --policy.type=diffusion (DiffusionPolicy)  [TRAIN]
  Inference server: load ckpt → denoise → action chunk            [DEPLOY]
       │ per-decision: obs ─► server ─► next T_a joint targets
       ▼ (same MCP bridge feeds joint targets into Unity FixedUpdate)
```

**Pipeline:** (1) collect N teleop/scripted demos per task via `BehaviourRecorder` (+camera/depth if used);
(2) write a Python adapter `armsmith.waypoints.v1 → LeRobotDataset`; (3) `lerobot-train
--policy.type=diffusion ...` (LeRobot ships a maintained Diffusion Policy impl alongside ACT); (4) inference
server via `DiffusionPolicy.from_pretrained` exposing a socket/MCP endpoint; (5) feed chunks into
`ArmController.SetTargets` over T_a FixedUpdates, re-plan (receding horizon); (6) sim-to-real: same
checkpoint runs on the physical SO-101 via the existing `armsmith_lerobot.py` path (`joint_map_lerobot.json`
maps game joints → Feetech motors).

**Libraries:** **LeRobot** (HF — has Diffusion Policy, ACT, SO-101 support; already referenced in repo);
`diffusers` (schedulers/U-Net); original `diffusion_policy` repo; `3D-Diffusion-Policy`; `mpd-public`.

---

## 7. Honest Assessment & Recommended Adoption Path

**Diffusion genuinely wins:** contact-rich/multimodal/hard-to-script behaviors; closed-loop reactivity to
moved objects/disturbances; learning "style" from demos; multimodal route choice in clutter.

**Classical IK still better/simpler:** deterministic point-to-point free-space moves to known poses
(IK is exact, instant, zero-data, verifiable — don't replace); safety-critical completeness/optimality
(keep RRT\*/feasibility checks); zero-data regime; debuggability.

**Data barrier (stated plainly):** diffusion is imitation learning — only as good as the demos. Unity
*mitigates* this hugely (cheap infinite ground-truth demos, synthetic depth for DP3, domain randomization),
a real strategic advantage. The existing **GA trainer can be repurposed to GENERATE demos** (GA-optimized
successful trajectories → diffusion training set — clean synergy).

### Recommended incremental path for ARMSMITH
1. **Keep IK as the substrate / baseline.** Don't rip it out.
2. **Repurpose recorder + GA as a demo factory.** Build the `armsmith.waypoints.v1 → LeRobotDataset`
   converter. (Lowest-risk, high-leverage first step.)
3. **Train a low-dim joint-space Diffusion Policy** on one task (reach-and-grasp a placed cube) via
   `--policy.type=diffusion`; obs = joint state + object pose; action = joint-deg chunks. Validate in-sim
   against IK on the same task.
4. **Add closed-loop inference** via MCP (receding horizon); benchmark robustness (move object, perturb
   mid-motion) — show diffusion recovers where waypoint playback fails. This is the concrete "why."
5. **Branch by goal:** vision/skills → **DP3** (synthetic point clouds); **pathfinding/collision-free
   planning (user's interest)** → **MPD-style diffusion motion planner** in joint space using Unity
   collision queries as guidance cost (outputs drop straight into existing export/playback); sample
   efficiency → **EquiDiff**; real-time/weak HW → **Consistency Policy**.
6. **Sim-to-real:** deploy the checkpoint on the physical SO-101 through the existing LeRobot bridge.

**Bottom line:** the user is directionally right — diffusion is a more powerful, more robust *behavior*
generator than scripted IK for manipulation and for multimodal collision-free planning, and it's the field
standard (2023→2025). It **complements** IK rather than replacing it: keep IK for exact free-space moves and
as a baseline/feasibility check, use GA+recorder as a demo factory, and adopt diffusion incrementally
starting with a low-dim joint-space policy on one task through the LeRobot pipeline already partly present.
The two most project-aligned entry points: **LeRobot Diffusion Policy** (drop-in, SO-101-ready) and an
**MPD-style diffusion motion planner** (matches the joint-space waypoint pipeline and the pathfinding goal).

---

### Citations
- Diffusion Policy — https://arxiv.org/abs/2303.04137 · https://diffusion-policy.cs.columbia.edu/
- 3D Diffusion Policy (DP3) — https://arxiv.org/abs/2403.03954 · https://3d-diffusion-policy.github.io/
- Consistency Policy — https://arxiv.org/abs/2405.07503 · https://consistency-policy.github.io/
- Equivariant Diffusion Policy — https://arxiv.org/abs/2407.01812 · https://equidiff.github.io/
- RDT-1B — https://arxiv.org/abs/2410.07864 · https://rdt-robotics.github.io/rdt-robotics/
- Diffuser — https://arxiv.org/abs/2205.09991 · https://diffusion-planning.github.io/
- Decision Diffuser — https://arxiv.org/abs/2211.15657
- AdaptDiffuser — https://arxiv.org/abs/2302.01877
- Hierarchical Diffuser — https://arxiv.org/abs/2401.02644
- Motion Planning Diffusion (MPD) — https://sites.google.com/view/mp-diffusion · https://github.com/jacarvalho/mpd-public
- LeRobot docs — https://huggingface.co/docs/lerobot/en/il_robots
