# CAD / Text-to-CAD / STL Generation & Export Research
## For: Unity Robot Arms Game — In-Game 3D-Printable Part Design

> **Written:** 2026-05-30  
> **Author:** Research session (Claude Code)  
> **Scope:** Text-to-CAD systems, parametric CAD tools, STL generation pipelines, Unity STL export/import, URDF integration — evaluated for embedding in a Unity game where players design and export 3D-printable robot arm parts.

---

## 5-Bullet Summary of Key Findings

1. **OpenSCAD-WASM is the confirmed best match** for in-browser / in-engine text/code-to-STL. CADAM (locally downloaded at `~/projects/MASTER_PROJECTS/_research_may_downloads/CADAM/`) already proves the pattern: a WebAssembly build of OpenSCAD runs entirely client-side, an LLM generates `.scad` code from natural-language prompts, and binary STL is exported with `--export-format=binstl`. This architecture can be transplanted nearly directly into a Unity WebGL build or a companion web overlay.

2. **Writing binary STL from a Unity `Mesh` requires only ~50 lines of C#** using `System.IO.BinaryWriter`. The format is exact and deterministic: 80-byte header, `uint32` triangle count, then per-triangle `(3×float normal) + (9×float vertices) + uint16 attribute = 50 bytes`. No third-party library is strictly required, though `karl-/pb_Stl` (MIT, UPM-installable) provides a battle-tested baseline with both import and export at runtime.

3. **For server-side / offline parametric generation**, `CadQuery` (5.2k ⭐, Apache 2.0) and `build123d` (2.4k ⭐, Apache 2.0) are the strongest Python options. Both wrap OpenCASCADE (OCCT), export clean STEP and STL, and can be called from a Python microservice that Unity talks to over HTTP. Ideal for generating complex arm-link geometry (hollow tubes, servo pockets, flange patterns) that OpenSCAD's CSG model struggles with.

4. **`hmm` (heightmap→STL, locally at `_research_may_downloads/hmm/`) is directly applicable** for terrain-style or cross-section extrusion surfaces — e.g. generating a custom gripper pad or palm-surface profile from a painted heightmap texture. It outputs binary STL. The algorithm is Garland–Heckbert Delaunay meshing, producing compact meshes ideal for printing.

5. **URDF-Importer** (Unity-Technologies, Apache 2.0) makes it trivial to load a fully-articulated robot arm into Unity for simulation. The recommended path is: player designs individual STL parts in-game → parts are assembled into a URDF description → URDF-Importer loads the live robot for preview/physics test → player exports the STL pack for slicing.

---

## Table of Contents

1. [Context: The Existing Robot Hand Pipeline](#1-context-the-existing-robot-hand-pipeline)
2. [CADAM — Text-to-CAD via OpenSCAD WASM + LLM](#2-cadam--text-to-cad-via-openscad-wasm--llm)
3. [OpenSCAD — Programmatic Parametric CAD](#3-openscad--programmatic-parametric-cad)
4. [CadQuery & build123d — Python Parametric CAD](#4-cadquery--build123d--python-parametric-cad)
5. [hmm — Heightmap PNG to STL](#5-hmm--heightmap-png-to-stl)
6. [Unity STL Export — Binary Format & C# Implementation](#6-unity-stl-export--binary-format--c-implementation)
7. [Unity STL Import — Runtime Loaders](#7-unity-stl-import--runtime-loaders)
8. [URDF in Unity](#8-urdf-in-unity)
9. [Architecture Options for the Game](#9-architecture-options-for-the-game)
10. [Comparison Matrix](#10-comparison-matrix)
11. [References](#11-references)

---

## 1. Context: The Existing Robot Hand Pipeline

Before diving into new tools, it is important to understand what already exists locally.

### 1.1 The `robot_hand` Project

Located at `~/projects/robot_hand/`, this project is a **gesture-controlled, tendon-driven, 3D-printed robotic hand** using FEETECH STS3215 serial bus servos. Relevant facts for the Unity game project:

- **STL files are hand-selected from InMoov i2** (Gaël Langevin, CC-BY-NC 3.0): finger phalanges, palm, wrist gears, servo beds, cable holders — all live under `stl/` and `wrist_forearm/` and `print_transfer/STL_files/`.
- **OpenSCAD is already in use** for custom servo mount adapters (`hardware/openscad/servo_bed_ST3215.scad`). The `.scad` file for the servo bed is richly parametric: `SERVO_L`, `SERVO_H`, `SERVO_W`, M2 hole positions, three layout variants (A/B/C). This is precisely the kind of parametric geometry the Unity game should generate.
- **Python pipeline** (`python/`) controls servos, reads MediaPipe hand landmarks, maps finger angles to servo positions. This pipeline can be wrapped into a FastAPI microservice callable from Unity.
- **No in-engine 3D design exists yet** — the game project is a greenfield opportunity to add an in-game CAD editor that produces printable `.stl` files matching the physical robot's spec.

### 1.2 Key Insight from Prior Research

From `RELATED_RESEARCH.md`:
> *"CADAM (text-to-CAD) is directly applicable for generating hand component STLs"*

CADAM is already downloaded. The question is how to embed or adapt it. See Section 2.

---

## 2. CADAM — Text-to-CAD via OpenSCAD WASM + LLM

**Source repo:** `~/projects/MASTER_PROJECTS/_research_may_downloads/CADAM/`  
**Live demo:** https://adam.new/cadam  
**Stack:** Vite + React + TypeScript frontend, Supabase Edge Functions (Deno), OpenRouter LLM API

### 2.1 How CADAM Works

CADAM is a full-stack text-to-CAD application. The architecture has three layers:

#### Layer 1 — Frontend (Browser)

- A **Web Worker** (`src/worker/worker.ts`) runs OpenSCAD entirely inside the browser via WebAssembly.
- `OpenSCADWrapper` (`src/worker/openSCAD.ts`) wraps the WASM module, exposing `exportFile()` and `preview()` methods.
- For **export** (download): calls `openscad --export-format=binstl --enable=manifold --enable=fast-csg --enable=lazy-union` → produces a binary STL blob.
- For **preview** (live 3D view): calls `openscad --backend=manifold --enable=lazy-union` → produces both an STL and an OFF file. The OFF file carries per-face RGBA colour (from OpenSCAD's `color()` calls), which the client renders in WebGL.
- The vendored WASM build is at `src/vendor/openscad-wasm/openscad.wasm` (~the 2025.03.25 playground build from `openscad/openscad-wasm`).

```
// Confirmed export format flags from openSCAD.ts:
const EXPORT_FORMAT_FLAGS = {
  stl: 'binstl',   // binary STL
  dxf: 'dxf',
};
```

#### Layer 2 — LLM Generation (Supabase Edge Functions / Deno)

- `supabase/functions/parametric-chat/index.ts` implements the **"Adam" AI CAD agent**.
- The agent is prompted as: *"You are Adam, an AI CAD editor that creates and modifies OpenSCAD models."*
- Uses **two tool-call paths** via OpenRouter:
  - `build_parametric_model` — full model generation from user intent. Accepts `text` (user request), `imageIds`, `baseCode`, `error` (for self-repair).
  - `apply_parameter_changes` — lightweight tweaks (e.g. "height to 80") that patch named variables without regenerating.
- The LLM is instructed to:
  - Use `snake_case` descriptive variable names (`wheel_radius`, not `r`)
  - Expose all tunable values as top-level variables
  - Wrap distinct parts in `color()` calls with CSS named colours
  - Expose colours as string parameters (`body_color = "SteelBlue"`)
  - **Never** include markdown fences in the output — raw OpenSCAD only
- For STL import, the system uses `import("filename.stl")` in OpenSCAD and modifies around it.

#### Layer 3 — Parametric Artifact (Shared Type)

From `shared/types.ts`, the core data structure:

```typescript
type ParametricArtifact = {
  title: string;
  version: string;
  code: string;            // raw OpenSCAD source
  parameters: Parameter[]; // extracted typed parameters for the UI panel
  suggestions?: string[];
};

type Parameter = {
  name: string;
  displayName: string;
  value: string | boolean | number | string[] | number[] | boolean[];
  defaultValue: ...;
  type?: ParameterType;  // 'string' | 'number' | 'boolean' | 'string[]' | etc.
  range?: { min?, max?, step? };
  options?: { value, label }[];
};
```

This is the "parametric panel" — users see sliders and dropdowns for each exposed variable. Every change triggers a re-render via the Web Worker without touching the LLM.

### 2.2 Output Formats

| Format | How triggered | Use case |
|--------|--------------|---------|
| Binary STL | `--export-format=binstl` | Download for slicing |
| ASCII STL | Default STL (preview path) | Live WebGL render |
| OFF (with color) | `--backend=manifold` second output | Preview with color |
| DXF | `--export-format=dxf` | 2D laser cutting |
| SVG | `--export-format=svg` (2D fallback) | Flat geometry |

### 2.3 Adapting CADAM for the Unity Game

**Option A — Unity WebGL overlay:** Ship the CADAM frontend as a WebGL overlay panel within Unity. The Web Worker runs in the browser alongside the Unity WebGL build. Message passing between the Unity canvas and the CADAM iframe via `postMessage`. When the player clicks "Export STL", the binary STL blob is offered as a download or sent to a backend.

**Option B — Companion web app:** Run CADAM as a separate web service (self-hosted). Unity opens a WebView (using the `UniWebView` asset or similar) to the CADAM URL. The player designs there; completed STL is saved to a shared folder or API endpoint.

**Option C — Extract the WASM worker:** Copy `src/vendor/openscad-wasm/` and `src/worker/` into a lightweight Node/Deno sidecar that accepts OpenSCAD code via HTTP and returns binary STL bytes. Unity calls this sidecar. This avoids the full React frontend dependency.

**Recommended for this project:** Option C for standalone / desktop builds, Option A for WebGL builds.

---

## 3. OpenSCAD — Programmatic Parametric CAD

**Website:** https://openscad.org  
**WASM repo:** https://github.com/openscad/openscad-wasm (391 ⭐, GPL-2.0)  
**Format:** `.scad` (plain text, Turing-complete CSG scripting language)

### 3.1 Why OpenSCAD is Ideal for Robot Arm Parts

OpenSCAD uses **Constructive Solid Geometry (CSG)**: you describe parts as unions, differences, and intersections of primitives (box, cylinder, sphere, polyhedron). This is perfect for robot arm segments because:

1. **Every dimension is a named variable** — `link_length`, `bore_diameter`, `wall_thickness`. Change a number, rerender, done.
2. **LLM-generation is reliable** — OpenSCAD's grammar is small and unambiguous. GPT-4/Claude can generate valid `.scad` reliably (as CADAM proves).
3. **No GUI required** — the CLI (`openscad -o output.stl input.scad`) and the WASM port both work headlessly.
4. **MCAD library** — the `openscad-wasm` MCAD optional bundle includes standard mechanical parts (bolts, nuts, gears, motors) as parametric modules.
5. **Manifold backend** (since OpenSCAD 2023+) — dramatically faster mesh generation vs old CGAL backend, critical for interactive previews.

### 3.2 WASM API (from openscad/openscad-wasm)

```javascript
import OpenSCAD from "./openscad.js";

const instance = await OpenSCAD({ noInitialRun: true });

// Write .scad code into the virtual FS
instance.FS.writeFile("/input.scad", `
  link_length = 80;
  bore_diameter = 10;
  wall = 3;
  difference() {
    cylinder(h=link_length, r=bore_diameter/2 + wall, $fn=64);
    cylinder(h=link_length+1, r=bore_diameter/2, $fn=64);
  }
`);

// Compile to binary STL
instance.callMain(["/input.scad", "--enable=manifold", "-o", "/out.stl"]);

// Read the STL bytes
const stlBytes = instance.FS.readFile("/out.stl"); // Uint8Array
```

The `stlBytes` can be saved directly to disk or sent to Unity's STL importer.

### 3.3 Key CLI Flags

| Flag | Purpose |
|------|---------|
| `--enable=manifold` | Use the fast Manifold CSG backend (recommended) |
| `--enable=fast-csg` | Faster CSG evaluation (older flag, now default with manifold) |
| `--enable=lazy-union` | Defer union evaluation for speed |
| `--export-format=binstl` | Force binary STL output (even if output filename is `.stl`) |
| `--export-format=off` | OFF format (preserves face colors from `color()`) |
| `-D name=value` | Override a parameter variable at compile time |

The `-D` flag is how CADAM injects slider values into the SCAD without re-generating LLM code:
```
openscad -Dlink_length=120 -Dbore_diameter=12 input.scad -o out.stl
```

### 3.4 Existing .scad Usage in This Project

The servo bed file (`hardware/openscad/servo_bed_ST3215.scad`) demonstrates the style well:
- All dimensions extracted from the official DXF and stored as named constants
- Three layout variants driven by a single `variant = "A"` variable
- Rich comments explaining physical constraints
- M2 hole positions computed from body geometry

This is the template for all future arm-link `.scad` files.

---

## 4. CadQuery & build123d — Python Parametric CAD

### 4.1 CadQuery

**Repo:** https://github.com/CadQuery/cadquery (5.2k ⭐, Apache 2.0)  
**Install:** `pip install cadquery` or `mamba install -c conda-forge cadquery`  
**Kernel:** OpenCASCADE (OCCT) via the `OCP` Python bindings  

CadQuery uses a **fluent / method-chaining** API modelled after how you would describe geometry in English:

```python
import cadquery as cq

# Parametric hollow arm link
link_length = 80    # mm
outer_r = 12        # mm
inner_r = 9         # mm
flange_t = 4        # mm
bolt_pcd = 20       # mm pitch circle diameter

result = (
    cq.Workplane("XY")
    .circle(outer_r).circle(inner_r)   # annular cross-section
    .extrude(link_length)
    # Add flanges at each end
    .faces(">Z").workplane()
    .circle(outer_r + flange_t).circle(inner_r)
    .extrude(3)
    # Add 4 bolt holes on PCD
    .faces(">Z").workplane()
    .pushPoints([(bolt_pcd/2 * math.cos(a), bolt_pcd/2 * math.sin(a))
                 for a in [0, 90, 180, 270]])
    .hole(3.2)  # M3 clearance
)

# Export
cq.exporters.export(result, "arm_link.stl")
cq.exporters.export(result, "arm_link.step")  # lossless for downstream CAD
```

**Key capabilities:**
- Boolean operations, fillets, chamfers, lofts, sweeps, helical solids
- Nested assembly support (`cq.Assembly`)
- STEP export (lossless — preserves exact geometry, editable in Fusion 360, FreeCAD, SolidWorks)
- STL, VRML, AMF, 3MF export
- Import STEP files
- CQ-editor: a full GUI IDE for interactive development

**Limitation for Unity:** CadQuery runs on CPython with native OCCT bindings — it cannot run in WASM or inside Unity directly. Must be deployed as a **sidecar microservice** (Flask/FastAPI on localhost or a cloud endpoint).

### 4.2 build123d

**Repo:** https://github.com/gumyr/build123d (2.4k ⭐, Apache 2.0)  
**Install:** `pip install build123d`  
**Kernel:** Same OCCT / OCP as CadQuery (build123d is a spiritual successor / refactor)

build123d provides two complementary APIs:

**Algebra Mode** (stateless, composable):
```python
from build123d import *

outer = Cylinder(radius=12, height=80)
inner = Cylinder(radius=9, height=82)  # slightly taller for clean Boolean
link = outer - inner  # difference → hollow tube

# Export
export_stl(link, "arm_link.stl")
export_step(link, "arm_link.step")
```

**Builder Mode** (context-manager, design-history style):
```python
with BuildPart() as arm_link:
    Cylinder(radius=12, height=80)
    Cylinder(radius=9, height=82, mode=Mode.SUBTRACT)
    # Flanges...
    with BuildSketch(Plane.XY):
        Circle(16)
        Circle(9, mode=Mode.SUBTRACT)
    extrude(amount=3)

export_stl(arm_link.part, "arm_link.stl")
```

**Advantages over CadQuery for arm generation:**
- Operator-driven (`obj + sub`, `obj - sub`) makes LLM-generated code more natural
- PEP 8 compliant, mypy typed — easier to validate AI-generated code
- Active development (2,753 commits, latest release Nov 2025 v0.10.0)
- Same OCCT kernel = same output quality

**Recommendation:** Use `build123d` for any arm geometry that requires fillets, lofts, or swept profiles (e.g. ergonomic grip surfaces). Use OpenSCAD for simpler prismatic geometry where WASM in-browser rendering is needed.

### 4.3 Python Microservice Architecture

```
Unity Game (C#)
    │
    │  HTTP POST /generate
    │  Body: { "prompt": "hollow arm link 80mm, M3 bolt pattern",
    │           "format": "stl" }
    ▼
FastAPI server (Python 3.12)
    │
    ├── LLM call → Claude/GPT → build123d or .scad code
    │
    ├── build123d.export_stl() OR
    │   openscad --export-format=binstl
    │
    └── Returns binary STL bytes (application/octet-stream)

Unity receives STL bytes → passes to STL loader → renders mesh
```

This is the most robust path for complex geometry.

---

## 5. hmm — Heightmap PNG to STL

**Source:** `~/projects/MASTER_PROJECTS/_research_may_downloads/hmm/`  
**Original repo:** https://github.com/fogleman/hmm  
**Algorithm:** Garland–Heckbert (1995) Delaunay adaptive triangulation  
**Input:** Grayscale PNG/JPG heightmap  
**Output:** Binary STL  

### 5.1 Relevance to Robot Arm Game

`hmm` is niche but powerful for specific use cases in an arm-design game:

1. **Custom grip/palm surfaces** — Player paints a heightmap in-game. `hmm` converts it to a 3D surface that can be boolean-merged with the base arm link geometry. A low-poly Delaunay mesh is more printable than a voxelated mesh.
2. **Terrain-like base plates** — Anti-skid textures on flat mounting surfaces.
3. **Iterative tool-path input** — If a slicer-in-the-loop is planned, `hmm`'s `-e` error parameter controls triangle count vs. quality tradeoff.

### 5.2 Usage

```bash
# Basic: heightmap to STL, Z scale 10mm per white pixel
hmm input.png output.stl -z 10

# With error budget and solid base for printing
hmm input.png output.stl -z 10 -e 0.001 -b 0.3

# Triangle-count budget (good for in-game preview LOD)
hmm input.png output.stl -z 10 -t 5000
```

The C++ source (`src/stl.cpp`) writes binary STL — compatible with any slicer.

### 5.3 Integration Path

`hmm` is a compiled C++ binary. In a Unity desktop build, it can be called via `System.Diagnostics.Process`. In a WebGL build, it would need to be compiled to WASM (non-trivial but possible given it has minimal dependencies: only `stb_image` and `glm`).

---

## 6. Unity STL Export — Binary Format & C# Implementation

This is the core of the in-game pipeline: take a `UnityEngine.Mesh` (built by the player in the editor) and write it to a `.stl` file for printing.

### 6.1 Binary STL Format Specification

Binary STL is deliberately simple. The spec (Paul Bourke, originally from 3D Systems):

```
HEADER:   80 bytes (arbitrary, often just zeros or a description string)
COUNT:    4 bytes  (uint32, little-endian) — number of triangles

Then, for each triangle (50 bytes each):
  NORMAL: 12 bytes (3 × float32, little-endian) — face normal vector
  V1:     12 bytes (3 × float32, little-endian) — vertex 1
  V2:     12 bytes (3 × float32, little-endian) — vertex 2
  V3:     12 bytes (3 × float32, little-endian) — vertex 3
  ATTR:    2 bytes (uint16, little-endian) — attribute byte count (usually 0)

Total file size: 80 + 4 + (triangleCount × 50) bytes
```

**Important notes:**
- All floats are IEEE 754 single-precision (32-bit), little-endian.
- Normal vectors are optional for most slicers — set to `(0,0,0)` and slicers recompute. However, computing them is cheap and makes the file more spec-compliant.
- Unity uses a **left-handed coordinate system** (Y-up, Z-forward). Standard STL / most slicers / OpenSCAD use **right-handed** (Z-up). You **must** flip the winding order and swap axes on export.

### 6.2 Unity Coordinate System Conversion

```
Unity:      X=right,  Y=up,   Z=forward  (left-handed)
STL/print:  X=right,  Y=forward, Z=up   (right-handed, Z-up convention)

Conversion:
  stl.X =  unity.X
  stl.Y =  unity.Z      ← swap Y and Z
  stl.Z =  unity.Y

Winding order: REVERSE triangle indices (CCW→CW in Unity = CW→CCW in STL right-hand rule)
```

### 6.3 Complete C# Binary STL Exporter

```csharp
using System.IO;
using UnityEngine;

/// <summary>
/// Exports a Unity Mesh to binary STL format.
/// Handles left-to-right-handed coordinate conversion (Y↔Z swap + winding flip).
/// 
/// Binary STL format:
///   [80 bytes header]
///   [uint32 triangle count]
///   For each triangle:
///     [float32×3 normal] [float32×3 v0] [float32×3 v1] [float32×3 v2] [uint16 attribute=0]
///     = 50 bytes per triangle
/// 
/// Usage:
///   StlExporter.ExportMesh(meshFilter.sharedMesh, transform, "arm_link.stl");
/// </summary>
public static class StlExporter
{
    // ─── Coordinate system conversion ──────────────────────────────────────
    // Unity: left-handed, Y-up, Z-forward
    // STL/slicer: right-handed, Z-up (standard CAD convention)
    private static Vector3 ToSTLSpace(Vector3 v) =>
        new Vector3(v.x, v.z, v.y);   // swap Y and Z

    // ─── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Export a single Mesh to binary STL.
    /// Applies the supplied Transform so the exported geometry is in world space.
    /// Pass Transform.identity for local/object space export.
    /// </summary>
    public static void ExportMesh(Mesh mesh, Transform transform, string filePath)
    {
        // Bake world-space transform into vertex positions
        Vector3[] vertices = mesh.vertices;
        int[]     triangles = mesh.triangles;

        // Transform all vertices to world space
        if (transform != null)
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = transform.TransformPoint(vertices[i]);

        int triCount = triangles.Length / 3;

        using (var stream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            // ── Header (80 bytes, zero-padded) ──────────────────────────
            byte[] header = new byte[80];
            byte[] headerText = System.Text.Encoding.ASCII.GetBytes(
                $"Binary STL exported from Unity | {triCount} triangles");
            System.Array.Copy(headerText,
                header,
                Mathf.Min(headerText.Length, 80));
            writer.Write(header);   // 80 bytes

            // ── Triangle count (uint32, little-endian) ──────────────────
            writer.Write((uint)triCount);  // 4 bytes

            // ── Per-triangle data (50 bytes each) ───────────────────────
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Unity winding = CW from front face (left-handed)
                // STL right-hand rule requires CCW, so swap v1 and v2
                Vector3 v0 = ToSTLSpace(vertices[triangles[i    ]]);
                Vector3 v1 = ToSTLSpace(vertices[triangles[i + 2]]); // swapped
                Vector3 v2 = ToSTLSpace(vertices[triangles[i + 1]]); // swapped

                // Compute face normal (cross product, right-hand rule)
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // Write normal (12 bytes)
                WriteVector3(writer, normal);

                // Write vertices (36 bytes)
                WriteVector3(writer, v0);
                WriteVector3(writer, v1);
                WriteVector3(writer, v2);

                // Attribute byte count (2 bytes, always 0 for standard STL)
                writer.Write((ushort)0);
            }
        }

        Debug.Log($"[StlExporter] Wrote {triCount} triangles to: {filePath}");
    }

    /// <summary>
    /// Export multiple MeshFilters merged into a single STL file.
    /// Useful for exporting a full arm assembly as one printable body.
    /// </summary>
    public static void ExportMeshes(MeshFilter[] meshFilters, string filePath)
    {
        // Count total triangles
        int totalTris = 0;
        foreach (var mf in meshFilters)
            if (mf != null && mf.sharedMesh != null)
                totalTris += mf.sharedMesh.triangles.Length / 3;

        using (var stream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            // Header
            byte[] header = new byte[80];
            byte[] txt = System.Text.Encoding.ASCII.GetBytes(
                $"Multi-mesh STL | Unity export | {totalTris} triangles");
            System.Array.Copy(txt, header, Mathf.Min(txt.Length, 80));
            writer.Write(header);

            // Total triangle count
            writer.Write((uint)totalTris);

            // Write each mesh
            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;

                // Transform to world space
                for (int i = 0; i < verts.Length; i++)
                    verts[i] = mf.transform.TransformPoint(verts[i]);

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 v0 = ToSTLSpace(verts[tris[i    ]]);
                    Vector3 v1 = ToSTLSpace(verts[tris[i + 2]]);
                    Vector3 v2 = ToSTLSpace(verts[tris[i + 1]]);
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    WriteVector3(writer, normal);
                    WriteVector3(writer, v0);
                    WriteVector3(writer, v1);
                    WriteVector3(writer, v2);
                    writer.Write((ushort)0);
                }
            }
        }

        Debug.Log($"[StlExporter] Merged {meshFilters.Length} meshes → {totalTris} triangles → {filePath}");
    }

    /// <summary>
    /// Return the binary STL as a byte array (useful for HTTP upload or WebGL blob).
    /// Same logic as ExportMesh but returns bytes instead of writing a file.
    /// </summary>
    public static byte[] MeshToStlBytes(Mesh mesh, Transform transform = null)
    {
        Vector3[] vertices = mesh.vertices;
        int[]     triangles = mesh.triangles;

        if (transform != null)
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = transform.TransformPoint(vertices[i]);

        int triCount = triangles.Length / 3;

        using (var ms = new MemoryStream(80 + 4 + triCount * 50))
        using (var writer = new BinaryWriter(ms))
        {
            byte[] header = new byte[80];
            writer.Write(header);
            writer.Write((uint)triCount);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = ToSTLSpace(vertices[triangles[i    ]]);
                Vector3 v1 = ToSTLSpace(vertices[triangles[i + 2]]);
                Vector3 v2 = ToSTLSpace(vertices[triangles[i + 1]]);
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                WriteVector3(writer, normal);
                WriteVector3(writer, v0);
                WriteVector3(writer, v1);
                WriteVector3(writer, v2);
                writer.Write((ushort)0);
            }

            return ms.ToArray();
        }
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static void WriteVector3(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.x);
        writer.Write(v.y);
        writer.Write(v.z);
    }
}
```

### 6.4 Usage Examples

```csharp
// Export a single arm link (world space)
StlExporter.ExportMesh(
    armLinkGameObject.GetComponent<MeshFilter>().sharedMesh,
    armLinkGameObject.transform,
    Application.persistentDataPath + "/arm_link_export.stl"
);

// Export all parts in an assembly
var allParts = robotAssembly.GetComponentsInChildren<MeshFilter>();
StlExporter.ExportMeshes(allParts,
    Application.persistentDataPath + "/full_arm_assembly.stl");

// Get bytes for upload
byte[] stlBytes = StlExporter.MeshToStlBytes(mesh, transform);
StartCoroutine(UploadStl(stlBytes, "https://myserver.com/upload"));
```

### 6.5 Validation

After export, verify with:
```bash
# Check file size formula: 80 + 4 + (triCount × 50)
python3 -c "
import struct
with open('arm_link_export.stl','rb') as f:
    header = f.read(80)
    tri_count = struct.unpack('<I', f.read(4))[0]
    expected_size = 80 + 4 + tri_count * 50
    actual_size = f.seek(0,2) or f.tell()
    print(f'Triangles: {tri_count}, Expected: {expected_size}B, Match: {actual_size==expected_size}')
"

# Or open in Bambu Studio / PrusaSlicer — slicers catch broken STLs immediately
```

### 6.6 Available Unity Packages

| Package | Stars | Runtime export | Runtime import | Format | License |
|---------|-------|----------------|----------------|--------|---------|
| `karl-/pb_Stl` | 202 ⭐ | ✅ Binary + ASCII | ✅ Binary + ASCII | STL | MIT |
| ProBuilder (Unity package) | Built-in | ✅ (uses pb_Stl) | Editor only | STL | Built-in |
| TriLib 2 (Asset Store) | 229 reviews | ✅ | ✅ FBX/OBJ/STL/GLB/PLY/3MF | Multi | $45 |

**`karl-/pb_Stl`** is the recommended free option. Add to `Packages/manifest.json`:
```json
"co.parabox.stl": "https://github.com/karl-/pb_Stl.git"
```

Then at runtime:
```csharp
using Parabox.Stl;

// Export
Exporter.WriteBinary(new List<GameObject>{ armGO }, filePath);

// Import
Mesh[] imported = Importer.Import(filePath);
```

**`TriLib 2`** ($45, Asset Store) is the right choice if you also need FBX/GLB/OBJ import from player-uploaded files, not just STL. Supports Unity 2021.3+ with URP/HDRP.

---

## 7. Unity STL Import — Runtime Loaders

Players may upload their own STL files (e.g. hand-designed servo mounts) to see them in-game before printing. Two main options:

### 7.1 pb_Stl Importer

`karl-/pb_Stl` includes `Runtime/Importer.cs` with full binary and ASCII STL parsing:

```csharp
using Parabox.Stl;

// Synchronous import from file path
Mesh[] meshes = Importer.Import("/path/to/part.stl");
// Returns array because large models may exceed Unity's 65k vertex limit
// and are automatically split into multiple meshes

// Apply to a GameObject
var go = new GameObject("ImportedPart");
go.AddComponent<MeshFilter>().sharedMesh = meshes[0];
go.AddComponent<MeshRenderer>().material = defaultMaterial;
```

It handles:
- Binary and ASCII STL auto-detection
- Left/right-handed coordinate conversion (configurable)
- Automatic mesh splitting for >65k vertices (Unity's pre-2021 limit; Unity 2021+ supports 2^32 vertices per mesh)
- Multi-mesh export (merges with relative transforms)

### 7.2 TriLib 2 (Asset Store, $45)

A commercial runtime model loader supporting STL, FBX, OBJ, GLTF/GLB, PLY, 3MF, and point clouds. Latest version 2.6.2 (April 2026). Good if the game also accepts FBX/OBJ exports from programs like Blender.

### 7.3 Custom Implementation

Binary STL parsing is equally short (~30 lines C#):

```csharp
public static Mesh LoadBinaryStl(string path)
{
    using var reader = new BinaryReader(File.OpenRead(path));
    reader.ReadBytes(80);   // skip header
    int triCount = (int)reader.ReadUInt32();

    var vertices  = new Vector3[triCount * 3];
    var triangles = new int[triCount * 3];

    for (int i = 0; i < triCount; i++)
    {
        reader.ReadBytes(12); // skip normal (3 floats)
        for (int v = 0; v < 3; v++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            // Convert STL right-hand Z-up → Unity left-hand Y-up
            vertices[i * 3 + v] = new Vector3(x, z, y);
            triangles[i * 3 + v] = i * 3 + v;
        }
        reader.ReadUInt16(); // skip attribute
    }

    var mesh = new Mesh();
    if (triCount * 3 > 65535)
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    mesh.vertices  = vertices;
    mesh.triangles = triangles;
    mesh.RecalculateNormals();
    mesh.RecalculateBounds();
    return mesh;
}
```

---

## 8. URDF in Unity

**Package:** [Unity-Technologies/URDF-Importer](https://github.com/Unity-Technologies/URDF-Importer) (323 ⭐, Apache 2.0, v0.5.2)

### 8.1 What URDF Provides

URDF (Unified Robot Description Format) is the standard ROS format for describing:
- **Links** — rigid bodies with visual meshes (STL or DAE), collision meshes, and inertia
- **Joints** — kinematic connections (revolute, prismatic, fixed, etc.) with axis, limits, and effort
- **Actuators** — motor specs

URDF-Importer parses a `.urdf` XML file and creates a Unity scene with:
- `ArticulationBody` components for each link (uses PhysX 4.0 articulation solver)
- Visual meshes assigned to `MeshRenderer`
- Collision meshes on colliders (with optional V-HACD convex decomposition)
- Joint limits enforced by the physics engine

### 8.2 Why This Matters for the Arm Game

The ideal game loop is:

```
1. Player designs/generates individual arm link STLs in-game
2. Game assembles them into a URDF file programmatically
3. URDF-Importer loads the complete robot for live physics preview
4. Player sees joint motion, checks for collisions, adjusts dimensions
5. Player exports the full STL pack for slicing
```

Step 2 is straightforward — URDF is XML. C# template:

```xml
<!-- Generated arm_robot.urdf -->
<robot name="custom_arm">
  <link name="base_link">
    <visual>
      <geometry><mesh filename="package://meshes/base.stl"/></geometry>
    </visual>
  </link>
  
  <link name="link_1">
    <visual>
      <geometry><mesh filename="package://meshes/link_1.stl"/></geometry>
    </visual>
    <inertial>
      <mass value="0.15"/>
      <inertia ixx="..." iyy="..." izz="..." ixy="0" ixz="0" iyz="0"/>
    </inertial>
  </link>
  
  <joint name="joint_1" type="revolute">
    <parent link="base_link"/>
    <child link="link_1"/>
    <origin xyz="0 0 0.05" rpy="0 0 0"/>
    <axis xyz="0 0 1"/>
    <limit lower="-1.57" upper="1.57" effort="10" velocity="1"/>
  </joint>
</robot>
```

### 8.3 Install in Unity

Via Package Manager → Add from Git URL:
```
https://github.com/Unity-Technologies/URDF-Importer.git?path=/com.unity.robotics.urdf-importer#v0.5.2
```

### 8.4 URDF + STL Workflow

The physical robot_hand project already uses:
- STS3215 servos (16.5 kg·cm torque, 360° rotation)
- 6-servo allocation (finger curl × 5 + thumb opposition)
- InMoov i2 STL geometry

A URDF for this robot can be auto-generated from the existing STL files + servo specs from `hardware/STS3215_notes.md`. Then URDF-Importer makes it interactive in Unity without any manual scene setup.

---

## 9. Architecture Options for the Game

Three architectures are viable. They are not mutually exclusive.

### 9.1 Option A — Pure In-Engine (Procedural Mesh + C# STL Export)

```
Unity C# ──► ProceduralMeshGenerator.cs
                 │  generates Unity Mesh from params
                 ▼
           MeshFilter/MeshRenderer  (player sees 3D preview)
                 │
                 ▼
           StlExporter.ExportMesh()  (player clicks "Export for Print")
                 │
                 ▼
           arm_link.stl → saves to disk / offers download
```

**Pros:** Fully self-contained, no external dependencies, works offline, WebGL compatible.  
**Cons:** Limited geometry (box/cylinder/sphere combinations only unless you implement CSG). Not suitable for complex filleted/swept geometry.

**Best for:** Simple arm links, rectangular servo mounts, cylindrical tubes — i.e. the majority of structural parts.

### 9.2 Option B — OpenSCAD WASM Overlay

```
Unity WebGL build (in browser)
    │
    ├── Unity canvas: physics preview, joint animation, UI
    │
    └── Hidden <iframe> or Web Worker: CADAM / OpenSCAD WASM
             │
             │  User types: "hollow arm link, 80mm, M3 flange"
             ▼
        LLM generates .scad code
             ▼
        openscad-wasm compiles to STL bytes
             ▼
        postMessage({stlBytes}) → Unity
             ▼
        Unity: load STL into scene, preview, allow export
```

**Pros:** Full OpenSCAD power, AI-assisted design, live parameter panel — all in browser, no server.  
**Cons:** Unity WebGL + complex web workers = non-trivial integration. CADAM codebase needs minor adaptation.

**Best for:** A browser-based version of the game where text-to-CAD is the primary mechanic.

### 9.3 Option C — Python Sidecar Microservice

```
Unity Desktop Build (C#)
    │
    │  HTTP POST /api/generate
    │  { prompt, params, format: "stl" }
    ▼
Python FastAPI on localhost:8765
    │
    ├── parse prompt with Claude/OpenAI
    ├── generate build123d or .scad code
    ├── compile → binary STL bytes
    └── return bytes to Unity
    
Unity: receives bytes → StlLoader.LoadBinaryStl() → render mesh
```

**Pros:** Handles complex geometry (fillets, lofts, STEP export), fastest iteration.  
**Cons:** Requires Python runtime on the player's machine (bundle with PyInstaller or ship a pre-built binary).

**Best for:** Desktop game targeting makers/hackers who likely have Python installed anyway.

### 9.4 Recommended Combined Architecture

For maximum flexibility:

| Geometry type | Generator | Why |
|--------------|-----------|-----|
| Structural links (tubes, beams) | Procedural C# mesh | Zero deps, instant |
| Custom servo mounts | OpenSCAD WASM (via CADAM pattern) | AI-assisted, no server |
| Complex organic shapes (grippers, ergonomic) | CadQuery/build123d sidecar | True BREP quality |
| Heightmap-based grip surfaces | `hmm` sidecar | Fast Delaunay mesh |
| Full assembly preview | URDF-Importer + Physics | ArticulationBodies |
| Save for printing | `StlExporter.cs` (custom or pb_Stl) | No deps, exact format |

---

## 10. Comparison Matrix

| Tool | Type | Runtime in Unity | Output | LLM-generatable | License | Best for |
|------|------|-----------------|--------|----------------|---------|---------|
| OpenSCAD WASM | Browser CSG | Via Web Worker (WebGL) | STL, DXF, OFF | ✅ Excellent | GPL-2.0 | In-browser text-to-CAD |
| CADAM pattern | Full stack | As overlay/iframe | STL + preview | ✅ Proven | (source available) | Drop-in text-to-CAD |
| CadQuery | Python BREP | Via HTTP sidecar | STL, STEP, etc. | ✅ Good | Apache 2.0 | Complex parametric parts |
| build123d | Python BREP | Via HTTP sidecar | STL, STEP, etc. | ✅ Good | Apache 2.0 | Modern algebraic CAD |
| hmm | C++ heightmap | Via Process (desktop) | Binary STL | ❌ N/A | MIT | Texture/terrain surfaces |
| pb_Stl (karl-) | Unity C# | ✅ Native | STL (read/write) | N/A | MIT | Unity STL I/O |
| TriLib 2 | Unity C# | ✅ Native | Multi-format | N/A | $45 | Multi-format import |
| URDF-Importer | Unity C# | ✅ Native | (imports URDF) | N/A | Apache 2.0 | Full robot preview |
| Custom StlExporter | Unity C# | ✅ Native | Binary STL write | N/A | Public domain | Reliable, dep-free export |

---

## 11. References

### Local Files
- `~/projects/robot_hand/README.md` — Full gesture-controlled robot hand project
- `~/projects/robot_hand/hardware/openscad/servo_bed_ST3215.scad` — Example parametric OpenSCAD servo mount (3 variants, from DXF-verified dimensions)
- `~/projects/robot_hand/docs/design_research.md` — Anthropometry, surface finish, MediaPipe requirements
- `~/projects/MASTER_PROJECTS/_research_may_downloads/CADAM/` — Full CADAM source (text-to-CAD via OpenSCAD WASM + Claude/LLM)
- `~/projects/MASTER_PROJECTS/_research_may_downloads/hmm/` — hmm heightmap→STL C++ tool
- `~/projects/robot_hand/RELATED_RESEARCH.md` — Prior research pointers

### Web Resources
- OpenSCAD WASM: https://github.com/openscad/openscad-wasm
- OpenSCAD docs: https://openscad.org/documentation.html
- CadQuery: https://github.com/CadQuery/cadquery | https://cadquery.readthedocs.io
- build123d: https://github.com/gumyr/build123d | https://build123d.readthedocs.io
- pb_Stl (Unity STL import/export): https://github.com/karl-/pb_Stl
- TriLib 2 (Asset Store): https://assetstore.unity.com/packages/tools/modeling/trilib-2-model-loading-package-157548
- URDF-Importer: https://github.com/Unity-Technologies/URDF-Importer
- Binary STL specification: http://paulbourke.net/dataformats/stl/
- hmm (Garland-Heckbert): https://github.com/fogleman/hmm
- Garland & Heckbert 1995: http://mgarland.org/files/papers/scape.pdf
- CADAM live: https://adam.new/cadam
- OpenSCAD MCAD library: https://github.com/openscad/MCAD

### Key Papers
- Garland, M. & Heckbert, P. (1995). *Fast Polygonal Approximation of Terrains and Height Fields.* CMU-CS-95-181.
- Bourke, P. STL format: http://paulbourke.net/dataformats/stl/ (standard reference)

---

*Report generated: 2026-05-30 | Project: unity_projects/robot_arms*  
*See also: `~/projects/robot_hand/RELATED_RESEARCH.md` for prior session pointers*
