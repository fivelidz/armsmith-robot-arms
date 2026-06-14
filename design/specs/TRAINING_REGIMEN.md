# ARMSMITH Training Regimen Spec

> A unified, selectable training system for the SO-101 arm: pick a backend (model), shape the reward,
> run a curriculum, randomize conditions, evaluate, and watch it learn. Grounded on the now-verified
> physics (MotorPhysicsCheck: realistic STS3215 drives + servo rate/ticks + gravity hold).

## 1. Goals
- Make training a **first-class, legible loop**: choose what to train, on what task, with what conditions,
  and SEE it improve (curves + arm behaviour, including multiple generations at once).
- Support **model inclusion/exclusion**: train with Motion-GA, Sensor-Policy (neural net), or the
  Diffusion pipeline — and turn sensor MODULES on/off so you can ask "which information helps which task".
- Be **honest about the physics**: the arm reaches modest commands fast and sags realistically at extreme
  extension; the regimen keeps targets within the dependable envelope and uses the verified IK/grasp path.

## 2. Training backends (the "models" — include/exclude)
| Backend | Genome | Control | When to use |
|---|---|---|---|
| **Motion-GA** | MotionGenome (keyframes: angles+gripper+hold) | open-loop replay of keyframes | fast, no sensors, finds a fixed trajectory for a fixed scene |
| **Sensor-Policy** | PolicyGenome (small MLP) | closed-loop: obs -> joint deltas each step | reacts to object/sensor state; generalises across randomized scenes |
| **Diffusion (Python)** | LeRobot DiffusionPolicy (external) | closed-loop action chunks via MCP server | best behaviour quality; needs demos (GA + recorder = demo factory) |

Selection: a `TrainingBackend` enum (`MotionGA`, `SensorPolicy`, `Diffusion`) on the trainer. Diffusion
trains externally (scripts/diffusion) and is DEPLOYED in-sim via DiffusionPolicyClient — so "train
diffusion" in-UI = collect demos + export + (offline) train + load. The UI exposes all three; Motion-GA
and Sensor-Policy run fully in-engine.

## 3. Reward shaping (per scenario, with adjustable weights)
Base reward = task term + shaping terms. ScenarioManager.ComputeReward already gives the task term; we add
SHAPING with player-tunable weights (Conditions UI):
- `w_reach`   : -dist(tip, target)              (always-on dense guidance toward the object)
- `w_grasp`   : + bonus when holding the object
- `w_place`   : -dist(object, goal)             (toward the place location once grasped)
- `w_success` : + large bonus on scenario success (+ at-rest check)
- `w_energy`  : - sum |joint deltas|            (smooth, efficient motion; sim-to-real friendly)
- `w_selfpen` : - SelfCollision.MaxSelfPenetration  (discourage near-self-intersection)
- `w_oob`     : - large penalty if the object leaves the table (ties into the OOB watchdog)
Defaults make a sensible dense curriculum reward; weights are exposed so you can study their effect.

## 4. Curriculum (easy -> hard)
A `difficulty` 0..1 scales the task + randomization so a policy learns reach before full pick-place:
- **L0 Reach** (diff 0.0-0.2): touch a fixed target; reward = w_reach only. Tiny randomization.
- **L1 Reach+Grasp** (0.2-0.4): grasp a fixed cube; + w_grasp.
- **L2 Pick-Place fixed** (0.4-0.6): grasp + place on a fixed pad; + w_place + w_success.
- **L3 Pick-Place randomized** (0.6-0.8): cube + pad positions randomized within reach.
- **L4 Scrambled world** (0.8-1.0): heavy domain randomization (pose/size/mass/color/lighting/sensor
  noise/table height) — robustness; ties into the "scrambled world" mode (MG2).
Auto-advance: when success-rate over the last N evals exceeds a threshold, bump difficulty (or manual).

## 5. Conditions (Conditions UI — what the player edits)
- Scenario (which task) + difficulty/curriculum level.
- Randomization STRENGTH slider (0 = fixed, 1 = scrambled): object pose range, size, mass, color,
  lighting, camera jitter, sensor noise, table height.
- Reward weights (section 3) as sliders.
- Sensor MODULES on/off (MotorEncoders / TaskState / IMU / RangeFinder / Lidar2D / DepthCamera /
  EFleshTactile) — defines the observation the Sensor-Policy/Diffusion sees (model inclusion/exclusion of
  INFORMATION).
- GA params: population, elite, mutation rate/sigma, keys-per-genome (Motion-GA), hidden size (Policy).
- Rollout speed-up (headless fast eval).

## 6. Eval protocol
- Each genome is rolled out from a HARD-HOMED start (clean articulation, verified physics) on K randomized
  resets; fitness = mean shaped reward; success-rate = fraction of resets that hit the scenario success.
- Best genome tracked per generation; success-rate + best/mean fitness logged for the curves.
- Determinism: per-eval RNG seed so curves are reproducible; OOB watchdog + NaN watchdog keep evals clean.

## 7. Training UI (what the player sees)
- **Backend selector** (Motion-GA / Sensor-Policy / Diffusion) + start/stop/step-one-generation.
- **Live curves**: best fitness, mean fitness, success-rate vs generation (a small in-world line plot).
- **Progress**: generation #, population size, eval %, current difficulty, elapsed.
- **Population view**: compact list/grid of genomes with fitness, lock/select (interactive evolution).
- **MULTI-GENERATION view** (MG1): overlay/ghost several recent generations' best behaviours, OR a small
  grid of mini-arms, so you see the SPREAD of evolving behaviour, not just one.
- **Export**: best -> waypoints demo (F11) feeding the diffusion factory.

## 8. Implementation plan (incremental, each headless-verifiable)
- TR1: `TrainingConfig` (backend enum, difficulty, randomization strength, reward weights, sensor mask,
  GA params) — one serializable struct the trainer + UI share.
- TR2: wire reward shaping + curriculum scaling into EvolutionTrainer eval (both Motion-GA and Policy).
- TR3: TrainingBackend selection (already have policyMode bool -> enum incl. Diffusion-deploy hook).
- TR4: headless `TrainingSmokeCheck` — run a few generations of each backend, assert fitness IMPROVES.
- TR5: Training UI panel (curves + selector + progress + multi-gen).
- TR6: Conditions UI panel (sliders + sensor toggles + scenario).
- TR7: scrambled-world randomization + strength slider (MG2).
- TR8: multi-generation visualization (MG1) using the existing PathVisualizer/ghost rendering.

## 9. Honest constraints
- The arm sags at extreme extension; curriculum keeps targets in the dependable envelope and uses the
  analytic-IK grasp path that's verified to reach+grasp+lift.
- In-engine GA/Policy train fast headlessly; Diffusion training is external (Python/LeRobot) — the UI
  orchestrates collect/export and deployment, not the gradient steps.
