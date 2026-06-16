# Realistic Parallel-Jaw Grasp Physics — Research Study

**Date:** 2026-06-16
**Purpose:** Inform replacing the current kinematic-attach ("perfect weld") grasp in
`UnityProject/Assets/Scripts/Gripper.cs` with a *realistic* grasp model that predicts
real-world failures (slip, drop on weak grip, drop under acceleration) for the ARMSMITH
Unity PhysX ArticulationBody arm, targeting a real STS3215-driven SO-101-class gripper.
**Scope:** web + reasoning survey, no code changes. This is a design input document.

---

## 0. TL;DR — the recommendation up front

For a Unity PhysX `ArticulationBody` parallel-jaw gripper picking ~50 g cubes, the best
practical model is a **hybrid force-limited attachment with an analytic friction-cone gate**
(option B below), *not* a pure real-friction-contact solve and *not* the current pure
kinematic weld.

Concretely:
1. **Gate the grasp on real physics-derived contact**: only form a hold when *both* jaws
   report contact with the object AND the analytic friction criterion (Section 3) says the
   commanded grip force can hold the object's weight. This makes "weak grip → no/late grab"
   and "too-light squeeze → drop" emergent.
2. **Hold with a force-limited / break-force attachment** rather than an infinitely rigid
   weld. Each physics step, compute the wrench needed to keep the object following the EE
   (gravity + inertial `m·a`). If that exceeds the friction-cone capacity
   `F_hold = 2·μ·F_grip`, the object **slips/drops** (release the attachment, restore
   dynamics, let gravity take it). This reproduces "drop under acceleration."
3. **Keep numerical stability** by *not* relying on PhysX to converge a 2-jaw squeeze on a
   tiny light cube every frame (the classic instability). The attachment carries the object;
   physics only *decides* when the attachment is valid.

This is the same architecture every production manipulation simulator converges on (Isaac
Lab's *Surface Gripper* with `shear_force_limit`/`coaxial_force_limit`, PyBullet's
`createConstraint` grasp with `maxForce`, MuJoCo's `adhesion` actuator + friction cone). It
is cheap, stable, and — crucially — *can fail*, which is the whole point.

---

## 1. How leading simulators handle grasping

There is a strong industry consensus, and it is **not** "simulate true friction contact and
hope." Every major stack provides an explicit grasp-assist / attachment mechanism *because
contact-only grasping of small objects is numerically fragile* (Section 2).

### 1.1 NVIDIA Isaac Sim / Isaac Lab (PhysX 5 — same engine family as Unity)

Isaac Lab ships a first-class **Surface Gripper** asset whose API is essentially the
hybrid model we recommend:

- Parameters (from the official tutorial): `max_grip_distance` [m] (max distance at which it
  can grasp), `shear_force_limit` [N] (force limit perpendicular to gripper axis),
  `coaxial_force_limit` [N] (force limit along gripper axis), `retry_interval` [s].
- State machine: command in {Open, Idle, Close}; reported state in {Open(-1), Closing(0),
  Closed(1)}. When closing and an object is within `max_grip_distance`, it forms an
  attachment; the attachment **breaks if the shear or coaxial force exceeds the limits** —
  i.e. it is a *force-limited joint*, which is exactly a tunable break-force grasp.
- Note Isaac's surface gripper is **CPU-only** as of Isaac Sim 5.0 — telling: even NVIDIA
  does not push the contact-rich grasp through their GPU PhysX solve for this; they use a
  constraint with force limits.
- Source: Isaac Lab "Interacting with a surface gripper" tutorial —
  https://isaac-sim.github.io/IsaacLab/main/source/tutorials/01_assets/run_surface_gripper.html
- PhysX 5 docs (the underlying engine): https://nvidia-omniverse.github.io/PhysX/

For *dexterous / contact-rich* tasks (e.g. NVIDIA's factory/insertion and GR00T work) Isaac
*does* use true friction contacts, but those run with SDF colliders, high solver iteration
counts, small substeps, and careful mass/inertia tuning — far more expensive than a hobby
pick-and-place needs.

### 1.2 MuJoCo (the gold standard for contact realism)

MuJoCo can do *both* and documents the tradeoffs precisely:

- **True friction contact** with a rich, *soft* contact model (convex, complementarity-free).
  Key knobs (all citable from the MuJoCo Computation chapter):
  - `condim` — contact dimensionality: 1 (frictionless), 3 (tangential friction), **4
    (adds torsional friction)**, 6 (adds rolling friction). The docs explicitly say:
    *"condim = 4 … is useful for modeling soft fingers, and can substantially improve the
    stability of simulated grasping."* This is the single most important grasp-stability
    lever and it is **absent from PhysX** (PhysX has no torsional/spin friction at a point).
  - Friction cones: **elliptic** (more principled) vs **pyramidal** (faster). MuJoCo notes
    that with *soft* contacts the pyramidal approximation made *"fine grasping behaviors"*
    hard, motivating the elliptic cone.
  - `solref` (timeconst, dampratio) and `solimp` (impedance) — per-contact stiffness/damping.
  - `margin`/`gap` — collision inflation; and an **`adhesion` actuator** (body transmission)
    explicitly *"to model vacuum grippers and biomechanical adhesive appendages"* — i.e.
    MuJoCo's own "sticky gripper."
  - `friction loss` as a constraint, `noslip_iterations` post-pass to kill residual slip.
  - Source: https://mujoco.readthedocs.io/en/stable/computation/index.html (Contact, condim,
    Friction cones, Solver, Actuation model→body/adhesion).
- **Takeaway for us:** MuJoCo achieves stable *real* friction grasps largely because of
  torsional friction (condim=4) and a soft, well-parameterized contact model. PhysX gives us
  neither for free, which is exactly why the Unity project hit the "perfect weld" wall.

### 1.3 PyBullet

- The community-standard grasp recipe is **not** contact-only: you detect contact, then call
  `p.createConstraint(..., JOINT_FIXED, ...)` between the gripper link and the object, and
  set `changeConstraint(maxForce=...)`. The `maxForce` makes it a **break-force grasp** that
  releases under sufficient load — again the hybrid model.
- PyBullet's contact solve (sequential-impulse, ERP/CFM, `numSolverIterations`,
  `contactStiffness`/`contactDamping`, `lateralFriction`, `spinningFriction`,
  `rollingFriction`) *can* do friction grasps but is widely reported as twitchy for small
  objects without the fixed-constraint assist.
- Source: PyBullet Quickstart Guide (`createConstraint`, `changeConstraint`,
  `changeDynamics`): https://pybullet.org/wordpress/index.php/forum-2/ and the canonical
  quickstart doc linked from https://pybullet.org.

### 1.4 Manipulation benchmarks

- **robosuite** (MuJoCo-backed): grippers are real 1-DoF MuJoCo models squeezing objects via
  **true friction contact** — robosuite leans on MuJoCo's good contact model rather than a
  weld. For sim2real it provides a **`DynamicsModder`** to randomize `friction` (sliding,
  torsional, rolling), `mass`, `solref`, `solimp`, etc. Sources:
  https://robosuite.ai/docs/modules/robots.html and
  https://robosuite.ai/docs/algorithms/sim2real.html
- **ManiSkill** (SAPIEN/PhysX): uses true contact for grasping but is well known for needing
  careful contact tuning; many tasks use *suction*/magnetic grasps to sidestep instability.
- **Meta-World** (MuJoCo): tasks use real friction contacts on simple primitives; objects are
  deliberately easy-to-grasp shapes — i.e. the *task design* avoids the hard contact regime.

### 1.5 Mimic joints

A "mimic joint" couples the two jaws so one command drives both symmetrically (common in
URDF/Isaac for Robotiq-style grippers). It is **orthogonal** to the grasp-hold question: it
only keeps the jaws synchronized. ARMSMITH already drives both jaws from one `closeAmount`,
so this is effectively handled.

### 1.6 Summary table

| Stack | Default grasp mechanism | Real friction? | "Sticky" assist available? | Failure (slip/drop) emergent? |
|---|---|---|---|---|
| Isaac Lab | **Surface Gripper** (force-limited attach) | optional, costly | yes (built-in) | **yes** (force limits) |
| MuJoCo | true soft-contact friction (condim 3/4) | yes (good) | yes (`adhesion`) | yes (native) |
| PyBullet | `createConstraint` fixed + `maxForce` | optional | yes (idiom) | **yes** (maxForce) |
| robosuite | true MuJoCo friction | yes | via adhesion | yes |
| ManiSkill | true PhysX friction / suction | yes / no | suction common | yes / no |
| Meta-World | true MuJoCo friction (easy shapes) | yes | no | yes |
| **ARMSMITH now** | **kinematic weld + ignore-collision** | **no** | n/a (always sticky) | **NO — never fails** |

---

## 2. The "perfect grasp" problem — why kinematic/sticky is common

Why is welding/attaching so common? Because **stable two-jaw friction grasping of small light
objects is one of the hardest regimes for any rigid-body solver**, and PhysX in particular.

Root causes:
1. **Antagonistic contacts on a thin object.** Two jaws push inward from opposite sides. The
   solver must hold a tiny residual penetration on *both* faces simultaneously. Sequential /
   iterative solvers (PhysX, Bullet) resolve contacts one at a time per iteration; with too
   few iterations the jaws "fight," producing jitter, squeeze-out (object squirts free), or
   buzzing. The ARMSMITH code comment already documents this: *holding while lifting jammed
   the arm (0.3 cm empty vs 54 cm jam)* — the held cube's contacts fed back into the
   ArticulationBody Featherstone solver.
2. **Mass-ratio ill-conditioning.** A heavy/strong arm link squeezing a 50 g cube is a large
   inertia ratio. Solvers converge slowly when adjacent bodies differ greatly in effective
   mass; the light object gets large corrective velocities and pops out.
3. **No torsional friction at a point in PhysX.** A real fingertip resists the object
   *spinning* about the grip axis via a finite contact patch. PhysX point contacts only give
   normal + tangential (Coulomb) friction — no `condim=4` spin term. So a pinched object
   tends to pivot/roll out of a PhysX pinch even when a real gripper would hold it. MuJoCo's
   docs call torsional friction out explicitly as the thing that *"substantially improves the
   stability of simulated grasping."*
4. **Collision margins & discrete contacts.** Small objects relative to collision margin /
   contact-offset produce flicker between "touching" and "separated," so grip force pulses.
5. **Stiff drive vs. soft contact mismatch.** A high-stiffness prismatic jaw drive commands a
   target *inside* the object; the contact must generate a large opposing force in one step,
   overshooting and ringing.

This is why the project's current code makes the object kinematic and teleports it: it
sidesteps every one of the above. The cost is that it **can never fail** — no slip, no drop —
which defeats the sim2real goal.

### What makes REAL friction-contact grasping stable (if you must do it)

If true contact grasping is attempted in PhysX/Unity, the levers are:
- **Solver iterations:** raise `ArticulationBody.solverIterations` (positional) and
  `solverVelocityIterations` on the jaws and the object — grasp needs far more than default.
  (Unity API: `ArticulationBody.solverIterations`, `.solverVelocityIterations`.)
- **Substepping / smaller fixed timestep:** drop `Time.fixedDeltaTime` (e.g. 0.005 s or less)
  so contacts are resolved more often per simulated second.
- **Drive tuning:** moderate jaw drive `stiffness`, add `damping`, and cap
  `SetDriveForceLimit(...)` so the jaw cannot command infinite squeeze force (this *is* your
  grip-force knob). Unity API: `SetDriveStiffness`, `SetDriveDamping`, `SetDriveForceLimit`.
- **Friction:** high `PhysicMaterial.dynamicFriction`/`staticFriction` on jaw pads and object
  (≥1.0), `Combine = Maximum`.
- **Mass ratios:** make the cube not absurdly light relative to the jaw links; give the cube a
  sane inertia tensor (avoid auto-tensor degeneracies on thin boxes).
- **Collision:** reduce contact offset / use convex hull pads; ensure the jaw pad geometry is
  flat (box) not a sphere/edge so the contact is a face, not a point.
- **Compliance:** a small foam-like compliance (lower jaw drive stiffness) mimics a real
  rubber pad and is far more stable than a rigid pinch — analogous to MuJoCo's soft contacts.

Even with all this, PhysX still lacks torsional friction, so an object can pivot out. This is
the fundamental reason we recommend the *hybrid* model rather than betting on contact-only.

---

## 3. Friction-based grasp criteria — the physics of hold vs. slip

This is the analytic core that makes the hybrid model "real." A parallel-jaw grip holds an
object by **friction**, driven by the **normal (grip) force** the jaws apply.

### 3.1 Two-finger Coulomb model

Each jaw presses on the object with normal force `F_grip`. By Coulomb friction, each contact
can resist tangential load up to `μ·F_grip`. With **two** opposing jaws sharing the load, the
maximum friction force the grasp can resist along the slip (vertical, for a lift) direction:

```
F_hold = 2 · μ · F_grip
```

where `μ` is the friction coefficient between the jaw pad and the object surface.

### 3.2 Required grip force to hold a mass under acceleration

To hold an object of mass `m` against gravity *and* an additional acceleration `a` of the
end-effector (the object must accelerate with the gripper), the load to resist is
`m·(g + a)` (worst case, acceleration aligned with gravity / the slip axis). Setting
`F_hold ≥ load`:

```
2 · μ · F_grip ≥ m · (g + a)
⇒  F_grip ≥  m · (g + a) / (2 · μ)        ← minimum grip force to hold
```

Equivalently, **the grasp holds iff**:

```
2 · μ · F_grip  ≥  m · (g + a)
```

- Static lift (a = 0):  `F_grip ≥ m·g / (2μ)`.
- A safety factor `k` (typ. 1.5–2.0) is normally applied:
  `F_grip = k · m·(g+a) / (2μ)` to account for μ uncertainty, partial contact, dynamic jolts.

### 3.3 The friction cone (geometric form)

At each contact, the total contact force must lie inside the **friction cone**: the set of
forces whose tangential component does not exceed `μ` × normal component. Half-angle
`θ = atan(μ)`. If the *required* contact force to keep the object moving with the gripper
falls **outside** the cone (tangential demand > `μ·F_normal`), the contact **slips**. For a
two-jaw pinch the combined admissible set is the intersection of both cones; the simplest
usable scalar test is the `F_hold = 2μF_grip` inequality above. (MuJoCo formalizes the
elliptic/pyramidal cone; PyBullet/PhysX use a Coulomb pyramid per contact.)

### 3.4 Torque / pivot consideration (why grip *position* matters)

If the object's center of mass is offset from the line between the two contact points, the
weight creates a **moment** that must be resisted by *torsional* friction (finite contact
patch) or by the second jaw's normal force differential. With point contacts (PhysX) there is
no torsional friction, so an off-center grasp tends to pivot. Practical analytic gate: also
require the grasp point to be within some tolerance of the object's CoM projection, or apply a
torsional-capacity term `τ_max ≈ μ · F_grip · r_patch` (MuJoCo's interpretation: torsional
coefficient has *units of length* = contact-patch radius). For light cubes grasped centrally
this is usually negligible.

### 3.5 Mapping STS3215 servo command → grip force

The real gripper is driven by a **Feetech STS3215** serial bus servo. Its commanded "torque"
/ load maps to a jaw closing force through the gripper linkage:

```
F_grip ≈  (τ_servo · η) / r_eff
```

where `τ_servo` is servo output torque (≈ up to ~kg·cm stall at the rated voltage; STS3215 is
~30 kg·cm class), `η` is linkage/transmission efficiency, and `r_eff` is the effective moment
arm from the servo axis to the jaw contact (depends on the parallel-jaw mechanism). In ARMSMITH
the abstract control is `closeAmount ∈ [0,1]` / `gripper_deg ∈ [0,90]`. The simplest faithful
mapping: treat the servo's torque limit as the cap, so **commanded `closeAmount` (beyond the
point of contact) maps monotonically to `F_grip` up to `F_grip_max`** set by the servo's
torque-limit register. Calibrate `F_grip_max` once on the real arm (hang known masses, find
the lightest grip that slips) and match it in sim. This single number is the bridge between
sim and real for grasp capacity.

---

## 4. Hybrid approaches — force-limited / break-force attachment

This is the recommended family and is well-precedented.

### 4.1 The pattern

Replace "infinitely rigid weld" with an attachment that has a **finite holding wrench**:
- **Break-force fixed joint:** a joint that connects object→gripper but auto-releases when the
  constraint force exceeds `breakForce`/`breakTorque`. (Unity's classic `Joint.breakForce`.)
- **Force-limited drive / spring attachment:** a stiff spring-damper pulling the object to the
  grasp pose, with a maximum force; once the demanded force exceeds the max, tracking error
  grows and the object lags/slips out.
- **Isaac Surface Gripper** = this exact idea with `shear_force_limit` + `coaxial_force_limit`.
- **PyBullet** = `createConstraint(JOINT_FIXED)` + `changeConstraint(maxForce=...)`.

### 4.2 Tuning break-force to match a real friction grasp

Set the attachment's holding capacity equal to the **analytic friction capacity** from
Section 3, so the *break* event coincides with a *real slip* event:

```
breakForce  =  F_hold  =  2 · μ · F_grip
breakTorque =  τ_max   ≈  μ · F_grip · r_patch     (small; often left generous)
```

with `F_grip` derived from the current commanded `closeAmount` (Section 3.5) and `μ` from the
jaw-pad/object material pair (measure on the real arm: tilt-test or pull-test). Then:
- A *weak* grip (low `closeAmount` → low `F_grip`) gives a low `breakForce` → the object drops
  under its own weight or a small jolt. **Weak-grip drop becomes emergent.**
- A *fast* lift/swing raises the demanded force to `m·(g+a)`; when `a` is large the demand
  exceeds `breakForce` → **drop-under-acceleration becomes emergent.**
- Domain-randomize `μ` and `F_grip_max` (Section 5) so a policy can't exploit a knife-edge.

### 4.3 CRITICAL Unity-specific caveat (must read before implementing)

Unity's `FixedJoint`/`ConfigurableJoint` connect a **Rigidbody to a Rigidbody**. They do
**not** connect a `Rigidbody` to an `ArticulationBody`, and there is **no
ArticulationBody↔Rigidbody joint** in the API. (Confirmed against the ArticulationBody
scripting reference: its joints are only parent→child within the articulation tree; there is
no `connectedBody` to an external Rigidbody.) Consequences:
- You **cannot** simply add a break-force `FixedJoint` between the gripper link
  (ArticulationBody) and the cube (Rigidbody). This is why the project went kinematic-attach.
- **Workable implementations of the hybrid model in Unity:**
  1. **Manual force-limited follower (recommended).** Keep the object a *dynamic* Rigidbody
     (NOT kinematic). Each `FixedUpdate`, compute the wrench needed to drive it to the grasp
     pose (a PD spring toward `targetPos/targetRot`). **Clamp** that wrench to `F_hold`/`τ_max`
     (Section 4.2). Apply it with `Rigidbody.AddForce`/`AddTorque`. Because the force is
     *capped*, gravity + inertia naturally win when the grip is too weak or `a` too high →
     the object lags, slides between the jaws, and falls. Slip/drop is now *physically
     produced by the force balance*, not scripted. This is the cleanest match to reality and
     stays stable (the object never fights a rigid weld).
  2. **Kinematic hold + analytic slip test (cheapest, "gravity-aware slip model").** Keep the
     current kinematic-follow, but every step evaluate `2μF_grip ≥ m(g+a)`. If it fails, flip
     to dynamic and release (drop). Add a slip *creep* term when the demand is near capacity
     (let the object slide a few mm/s down the jaw) for graceful failure. Less physically
     rich than (1) but trivial to bolt onto existing code and *does* fail correctly.
  3. **Two real Rigidbody jaw-pad children with a real FixedJoint to the object.** Not
     recommended: reintroduces the contact instability and articulation feedback the project
     already fought.

Recommendation: implement **(1) force-limited follower**; fall back to **(2)** if (1) needs
more tuning time than available. Both make failure emergent; (1) is more faithful.

---

## 5. Sim-to-real for grasping

### 5.1 Why grasp sim2real is hard

- **Friction is the least observable, most variable parameter.** Real μ varies with surface
  finish, dust, humidity, pad wear; sim μ is a guess. Grasp success is *directly* gated by μ,
  so error here maps straight to dropped objects.
- **Contact geometry & compliance.** Real rubber pads deform and conform (increasing the
  effective contact patch and torsional resistance); rigid sim pads do not. This is the single
  biggest qualitative gap (and why MuJoCo's `condim=4`/soft contacts help).
- **Actuation gap.** STS3215 has backlash, finite torque, position-control (not direct force),
  PWM/voltage-dependent torque, and latency. Sim drives are near-ideal.
- **Mass/inertia/CoM error** of the manipulated object.
- **Timing/control-rate mismatch** between sim (e.g. 50–200 Hz) and the real servo bus.

### 5.2 Domain randomization for grasping (what to randomize)

From robosuite's `DynamicsModder` and the broad DR literature, randomize per-episode:
- **Friction** μ (sliding; + torsional/rolling where supported) — *the* key one. e.g.
  μ ∼ U(0.4, 1.1) for plastic/rubber on plastic.
- **Object mass** (± 30–50%), **CoM offset**, **inertia**.
- **Object initial pose** (position + yaw) within the jaw reach.
- **Grip-force cap** `F_grip_max` (servo torque-limit uncertainty) ± 20–30%.
- **Contact solver params** (`solref`/`solimp` analog; in Unity: jaw drive stiffness/damping,
  contact offset) to avoid over-fitting to one stiffness.
- **Latency / control-rate jitter**, sensor noise on object pose.
- Sources: robosuite sim2real (DynamicsModder: friction, mass, solref, solimp, damping,
  armature) https://robosuite.ai/docs/algorithms/sim2real.html ; Isaac Lab DR &
  sim-to-real deployment docs https://isaac-sim.github.io/IsaacLab/ .

### 5.3 What fidelity is actually needed for a small SO-101-class arm + light cubes

**Low-to-moderate.** For pick-and-place of light (~50 g) rigid cubes with a 1-DoF parallel
gripper, you do **not** need a full soft-contact / FEM / tactile sim. You need:
1. A grasp that **sometimes fails** in the *same regimes* the real arm fails: too-weak grip,
   off-center grasp, fast/jerky lift, low-friction object. The analytic friction gate +
   force-limited hold (Sections 3–4) captures all four with one inequality.
2. Roughly calibrated `μ` and `F_grip_max` (two numbers, measured once on the real arm).
3. Domain randomization around those two numbers + object mass/pose so behavior transfers
   without per-object tuning.
This is the classic "**good-enough physics + DR**" recipe; chasing contact-solver realism in
PhysX would cost far more engineering for negligible benefit at this object class.

---

## 6. Concrete recommendation for ARMSMITH

### 6.1 Decision matrix (the three candidates from the brief)

| Criterion | A. Real-friction-contact (PhysX 2-jaw squeeze) | B. Force-limited / break-force attachment | C. Gravity-aware slip on kinematic hold |
|---|---|---|---|
| (a) Realistic slip/drop | Yes, but pivot-out artifacts (no torsional friction) | **Yes — drop on weak grip & under accel, by force balance** | Yes for weak-grip & accel via analytic test; less organic |
| (b) Numerical stability in PhysX | **Poor** for 50 g cube (jitter, jam, squeeze-out; project already hit this) | **Good** — object never fights a rigid weld; force is capped | **Excellent** — object kinematic, no contact solve |
| (c) Transfers to STS3215 gripper | In principle, but μ/torsional gap hurts | **Good** — `breakForce = 2μF_grip`, `F_grip` from servo torque limit | Good — same analytic criterion, simpler hold |
| Implementation cost in Unity | High (solver iters, substeps, materials, still no torsional friction) | **Medium** (manual capped PD follower; no AB↔RB joint needed) | Low (extend current kinematic code with a slip test) |
| Faithfulness of failure | Highest *if* it converged | **High and stable** | Medium (scripted gate, optional creep) |

### 6.2 Recommendation

**Adopt B (force-limited follower) with the analytic friction gate from A's physics, and use
C as the fallback / fast path.** Rationale: B keeps PhysX numerically calm (the object is a
normal dynamic Rigidbody pulled by a *capped* force, never a rigid weld that fights the
articulation), while making slip and drop *emerge from the force balance* exactly as on the
real arm. It maps cleanly to the STS3215 (grip-force cap = servo torque limit; `μ` measured
once), and supports domain randomization on the two physical unknowns.

### 6.3 Implementation sketch for `Gripper.cs` (design, not final code)

State to add:
- `float frictionMu = 0.7f;`            // jaw-pad↔object; domain-randomize
- `float gripForceMax = ...;`           // N at closeAmount=1; from STS3215 torque limit
- `float safetyFactor = 1.0f;`          // 1.0 = exact physics; >1 = more forgiving
- replace `held.isKinematic = true` with **dynamic** hold + capped PD follower.

Per-FixedUpdate while holding:
1. `F_grip = gripForceMax * f(closeAmount_above_contact)`  // 0 below contact threshold
2. `F_hold = 2 * frictionMu * F_grip`                       // friction-cone capacity
3. Estimate object load: `load = mass * (g + a_ee)` where `a_ee` = magnitude of the EE
   acceleration along the slip axis (finite-difference the EE velocity).
4. Compute desired wrench `W_des` from a PD spring toward the grasp pose
   (target = EE-relative `heldLocalPos/heldLocalRot`).
5. **Clamp** `|W_des|` to `F_hold` (and torque to `τ_max ≈ μ·F_grip·r_patch`).
6. `held.AddForce(clamped); held.AddTorque(clampedTorque);`  // object stays dynamic
7. If `load > F_hold` persistently (or the tracking error exceeds a slip threshold), the
   object slides out naturally; once it leaves the jaw region, treat as released/dropped.

Grasp *formation* gate (replaces the radius-only `TryGrab`):
- Require contact on **both** jaws (overlap or `OnCollisionStay`/contact query against the
  cube), AND `2μF_grip ≥ mass·g` at the moment of closing, before forming the hold. A weak
  squeeze then simply fails to pick up — emergent.

Keep:
- The NaN/▮distance safety guard and floor guard already in `HeldFollow`.
- Hysteresis on grab/release (`>0.55` grab, `<0.15` release) to avoid oscillation.

Drop the `SetHeldCollisionIgnored(true)` weld hack: with a *dynamic capped* follower the cube
no longer "jams" the articulation the way the rigid kinematic weld did, *provided* the follower
force is force-limited and you keep ignoring jaw↔cube self-collision only enough to avoid the
pads re-ejecting it (tune; you may be able to leave pad contact ON for extra realism since the
hold force is now bounded).

### 6.4 What to measure on the real arm to calibrate (one-time)

1. **`F_grip_max`**: heaviest mass the closed gripper holds at full commanded torque; or
   read servo torque-limit register and convert via linkage `r_eff`.
2. **`μ`**: pull-test (force gauge) or tilt-test (angle at which a held object slips →
   μ ≈ tan(θ_slip)) for each object material.
3. **Slip-onset acceleration**: jerk a held object and find the `a` at which it drops; verify
   sim drops at the same `a` (validates `2μF_grip ≥ m(g+a)`).

---

## 7. Cited sources

- **Isaac Lab — Surface Gripper tutorial** (force-limited attach: `max_grip_distance`,
  `shear_force_limit`, `coaxial_force_limit`, `retry_interval`; CPU-only):
  https://isaac-sim.github.io/IsaacLab/main/source/tutorials/01_assets/run_surface_gripper.html
- **Isaac Lab docs root** (DR, actuators, contact sensors, sim-to-real deployment):
  https://isaac-sim.github.io/IsaacLab/
- **NVIDIA PhysX 5 docs** (engine shared with Unity ArticulationBody):
  https://nvidia-omniverse.github.io/PhysX/
- **MuJoCo Computation chapter** (soft contact model, `condim` incl. torsional friction for
  grasp stability, elliptic vs pyramidal friction cones, `solref`/`solimp`, `margin`/`gap`,
  `adhesion` actuator, NoSlip, solver iterations):
  https://mujoco.readthedocs.io/en/stable/computation/index.html
- **MuJoCo XML reference** (geom friction, contact pair, adhesion actuator):
  https://mujoco.readthedocs.io/en/stable/XMLreference.html
- **robosuite — Robots** (1-DoF parallel grippers, true MuJoCo friction grasping):
  https://robosuite.ai/docs/modules/robots.html
- **robosuite — Sim-to-Real Transfer** (`DynamicsModder`: friction sliding/torsional/rolling,
  mass, solref, solimp, damping, armature; visual + sensor DR):
  https://robosuite.ai/docs/algorithms/sim2real.html
- **PyBullet Quickstart Guide** (grasp via `createConstraint(JOINT_FIXED)` +
  `changeConstraint(maxForce=...)`; `changeDynamics` lateral/spinning/rolling friction,
  contactStiffness/Damping, numSolverIterations): https://pybullet.org (Quickstart Guide PDF/Doc)
- **Unity — ArticulationBody scripting reference** (Featherstone reduced-coordinate solver;
  `solverIterations`/`solverVelocityIterations`, `SetDriveForceLimit`, `SetDriveStiffness`,
  `SetDriveDamping`, `jointFriction`; NO ArticulationBody↔Rigidbody joint):
  https://docs.unity3d.com/ScriptReference/ArticulationBody.html
- **Feetech STS3215** (serial bus servo torque/position control — basis for `F_grip` cap):
  manufacturer datasheet; see project `research/arm_hardware/REPORT.md` and
  `scripts/realbot/` for the existing STS3215 control code.
- **Friction-cone / grip-force mechanics** (`F_hold = 2μF_grip`, `F_grip ≥ m(g+a)/2μ`):
  standard result in robotic grasping mechanics — see e.g. Murray, Li & Sastry, *A
  Mathematical Introduction to Robotic Manipulation* (friction cones, force closure); and
  Mason, *Mechanics of Robotic Manipulation* (MIT Press).

---

## 8. Cross-references in this repo
- Current implementation to replace: `UnityProject/Assets/Scripts/Gripper.cs`
  (kinematic-attach + `SetHeldCollisionIgnored` weld; documented jam workaround).
- Real-robot bridge / STS3215 control: `design/specs/REAL_ROBOT_PORT_SPEC.md`,
  `scripts/realbot/` (armsmith_player.py, joint_map.json).
- Grasp geometry helper already present: `scripts/vision/grasp_geometry.py`.
- Prior manipulation survey: `research/manipulation_repos/REPORT.md` (MuJoCo/robosuite/Isaac).
