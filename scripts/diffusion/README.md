# ARMSMITH Diffusion Policy pipeline

End-to-end: Unity demos -> dataset -> trained Diffusion Policy. See
`research/diffusion_pathfinding/REPORT.md` for the why.

## 1. Collect demonstrations (in Unity)
- Play the sim, perform a task (teleop / scripted), record with `BehaviourRecorder` (key G/F10), OR
- Evolve a behaviour and press **F11** — it exports the best genome to `Exports/Demos/*.waypoints.json`
  (the GA is a demo factory, DF2).

## 2. Build a training dataset (DF1)
```bash
python3 ../realbot/waypoints_to_lerobot.py <demos_dir> -o dataset/
# safety-check any demo first (optional):
python3 ../realbot/verify_waypoints.py <demo>.waypoints.json
```
Produces `dataset/manifest.json` + `dataset/episodes/` (action/observation.state = joint+gripper deg).

## 3. Train a Diffusion Policy (DF3)
```bash
python3 train_diffusion_policy.py dataset/ --dry-run                 # validate + plan (no ML deps)
python3 train_diffusion_policy.py dataset/ --backend torch --epochs 200 -o ckpt/   # self-contained DDPM
python3 train_diffusion_policy.py dataset/ --backend lerobot --repo-id you/armsmith # delegate to LeRobot
```
- `torch` backend: minimal self-contained conditional-DDPM (obs = recent joint states, action = future
  joint+gripper chunk). Good for a first end-to-end run. VERIFIED: loss decreases, checkpoint saved.
- `lerobot` backend: prints the exact `lerobot-train --policy.type=diffusion ...` invocation (recommended
  for real training; LeRobot's DiffusionPolicy is maintained + SO-101-ready).

## 4. Deploy (DF4 — DONE)
Serve the trained checkpoint and drive the arm live, receding-horizon.
```bash
python3 serve_diffusion_policy.py ckpt/diffusion_policy_torch.pt        # TCP 127.0.0.1:6020
python3 serve_diffusion_policy.py ckpt/...pt --check                    # load + 1 sample, print, exit
python3 serve_diffusion_policy.py --echo                                # ML-free smoke server
```
In Unity (Play mode), press **key 4** to toggle `DiffusionPolicyClient`: it connects on a background
thread, sends the current joint+gripper observation, gets an action chunk, executes the first few
actions then re-requests (receding horizon). HUD shows "DIFFUSION POLICY LIVE".
Protocol (newline-delimited JSON / TCP):
- `{"obs": [[j0..jn,grip], ...]}` -> `{"action": [[...], ...], "horizon": H, "dim": D}`
- `{"ping": true}` -> `{"ok": true, "horizon": H, "dim": D, "features": [...]}`
The SAME checkpoint can drive the real SO-101 via `../realbot/armsmith_lerobot.py`.

## In-sim diffusion MOTION PLANNER (DF5, already built, C#)
Separate from policy learning: `Visualization/DiffusionMotionPlanner.cs` does planning-as-denoising
(collision-free multimodal EE paths) live in Unity, drawn by `PathVisualizer` (key 6) and executable
via `PlannedPathFollower` (key 5). Swap its seed+denoise for a learned trajectory-diffusion model later.
