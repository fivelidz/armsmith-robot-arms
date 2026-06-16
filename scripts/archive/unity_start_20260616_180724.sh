#!/usr/bin/env bash
# Robust Unity editor + MCP bridge startup with auto-cleanup AND a crash-isolating render strategy.
#
# WHY THIS EXISTS (2026-06-11, S7):
#   On this machine (KDE Plasma 6.5 *Wayland*, AMD Radeon 8060S / RADV, Mesa 25.3, kernel 6.18) Unity 6's
#   editor renders OpenGL through GLX -> XWayland. When Unity crashes hard, it does NOT release its
#   XWayland/GLX surface, which poisons the *shared* XWayland for every later launch: the next editor hangs
#   forever at "Selected window backend: (null)". The only cure was a full graphics-session restart, because
#   that respawns XWayland. (Plasma X11 session is NOT an option here — kwin_x11 was removed in Plasma 6.5.)
#
#   FIX STRATEGY (staged, set RENDER_MODE to override):
#     1) vulkan   : launch on XWayland but force Unity onto the Vulkan render path (-force-vulkan), which
#                   uses Vulkan WSI instead of the fragile GLX surface. RADV here is rock solid.
#     2) gamescope: run Unity inside a nested `gamescope --backend sdl` micro-compositor. Unity gets a clean
#                   Vulkan-backed surface ISOLATED from your desktop's XWayland — so if Unity crashes it
#                   takes down only the gamescope instance, NOT your session. This is what stops the
#                   restart cycle for good.
#     3) xwayland : the old plain SDL_VIDEODRIVER=x11 path (last-resort fallback; the original behaviour).
#
#   Default AUTO order: vulkan -> gamescope -> xwayland (each tried until the bridge answers).
#   Override with:  RENDER_MODE=gamescope ./scripts/unity_start.sh   (or vulkan / xwayland / auto)
#
# Usage: ./scripts/unity_start.sh   (waits until the bridge answers, then exits 0)
set -u
PROJ="/home/fivelidz/projects/unity_projects/robot_arms/UnityProject"
UNITY="$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity"
LOG="$PROJ/Logs/editor_auto.log"
PORT=6990
SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
export DISPLAY="${DISPLAY:-:0}"

# Which render strategies to try, in order. AUTO picks a sensible sequence.
RENDER_MODE="${RENDER_MODE:-auto}"
case "$RENDER_MODE" in
  auto)      MODES=(vulkan gamescope xwayland) ;;
  vulkan)    MODES=(vulkan) ;;
  gamescope) MODES=(gamescope) ;;
  xwayland)  MODES=(xwayland) ;;
  *) echo "[unity_start] unknown RENDER_MODE='$RENDER_MODE' (use auto|vulkan|gamescope|xwayland)"; exit 64 ;;
esac

cleanup() {
  echo "[unity_start] cleanup: killing editor + MCP server, clearing lockfile/port..."
  pkill -f "6000.4.2f1/Editor/Unity -projectPath" 2>/dev/null
  pkill -f "mcp-for-unity --transport" 2>/dev/null
  pkill -f "mcpforunityserver" 2>/dev/null
  pkill -f "gamescope .*Unity" 2>/dev/null
  sleep 4
  rm -f "$PROJ/Temp/UnityLockfile" 2>/dev/null
  rm -f "$PROJ/Library/MCPForUnity/RunState/"*.pid 2>/dev/null
  if ss -tlnp 2>/dev/null | grep -q ":$PORT "; then
    echo "[unity_start]   port $PORT still held — killing holder"
    fuser -k ${PORT}/tcp 2>/dev/null
    sleep 2
  fi
}

# Launch the editor for a given render mode. Sets EDITOR_OK=1 if the bridge answers.
launch_mode() {
  local mode="$1"
  EDITOR_OK=0
  cleanup
  echo "[unity_start] === attempting render mode: $mode ==="
  : > /tmp/unity_start.stderr

  case "$mode" in
    vulkan)
      # XWayland surface, but Unity renders with Vulkan (-force-vulkan) -> skips the brittle GLX path.
      SDL_VIDEODRIVER=x11 nohup "$UNITY" -force-vulkan -projectPath "$PROJ" -logFile "$LOG" \
        >/tmp/unity_start.stderr 2>&1 &
      ;;
    gamescope)
      # Nested micro-compositor: isolates Unity's GPU surface from the desktop XWayland so a Unity crash
      # cannot poison the session. SDL backend nests cleanly inside the running KDE Wayland session.
      # --expose-wayland lets the nested app use xdg-shell; -f = fullscreen the nested output.
      if ! command -v gamescope >/dev/null 2>&1; then
        echo "[unity_start]   gamescope not installed — skipping this mode"
        return
      fi
      # Backend = WAYLAND: gamescope nests as a proper Wayland client into the running KWin compositor and
      # spins up its OWN isolated Xwayland (typically :1). The SDL backend tries to grab an SDL window and
      # crashes in a detached launch context; the wayland backend initialises cleanly (verified: xdg_backend
      # Initted, Xwayland on :1, exit 0). Unity runs inside it on gamescope's nested Xwayland, so a Unity
      # crash poisons only gamescope's throwaway :1 — NOT the desktop's :0. Unity renders with Vulkan.
      local gsbk="${GAMESCOPE_BACKEND:-wayland}"
      echo "[unity_start]   gamescope backend: $gsbk (override via GAMESCOPE_BACKEND=wayland|sdl|auto)"
      nohup gamescope --backend "$gsbk" -W 1920 -H 1080 --expose-wayland -- \
        env SDL_VIDEODRIVER=x11 "$UNITY" -force-vulkan -projectPath "$PROJ" -logFile "$LOG" \
        >/tmp/unity_start.stderr 2>&1 &
      ;;
    xwayland)
      # Original last-resort path: plain XWayland + SDL x11 + default (OpenGL) renderer.
      SDL_VIDEODRIVER=x11 nohup "$UNITY" -projectPath "$PROJ" -logFile "$LOG" \
        >/tmp/unity_start.stderr 2>&1 &
      ;;
  esac

  sleep 10
  if ! pgrep -f "6000.4.2f1/Editor/Unity -projectPath" >/dev/null; then
    echo "[unity_start]   editor process gone after launch ($mode). stderr:"
    head -10 /tmp/unity_start.stderr 2>/dev/null
    echo "[unity_start]   log tail:"; tail -6 "$LOG" 2>/dev/null
    return
  fi
  echo "[unity_start]   editor PID(s): $(pgrep -f '6000.4.2f1/Editor/Unity -projectPath' | tr '\n' ' ')"

  # Detect the known XWayland-poison hang early: backend (null) + no further log progress.
  echo "[unity_start]   waiting for window backend / asset import to progress..."
  local last_size=0 stuck=0
  for chk in $(seq 1 6); do
    sleep 8
    local sz; sz=$(stat -c %s "$LOG" 2>/dev/null || echo 0)
    if grep -q "Selected window backend: (null)" "$LOG" 2>/dev/null && [ "$sz" = "$last_size" ]; then
      stuck=$((stuck+1))
    else
      stuck=0
    fi
    last_size="$sz"
    # If it's importing assets / loaded scripts, it's healthy — move on to the bridge wait.
    if grep -qE "Start importing Assets|Refreshing native plugins|McpLog|Bridge" "$LOG" 2>/dev/null; then
      echo "[unity_start]   editor is progressing (mode $mode) ✓"; break
    fi
    if [ "$stuck" -ge 3 ]; then
      echo "[unity_start]   HANG DETECTED: backend (null) + no log progress for mode $mode — abandoning it."
      return
    fi
  done

  echo "[unity_start]   waiting for MCP bridge on :$PORT (up to ~220s; first import is slow)..."
  for i in $(seq 1 22); do
    sleep 10
    # If the editor process vanished, the mode failed (crash) — stop waiting.
    if ! pgrep -f "6000.4.2f1/Editor/Unity -projectPath" >/dev/null; then
      echo "[unity_start]   editor process died during bridge wait ($mode) — abandoning."
      return
    fi
    if ss -tlnp 2>/dev/null | grep -q ":$PORT "; then
      R=$(timeout 12 python3 "$SELF_DIR/mcp.py" console 1 2>&1 | head -c 80)
      if echo "$R" | grep -qE "Retrieved|log entries"; then
        echo "[unity_start]   BRIDGE READY via mode '$mode' (after ~$((i*10))s)"
        EDITOR_OK=1
        return
      fi
    fi
    echo "[unity_start]   [$i/22] still initialising ($mode)... (port=$(ss -tlnp 2>/dev/null | grep -q ":$PORT " && echo up || echo down))"
  done
  echo "[unity_start]   bridge not confirmed for mode '$mode'."
}

echo "[unity_start] render strategy: ${MODES[*]}"
for m in "${MODES[@]}"; do
  launch_mode "$m"
  if [ "${EDITOR_OK:-0}" = "1" ]; then
    echo "[unity_start] DONE — Unity up + bridge ready (render mode: $m)."
    echo "$m" > /tmp/unity_render_mode.txt
    exit 0
  fi
  echo "[unity_start] mode '$m' did not come up; trying next strategy..."
done

echo "[unity_start] ERROR: all render strategies failed. Last log tail:"
tail -10 "$LOG" 2>/dev/null
echo "[unity_start] If every mode hangs at 'Selected window backend: (null)', the desktop XWayland is"
echo "[unity_start] poisoned by a prior Unity crash — a graphics-session restart is the last resort."
exit 2
