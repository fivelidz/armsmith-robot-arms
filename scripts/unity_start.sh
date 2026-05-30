#!/usr/bin/env bash
# Robust Unity editor + MCP bridge startup with auto-cleanup.
# Solves the common failure modes: stale lockfile, orphaned MCP server holding port 6990,
# leftover editor processes, and silent launch failures.
# Usage: ./scripts/unity_start.sh   (waits until the bridge answers, then exits 0)
set -u
PROJ="/home/fivelidz/projects/unity_projects/robot_arms/UnityProject"
UNITY="$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity"
LOG="$PROJ/Logs/editor_auto.log"
PORT=6990
export DISPLAY="${DISPLAY:-:0}"

echo "[unity_start] 1/5 killing any existing editor + MCP server..."
pkill -f "6000.4.2f1/Editor/Unity -projectPath" 2>/dev/null
pkill -f "mcp-for-unity --transport" 2>/dev/null
pkill -f "mcpforunityserver" 2>/dev/null
sleep 4

echo "[unity_start] 2/5 clearing stale lockfile + run-state..."
rm -f "$PROJ/Temp/UnityLockfile" 2>/dev/null
rm -f "$PROJ/Library/MCPForUnity/RunState/"*.pid 2>/dev/null

# free the port if something still holds it
if ss -tlnp 2>/dev/null | grep -q ":$PORT "; then
  echo "[unity_start]   port $PORT still held — killing holder"
  fuser -k ${PORT}/tcp 2>/dev/null
  sleep 2
fi

echo "[unity_start] 3/5 launching editor (nohup, DISPLAY=$DISPLAY, SDL x11)..."
# CRITICAL FIX (2026-05-30): Unity 6 on this XWayland session logs "Selected window backend: (null)"
# and HANGS at 0% CPU after licensing because its SDL tries Wayland, fails the registry, and does not
# fall back to X11. Forcing SDL_VIDEODRIVER=x11 (while keeping the full env, DISPLAY=:0, and NOT unsetting
# WAYLAND_DISPLAY) lets it use XWayland and proceed to asset import. Use nohup (NOT setsid: setsid stalls
# GUI init). Verified: editor then imports + brings up the MCP bridge on :6990.
SDL_VIDEODRIVER=x11 nohup "$UNITY" -projectPath "$PROJ" -logFile "$LOG" >/tmp/unity_start.stderr 2>&1 &
sleep 8
if ! pgrep -f "6000.4.2f1/Editor/Unity -projectPath" >/dev/null; then
  echo "[unity_start] ERROR: editor process not found after launch. stderr:"
  cat /tmp/unity_start.stderr 2>/dev/null | head -10
  echo "[unity_start] log tail:"; tail -10 "$LOG" 2>/dev/null
  exit 1
fi
echo "[unity_start]   editor PID(s): $(pgrep -f '6000.4.2f1/Editor/Unity -projectPath' | tr '\n' ' ')"

echo "[unity_start] 4/5 waiting for MCP bridge on :$PORT (up to ~150s)..."
for i in $(seq 1 15); do
  sleep 10
  if ss -tlnp 2>/dev/null | grep -q ":$PORT "; then
    R=$(timeout 12 python3 "$(dirname "$0")/mcp.py" console 1 2>&1 | head -c 80)
    if echo "$R" | grep -qE "Retrieved|log entries"; then
      echo "[unity_start] 5/5 BRIDGE READY (after ~$((i*10))s)"; exit 0
    fi
  fi
  echo "[unity_start]   [$i] still initialising..."
done
echo "[unity_start] WARN: bridge not confirmed ready; check $LOG"
tail -6 "$LOG" 2>/dev/null
exit 2
