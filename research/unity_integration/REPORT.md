# Unity MCP Integration Research Report

**Date:** 2026-05-30  
**Scope:** How the user's "MCP for Unity" integration works and how another agent can drive the Unity Editor programmatically.  
**Project target:** `/home/fivelidz/projects/unity_projects/robot_arms/UnityProject`

---

## 1. Overview

"MCP for Unity" is an open-source package maintained by CoplayDev (formerly justinpbarnett/unity-mcp), originally created by Justin P. Barnett and Shutong Wu, now sponsored by Aura. It bridges AI assistants to the Unity Editor via the Model Context Protocol (MCP). The architecture has three layers:

```
MCP Client (Claude / agent)
    ↕ JSON-RPC over HTTP (port 8080 default, or stdio)
Python Server  [uvx --from mcpforunityserver mcp-for-unity]
    ↕ WebSocket  /hub/plugin
Unity Editor Plugin  [C# package com.coplaydev.unity-mcp]
```

The Python server auto-discovers `@mcp_for_unity_tool` registrations from the C# side and exposes them as MCP tools. The C# package runs inside the Unity Editor process, receiving commands on the Unity main thread.

---

## 2. Package Details

### 2.1 Git URL and version used in this user's projects

Confirmed in:  
`/home/fivelidz/projects/unity_game_jam/GoblinFortDefense/UnityProject/Packages/manifest.json`

```json
"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"
```

Locked commit hash (from `packages-lock.json`):
```
hash: 73eb27aeccfa8e0676eaf3304136e9b85953d913
```

The `#main` branch tracks the stable release channel. A `#beta` channel is available for pre-release features.

**Current stable version:** v9.7.1 (released 2026-05-24)  
**PyPI server package:** `mcpforunityserver`  
**Unity version range supported:** 2021.3 LTS → Unity 6.x LTS

### 2.2 Package search results

| Project path | Has com.coplaydev.unity-mcp? |
|---|---|
| `unity_game_jam/GoblinFortDefense/UnityProject/` | YES — `#main` branch |
| `unity_projects/diplomacy_game_with_AI/client/unity/` | NO |

No existing `UnityProject` at `/home/fivelidz/projects/unity_projects/robot_arms/UnityProject/` yet — it must be created.

---

## 3. Port Configuration

There is a deliberate port split in this user's setup:

| Port | Used by | Source |
|------|---------|--------|
| **8080** | Default MCP server port (official default, GoblinFortDefense, install docs) | `GoblinFortDefense/CLAUDE.md`, `.claude/mcp.json`, official install guide |
| **6990** | `unity_mcp.py` helper script (game jam directory) | `unity_game_jam/unity_mcp.py` line 6 |

The `unity_mcp.py` script targets port **6990** (marked "PERMANENT: always port 6990" in the code). This is a local override from a previous session where the server was launched on a non-default port. The **official default is 8080** per all documentation and the GoblinFortDefense project.

**Decision for robot_arms:** The recommended approach is to use port **8080** (official default) unless you have a reason to override. The `unity_mcp.py` script must be updated or re-called with the correct port if 8080 is used.

---

## 4. Helper Script: unity_mcp.py

Location: `/home/fivelidz/projects/unity_game_jam/unity_mcp.py`

This is a standalone Python 3 script (no dependencies beyond stdlib) that drives the MCP server over HTTP. It implements the MCP session handshake and wraps tool/resource calls.

### Protocol flow

1. **Initialize** — POST JSON-RPC `{"method":"initialize","params":{"protocolVersion":"2024-11-05",...}}` to `http://127.0.0.1:6990/mcp`
2. **Capture session** — Read `Mcp-Session-Id` header from response
3. **Call tool/resource** — POST subsequent requests with `Mcp-Session-Id` header set
4. **Parse SSE or JSON** — Response is either `data: {...}` (SSE) or raw JSON; script handles both

### Commands supported

| CLI arg | MCP operation | Parameters |
|---------|---------------|------------|
| `state` | `resources/read` | URI: `mcpforunity://editor_state` |
| `console` | `tools/call` → `read_console` | count=30 |
| `play` | `tools/call` → `manage_editor` | action=set_play_mode, play_mode_state=Playing |
| `stop` | `tools/call` → `manage_editor` | action=set_play_mode, play_mode_state=Stopped |
| `hierarchy` | `tools/call` → `manage_scene` | action=get_hierarchy, page_size=50 |
| `errors` | `tools/call` → `read_console` | count=50, log_type=Error |
| `warnings` | `tools/call` → `read_console` | count=20, log_type=Warning |
| `save` | `tools/call` → `manage_scene` | action=save |
| `tools` | `tools/list` | (none) |

The resource URI `mcpforunity://editor_state` is the legacy form; the canonical URI in v9.x is `mcpforunity://editor/state` (see Resources section below). Both may work depending on server version.

---

## 5. Full MCP Tool Catalogue (v9.7, as of 2026-05-24)

Source: https://coplaydev.github.io/unity-mcp/reference/tools (fetched 2026-05-30)

Total: **43 tools** in **9 groups**.

### Group: `core` (30 tools — always enabled)

| Tool | Description |
|------|-------------|
| `apply_text_edits` | Apply small text edits to a C# script identified by URI |
| `batch_execute` | Execute multiple MCP commands in a single batch |
| `create_script` | Create a new C# script at the given project path |
| `debug_request_context` | Return FastMCP request context details (client_id, session_id) |
| `delete_script` | Delete a C# script by URI or Assets-relative path |
| `execute_custom_tool` | Execute a project-scoped custom tool registered by Unity |
| `execute_menu_item` | Execute a Unity menu item by path |
| `find_gameobjects` | Search GameObjects by name, tag, layer, component type, or path |
| `find_in_file` | Search a file with regex, return line numbers and excerpts |
| `get_sha` | Get SHA256 and metadata for a C# script without returning content |
| `manage_asset` | Import, create, modify, delete assets in Unity |
| `manage_build` | Trigger builds, switch platforms, configure settings, manage scenes |
| `manage_camera` | Manage Unity Camera and Cinemachine cameras |
| `manage_components` | Add, remove, or set properties on components attached to GameObjects |
| `manage_editor` | Control and query Unity editor state and settings (incl. play mode) |
| `manage_gameobject` | CRUD operations on GameObjects |
| `manage_graphics` | Volumes, post-processing, light baking, rendering stats, pipeline |
| `manage_material` | Set material properties, colors, shaders |
| `manage_packages` | Query, install, remove, embed UPM packages; configure registries |
| `manage_physics` | Physics settings, collision matrix, materials, joints, queries |
| `manage_prefabs` | Manage Unity Prefab assets |
| `manage_scene` | CRUD operations on Unity scenes (load, save, hierarchy, etc.) |
| `manage_script` | Compatibility router for legacy script operations |
| `manage_script_capabilities` | Get manage_script supported ops, limits, and guards |
| `manage_tools` | Activate/deactivate tool groups per session |
| `read_console` | Get or clear Unity Editor console messages |
| `refresh_unity` | Request asset database refresh and optional script compilation |
| `script_apply_edits` | Structured C# edits (methods/classes) with safer boundaries |
| `set_active_instance` | Set the active Unity instance for this client/session |
| `validate_script` | Validate a C# script and return diagnostics |

### Group: `animation` (1 tool)

| Tool | Description |
|------|-------------|
| `manage_animation` | Animator control and AnimationClip creation |

### Group: `docs` (2 tools)

| Tool | Description |
|------|-------------|
| `unity_docs` | Fetch official Unity documentation from docs.unity3d.com |
| `unity_reflect` | Inspect Unity's live C# API via reflection |

### Group: `probuilder` (1 tool — requires com.unity.probuilder)

| Tool | Description |
|------|-------------|
| `manage_probuilder` | Manage ProBuilder meshes for in-editor 3D modeling |

### Group: `profiling` (1 tool)

| Tool | Description |
|------|-------------|
| `manage_profiler` | Profiler session control, counter reads, memory snapshots, Frame Debugger |

### Group: `scripting_ext` (2 tools)

| Tool | Description |
|------|-------------|
| `execute_code` | Execute arbitrary C# code inside the Unity Editor |
| `manage_scriptable_object` | Create and modify ScriptableObject assets via SerializedObject |

### Group: `testing` (2 tools)

| Tool | Description |
|------|-------------|
| `run_tests` | Start a Unity test run asynchronously; returns job_id |
| `get_test_job` | Poll an async Unity test job by job_id |

### Group: `ui` (1 tool)

| Tool | Description |
|------|-------------|
| `manage_ui` | Manage UI Toolkit elements (UXML, USS, UIDocument) |

### Group: `vfx` (3 tools)

| Tool | Description |
|------|-------------|
| `manage_shader` | Create, read, update, delete shader scripts |
| `manage_texture` | Procedural texture generation |
| `manage_vfx` | Manage VFX components (ParticleSystem, VisualEffect, LineRenderer, TrailRenderer) |

**Note:** Groups `animation`, `probuilder`, `profiling`, `scripting_ext`, `testing`, `ui`, and `vfx` are **off by default** — activate them with `manage_tools` or at server startup using `--include-groups`.

---

## 6. MCP Resources Catalogue (v9.7, as of 2026-05-24)

Resources are **read-only** state surfaces. Total: **25 resources**.

| Resource name | URI |
|---|---|
| `cameras` | `mcpforunity://scene/cameras` |
| `custom_tools` | `mcpforunity://custom-tools` |
| `editor_active_tool` | `mcpforunity://editor/active-tool` |
| `editor_prefab_stage` | `mcpforunity://editor/prefab-stage` |
| `editor_selection` | `mcpforunity://editor/selection` |
| `editor_state` | `mcpforunity://editor/state` |
| `editor_windows` | `mcpforunity://editor/windows` |
| `gameobject` | `mcpforunity://scene/gameobject/{instance_id}` |
| `gameobject_api` | `mcpforunity://scene/gameobject-api` |
| `gameobject_component` | `mcpforunity://scene/gameobject/{instance_id}/component/{component_name}` |
| `gameobject_components` | `mcpforunity://scene/gameobject/{instance_id}/components` |
| `get_tests` | `mcpforunity://tests` |
| `get_tests_for_mode` | `mcpforunity://tests/{mode}` |
| `menu_items` | `mcpforunity://menu-items` |
| `prefab_api` | `mcpforunity://prefab-api` |
| `prefab_hierarchy` | `mcpforunity://prefab/{encoded_path}/hierarchy` |
| `prefab_info` | `mcpforunity://prefab/{encoded_path}` |
| `project_info` | `mcpforunity://project/info` |
| `project_layers` | `mcpforunity://project/layers` |
| `project_tags` | `mcpforunity://project/tags` |
| `renderer_features` | `mcpforunity://pipeline/renderer-features` |
| `rendering_stats` | `mcpforunity://rendering/stats` |
| `tool_groups` | `mcpforunity://tool-groups` |
| `unity_instances` | `mcpforunity://instances` |
| `volumes` | `mcpforunity://scene/volumes` |

---

## 7. Project Layout Required

Any Unity project driven by MCP for Unity must have this minimum on-disk structure:

```
UnityProject/
├── Assets/
│   └── (scenes, scripts, settings…)
├── Packages/
│   └── manifest.json          ← MUST contain com.coplaydev.unity-mcp entry
├── ProjectSettings/
│   └── ProjectVersion.txt     ← MUST contain m_EditorVersion: 6000.4.2f1
└── Library/                   ← auto-generated by Unity; do NOT commit to git
```

### Minimal manifest.json for robot_arms

```json
{
  "dependencies": {
    "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main",
    "com.unity.modules.ai": "1.0.0",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.ugui": "2.0.0"
  }
}
```

Add further packages (Input System, URP, Cinemachine, etc.) as needed by the project.

### Minimal ProjectSettings/ProjectVersion.txt

```
m_EditorVersion: 6000.4.2f1
m_EditorVersionWithRevision: 6000.4.2f1 (7a4c1aeef971)
```

(This exact revision string was taken from the verified GoblinFortDefense project at the same Unity version.)

---

## 8. Recipe: Creating and Launching the robot_arms Unity Project

### Step 1 — Verify Unity 6000.4.2f1 is installed

```bash
ls ~/Unity/Hub/Editor/6000.4.2f1/Editor/Unity
# Expected: binary exists
```

### Step 2 — Create the project via Unity Hub (GUI, recommended first time)

```bash
unityhub
# In Hub: Projects → New Project → 3D Core (or blank)
# Location: /home/fivelidz/projects/unity_projects/robot_arms/
# Name: UnityProject
# Unity version: 6000.4.2f1
```

This creates the full directory structure including a working `ProjectSettings/` with correct GUID.

### Step 3 — Alternatively, create the project headless (CLI)

```bash
/home/fivelidz/Unity/Hub/Editor/6000.4.2f1/Editor/Unity \
  -batchmode \
  -quit \
  -createProject /home/fivelidz/projects/unity_projects/robot_arms/UnityProject
```

### Step 4 — Add the MCP package to manifest.json

After the project is created, edit `Packages/manifest.json` to include the MCP package:

```bash
# Backup first (per project rules)
cp /home/fivelidz/projects/unity_projects/robot_arms/UnityProject/Packages/manifest.json \
   /home/fivelidz/projects/unity_projects/robot_arms/UnityProject/Packages/manifest.json.bak

# Then edit manifest.json to add:
# "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main",
```

Unity resolves the git URL on first open and populates `packages-lock.json` automatically.

### Step 5 — Start the Python MCP server (HTTP mode)

```bash
uvx --from mcpforunityserver mcp-for-unity \
  --transport http \
  --http-host 127.0.0.1 \
  --http-port 8080 \
  > /tmp/mcp_server.log 2>&1 &

echo "MCP server PID: $!"
```

Check the log:
```bash
tail -f /tmp/mcp_server.log
```

### Step 6 — Launch the Unity Editor (windowed) against the project

```bash
/home/fivelidz/Unity/Hub/Editor/6000.4.2f1/Editor/Unity \
  -projectPath /home/fivelidz/projects/unity_projects/robot_arms/UnityProject
```

The Unity C# plugin (installed via the manifest) will attempt to connect to the Python server via WebSocket on startup. There is a built-in retry loop (see `McpReconnect.cs` pattern from GoblinFortDefense) — if the editor opens before the server is up, it retries every 8 seconds for up to 30 attempts.

**Important:** The server must be started BEFORE or SHORTLY AFTER opening Unity. Unity connects out to the Python server (not the other way around) via WebSocket at `ws://127.0.0.1:8080/hub/plugin`.

### Step 7 — Verify connectivity

```bash
# Quick connectivity check — get session ID and editor state:
SID=$(curl -s -X POST http://127.0.0.1:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}},"id":1}' \
  -D - 2>/dev/null | grep -i "mcp-session-id" | awk '{print $2}' | tr -d '\r')

echo "Session ID: $SID"

curl -s -X POST http://127.0.0.1:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SID" \
  -d '{"jsonrpc":"2.0","method":"resources/read","params":{"uri":"mcpforunity://editor/state"},"id":2}'
```

Expected: JSON response containing `isPlayingOrWillChangePlaymode`, `isCompiling`, `activeScene`, etc.

### Step 8 — Run unity_mcp.py against the new project

If using port 8080 (recommended), the existing script at `unity_game_jam/unity_mcp.py` must be updated. The only change needed is line 6:

```python
# Change from:
BASE = "http://127.0.0.1:6990/mcp"  # old game_jam port
# To:
BASE = "http://127.0.0.1:8080/mcp"  # standard port
```

Then verify:
```bash
python3 /home/fivelidz/projects/unity_game_jam/unity_mcp.py state
python3 /home/fivelidz/projects/unity_game_jam/unity_mcp.py tools
python3 /home/fivelidz/projects/unity_game_jam/unity_mcp.py console
```

---

## 9. Running the Editor Headless

For CI or agent-only workflows where no display is available:

```bash
# Headless (no display required — uses virtual framebuffer trick):
Xvfb :99 -screen 0 1024x768x24 &
DISPLAY=:99 /home/fivelidz/Unity/Hub/Editor/6000.4.2f1/Editor/Unity \
  -projectPath /home/fivelidz/projects/unity_projects/robot_arms/UnityProject \
  -logFile /tmp/unity_robot_arms.log &

# Monitor startup:
tail -f /tmp/unity_robot_arms.log | grep -E "MCP|bridge|error|Error" &
```

Note: Unity 6 requires a display even for "headless" Editor usage (as opposed to `-batchmode` builds). The `Xvfb` virtual framebuffer provides this without a physical monitor. The MCP bridge only works in full Editor mode (not `-batchmode`).

---

## 10. MCP Client Configuration for Claude Code / qalcode

To have Claude Code / qalcode automatically connect to the Unity MCP server, add a `.claude/mcp.json` at the project root:

```json
{
  "mcpServers": {
    "unityMCP": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

This is the same pattern used in:
- `GoblinFortDefense/` (confirmed working)
- The official MCP for Unity install documentation

---

## 11. Custom Tools in GoblinFortDefense (Architecture Reference)

The GoblinFortDefense project adds custom `ai_graph_*` tools by placing C# files in `Assets/Editor/AiGraphMcp/`. These are auto-discovered by the MCP server via the `[McpForUnityTool]` attribute. This pattern is documented in the official "Adding Custom Tools" guide.

The same pattern is available for the robot_arms project to add custom tools (e.g., robot joint control, inverse kinematics, articulation body manipulation).

**Known issue with custom tools (BUG-S45-MCP-1 from GoblinFortDefense testing):** If a custom C# tool handler reads parameters from a `JObject` directly without declaring them structurally in the `[McpForUnityTool]` attribute, the Python wrapper's schema validator will block parameter-bearing calls. Parameters must be declared in a `Parameters` field on the attribute, or the tool must use reflection-based dispatch. The built-in tools (manage_scene, manage_gameobject, etc.) do not have this issue.

---

## 12. Known Gotchas on Linux (CachyOS)

1. **Xvfb required for headless** — Unity 6 Editor mode needs a display; use `Xvfb :99` as virtual framebuffer.
2. **Port 6990 vs 8080** — The `unity_mcp.py` helper was saved with port 6990 from a non-default setup. The standard and documented default is 8080.
3. **Domain reloads reset connection** — Every Unity script compilation triggers a domain reload which disconnects the WebSocket bridge. The `McpReconnect.cs` pattern (from GoblinFortDefense) handles this by auto-retrying the connection. Consider adding this script to the robot_arms project.
4. **uvx availability** — Requires `uv` installed: `pip install uv` or `curl -LsSf https://astral.sh/uv/install.sh | sh`. The server is run via `uvx --from mcpforunityserver mcp-for-unity`.
5. **First package resolution** — On first Unity open with the MCP git URL in manifest.json, Unity will clone the repo. This requires internet access and takes ~30–60 seconds. Subsequent opens use the cached packages-lock.json.

---

## 13. Summary Reference Card

| Item | Value |
|------|-------|
| Package name | `com.coplaydev.unity-mcp` |
| Git URL (stable) | `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main` |
| Git URL (beta) | `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta` |
| PyPI server | `mcpforunityserver` |
| Latest version | v9.7.1 (2026-05-24) |
| Default HTTP port | 8080 |
| MCP endpoint | `http://127.0.0.1:8080/mcp` |
| Tools total | 43 (in 9 groups) |
| Resources total | 25 |
| Unity version in use | 6000.4.2f1 |
| Editor binary | `/home/fivelidz/Unity/Hub/Editor/6000.4.2f1/Editor/Unity` |

### Exact command to launch the editor with the bridge (robot_arms project)

```bash
# Step 1: Start Python MCP server
uvx --from mcpforunityserver mcp-for-unity \
  --transport http --http-host 127.0.0.1 --http-port 8080 \
  > /tmp/mcp_server.log 2>&1 &

# Step 2: Launch Unity Editor (windowed)
/home/fivelidz/Unity/Hub/Editor/6000.4.2f1/Editor/Unity \
  -projectPath /home/fivelidz/projects/unity_projects/robot_arms/UnityProject &

# Step 3: Verify bridge (wait ~10s for Unity to open)
sleep 10
python3 /home/fivelidz/projects/unity_game_jam/unity_mcp.py tools
```

---

*Research conducted by Claude Code (file-search agent). No files were modified. All findings sourced from local project files and live GitHub/docs pages.*
