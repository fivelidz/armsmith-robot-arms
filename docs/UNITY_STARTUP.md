# Unity Editor + MCP Bridge — Startup & Troubleshooting

## TL;DR — how to (re)start Unity
```bash
cd /home/fivelidz/projects/unity_projects/robot_arms
./scripts/unity_start.sh        # cleans stale state, launches editor, waits for bridge on :6990
```
Then drive it with `python3 scripts/mcp.py tool <name> '<json-args>'` (see scripts/mcp.py).

## The big gotcha (SOLVED 2026-05-30)
On this machine (CachyOS, Wayland + XWayland, AMD Radeon 8060S / Mesa), the **windowed Unity 6
editor hangs at 0% CPU right after licensing**, with this telltale log line:
```
Selected window backend: (null)
Failed to get the Wayland registry
```
Root cause: Unity's bundled SDL tries the Wayland video backend, fails to bind the Wayland registry,
and does NOT fall back to X11 on its own — so it never creates a window and blocks on `ppoll`.

### The fix
Launch with **`SDL_VIDEODRIVER=x11`** in the FULL interactive environment (keep DISPLAY=:0, do NOT
unset WAYLAND_DISPLAY, use `nohup` not `setsid`):
```bash
SDL_VIDEODRIVER=x11 nohup "$UNITY" -projectPath "$PROJ" -logFile "$LOG" &
```
With this, the editor uses XWayland, proceeds to asset import, compiles, and brings up the MCP bridge.
`scripts/unity_start.sh` bakes this in.

### What does NOT work (tested)
- Plain `nohup`/`setsid` with default env → hangs at "(null)" backend.
- `setsid` → stalls GUI init (detaches from session).
- Unsetting WAYLAND_DISPLAY, or setting GDK_BACKEND/QT_QPA_PLATFORM, or `env -i` minimal env,
  or `xvfb-run` → editor **exits instantly** (silent).
- Only `SDL_VIDEODRIVER=x11` + full env + nohup works.

## Other recovery facts
- **Headless batchmode always works** (good for compile checks):
  `"$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" -logFile Logs/x.log`
  Exit 0 + no `error CS` = project is healthy.
- **MCP bridge wedges** after heavy recompile-during-play cycles (socket-exception spam in log,
  pings stop answering). Fix = restart editor via `unity_start.sh` (it kills the orphaned
  `mcp-for-unity` python server that keeps holding port 6990).
- **Stale state to clear on restart**: `UnityProject/Temp/UnityLockfile`,
  `UnityProject/Library/MCPForUnity/RunState/*.pid`, orphaned `mcp-for-unity` python procs.
- Check X health: `DISPLAY=:0 xdpyinfo | head` (should print "name of display: :0").

## Quick health probe
```bash
ss -tlnp | rg 6990                     # bridge port listening?
python3 scripts/mcp.py console 3       # bridge answers?
rg "error CS" UnityProject/Logs/*.log  # compile errors?
ps -eo pid,%cpu,etime,cmd | rg "Editor/Unity -projectPath" | rg -v bash  # editor alive + CPU>5%?
```
