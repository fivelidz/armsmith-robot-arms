# Robot Arm Evolution & Control — Research Report
## Unity Pick-and-Place Game with Player-Guided Generation

**Author:** Research synthesis for fivelidz / robot_arms Unity project  
**Date:** 2026-05-30  
**Context:** Player watches generations of robot arms improve at pick-and-place tasks; player participates in design/selection. This project sits in the same stack as the physical `robot_hand` (FEETECH STS3215 servos, tendon-driven, 3D-printed), so bridging simulation to hardware is a meaningful future path.

---

## Table of Contents

1. [Inverse Kinematics for Game Arms](#1-inverse-kinematics-for-game-arms)
2. [Evolutionary Approaches](#2-evolutionary-approaches)
3. [Imitation Learning + Reinforcement Learning](#3-imitation-learning--reinforcement-learning)
4. [Fitness / Reward Design for Pick-and-Place](#4-fitness--reward-design-for-pick-and-place)
5. [Practical Recommendation — Phased Approach](#5-practical-recommendation--phased-approach)
6. [Quick Reference Summary](#6-quick-reference-summary)
7. [References](#7-references)

---

## 1. Inverse Kinematics for Game Arms

Inverse Kinematics (IK) answers the question: *given a desired end-effector position/orientation, what joint angles produce it?* For a Unity robot arm with revolute joints (rotation-only), four main approaches exist.

### 1.1 CCD — Cyclic Coordinate Descent

**How it works:** Iterates from the end-effector back toward the root, one joint at a time. Each joint is rotated to minimise the distance between the end-effector and the target. The pass repeats until convergence or a max-iteration count is reached.

**Pseudocode:**
```
function CCD(joints[], target, maxIterations, tolerance):
    for iter in 0..maxIterations:
        for i in (numJoints-1) downto 0:          // leaf → root
            endEffector = joints[last].worldPosition
            if distance(endEffector, target) < tolerance:
                return SUCCESS

            toEnd    = normalize(endEffector  - joints[i].worldPosition)
            toTarget = normalize(target       - joints[i].worldPosition)

            angle = signedAngle(toEnd, toTarget, joints[i].rotationAxis)
            angle = clamp(angle, joints[i].minAngle, joints[i].maxAngle)

            joints[i].Rotate(angle)

    return PARTIAL   // ran out of iterations
```

**Pros:**
- Extremely simple to implement; ~50 lines of C#
- Works naturally with joint angle limits
- Real-time friendly (~100 joints at 60 fps is fine)
- Intuitively debuggable — each rotation step is visible

**Cons:**
- Greedy; can get stuck in local minima
- Popping/jitter near singularities
- Doesn't minimise energy or prefer natural poses
- Slow convergence for high-DOF chains (>8 joints)

**Best for:** A 3–6 DOF arm, especially early development. Unity's built-in `Animation.Rigging` package uses a CCD solver by default.

---

### 1.2 FABRIK — Forward And Backward Reaching IK

**How it works:** Two-pass geometric algorithm. Backward pass pulls bones toward the target; forward pass re-anchors at the root. Purely positional — orientation is computed afterward. Converges in very few iterations.

**Pseudocode:**
```
function FABRIK(joints[], target, maxIterations, tolerance):
    // Pre-compute segment lengths
    lengths[i] = distance(joints[i], joints[i+1])
    totalLength = sum(lengths)

    if distance(joints[0], target) > totalLength:
        // Target unreachable — stretch straight toward target
        for i in 0..numJoints-2:
            r = distance(target, joints[i])
            λ = lengths[i] / r
            joints[i+1] = lerp(joints[i], target, λ)
        return UNREACHABLE

    root = joints[0].position     // save original root

    for iter in 0..maxIterations:
        // ---- BACKWARD PASS (leaf → root) ----
        joints[last] = target
        for i in (last-1) downto 0:
            r = distance(joints[i+1], joints[i])
            λ = lengths[i] / r
            joints[i] = lerp(joints[i+1], joints[i], λ)

        // ---- FORWARD PASS (root → leaf) ----
        joints[0] = root
        for i in 0..last-1:
            r = distance(joints[i+1], joints[i])
            λ = lengths[i] / r
            joints[i+1] = lerp(joints[i], joints[i+1], λ)

        if distance(joints[last], target) < tolerance:
            return SUCCESS

    return PARTIAL
```

> **Note:** This gives joint *positions*. To recover orientations, compute each bone's forward vector from position differences and use `Quaternion.LookRotation`.  
> Joint limits require a constrained variant (project each point onto a cone/plane after each step).

**Pros:**
- Extremely fast convergence (3–5 iterations typical)
- Handles branching chains (tree structures — e.g. a gripper with multiple fingers)
- Produces natural-looking poses without energy terms
- Easy to extend with sub-base constraints

**Cons:**
- Joint angle limits require extra work (constrained FABRIK paper, Aristidou 2016)
- Works in position space — orientation IK (e.g. matching end-effector orientation) needs an extra pass
- Slightly harder to understand than CCD

**Best for:** Multi-segment arms (4–8+ DOF), multi-finger grippers, any situation where you want fast visual quality. **FABRIK is the recommended default for this game.**

---

### 1.3 Jacobian Transpose / Pseudo-Inverse

**How it works:** The Jacobian **J** maps joint-velocity space to end-effector velocity space. Given a desired end-effector velocity `Δx`, solve for joint velocities `θ̇`:

- **Transpose:** `θ̇ = Jᵀ · Δx` (approximate, fast, no matrix inversion)
- **Pseudo-inverse:** `θ̇ = J⁺ · Δx = Jᵀ(JJᵀ)⁻¹ · Δx` (accurate, expensive, singular at degenerate configs)

```
function JacobianTransposeStep(joints[], target, stepSize):
    J = computeJacobian(joints)           // (3 × numJoints) matrix
    deltaX = target - endEffector.position
    deltaTheta = J.Transpose() * deltaX
    for i in 0..numJoints-1:
        joints[i].angle += stepSize * deltaTheta[i]
        joints[i].angle = clamp(joints[i].angle, min, max)
```

**Pros:**
- Principled mathematical foundation
- Jacobian pseudo-inverse can minimise secondary objectives (energy, obstacle avoidance)
- Handles redundant chains (more DOF than needed) elegantly

**Cons:**
- Requires matrix operations every frame (acceptable for ≤8 joints, painful for more)
- Singular configurations → instability (need DLS to fix)
- More complex to implement correctly

---

### 1.4 Damped Least Squares (DLS)

Augmented pseudo-inverse that avoids singularity blow-up:

```
θ̇ = Jᵀ (JJᵀ + λ²I)⁻¹ · Δx
```

`λ` is the damping factor. Near singularities it keeps motion smooth at the cost of accuracy. This is used in production robot arms (Wampler 1986, Nakamura & Hanafusa 1986).

**Best for:** Research-grade work or when you need accurate orientation matching. For a game, FABRIK or CCD + Joint Limits gives better results with less code.

---

### 1.5 Comparison Table

| Method | Speed | Joint Limits | Singularities | Code Complexity | Best Use |
|---|---|---|---|---|---|
| CCD | ✅ Fast | ✅ Easy | ⚠️ Jitter | ⭐ Simple | Short arms (≤5 DOF), fast prototyping |
| FABRIK | ✅✅ Very fast | ⚠️ Extra work | ✅ Fine | ⭐⭐ Moderate | Multi-finger, branching, 4–8 DOF arms |
| Jacobian Transpose | ⚠️ Moderate | ⚠️ Extra work | ❌ Diverges | ⭐⭐⭐ Complex | Secondary-objective optimisation |
| DLS | ⚠️ Moderate | ⚠️ Extra work | ✅ Stable | ⭐⭐⭐⭐ Complex | When orientation control is required |

**Recommendation for this game:** Start with **FABRIK** (fast, looks good, handles evolved arm geometries with varying segment counts). Use Unity's `Animation.Rigging` package (`com.unity.animation.rigging`) which provides a FABRIK component out-of-the-box. Fall back to CCD for single-chain arms with tight joint limits.

---

## 2. Evolutionary Approaches

### 2.1 Neuroevolution — NEAT and HyperNEAT

**What it is:** Evolve the *topology and weights* of a neural network controller simultaneously, rather than just tuning weights on a fixed architecture.

**NEAT (NeuroEvolution of Augmenting Topologies — Stanley & Miikkulainen, 2002):**

- Population of `genomes`; each encodes a set of *node genes* and *connection genes*
- Mutations add new nodes (split an existing connection) or new connections
- Crossover tracks *innovation numbers* to align matching genes across different topologies
- *Speciation* protects innovation: new structural mutations compete within their species, not against optimised incumbents
- Networks start minimal and grow only as needed → avoids over-parameterisation

```
NEAT Loop:
    population = initialise(N minimal networks)
    for each generation:
        fitness_scores = evaluate_all(population, environment)
        species = speciate(population)              // group by genomic distance
        survivors = select_within_species(species, fitness_scores)
        offspring = crossover_and_mutate(survivors)
            // mutations: add_node, add_connection, change_weight, toggle_connection
        population = survivors + offspring
```

**HyperNEAT:**  
Encodes the network as a *Compositional Pattern Producing Network* (CPPN) that maps geometric positions of neurons to connection weights. Works well for controllers with geometric symmetry — e.g. a symmetric robot arm where left/right joints should have mirror-image weights. For a pick-and-place arm, HyperNEAT pays off when the arm has many joints (8+) arranged in a regular spatial structure.

**Key parameters for a robot arm game:**
- Inputs: joint angles (×N), end-effector position (×3), target object position (×3), gripper state (×1)
- Outputs: joint torque/velocity targets (×N), gripper open/close (×1)
- Fitness: pick-and-place score (see §4)

**Pros:**
- Discovers topology automatically — great for evolved arms with different DOF counts
- Naturally handles variable-sized genomes (different arm morphologies get different-sized nets)
- Speciation prevents premature convergence

**Cons:**
- Slower per generation than fixed-topology ES (topology crossover is expensive)
- NEAT implementations are available in Python (neat-python, PyTorch NEAT); calling from Unity requires a Python sidecar or exporting the evolved network as ONNX
- HyperNEAT is significantly more complex to implement

**Library pointers:**
- `neat-python` — reference implementation (Python)
- `MultiNEAT` — C++ with Python bindings, faster
- SharpNEAT — C# port, usable directly in Unity

---

### 2.2 Evolution Strategies — CMA-ES and OpenAI-ES

Unlike NEAT, Evolution Strategies (ES) assume a **fixed network topology** or **fixed parameter vector** and optimise only the values.

#### CMA-ES (Covariance Matrix Adaptation Evolution Strategy — Hansen, 2001)

- Maintains a multivariate Gaussian `N(μ, Σ)` over parameter space
- Each generation: sample `λ` candidate solutions, evaluate, update `μ` and `Σ` using the top-`μ` survivors
- `Σ` adapts the search shape — elongates along promising dimensions, contracts along unpromising ones
- State of the art for low-to-medium dimensional black-box optimisation (up to ~1000 parameters)

```
CMA-ES Loop:
    μ, Σ = initialise()
    for each generation:
        candidates = [sample_from_gaussian(μ, Σ) for _ in range(λ)]
        fitness = [evaluate(c, env) for c in candidates]
        ranked = sort_by_fitness(candidates, fitness)
        μ_new = weighted_mean(ranked[:μ_select])
        Σ_new = update_covariance(ranked[:μ_select], μ, Σ)
        μ, Σ = μ_new, Σ_new
```

**Best for:** Tuning a fixed-topology controller (e.g. a 2-layer MLP) for a specific arm morphology. Also excellent for tuning **motion primitive parameters** (keyframe timings, via-point positions, trajectory splines) — see §5.

#### OpenAI-ES (Salimans et al., 2017 — arXiv:1703.03864)

- Simpler: perturb parameter vector θ with isotropic Gaussian noise `εᵢ ~ N(0, σ²I)`
- Estimate gradient: `∇θ J ≈ (1/nσ) Σᵢ εᵢ · F(θ + εᵢ)`
- Update θ with Adam or SGD
- Highly parallelisable: each worker evaluates one perturbation independently, only communicates a scalar fitness
- Used to train humanoid locomotion policies on MuJoCo in 10 minutes across 1,000 CPUs

**For the game:** Run OpenAI-ES in a background thread / Python sidecar, evaluating candidates in fast Unity headless batches.

**Comparison:**

| Method | Dimensions | Convergence | Parallelism | When to use |
|---|---|---|---|---|
| CMA-ES | ≤1000 | Excellent | Limited | Tuning motion params, small nets |
| OpenAI-ES | 10k–1M | Good | Excellent | Larger nets, many workers |

---

### 2.3 Morphological Evolution — Evolving the Arm Design

This is the most visually spectacular form for a game: the *arm itself* evolves, not just the controller.

#### Karl Sims' Evolved Virtual Creatures (1994)

Karl Sims' seminal work at Thinking Machines Corporation evolved:
- **Morphology:** graph-based genotype encoding blocks (cuboids), connected by revolute/twist/sliding joints, with recursive grammar rules producing repetitive structures
- **Controller:** a neural network evolved alongside morphology — each node in the body graph maps to a node in the neural graph
- **Co-evolution of body and brain:** morphology and controller are evolved together; a body shape is only useful paired with a matching controller

The creatures were selected for swimming, walking, jumping, following objects, and competing for a cube. Pathological solutions ("cheating") emerged — e.g. very tall creatures that fell over and rolled toward the target.

**Paper:** *Evolving Virtual Creatures*, K. Sims, SIGGRAPH 1994.  
**Video:** https://www.karlsims.com/evolved-virtual-creatures.html

#### Genome Representation for Arm Morphology

For a pick-and-place arm (rather than full creature), a simplified morphology genome might encode:

```
ArmGenome {
    numLinks:        int in [2, 8]
    linkLengths:     float[] (one per link)
    linkWidths:      float[] (collision geometry)
    jointTypes:      enum[] {revolute_x, revolute_y, revolute_z, universal}
    jointLimits:     (min, max)[] in radians
    mountAngle:      float  (base rotation on platform)
    gripperType:     enum {parallel_jaw, 3finger, suction_cup, magnetic}
    gripperSpan:     float  (opening width)
}
```

**Mutation operators:**
- Add a link (insert a new bone)
- Remove a link
- Scale a link length ×(0.5–2.0)
- Change joint axis
- Swap gripper type

**Crossover:** Segment-aligned crossover — two parent genomes with different lengths are aligned by link index; shorter genome is padded.

**Challenge — Bootstrap Problem:** Random morphologies rarely do useful things. Options:
1. **Separate controller training per individual:** evaluate each morphology by running CMA-ES for 50 generations to find the best controller, *then* score the morphology. Expensive.
2. **Co-evolution:** evolve both simultaneously with a shared fitness signal (Sims' approach).
3. **Player selection:** the player evaluates morphologies visually and selects which arms proceed — a form of *interactive evolutionary computation* (see §5).

---

## 3. Imitation Learning + Reinforcement Learning

### 3.1 Behaviour Cloning (Imitation)

**Concept:** Collect expert demonstrations (player manually guides the arm to pick and place), then train a policy network to mimic the state → action mapping via supervised learning.

```
# Data collection
demonstrations = []
while collecting:
    state = observe(arm, target, gripper)
    action = human_input()  # or scripted IK target
    demonstrations.append((state, action))

# Supervised training
policy = NeuralNetwork(input=state_dim, output=action_dim)
for epoch in epochs:
    for (s, a) in demonstrations:
        prediction = policy(s)
        loss = MSE(prediction, a)
        backprop(loss)
```

**Problem — Distribution Shift:** The policy performs well on states it has seen, but makes small errors → drifts to states outside the training distribution → errors compound. Fix: *DAgger* (Dataset Aggregation) — periodically query the expert on states the policy actually visits.

**In the game context:** Record a "demo" of each arm design completing a pick-and-place via scripted IK. Use these as initialisation for RL — the policy starts already knowing the rough shape of the solution.

---

### 3.2 PPO — Proximal Policy Optimisation (RL)

PPO (Schulman et al., 2017) is the dominant on-policy RL algorithm for continuous control. It:
- Collects a rollout buffer of (state, action, reward, next_state) tuples
- Optimises a clipped surrogate objective to prevent large policy updates
- Is more stable than vanilla policy gradient and simpler than SAC/TD3

```
PPO Update:
    π_old = current_policy

    for update_step in N:
        rollouts = collect_rollouts(π_old, environment)
        advantages = compute_GAE(rollouts, γ, λ)

        for minibatch in rollouts:
            ratio = π(a|s) / π_old(a|s)
            clipped = clip(ratio, 1-ε, 1+ε)
            policy_loss = -min(ratio * A, clipped * A)
            value_loss = MSE(V(s), returns)
            entropy_bonus = -H(π(·|s))

            loss = policy_loss + c1*value_loss - c2*entropy_bonus
            gradient_step(loss)
```

**Typical hyperparameters for robot manipulation:**
- `learning_rate`: 3e-4
- `gamma` (discount): 0.99
- `lambda` (GAE): 0.95
- `clip_eps`: 0.2
- `batch_size`: 2048–8192
- `epochs_per_update`: 3–10

---

### 3.3 Unity ML-Agents (`com.unity.ml-agents`)

Unity ML-Agents provides first-class PPO (and SAC) support directly in the Unity editor.

**Setup for a robot arm:**

1. **Install package:**  
   `Window → Package Manager → Add by name → com.unity.ml-agents` (v3.x as of 2026)

2. **Agent component (`ArmAgent.cs`):**
```csharp
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class ArmAgent : Agent
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform[] joints;
    [SerializeField] private Rigidbody targetObject;

    public override void CollectObservations(VectorSensor sensor)
    {
        // Joint angles (N floats)
        foreach (var joint in joints)
            sensor.AddObservation(joint.localRotation);

        // End-effector position relative to arm base
        sensor.AddObservation(transform.InverseTransformPoint(endEffector.position));

        // Target position relative to arm base
        sensor.AddObservation(transform.InverseTransformPoint(target.position));

        // Object position & velocity
        sensor.AddObservation(transform.InverseTransformPoint(targetObject.position));
        sensor.AddObservation(targetObject.velocity);

        // Gripper state
        sensor.AddObservation(gripperOpenAmount);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Continuous: one torque per joint + gripper command
        for (int i = 0; i < joints.Length; i++)
            ApplyTorque(joints[i], actions.ContinuousActions[i]);

        ApplyGripper(actions.ContinuousActions[joints.Length]);

        // Reward signal — see Section 4
        float reward = ComputePickPlaceReward();
        AddReward(reward);
    }

    public override void OnEpisodeBegin()
    {
        // Randomise target object position
        targetObject.position = RandomPositionOnTable();
        ResetArmToHome();
    }
}
```

3. **Training config (`arm_ppo.yaml`):**
```yaml
behaviors:
  RobotArm:
    trainer_type: ppo
    hyperparameters:
      batch_size: 2048
      buffer_size: 20480
      learning_rate: 3.0e-4
      beta: 5.0e-3          # entropy coeff
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: true
      hidden_units: 256
      num_layers: 3
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 5000000
    time_horizon: 1000
```

4. **Train:**
```bash
mlagents-learn arm_ppo.yaml --run-id=arm_v1
```

5. **Run inference:** Drag the exported `.onnx` onto the `Behavior Parameters → Model` field. The arm runs its trained policy at runtime with no Python dependency.

**Multi-agent speedup:** Duplicate the arm scene 8–16 times in one Unity scene. All agents contribute to the same training run, collecting experience in parallel — typically 4–8× faster than single-agent.

**Note:** ML-Agents PPO currently (v3) supports running on-device inference via Unity Sentis (`com.unity.sentis`), the successor to the Barracuda inference engine.

---

## 4. Fitness / Reward Design for Pick-and-Place

Reward design is where most projects succeed or fail. For pick-and-place, a composite shaping reward works best. All terms are evaluated per simulation step unless noted.

### 4.1 Component Rewards

| Component | Formula | Notes |
|---|---|---|
| **Reach reward** | `r_reach = -α · dist(end_effector, object)` | Always on; encourages arm to move toward object |
| **Grasp success** | `+R_grasp` (one-time on grasp) | Binary: did the gripper close around the object? |
| **Transport reward** | `r_transport = -β · dist(object, target_bin)` | Active only after grasp is detected |
| **Place success** | `+R_place` (one-time on place) | Binary: did object land in target zone? |
| **Energy penalty** | `-γ · Σᵢ torqueᵢ²` | Discourages wasteful thrashing |
| **Time penalty** | `-δ` per step | Encourages speed |
| **Collision penalty** | `-ε` on self-collision or table collision | Optional; prevents spastic motion |

### 4.2 Recommended Full Formula

```
R(t) = r_reach(t)        // step reward: distance to object
     + r_transport(t)    // step reward: distance object-to-bin (only if grasped)
     + R_grasp           // one-time: +1.0 on successful grasp
     + R_place           // one-time: +10.0 on successful place in bin
     - γ · energy(t)     // step penalty
     - δ                 // time step penalty
```

**Typical magnitudes:**
- `α = 0.1` (reach weight)
- `β = 0.2` (transport weight — slightly higher to prioritise delivery)
- `γ = 0.001` (energy — small, don't over-penalise)
- `δ = 0.001` (time — small)
- `R_grasp = 1.0`
- `R_place = 10.0`

### 4.3 Grasp Detection

In Unity, detect a grasp using a trigger collider on the gripper fingers:

```csharp
bool grasping = false;

void OnTriggerStay(Collider other)
{
    if (other.CompareTag("PickableObject"))
    {
        float gripForce = Mathf.Abs(leftFinger.position.x - rightFinger.position.x);
        grasping = (gripForce < graspThreshold);
    }
}
```

Alternatively, use a `FixedJoint` that snaps the object to the gripper when contact force exceeds a threshold — simpler and more stable for game-speed physics.

### 4.4 Avoiding Reward Hacking

Common failure modes and fixes:

| Failure | Cause | Fix |
|---|---|---|
| Arm flails and scores reach reward | Reach reward too strong | Cap at -0.001/step; add velocity penalty |
| Arm slides object instead of picking | Grasp detection too loose | Require both fingers in contact simultaneously |
| Arm drops object at target zone boundary | `R_place` detection radius too small | Increase zone radius; add partial credit |
| Arm finds a static pose that avoids penalties | Time penalty absent | Add per-step time penalty |

---

## 5. Practical Recommendation — Phased Approach

### The Three Options

#### Option A: Scripted IK + Player Tuning
- Deterministic FABRIK solver moves the arm to programmed waypoints (home → above object → grasp → above bin → release)
- Player tweaks parameters: approach height, speed profile, gripper force
- No training; instant feedback

**Pros:** Zero latency, deterministic, easy to show players "cause and effect"  
**Cons:** Not actually evolving/learning; limited emergent behaviour; doesn't scale to varied environments

#### Option B: Parameter-Evolution of Motion Primitives
- The arm uses scripted IK for execution, but its *motion parameters* are a **genome**
- Genome: `[approach_height, grasp_speed, transport_speed, release_height, joint_stiffness×N, ...]` (~20–50 parameters)
- Evolve with CMA-ES or a simple genetic algorithm (tournament selection, Gaussian mutation)
- Evaluation: run the arm autonomously for N episodes, score with §4 fitness function
- Generation time: 1–10 seconds per individual at game speed (~100 simulated seconds per generation)

**Pros:**  
- Playable within a weekend of development  
- Visible, interpretable evolution (player can see *which* parameters changed)  
- No ML dependencies — pure C# in Unity  
- Can add morphology evolution later (different arm lengths = different genome segments)  

**Cons:**  
- Controller quality ceiling: it's still scripted IK — the arm can't discover novel strategies  
- Doesn't generalise well to randomised object positions without a lot of primitives  

#### Option C: Full ML-Agents RL (PPO)
- End-to-end learned policy; no scripted IK
- Arm observes environment and outputs joint torques or angle targets
- Train with PPO via `mlagents-learn`; deploy `.onnx` into Unity

**Pros:**  
- Genuinely learns generalised pick-and-place; handles varied positions  
- "Modern" path; impressive emergent behaviour  
- Direct integration with Unity (no Python at runtime)  

**Cons:**  
- Training time: 1–5M steps (~30–120 min) before competent behaviour — opaque to player during training  
- Reward shaping is critical and often requires multiple failed experiments to tune  
- Harder to evolve *morphology* (each morphology = full retraining)  
- ONNX model is a black box; player can't easily understand "why" it does what it does  

---

### 5.1 Recommended Phased Approach

```
Phase 1 (MVP — ~1 week): Scripted IK + Morphology Viewer
├── Implement FABRIK solver in C#
├── Expose arm genome (link lengths, gripper type) via Unity Editor
├── Player can build/adjust arm morphology and watch it attempt pick-and-place
└── Score displayed; no evolution yet — just interactive design

Phase 2 (Evolution Loop — ~2 weeks): Parameter-Evolution with CMA-ES
├── Define motion-parameter genome (~20–40 floats)
├── Implement CMA-ES in C# (or call Python via subprocess for first pass)
├── Run a generation of N=20 candidates each episode, score them
├── Player sees the current generation visualised; can manually promote/demote individuals
├── Carry best 20% to next generation; rest are re-evolved
└── Add simple morphology mutation: ±10% link lengths, swap gripper type

Phase 3 (Morphology Evolution — ~3 weeks): Evolving the Arm Design
├── Expand genome to include structural parameters (link count, joint types, gripper shape)
├── Use tournament selection with crossover
├── Add "player pick" interaction: player can select any arm in the current generation
│   to guarantee its survival (interactive evolutionary computation / IEC)
├── Visualise ancestry tree ("this arm descended from YOUR arm from generation 7")
└── Score and rank arms by pick-and-place performance across varied object positions

Phase 4 (RL Integration — optional enrichment): ML-Agents PPO on Best Morphologies
├── Take top-N morphologies from Phase 3
├── Train a PPO policy for each via mlagents-learn (headless, background)
├── Allow player to "unlock" RL training on a favourite design
└── Compare evolved-controller arm vs RL-controller arm on same morphology
    — dramatic demo of two different learning paradigms on the same body

Phase 5 (Sim-to-Real — long term): Bridge to Physical robot_hand
├── The physical arm (FEETECH STS3215 servos) is already in the stack
├── Export best-evolved morphology as printable dimensions
├── Use FABRIK output as setpoints for servo position control
└── Demonstrate: virtually-evolved arm → 3D printed → running on hardware
```

### 5.2 Complexity vs. Payoff Matrix

| Phase | Dev Time | Player Engagement | Technical Complexity | "Wow Factor" |
|---|---|---|---|---|
| 1: Scripted IK | 1 week | Medium | Low | Medium |
| 2: Parameter-ES | 2 weeks | High | Low-Medium | High |
| 3: Morphology evolution | 3 weeks | Very high | Medium | Very high |
| 4: RL integration | 2+ weeks | Medium-High | High | High |
| 5: Sim-to-real | Ongoing | Very high | Very high | Extremely high |

### 5.3 Core Design Principle: Make Evolution Legible

The hardest problem in this game isn't the algorithm — it's making sure the player *understands* and *cares about* what's happening. Specific recommendations:

1. **Visualise the genome directly** — show a sidebar with the actual arm dimensions and their fitness scores, not just a number
2. **Slow-motion replay of the best arm** — after each generation, play the top performer in slow motion with annotations
3. **Show the family tree** — render a lineage diagram showing which arm came from which
4. **Make player selection meaningful** — player picks must provably influence future generations (IEC)
5. **Fitness breakdown** — show per-component rewards (reach: 0.3, grasp: 1.0, transport: 0.8...) so player understands failure modes
6. **Failure modes are interesting** — show a "hall of shame" for arms that found ridiculous solutions (e.g. knocked objects off the table by accident and scored partial reward)

---

## 6. Quick Reference Summary

- **IK:** Use **FABRIK** (`com.unity.animation.rigging` or custom C#) as the default solver. It handles variable DOF gracefully as morphologies evolve, and converges in 3–5 iterations. Add CCD fallback for simple single-chain arms during early dev.

- **Neuroevolution:** **NEAT** (SharpNEAT in C#) is the best evolutionary neural controller, especially when arm DOF varies across the population. HyperNEAT is worth it if the arm has ≥8 joints with geometric symmetry.

- **Evolution Strategies:** **CMA-ES** is the right tool for tuning motion primitive parameters (Phase 2 above). **OpenAI-ES** scales better for larger networks. Both are complementary to NEAT — use CMA-ES first, graduate to NEAT when you need topology evolution.

- **Morphology evolution:** Reference **Karl Sims 1994** for the graph-grammar body representation. For a game, a simplified flat genome (link lengths + joint limits + gripper type) is sufficient and more interpretable. Player selection (IEC) is a key differentiator over pure automated evolution.

- **Reward design:** Composite shaping reward (reach + grasp + transport + place) with small energy and time penalties. One-time bonuses for grasp (+1) and place (+10) provide the dominant learning signal. Watch for reward hacking.

- **Recommended path:** Phase 1 (FABRIK + manual design) → Phase 2 (CMA-ES on motion params) → Phase 3 (morphology GA + player selection) → Phase 4 (optional ML-Agents PPO on top morphologies). Ship Phase 2 as your first playable loop; it has the best effort-to-engagement ratio.

---

## 7. References

| Source | Relevance |
|---|---|
| Stanley & Miikkulainen, "Evolving Neural Networks through Augmenting Topologies", 2002 | NEAT algorithm |
| Sims, K., "Evolving Virtual Creatures", SIGGRAPH 1994 | Morphology co-evolution; the canonical reference |
| Aristidou & Lasenby, "FABRIK: A fast, iterative solver for the Inverse Kinematics problem", 2011 | FABRIK algorithm |
| Hansen, N., "The CMA Evolution Strategy", 2016 (tutorial) | CMA-ES |
| Salimans et al., "Evolution Strategies as a Scalable Alternative to RL", OpenAI 2017 (arXiv:1703.03864) | OpenAI-ES |
| Schulman et al., "Proximal Policy Optimization Algorithms", OpenAI 2017 | PPO |
| Unity Technologies, ML-Agents Toolkit (`com.unity.ml-agents`), GitHub | Unity RL integration |
| Wampler, C.W., "Manipulator Inverse Kinematic Solutions...", 1986 | DLS / Damped Least Squares |
| neat-python docs (readthedocs.io) | NEAT implementation reference |
| karlsims.com/evolved-virtual-creatures.html | Sims video + papers |

---

*Report generated: 2026-05-30*  
*Stack context: fivelidz / superlocal — robot_arms Unity project, part of the MediaPipe/robot_hand hardware stack*
