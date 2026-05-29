# Spec Guide — Cameras, Vision & Sim-to-Real Crossover

Implements intentions I5, I14, I15 (see PROMPT_LOG.md). Pillar C.

## Goal
The in-game arm carries a **wrist computer-vision camera**, and a **second fixed camera** watches the
workspace. BOTH are displayed in the HUD AND feed the training layer. Their intrinsics/placement are
configurable to MATCH the real rig (wrist UVC module + Logitech C922), so a policy trained on these
image streams transfers to the real arm — and conversely the same camera layout lets us "follow actual
performance and adaptations" of a real arm replayed in-game.

## Real reference (from research/cameras + robot_hand prior project)
- **Wrist cam**: small UVC module on the gripper. Game: `Camera` parented to gripper, ~80° FOV, near-clip 0.01 m.
- **Environment cam**: Logitech C922 — 1080p@30 / 720p@60, **78° diagonal FOV**, fixed front-overhead.
- Prior `robot_hand` used a **2nd camera observing the robot** (`robot_observer.py`) for closed-loop
  pose verification — we generalise this into the "environment/observer" camera.

## In-game camera set
| Cam | Parent | FOV | Render target | Role |
|-----|--------|-----|---------------|------|
| MainCam | rig (orbit) | 60° | screen | primary play view (mouse orbit/pan/zoom) |
| WristCam | gripper | 80° | RT 256x256 | robot's-eye view; PRIMARY vision training input; matches real UVC |
| EnvCam | fixed mount | 78° | RT 320x240 | observer view; secondary training input; matches C922 |
| TopCam (opt) | overhead | 50° | RT 256x256 | placement alignment |

HUD: corner `RawImage` panels bound to each RT; togglable (`V`), cycle focus (`C`).

## Camera config (matches real intrinsics)
A `CameraRigConfig` ScriptableObject/JSON so the same numbers describe sim and real:
```json
{
  "wrist":  { "fovDeg": 80, "width": 256, "height": 256, "nearClip": 0.01,
              "localPos": [0,0.02,0.03], "localEuler": [20,0,0] },
  "env":    { "fovDeg": 78, "width": 320, "height": 240, "nearClip": 0.05,
              "worldPos": [0.0,0.6,-0.6], "lookAt": [0,0.05,0.35] }
}
```
When porting to real life, the same FOV/resolution are set on the physical cameras; placement matches
the printed wrist mount (reBot UVC32 mount) and the C922 tripod position.

## Vision as a training input
- Each RT is read back to a small CPU texture (or kept on GPU for ML-Agents `CameraSensor`).
- **ML-Agents path:** add `CameraSensorComponent` for WristCam + EnvCam to the arm Agent -> visual obs.
- **Classical path (early):** run a simple blob/colour detector on the wrist RT to locate the cube in
  image space; feed (u,v,area) as observation. Mirrors the real YOLO+depth grasp demo (reBot).
- Reward can use vision-derived target centring (cube centred in wrist view => grasp-ready).

## Sim-to-real crossover plan
1. Train with WristCam+EnvCam observations in sim (domain randomise lighting/colour/texture).
2. Export policy (see TRAINING_SPEC) + camera config.
3. On real arm: same camera FOV/res/placement; same policy consumes real frames.
4. **Reverse direction (I15, M13):** stream real-arm joint telemetry + real camera frames back into the
   game; the EnvCam/observer view "follows actual performance"; compare sim vs real trajectories;
   detect adaptations (drift, slip) and feed them back into retraining (domain adaptation loop).

## Unity implementation notes
- URP. Each secondary `Camera` renders to a `RenderTexture` (`RenderTextureFormat.ARGB32`).
- Keep secondary cams at low res + reduced render rate (every N frames) for performance.
- `WristCam` near-clip tiny (0.01) so it sees objects in the jaws.
- Provide `CameraRig.Capture(camId) -> Texture2D` for screenshotting / dataset recording (LeRobot MP4 later).
- Component: `Assets/Scripts/CameraRig.cs` (+ `CameraRigConfig`).
```
