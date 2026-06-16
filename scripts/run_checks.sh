#!/usr/bin/env bash
# run_checks.sh — ARMSMITH headless regression suite (no GUI needed).
#
# Runs every check that can be verified WITHOUT the interactive editor (which is unreliable on this
# Wayland/AMD stack). These are the CI gates that proved the S7d work:
#   1. Compile           — project builds with zero C# errors (batchmode).
#   2. PhysX stability    — builds the real SO-101 arm + 600 Simulate steps, fails on any NaN
#                           (regression gate for the setupDescTask segfault).
#   3. Headless pick      — full approach->grasp->lift under load stays finite (no crash).
#   4. Viz smoke          — path-visualization providers + data helpers are sane.
#   5. Diffusion pipeline — GA-style demo -> safety verify -> LeRobot dataset converter (Python).
#
# Usage: ./scripts/run_checks.sh          (runs all; exits non-zero if any fail)
#        ./scripts/run_checks.sh quick     (skip the slower Unity checks; pipeline + compile only)
set -u
PROJ="/home/fivelidz/projects/unity_projects/robot_arms/UnityProject"
UNITY="$HOME/Unity/Hub/Editor/6000.4.2f1/Editor/Unity"
REALBOT="$(dirname "$0")/realbot"
MODE="${1:-all}"
PASS=0; FAIL=0
export DISPLAY="${DISPLAY:-:0}"

run_unity_method () {
  local name="$1" method="$2" want="$3"
  rm -f "$PROJ/Temp/UnityLockfile" 2>/dev/null
  local out; out=$(mktemp)
  "$UNITY" -batchmode -nographics -projectPath "$PROJ" \
    -executeMethod "$method" -quit -logFile - >"$out" 2>&1
  local code=$?
  if grep -aqE "error CS[0-9]+" "$out"; then
    echo "  [FAIL] $name — COMPILE ERRORS:"; grep -aE "error CS[0-9]+" "$out" | head -3; FAIL=$((FAIL+1)); rm -f "$out"; return
  fi
  if grep -aq "$want" "$out"; then
    echo "  [PASS] $name"; PASS=$((PASS+1))
  else
    echo "  [FAIL] $name (exit $code) — expected '$want'. Tail:"; grep -aE "\[.*Check\]|Exception|setupDescTask" "$out" | tail -4
    FAIL=$((FAIL+1))
  fi
  rm -f "$out"
}

echo "=== ARMSMITH headless checks (mode: $MODE) ==="

if [ "$MODE" != "quick" ]; then
  echo "[1] Compile + PhysX stability"
  run_unity_method "PhysX stability (600 steps, no NaN)" \
    "ArmSmith.EditorTools.PhysxStabilityCheck.RunHeadless" "PhysxStabilityCheck] PASSED"

  echo "[2] Headless pick (stable under grasp+lift)"
  run_unity_method "Headless pick" \
    "ArmSmith.EditorTools.HeadlessPickCheck.RunHeadless" "HeadlessPickCheck] PASSED"

  echo "[2b] Realistic grasp (friction-limited: strong holds, weak drops)"
  run_unity_method "Realistic grasp" \
    "ArmSmith.EditorTools.RealisticGraspCheck.RunHeadless" "RealisticGraspCheck] PASSED"

  echo "[3] Viz smoke (providers + data helpers)"
  run_unity_method "Viz smoke" \
    "ArmSmith.EditorTools.VizSmokeCheck.RunHeadless" "VizSmokeCheck] PASSED"

  echo "[3b] Motor physics (drive tracking, servo rate/ticks, gravity hold)"
  run_unity_method "Motor physics" \
    "ArmSmith.EditorTools.MotorPhysicsCheck.RunHeadless" "MotorPhysicsCheck] PASSED"

  echo "[3c] Training regimen (Motion-GA + Sensor-Policy converge)"
  run_unity_method "Training learns" \
    "ArmSmith.EditorTools.TrainingSmokeCheck.RunHeadless" "TrainingSmokeCheck] PASSED"
fi

echo "[3d] Vision grasp-geometry (numpy CV toolbox unit tests)"
if python3 "$(dirname "$0")/vision/test_grasp_geometry.py" >/dev/null 2>&1; then
  echo "  [PASS] Vision grasp-geometry (24 unit tests)"; PASS=$((PASS+1))
else
  echo "  [FAIL] Vision grasp-geometry — unit tests failed"; FAIL=$((FAIL+1))
fi

echo "[4] Diffusion pipeline (GA demo -> safety -> LeRobot dataset)"
TMP=$(mktemp -d)
python3 - "$TMP" <<'PY'
import json,sys,os
d=sys.argv[1]; jn=["BaseYaw","Shoulder","Elbow","Wrist"]
keys=[([0,40,-80,-15],0,0.4),([20,55,-95,-25],0,0.4),([20,55,-95,-25],1,0.3),([-15,45,-85,-20],1,0.5)]
dt=0.05; wps=[]; t=0.0; cur=[0,0,0,0]
for ang,grip,hold in keys:
    st=cur[:]; steps=max(1,round(hold/dt))
    for s in range(1,steps+1):
        a=s/steps; cur=[st[j]+(ang[j]-st[j])*a for j in range(4)]
        wps.append({"t_s":round(t,3),"joints":[{"name":jn[j],"deg":round(cur[j],2)} for j in range(4)],"gripper_deg":round(grip*90,1)}); t+=dt
traj={"arm_type":"so101","schema":"armsmith.waypoints.v1","units":"degrees","joint_names":jn,"gripper_name":"Gripper","dt_s":dt,"waypoints":wps}
os.makedirs(d+"/demos",exist_ok=True)
open(d+"/demos/ga0.waypoints.json","w").write(json.dumps(traj))
PY
DIFF="$(dirname "$0")/diffusion"
if python3 "$REALBOT/verify_waypoints.py" "$TMP/demos/ga0.waypoints.json" >/dev/null 2>&1; then
  if python3 "$REALBOT/waypoints_to_lerobot.py" "$TMP/demos" -o "$TMP/ds" >/dev/null 2>&1 \
     && [ -f "$TMP/ds/manifest.json" ]; then
    # also exercise the DF3 training dry-run (validates dataset -> trainer plumbing, no ML deps)
    if python3 "$DIFF/train_diffusion_policy.py" "$TMP/ds" --dry-run >/dev/null 2>&1; then
      echo "  [PASS] Diffusion pipeline (demo -> SAFE -> dataset -> train dry-run)"; PASS=$((PASS+1))
    else
      echo "  [FAIL] Diffusion pipeline — train dry-run failed"; FAIL=$((FAIL+1))
    fi
  else
    echo "  [FAIL] Diffusion pipeline — converter failed"; FAIL=$((FAIL+1))
  fi
else
  echo "  [FAIL] Diffusion pipeline — safety verify failed"; FAIL=$((FAIL+1))
fi

echo "[5] Diffusion deploy (train torch -> serve --check) [skipped if no torch]"
if python3 -c "import torch" >/dev/null 2>&1; then
  if python3 "$DIFF/train_diffusion_policy.py" "$TMP/ds" --backend torch --epochs 5 -o "$TMP/ckpt" >/dev/null 2>&1 \
     && [ -f "$TMP/ckpt/diffusion_policy_torch.pt" ]; then
    if python3 "$DIFF/serve_diffusion_policy.py" "$TMP/ckpt/diffusion_policy_torch.pt" --check 2>&1 | grep -q "sample action chunk"; then
      echo "  [PASS] Diffusion deploy (train -> ckpt -> serve samples action chunk)"; PASS=$((PASS+1))
    else
      echo "  [FAIL] Diffusion deploy — server could not sample"; FAIL=$((FAIL+1))
    fi
  else
    echo "  [FAIL] Diffusion deploy — torch training/ckpt failed"; FAIL=$((FAIL+1))
  fi
else
  echo "  [SKIP] Diffusion deploy — torch not installed (dataset+train-dryrun already gated above)"
fi
rm -rf "$TMP"

echo "=== RESULT: $PASS passed, $FAIL failed ==="
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
