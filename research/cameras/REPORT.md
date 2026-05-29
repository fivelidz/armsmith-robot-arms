# Camera Research — Wrist & Environment Cameras

The game shows **multiple camera displays from different viewpoints**, mirroring a real
teleoperation/embodied-AI rig. Two physical camera classes inform the in-game camera feeds.

## 1. Wrist camera (end-effector view)
- Product (user-supplied): AliExpress "1005008589283464" — a small **UVC wrist camera** (~32×32 mm module class), the kind that mounts on the gripper.
- The reBot repo ships a **UVC32 camera mount** STEP file plus mounts for Intel RealSense **D435i**, **D405**, Orbbec **Gemini 305 / Gemini 2** depth cameras.
- **Typical UVC module spec**: USB 2.0 UVC, 640×480–1920×1080, ~60–90° FOV, fixed focus, low latency.
- **Depth options** (RealSense D405/D435i): give RGB-D — the basis of the repo's YOLO + depth **visual grasping demo**.

### Game mapping
- A **wrist camera** = a Unity `Camera` parented to the gripper, rendering to a `RenderTexture` shown in a small HUD panel. This is the "robot's-eye view" used for aiming the grasp.
- Optional **depth tint** shader to emulate RGB-D for the higher-tier arm.
- FOV ≈ 70–90° to feel like the real module.

## 2. Environment camera (third-person / overhead)
- Product (user-supplied): **Logitech C922 Pro Stream Webcam**.
- **Known specs (C922):** 1080p @ 30 fps, 720p @ 60 fps, **78° diagonal FOV**, autofocus, dual mics, glass lens, USB-A. A standard "scene observation" webcam.

### Game mapping
- An **environment camera** = a fixed Unity `Camera` overlooking the workspace (the "C922 on a tripod" view) at ~78° FOV, RenderTexture HUD panel.

## Recommended in-game camera layout (multi-view)
1. **Main free camera** — orbit/pan/zoom around the workspace (mouse-driven), the primary play view.
2. **Wrist cam** — small panel, parented to gripper, ~80° FOV.
3. **Environment cam** — small panel, fixed overhead-front, ~78° FOV (the C922 analogue).
4. (Optional tier) **Top-down cam** — for precise placement alignment.

Each secondary view renders to a `RenderTexture` displayed via a `RawImage` in a corner-docked
UI panel. Players can toggle/enlarge panels. This both looks like a real robotics teleop console
and is genuinely useful for lining up grasps when the main camera angle is awkward.

### Implementation notes
- Use **URP** (already the render pipeline in the template project: `com.unity.render-pipelines.universal 17.4.0`).
- Secondary cameras render at low res (e.g. 256×256 / 320×240) to keep cost down.
- Wrist cam near-clip small (0.01 m) since it's close to objects.
