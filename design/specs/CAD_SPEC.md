# Spec Guide — In-Game Parametric CAD & Part Creation

Implements intentions I9 (evolve & create AutoCAD-style designs) + the CAD pillar (F). Designed to plug
into the existing extensible systems (ArmConfig, PlacementVerifier, StlExporter, SaveSystem).

## Goal
Let players (and the in-game AI / evolution) CREATE and EVOLVE 3D-printable parametric parts for the arm
— links, brackets, gripper fingers, sensor mounts — and export them as STL. "AutoCAD-style" = real,
editable geometry driven by parameters, not just sliders on premade meshes.

## Architecture (extensibility-first)
```
CadPart (data)  ──►  CadEvaluator  ──►  Unity Mesh  ──►  StlExporter (already exists)
   ▲                     │                  │
parameters            geometry            placed on arm ──► PlacementVerifier (already exists)
(evolvable genome)    primitives + CSG    via a Mount
```
- **`ICadPrimitive`** — the extension point. Box, Cylinder, Sphere, ServoBracket, FingerProfile, ...
  Each yields a mesh from its parameters. New primitives plug in without touching the evaluator.
- **`CadPart`** — a tree of primitives combined with CSG ops (union / subtract / intersect) + a
  parameter table (named floats). Serializable (JSON) so it saves + is an evolvable genome.
- **`CadEvaluator`** — walks the tree, evaluates primitives, applies CSG, returns a Unity Mesh + a
  collider proxy. (CSG via a lightweight mesh-boolean; for v1, "subtract" can be approximated by
  primitive holes to avoid a full boolean lib.)
- **Mount** — where the part attaches on the arm (a parented transform with pose) — reuses the module-
  mounting system (see MODULE_MOUNT_SPEC). PlacementVerifier checks it's valid.

## Two creation paths
1. **Parametric authoring (player):** pick a template part, drag parameter handles (length, radius,
   wall thickness, hole positions). Live mesh updates. Validate with PlacementVerifier. Export STL.
2. **Generative (AI / evolution):** the CadPart parameter vector is a genome. Evolve it for a fitness
   (fits the servo, light, strong, prints without supports). The in-game AI can also emit a CadPart
   from a text prompt ("a bracket that holds an STS3215 at 30°") — same data the player edits.

## Real-world CAD backends (from research/cad_3dprint)
- **v1 (in-Unity):** procedural primitives + simple CSG (holes via boolean-lite). Fast, no deps,
  exports STL directly with the existing StlExporter. Good enough for brackets/links/fingers.
- **v2 (high-fidelity sidecar):** OpenSCAD-WASM or build123d/CadQuery in a Python sidecar (like the
  realbot sidecar) for true CSG + STEP. The CadPart parameters are emitted as an OpenSCAD/.py script;
  the sidecar renders STL. This is the "CADAM" pattern (text → OpenSCAD → STL).
- Either way the CadPart JSON is the source of truth and is engine-agnostic.

## Evolving the ARM itself (morphology)
- ArmConfig (link lengths, joint limits, gripper) is ALREADY an evolvable genome. CAD extends this to
  the PART geometry: a link isn't just a length, it's a CadPart whose parameters evolve. The evolution
  layer (EvolutionTrainer) selects on task fitness; PlacementVerifier rejects invalid geometry early.

## Verification hooks (why the verifier came first)
Every CAD part / mount is checked by PlacementVerifier rules:
- part connects to its parent (no gap) — LinksConnectedRule generalised to CadPart sockets.
- part doesn't self-intersect the arm — NoSelfPenetrationRule.
- mount on a valid surface, sane orientation — ModuleMountRule.
- (new CAD rules) printable: min wall thickness, no overhang beyond N°, fits the servo bolt pattern.
These are added as new IPlacementRule implementations — no changes to existing code.

## Export
- `CadPart` → Unity Mesh → `StlExporter.ExportHierarchy` (binary STL, already working) + a `cad.json`
  with the parameters (so the design is reproducible + re-editable + printable).
- Future: STEP via the sidecar for CAD interchange.

## Milestones
- C1: CadPart + ICadPrimitive (Box, Cylinder, Hole) + CadEvaluator → Mesh. Export STL.
- C2: Parametric authoring UI (parameter handles, live update) + PlacementVerifier integration.
- C3: CadPart as a link in ArmConfig (geometry-evolvable arm).
- C4: Printability rules (wall thickness, overhang, bolt pattern).
- C5: OpenSCAD/build123d sidecar for high-fidelity CSG + STEP (text → CAD).
- C6: In-game AI emits CadPart from a prompt.
