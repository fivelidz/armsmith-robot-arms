# EVAL & LeRobot Spec (EV1–EV4)

> RoboLab-inspired evaluation + serving contract for ARMSMITH. Captures the composable success
> predicates (EV1), the inference client/server contract (EV2), and the LeRobot v3.0 export schema
> authority (EV3). Source study: `research/external/NVLABS_ROBOLAB_GROOT_WBC_STUDY.md`.

## Why
Previously each scenario's success was bespoke inline `switch` logic in `ScenarioManager.ComputeReward`.
That couples three concerns that should be independent:
1. **success detection** (did the task finish?),
2. **reward shaping** (the continuous gradient the GA/policy follows),
3. **curriculum difficulty** (how hard is this variant?).

RoboLab's insight: express success as **composable predicates** over world state. One declarative tree
then feeds success, a shaped `Margin()` signal, and difficulty labels — and reads as plain English for the
UI. This spec records that design and the serving/export contracts that pair with it.

---

## EV1 — Composable success predicates  ✅ IMPLEMENTED

**Code:** `UnityProject/Assets/Scripts/Evaluation/Predicates.cs`,
`UnityProject/Assets/Scripts/Evaluation/TaskEvaluator.cs`.
**Gate:** `ArmSmith.EditorTools.PredicateEvalCheck` (18 assertions, in `run_checks.sh` step 3e).

### Model
- `IPredicate { bool Evaluate(ctx); float Margin(ctx); string Describe(); }`
- `TaskContext` — an immutable snapshot of the scene: end-effector pose, gripper-close scalar, and
  name→position / name→velocity / name→exists resolver delegates. **No Unity types leak into the math**,
  so predicates are unit-testable headless with synthetic dictionaries.
- `Margin(ctx)` is a signed satisfaction distance (≥0 satisfied; magnitude = distance from boundary).
  This is the hook for **shaped reward** and for **curriculum tolerance sweeps**.

### Leaf predicates
| Predicate | Meaning |
|---|---|
| `NearXZ(a,b,tol)` | horizontal (XZ) distance a↔b < tol (in-container / on-pad / in-zone) |
| `Near(a,b,tol)` | full 3D distance a↔b < tol |
| `EeReaches(target,tol)` | gripper tip within tol of target (reach tasks) |
| `BelowHeight(a,maxY)` | object set down / inside a tray (world-Y < maxY) |
| `AboveAligned(a,b,dy,xzTol)` | a stacked on b (≥dy above, <xzTol aligned) |
| `AtRest(a,maxSpeed)` | object linear speed < maxSpeed (settled, not flung) |
| `Grasping(obj,reach,close)` | gripper closed past threshold AND near obj |

### Combinators
`And`, `Or`, `Not`, and `ForAll(members, factory, label)` (quantifier — used by SortIntoTray; exposes
`CountSatisfied` for progress reward). `And.Margin` = min child margin; `Or.Margin` = max child margin.

### Per-scenario trees (`TaskEvaluator.Build`)
- **ReachTouch** = `EeReaches(reachTarget, 4cm)`
- **PickPlace / PushToZone** = `NearXZ(cube,pad,6cm) ∧ AtRest(cube)`
- **TrayToTray** = `NearXZ(cube,trayB,6cm) ∧ BelowHeight(cube,7cm) ∧ AtRest(cube)`
- **DropInBin** = `NearXZ(cube,bin,6cm) ∧ BelowHeight(cube,5cm) ∧ AtRest(cube)`
- **StackTwo** = `AboveAligned(cube,cubeB,4cm,3cm) ∧ AtRest(cube)`
- **SortIntoTray** = `ForAll(sortCubes: NearXZ(_,trayB,7cm) ∧ BelowHeight(_,7cm)) ∧ AtRest(cube)`

Tolerances are copied verbatim from the legacy switch, so success behaviour is preserved exactly.

### Integration
`ScenarioManager.usePredicateSuccess` (default **off** to keep approved reward-shaping untouched) routes
the boolean success gate through `PredicateSuccess()`. `BuildContext()` snapshots the live scene;
`PredicateDescription()` returns the English breakdown for the UI/logs. The reward switch keeps its
hand-tuned shaping terms — only the success *gate* is unified.

### Curriculum hook (future)
A difficulty label = (number of conjuncts) × (inverse of the tightest tolerance). The GA/curriculum can
loosen tolerances early (`tol *= 1+slack`) and tighten over generations by scaling the predicate's `tol`
at build time — a single knob, because all tolerances live in `TaskEvaluator`.

---

## EV2 — InferenceClient serving contract  (DESIGN; partially built)

Mirror RoboLab's `extract_observation → pack_request → query_server → unpack` across our two policy
servers (diffusion + sensor-policy). Already partly realised by `scripts/diffusion/serve_diffusion_policy.py`
(DF4) + `Agent/DiffusionPolicyClient.cs` (receding-horizon TCP client). To formalise:

1. **extract_observation** (Unity): `SensorHub.BuildObservation()` → float[] (joint state + object pose +
   enabled sensor channels). The observation composition is exactly U3 (which channels feed the policy).
2. **pack_request**: `{ "schema":"armsmith.obs.v1", "obs":[...], "task":"TrayToTray", "horizon":N }` (JSON
   over the existing TCP socket).
3. **query_server**: Python policy returns an **action chunk** `{ "actions": [[deg×6, grip], ...] }`.
4. **unpack**: client applies the chunk receding-horizon (re-query every k steps), driving `IKAnglesFor`
   or direct joint targets.

Contract invariants: action = absolute joint degrees + gripper [0..1]; dt = policy fps; server is
stateless per request (history is client-supplied). This matches `train_diffusion_policy.py` I/O so train
and serve agree.

**TODO:** lift the JSON schema into a shared file both `DiffusionPolicyClient.cs` and the Python servers
read; add a sensor-policy server alongside the diffusion one using the same contract.

---

## EV3 — LeRobot v3.0 export schema authority  (DESIGN)

`scripts/realbot/waypoints_to_lerobot.py` already converts `armsmith.waypoints.v1` → a portable dataset
(`manifest.json` + per-episode arrays) and, when LeRobot is installed, a real `LeRobotDataset`. To make it
the single authority:

- **Feature schema** (align with LeRobot v3.0 `convert_to_lerobot.py`):
  - `observation.state` = joint positions (deg, ordered by `joint_map_lerobot.json`) + gripper.
  - `action` = next-step joint targets (deg) + gripper.
  - `observation.images.wrist` / `.env` = optional camera frames (when vision demos are recorded).
  - `timestamp`, `frame_index`, `episode_index`, `task` (the scenario name = a natural LeRobot "task").
- **Stats**: per-feature mean/std/min/max written to the manifest (already computed) — required for
  normalisation at train time; keep the keys identical to LeRobot's `stats.json`.
- **Task string** = the scenario enum name, so a multi-task dataset is just multiple `task` values; the
  EV1 predicate `Describe()` can be stored as the task's natural-language instruction.

**TODO:** add `--lerobot-v3` flag that emits the exact v3 directory layout; round-trip test
(waypoints → dataset → `train_diffusion_policy.py` already gated in `run_checks.sh` step 4).

---

## EV4 — this document  ✅

Living spec. Update when the serving schema or LeRobot layout changes. Cross-refs:
`HANDOVER.md` §8, `ROADMAP.md` (RoboLab-inspired eval + serving), `scripts/diffusion/README.md`.
