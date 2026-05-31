# Strategy — Text → Task → Control, and how training/generative learning best solves scenarios

This is the "brains" design for ARMSMITH: how a text instruction becomes a solved task, and how
training + generative learning should be structured so arms reliably solve the scenarios. Implements
intentions I58-I62.

## 1. The layered pipeline (the key idea)
Solving manipulation from raw text in one shot is unreliable. The robust pattern is a LADDER of
abstraction, where each layer is independently testable and the layer below is a fallback:

```
TEXT  ─►  TASK PLAN  ─►  SKILL/PRIMITIVE  ─►  CONTROL (IK + servos)  ─►  PHYSICS
"sort the red cube      [pick red @P, place    pick(obj), place(pos),    IK target + gripper +    ArticulationBody
 into the tray"          in tray @T]            reach(pos), open/close    servo ticks               + friction
```

- **Text → Task plan:** parse/LLM turns the instruction into an ordered list of high-level steps using a
  fixed vocabulary of verbs (pick, place, reach, sort, stack, open, close, move-to). This is the
  AgentCommands grammar — small, unambiguous, executable. (An LLM only ever emits THIS grammar, never
  raw joint angles — that's what makes text-to-task reliable.)
- **Task → Skill/primitive:** each verb maps to a parametric SKILL coroutine: `Pick(objPos)`,
  `Place(targetPos)`, `Reach(pos)`. Skills are the reusable "moves" (hover→descend→grip→lift, etc.).
- **Skill → Control:** skills drive the IK target + gripper; ArmController's DLS-IK turns target poses
  into joint angles; ServoModel turns angles into 4096-tick commands (digital twin).
- **Control → Physics:** ArticulationBody + friction make grasping emergent (no cheats).

**Why this is best:** text only has to be correct at the PLAN level (easy, discrete). Geometry/timing
is handled by tested skills + IK. Each layer is debuggable in isolation. The same plan runs in sim and
on the real arm (export waypoints).

## 2. Where generative learning fits (and why)
Two distinct things benefit from learning — keep them separate:

### A. Behaviour learning (HOW to execute a skill well)
- **Phase 0 — scripted skills (now):** hand-written Pick/Place/Sort solve the task slowly but reliably.
  These are the BASELINE and the source of demonstrations.
- **Phase 1 — imitation seed:** record scripted/hand-driven runs as (obs, action) DEMOS (DemoRecorder).
  Use them to WARM-START the policy population (don't start from random — start near a working solution).
- **Phase 2 — evolutionary refinement (ES/GA over the policy net):** evolve the closed-loop sensor
  policy (PolicyGenome) to make the skill FASTER, smoother, more robust to object position. Fitness =
  task reward − time − energy − collisions. Seeding from demos turns "evolve from scratch" (slow,
  often fails) into "evolve from competent" (fast, reliable). THIS is the recommended core loop.
- **Phase 3 — RL (optional):** Unity ML-Agents PPO on a fixed morphology when you want emergent skills
  beyond the scripted repertoire.

### B. Design/morphology learning (WHAT arm/sensors are best)
- Evolve/compare arm parameters (link lengths, which sensor modules) across tasks → the "module advisor"
  (which sensors help which task) and better arm designs. Player is the selector (interactive evolution).

### Recommended default loop (the "best" answer)
For a given scenario:
1. **Scripted skill** produces a working solution + a demonstration (guarantees the task is solvable).
2. **Warm-start** the policy population from that demonstration.
3. **Evolve** (CMA-ES/GA) the policy against the scenario with RANDOMIZED object positions (so it
   generalises, not memorises) for N generations; fitness curve shown live in the Builder/training panel.
4. **Select** the best; export its trajectory/policy (sim-to-real).
5. (Player can re-seed from a better hand-driven demo any time.)

This makes "text → solved, then learned to do it well & fast" the through-line: text gives the PLAN,
scripts guarantee SOLVABILITY, generative learning gives ROBUSTNESS + SPEED + generalisation.

## 3. Randomised scenarios (generalisation, not memorisation)
- Object spawns sampled from a RANDOM GRID / jittered positions within a region each reset.
- A `difficulty` / `randomness` knob: 0 = fixed positions (easy to learn), 1 = fully random grid.
- Training across randomised resets forces policies that generalise. Report success-rate over K random
  resets (not a single run) as the real metric.

## 4. Text → task: the grammar (extend AgentCommands)
Verbs the planner/LLM may emit (each maps to a tested skill):
```
reach <x y z> | reach <anchor>      moveto <anchor>          open | close | grip <0..1>
pick <obj|color|nearest>            place <anchor|x y z>      sort <color?> into <tray>
stack <objA> on <objB>              home                     wait <s>
scenario <name>                     train <N>                say <text>
```
- `pick`/`place`/`sort` resolve object/anchor names to live world positions, then call the skill.
- The in-game text box (and any LLM) only ever produces these lines → reliable, inspectable, exportable.

## 5. Multi-robot (future)
- N arms, each its own ArmController + sensors + policy. A shared blackboard/world-state lets them
  publish pose+intent and subscribe to others.
- Robot-robot tasks: object HAND-OFF (arm A picks, passes to arm B), collaborative place, do-not-collide.
- Training: multi-agent — cooperative fitness (shared success) or role-specialised policies.
- Catalogue (Pillar J): ORCA Hand + other open arms loadable via BuildFromKinematics, so heterogeneous
  teams (an arm + a dexterous hand) can be assembled.

## 6. Milestones to implement now
- M-T1: live text-command input box (I59).
- M-T2: scripted skills as the planner targets; `pick/place/sort` resolve live positions (extend AgentCommands).
- M-T3: randomised object positions + difficulty knob (I61).
- M-T4: warm-start policy population from a demonstration; train across randomised resets; success-rate metric (I60).
- M-T5: 2nd arm + shared world state stub; hand-off task (I62).
