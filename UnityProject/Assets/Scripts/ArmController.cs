using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Drives a ProceduralArm by mouse + keyboard. Two modes:
    ///  - IK mode: an end-effector target (gizmo) is moved with mouse/keys; FABRIK solves a virtual
    ///    chain; resulting bone directions are converted to per-joint angle targets for the
    ///    ArticulationBody drives (so the physics arm tracks the IK solution).
    ///  - Manual mode: select a joint (1..N) and rotate it with Q/E.
    /// Also handles gripper open/close and exposes current joint targets for recording/export.
    /// See design/GAME_DESIGN.md section 5 for the full control scheme.
    /// </summary>
    public class ArmController : MonoBehaviour
    {
        public ProceduralArm arm;
        public Transform ikTarget;            // visible gizmo the player drags
        public Camera mainCamera;

        public enum Mode { IK, Manual }
        public Mode mode = Mode.IK;

        [Header("Tuning")]
        public float keyMoveSpeed = 0.35f;     // m/s for WASD/QE fly-around of the position indicator
        public float manualJointSpeed = 60f;   // deg/s
        public int ikIterations = 12;

        [Header("Mouse follow")]
        public bool mouseFollow = true;        // IK target tracks the cursor on the work-plane
        public float workPlaneY = 0.05f;       // height of the follow plane above the worktop (m)
        public float followLerp = 12f;         // smoothing
        public float minTargetY = 0.02f;       // never let IK target go below worktop top (+margin)
        public float scrollDepthSensitivity = 0.45f; // m per scroll notch for pick-height (responsive)
        public float scrollDepthFine = 0.12f;        // fine depth step when Shift held

        int selectedJoint = 0;
        float[] targetAngles;                  // commanded joint angles (deg), what we export

        // FABRIK scratch
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<float> lens = new List<float>();

        public float[] TargetAngles => targetAngles;
        public Gripper Gripper => arm != null ? arm.gripper : null;

        public void Bind(ProceduralArm a, Transform target, Camera cam)
        {
            arm = a; ikTarget = target; mainCamera = cam;
            targetAngles = new float[arm.jointBodies.Count];

            // Natural "ready" pose: bend the arm forward+down so the gripper hovers over the worktop in
            // reach of the trays, instead of standing bolt upright. Two homes — one for the simple
            // procedural 4-DOF arm, one for the real SO-101 6-DOF URDF arm (empirically tuned so the
            // gripper sits ~3 cm above the worktop near the trays; see PROGRESS notes).
            // Start in the CALIBRATION position = all motors at 0 (the homing reference the real arm
            // zeros to). This is the pose Z/Home returns to, so the arm starts where calibration expects.
            // The 4-DOF procedural arm's tuned "ready" pose (gripper hovering over the worktop) — this is
            // the pose that worked well. (6-DOF kept for the STL-skin experiment, not the default.)
            float[] home4 = { 0f, 40f, -78f, -5f };
            float[] home6 = { 0f, -40f, -30f, -15f, 0f, 0f };
            float[] home = arm.jointBodies.Count >= 6 ? home6 : home4;
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < home.Length
                    ? Mathf.Clamp(home[i], arm.jointSpecs[i].minAngle, arm.jointSpecs[i].maxAngle)
                    : 0f;
            arm.SeedServoState(targetAngles);   // start servo rate-limiter at the calibration pose
            arm.SetJointTargets(targetAngles);

            // Start in MANUAL mode HOLDING the calibration pose (IK off) so the arm sits at calibration
            // and doesn't immediately fly to an IK target. Press Tab to switch to IK fly-around control.
            mode = Mode.Manual;
            // (The IK target is parked at the calibration tip once the arm settles — see FixedUpdate.)
        }

        // Per-servo direct keyboard control. Each joint gets a +/- key pair, with a label for the HUD.
        // Joint 0..5: T/G, Y/H, U/J, I/K, O/L, P/; . Works in BOTH IK and Manual mode (direct keys
        // temporarily drive the joints; in IK mode they nudge on top of the solution).
        static readonly (KeyCode up, KeyCode down, string label)[] JointKeys =
        {
            (KeyCode.T, KeyCode.G, "T/G"),
            (KeyCode.Y, KeyCode.H, "Y/H"),
            (KeyCode.U, KeyCode.J, "U/J"),
            (KeyCode.I, KeyCode.K, "I/K"),
            (KeyCode.O, KeyCode.L, "O/L"),
            (KeyCode.P, KeyCode.Semicolon, "P/;"),
        };
        public static string JointKeyLabel(int i) => i < JointKeys.Length ? JointKeys[i].label : "-";

        bool directKeyActive;   // true this frame if any per-joint key is held (suppresses IK fighting)

        // --- Calibrate / speed / pause-resume ---
        [Header("Calibrate / speed / pause")]
        public float[] zeroPose;             // the calibrated "zero" home pose (deg per joint)
        [Range(0.1f, 3f)] public float speedScale = 1f;   // manual-control speed multiplier
        public bool paused = false;          // when paused, the arm HOLDS; new targets are queued
        float[] queuedTargets;               // target captured while paused -> applied on resume
        bool hasQueued;

        void Update()
        {
            if (arm == null || arm.jointBodies.Count == 0) return;
            HandleModeToggle();
            HandleSpeedAndPause();
            HandleCalibrate();
            HandleGripper();
            HandleDirectJointKeys();          // labeled per-servo control (both modes)
            if (mode == Mode.IK) HandleIKInput();
            else HandleManualInput();
        }

        void HandleSpeedAndPause()
        {
            // Speed: < and > ... no (claw uses ,/.). Use - / = already = sim speed. Use [ ] = depth.
            // Dedicated manual speed keep on keys 9 / 0? Those are scenario-free. Use comma-less: , . taken.
            // We expose speedScale via UI; keys: hold Left-Ctrl + scroll? Keep it simple: keys '<' '>' shift.
            if (Input.GetKeyDown(KeyCode.Period) && Input.GetKey(KeyCode.LeftShift)) speedScale = Mathf.Min(3f, speedScale + 0.25f);
            if (Input.GetKeyDown(KeyCode.Comma) && Input.GetKey(KeyCode.LeftShift)) speedScale = Mathf.Max(0.1f, speedScale - 0.25f);

            // Pause/resume: P pauses (arm holds, new IK target is queued not driven); P again resumes &
            // the arm moves to the queued target. (Note: 'P' was playback in recorder; we use 'Return'.)
            if (Input.GetKeyDown(KeyCode.Return))
            {
                paused = !paused;
                if (!paused && hasQueued) { /* on resume, queued target becomes active automatically */ hasQueued = false; }
            }
        }

        void HandleCalibrate()
        {
            // Home/zero: press Home (or 'Z') to bring all motors back to the calibrated zero pose.
            if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.Z))
                GoToZero();
        }

        /// <summary>Capture the arm's CURRENT joint angles as the calibrated zero/home pose. Used by the
        /// Options "Set Zero" button and the calibrate key — this is the pose exported as the servo zero.</summary>
        public void CalibrateZeroHere()
        {
            if (arm == null) return;
            var cur = arm.GetJointAngles();
            zeroPose = new float[cur.Length];
            for (int i = 0; i < cur.Length; i++) zeroPose[i] = cur[i];
        }

        /// <summary>Bring the arm back to its calibrated zero/home pose (all motors to zeroPose).</summary>
        public void GoToZero()
        {
            if (zeroPose == null) zeroPose = new float[targetAngles.Length]; // default = all 0
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < zeroPose.Length ? zeroPose[i] : 0f;
            // also park the IK target at the resulting tip so IK doesn't yank it away
            if (ikTarget != null && arm.endEffector != null)
                ikTarget.position = arm.endEffector.position;
            mode = Mode.Manual; // hold the zero pose (IK off) until the player moves again
        }

        /// <summary>
        /// HARD-home the arm: teleport the articulation to a clean pose AND reset the controller's internal
        /// command state, so any accumulated bad/wedged articulation state from a contact-rich task is fully
        /// cleared. This is the robust "re-home the robot between tasks" primitive — fixes the
        /// "works once then jams on the next pick" non-determinism. Pass null for the zero pose.
        /// Switches to Manual so the freshly-homed pose is held until the next deliberate command.
        /// </summary>
        public void HardHome(float[] anglesDeg = null)
        {
            if (targetAngles == null) return;
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = (anglesDeg != null && i < anglesDeg.Length) ? anglesDeg[i] : 0f;
            if (arm != null) arm.HardResetJoints(anglesDeg);
            if (ikTarget != null && arm != null && arm.endEffector != null)
                ikTarget.position = arm.endEffector.position;
            hasQueued = false;       // clear any paused/queued hold
            mode = Mode.Manual;
        }

        /// <summary>Set the current pose as the calibrated zero (like homing a real arm's encoders).</summary>
        public void SetCurrentAsZero()
        {
            zeroPose = (float[])targetAngles.Clone();
        }

        // Direct per-servo control via labeled key pairs (T/G, Y/H, ...). Drives joints individually.
        void HandleDirectJointKeys()
        {
            directKeyActive = false;
            int n = Mathf.Min(arm.jointBodies.Count, JointKeys.Length);
            for (int i = 0; i < n; i++)
            {
                float dir = 0f;
                if (Input.GetKey(JointKeys[i].up)) dir += 1f;
                if (Input.GetKey(JointKeys[i].down)) dir -= 1f;
                if (dir != 0f)
                {
                    directKeyActive = true;
                    var js = arm.jointSpecs[i];
                    targetAngles[i] = Mathf.Clamp(
                        targetAngles[i] + dir * manualJointSpeed * speedScale * Time.deltaTime,
                        js.minAngle, js.maxAngle);
                }
            }
        }

        int settleFrames = 0;
        /// <summary>Re-run the proven settle-then-calibrate path from the HOME pose. Call before a task to
        /// guarantee a clean IK calibration (the FK reference can drift after activity). Sets joints to the
        /// home pose, lets physics settle, then recalibrates — exactly like startup.</summary>
        public void Recalibrate()
        {
            if (targetAngles == null) return;
            float[] home6 = { 0f, 0f, 0f, 0f, 0f, 0f };
            float[] home4 = { 0f, 40f, -78f, -5f };
            float[] home = arm.jointBodies.Count >= 6 ? home6 : home4;
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < home.Length ? home[i] : 0f;
            arm.SeedServoState(targetAngles);
            calibrated = false;
            settleFrames = 0;        // triggers the settle->calibrate path in FixedUpdate
            mode = Mode.Manual;      // hold the home pose while settling
        }

        void FixedUpdate()
        {
            if (arm == null || targetAngles == null) return;
            // Let the arm settle into its home pose, THEN calibrate the IK from the real rest geometry.
            if (settleFrames < 30)
            {
                settleFrames++;
                arm.SetJointTargets(targetAngles);
                if (settleFrames == 30)
                {
                    CalibrateIK();
                    if (zeroPose == null) SetCurrentAsZero();
                    // park the IK target at the calibration tip so switching to IK mode doesn't yank
                    if (ikTarget != null && arm.endEffector != null) ikTarget.position = arm.endEffector.position;
                }
                return;
            }
            if (mode == Mode.IK) SolveIK();   // keeps computing the (possibly queued) target angles

            if (paused)
            {
                // PAUSED: hold the pose captured at the moment of pausing. targetAngles keeps updating
                // (the queued goal you set while paused), but we don't drive to it until you resume.
                if (!hasQueued) { heldPose = (float[])arm.GetJointAngles().Clone(); hasQueued = true; }
                arm.SetJointTargets(heldPose);
                return;
            }
            arm.SetJointTargets(targetAngles);
        }
        float[] heldPose;

        /// <summary>Run ONE step of the live IK control loop (SolveIK + drive) — for headless tests that
        /// drive Physics.Simulate manually (Unity FixedUpdate doesn't fire there). Mirrors the player path
        /// in IK mode. Call after CalibrateIK + setting ikTarget.position.</summary>
        public void TickControl()
        {
            if (arm == null || targetAngles == null) return;
            if (mode == Mode.IK) SolveIK();
            arm.SetJointTargets(targetAngles);
        }

        void HandleModeToggle()
        {
            if (KeyBindings.Down(KeyBindings.Action.ToggleMode))
                mode = mode == Mode.IK ? Mode.Manual : Mode.IK;
        }

        public int wristRollJoint = -1;   // index of the claw-rotation joint (auto-found: "wrist_roll" / last Roll)

        void HandleGripper()
        {
            if (arm.gripper == null) return;
            if (KeyBindings.Down(KeyBindings.Action.GripToggle)) arm.gripper.Toggle();
            // open / close (hold to actuate continuously) — remappable.
            if (KeyBindings.Held(KeyBindings.Action.GripOpen))  arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 0f, 3f * Time.deltaTime));
            if (KeyBindings.Held(KeyBindings.Action.GripClose)) arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 1f, 3f * Time.deltaTime));
            if (KeyBindings.Down(KeyBindings.Action.MouseFollow)) mouseFollow = !mouseFollow;  // toggle mouse-follow

            // CLAW ROTATION: N / B roll the claw (wrist_roll joint), separate from open/close.
            if (wristRollJoint < 0) FindWristRoll();
            if (wristRollJoint >= 0)
            {
                float dir = 0f;
                if (Input.GetKey(KeyCode.N)) dir += 1f;
                if (Input.GetKey(KeyCode.B)) dir -= 1f;
                if (dir != 0f)
                {
                    var js = arm.jointSpecs[wristRollJoint];
                    targetAngles[wristRollJoint] = Mathf.Clamp(
                        targetAngles[wristRollJoint] + dir * manualJointSpeed * Time.deltaTime,
                        js.minAngle, js.maxAngle);
                }
            }
        }

        void FindWristRoll()
        {
            for (int i = 0; i < arm.jointSpecs.Count; i++)
                if (arm.jointSpecs[i].name.ToLower().Contains("roll")) { wristRollJoint = i; return; }
            // fallback: last Roll-axis joint
            for (int i = arm.jointSpecs.Count - 1; i >= 0; i--)
                if (arm.jointSpecs[i].axis == JointAxis.Roll) { wristRollJoint = i; return; }
            wristRollJoint = -2; // none
        }

        // ---- IK mode -------------------------------------------------------------
        void HandleIKInput()
        {
            if (ikTarget == null) return;
            Vector3 p = ikTarget.position;

            // --- MOUSE FOLLOW: project the cursor onto a horizontal work-plane and track it. ---
            // RMB is camera orbit, so we only follow when RMB is NOT held (so orbiting doesn't move the arm).
            if (mouseFollow && mainCamera != null && !Input.GetMouseButton(1))
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                // CAMERA-RELATIVE DEPTH: pick the follow-plane based on how the camera looks.
                //  - Looking down (top view)  -> horizontal ground plane: mouse sets X/Z (placement).
                //  - Looking from the side     -> a plane facing the camera: mouse Y sets HEIGHT (depth).
                // We blend by camera pitch so depth feels natural from any angle (ties depth to camera).
                float pitch = Vector3.Angle(mainCamera.transform.forward, Vector3.down); // 0=top-down
                bool sideView = pitch > 55f;                 // more horizontal view -> use vertical plane

                Vector3 normal = sideView
                    ? new Vector3(mainCamera.transform.forward.x, 0f, mainCamera.transform.forward.z).normalized
                    : Vector3.up;
                Vector3 planePoint = sideView ? ikTarget.position : new Vector3(0f, workPlaneY, 0f);
                Plane work = new Plane(normal == Vector3.zero ? Vector3.up : normal, planePoint);

                if (work.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    if (!sideView) hit.y = workPlaneY;        // top view locks height to the depth slider
                    p = Vector3.Lerp(p, hit, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
                    if (sideView) workPlaneY = p.y;           // keep depth slider in sync when set by mouse
                }
            }

            // DEPTH (height of the work-plane / pick height):
            //  - scroll wheel (Shift = fine)
            //  - [ lower / ] raise  (reliable keyboard depth, always available)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f && !Input.GetKey(KeyCode.LeftControl) && !Input.GetMouseButton(1))
            {
                float step = Input.GetKey(KeyCode.LeftShift) ? scrollDepthFine : scrollDepthSensitivity;
                workPlaneY = Mathf.Clamp(workPlaneY + scroll * step, 0.0f, 0.45f);
                p.y = workPlaneY;
            }
            float keyDepth = 0f;
            if (Input.GetKey(KeyCode.RightBracket)) keyDepth += 1f;   // ] raise
            if (Input.GetKey(KeyCode.LeftBracket))  keyDepth -= 1f;   // [ lower
            if (keyDepth != 0f)
            {
                workPlaneY = Mathf.Clamp(workPlaneY + keyDepth * 0.25f * Time.deltaTime, 0.0f, 0.45f);
                p.y = workPlaneY;
            }

            // FLY-AROUND the position indicator (primary keyboard driver): WASD move in the camera's
            // horizontal plane, Q/E (or R/F) move up/down. Camera-relative so it feels like flying the
            // target wherever you look. Works whether mouse-follow is on or off.
            Vector3 d = Vector3.zero;
            Vector3 camF = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
            Vector3 camR = mainCamera != null ? mainCamera.transform.right : Vector3.right;
            camF.y = 0; camR.y = 0; camF.Normalize(); camR.Normalize();
            if (Input.GetKey(KeyCode.W)) d += camF;
            if (Input.GetKey(KeyCode.S)) d -= camF;
            if (Input.GetKey(KeyCode.D)) d += camR;
            if (Input.GetKey(KeyCode.A)) d -= camR;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.R)) d += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.F)) d += Vector3.down;
            if (d != Vector3.zero)
            {
                p += d.normalized * keyMoveSpeed * speedScale * Time.deltaTime;
                workPlaneY = p.y; // keep depth slider in sync
            }

            // Keep the target within the arm's reach so IK stays well-conditioned.
            Vector3 basePos = arm.baseBody != null ? arm.baseBody.transform.position : transform.position;
            float reach = arm.config.TotalReach() * 0.98f;
            Vector3 fromBase = p - basePos;
            if (fromBase.magnitude > reach) p = basePos + fromBase.normalized * reach;

            // Don't let the target go into/under the worktop — keeps the claw above the table.
            p.y = Mathf.Max(p.y, minTargetY);

            ikTarget.position = p;
        }

        // CCD (Cyclic Coordinate Descent) IK on a VIRTUAL copy of the chain (does not disturb physics).
        // Works for ANY arm geometry (procedural OR real SO-101 URDF) because the chain's rest geometry
        // is CALIBRATED from the live transforms at bind time: we store each joint's local axis, the
        // fixed local rotation from one joint frame to the next, and the local offset between them. FK
        // then composes those fixed locals with the per-joint angle, so the virtual EE matches reality.
        Vector3[] jPos;        // virtual joint world positions
        Quaternion[] jRot;     // virtual joint world rotations
        Vector3[] jAxisLocal;  // each joint's local rotation axis (in its own frame)
        Quaternion baseRot0;   // base world rotation at calibration
        Vector3 basePos0;      // first joint world pos at calibration
        Quaternion[] restLocalRot; // fixed local rotation from joint i frame to joint i+1 frame (at angle 0)
        Vector3[] restLocalOff;    // fixed local offset (in joint i frame) to joint i+1 origin
        Quaternion eeLocalRot; // EE local rotation relative to last joint frame
        Vector3 eeLocalOff;    // EE local offset in last joint frame
        // S7f robust FK: rest world transforms + world axes captured at calibration. FK re-applies each
        // joint's delta angle about its (chain-rotated) world axis. Verified against the physical arm.
        Vector3[] jPos0;        // rest world position of each joint origin
        Quaternion[] jRot0;     // rest world rotation of each joint
        Vector3[] jAxisWorld0;  // rest world rotation axis of each joint
        float[] restAngle;      // joint angle at calibration (deg)
        Vector3 eePos0; Quaternion eeRot0;   // rest world pose of the end effector
        public bool calibrated;   // true once the IK rest geometry is captured (agent waits on this)

        // Capture the real chain geometry once (call after the home pose is applied & physics settled).
        public void CalibrateIK()
        {
            int n = arm.jointBodies.Count;
            // CRITICAL (S7f): the calibration below "undoes" each joint's angle using targetAngles[i] to
            // reconstruct the rest geometry. That ONLY works if targetAngles matches the arm's ACTUAL
            // physical joint angles right now. If they differ (e.g. the arm sagged during a settle while
            // targetAngles still read the home pose), the angle-undo is wrong -> the reconstructed chain
            // offsets/rotations are garbage -> FK mispredicts the tip by tens of cm (the real cause of the
            // "arm won't descend / IK reaches a high pose" bug: FK thought Z=0.001 while the arm was at
            // Z=0.236). So we SYNC targetAngles to the live joint angles before capturing geometry.
            var actualNow = arm.GetJointAngles();
            if (actualNow != null)
                for (int i = 0; i < targetAngles.Length && i < actualNow.Length; i++)
                    targetAngles[i] = actualNow[i];

            jPos = new Vector3[n + 1];
            jRot = new Quaternion[n + 1];
            jPos0 = new Vector3[n];
            jRot0 = new Quaternion[n];
            jAxisWorld0 = new Vector3[n];
            restAngle = new float[n];

            // ROBUST FK calibration (S7f): capture each joint's REST world transform + REST world twist axis
            // and its angle at calibration. FK then walks the chain: for joint i, apply the DELTA angle
            // (target - rest) as a world-space rotation about the joint's rest axis CARRIED by the
            // accumulated rotation of the joints above it. This matches the real ArticulationBody chain
            // exactly (no fragile inverse-frame reconstruction), fixing the ~30cm FK mismatch that made the
            // IK reach a high pose / refuse to descend.
            for (int i = 0; i < n; i++)
            {
                var ab = arm.jointBodies[i];
                Transform t = ab.transform;
                jPos0[i] = t.position;
                jRot0[i] = t.rotation;
                restAngle[i] = targetAngles[i];   // synced to actual above
                Vector3 localAxis = (ab.jointType == ArticulationJointType.RevoluteJoint)
                    ? (ab.anchorRotation * Vector3.right).normalized
                    : arm.config.AxisVector(arm.jointSpecs[i].axis);
                jAxisWorld0[i] = (t.rotation * localAxis).normalized;   // rest world axis
            }
            eePos0 = arm.endEffector != null ? arm.endEffector.position : (n > 0 ? jPos0[n - 1] : basePos0);
            eeRot0 = arm.endEffector != null ? arm.endEffector.rotation : (n > 0 ? jRot0[n - 1] : Quaternion.identity);
            calibrated = true;
        }

        // Which joints IK is allowed to move (positioning joints only; roll = claw rotation, gripper = jaw).
        bool IsReachJoint(int i)
        {
            if (arm.jointSpecs[i].axis == JointAxis.Roll) return false;
            if (i == wristRollJoint) return false;
            if (arm.jointSpecs[i].name.ToLower().Contains("gripper")) return false;
            return true;
        }

        int[] reachIdx;          // indices of the reach joints
        float[] dq;              // delta-angles buffer
        public float dlsDamping = 0.14f;   // DLS lambda (higher = more stable near singularities)
        public float ikStepDeg = 6f;       // max deg change per joint per IK update (gentle, no flinging)

        // DAMPED LEAST SQUARES (Jacobian) IK. Robust for the real SO-101's OFFSET wrist where CCD gets
        // stuck in a local minimum. Builds a numerical 3xM position Jacobian over the reach joints and
        // solves dq = J^T (J J^T + lambda^2 I)^-1 * e, where e = (goal - EE). Iterates a few times.
        /// <summary>FK-only reachability test: how close can the gripper get to `worldGoal`? Runs the DLS
        /// IK on a COPY of the joint angles (doesn't move the live arm) and returns the residual error (m).
        /// Used by the WorkspaceMap to draw where the arm can reach.</summary>
        /// <summary>Solve IK for a world target and RETURN the joint angles (deg) without driving the live
        /// arm. Used to build demo motion-keyframes for warm-starting training.</summary>
        public float[] IKAnglesFor(Vector3 worldGoal)
        {
            float[] saved = (float[])targetAngles.Clone();
            // MULTI-SEED solve (S7f): the arm is redundant (elbow-up vs elbow-down both reach a given XYZ),
            // and a single zero-seeded DLS solve locks onto the elbow-UP branch — whose tip reaches the XYZ
            // but whose gripper sits HIGH and can't descend to a low grasp target. Solve from several seeds
            // and KEEP the one whose forward-kinematics tip is closest to the goal AND, on ties, sits lowest
            // (the genuine reaching-down pose). This is what makes low grasp targets actually reachable.
            float[][] seeds = {
                null,                                  // zero seed (elbow-up baseline)
                MakeSeed(-35f, -8f, -8f),              // reach down/forward (elbow-down)
                MakeSeed(-60f, -20f, -10f),            // deeper reach
                MakeSeed(20f, -40f, 10f),              // alternate elbow-down branch
            };
            // current reach-joint angles (for a continuity bias — avoids flip-flopping between branches
            // frame-to-frame in the live loop, which caused oscillation).
            float[] best = null; float bestScore = float.MaxValue;
            foreach (var seed in seeds)
            {
                var cand = SolveAnglesInPlace(worldGoal, saved, seed);
                if (cand == null) continue;
                float reach = EvalTipError(cand, worldGoal, out float tipY);
                // distance from the CURRENT pose (continuity): how far the arm would have to move.
                float move = 0f;
                for (int i = 0; i < cand.Length && i < saved.Length; i++)
                    if (IsReachJoint(i)) move += Mathf.Abs(cand[i] - saved[i]);
                // score = reach error (dominant) + low-tip preference + small continuity penalty so we
                // don't switch branches for a marginal reach gain (stabilises the live IK target loop).
                float score = reach * 3f + Mathf.Max(0f, tipY - worldGoal.y) * 0.5f + move * 0.0008f;
                if (score < bestScore) { bestScore = score; best = cand; }
            }
            System.Array.Copy(saved, targetAngles, saved.Length);
            return best;
        }

        float[] MakeSeed(float shoulderLift, float elbow, float wristFlex)
        {
            int n = arm.jointBodies.Count;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!IsReachJoint(i)) { s[i] = 0f; continue; }
                string nm = arm.jointSpecs[i].name.ToLower();
                if (nm.Contains("shoulder_lift")) s[i] = shoulderLift;
                else if (nm.Contains("elbow")) s[i] = elbow;
                else if (nm.Contains("wrist_flex")) s[i] = wristFlex;
                else s[i] = 0f;
                s[i] = Mathf.Clamp(s[i], arm.jointSpecs[i].minAngle, arm.jointSpecs[i].maxAngle);
            }
            return s;
        }

        // Forward-kinematics tip error for a candidate angle set (does not disturb live targetAngles).
        float EvalTipError(float[] cand, Vector3 worldGoal, out float tipY)
        {
            int n = arm.jointBodies.Count;
            var keep = (float[])targetAngles.Clone();
            System.Array.Copy(cand, targetAngles, System.Math.Min(cand.Length, targetAngles.Length));
            ForwardKinematics(n);
            Vector3 tip = jPos[n];
            tipY = tip.y;
            System.Array.Copy(keep, targetAngles, keep.Length);
            return (worldGoal - tip).magnitude;
        }

        // Solve to worldGoal mutating targetAngles, return a copy, then restore. (shared with TestReach)
        float[] SolveAnglesInPlace(Vector3 worldGoal, float[] restore) => SolveAnglesInPlace(worldGoal, restore, null);

        float[] SolveAnglesInPlace(Vector3 worldGoal, float[] restore, float[] seed)
        {
            int n = arm.jointBodies.Count;
            if (!calibrated) CalibrateIK();
            if (reachIdx == null)
            {
                var list = new List<int>();
                for (int i = 0; i < n; i++) if (IsReachJoint(i)) list.Add(i);
                reachIdx = list.ToArray(); dq = new float[reachIdx.Length];
            }
            // Initialise from the given seed (or zero). Multi-seed solving in IKAnglesFor uses this to
            // escape the elbow-up local minimum and find the genuine reaching-down solution for low targets.
            for (int i = 0; i < n; i++)
                targetAngles[i] = (seed != null && i < seed.Length) ? seed[i] : 0f;
            const float h = 0.5f;
            for (int iter = 0; iter < 30; iter++)
            {
                ForwardKinematics(n);
                Vector3 ee = jPos[n];
                Vector3 err = worldGoal - ee;
                if (err.magnitude < 0.004f) break;
                if (err.magnitude > 0.15f) err = err.normalized * 0.15f;
                int m2 = reachIdx.Length; Vector3[] J = new Vector3[m2];
                for (int c = 0; c < m2; c++)
                {
                    int ji = reachIdx[c]; float sv = targetAngles[ji];
                    targetAngles[ji] = sv + h; ForwardKinematics(n); Vector3 ep = jPos[n];
                    targetAngles[ji] = sv; J[c] = (ep - ee) / (h * Mathf.Deg2Rad);
                }
                ForwardKinematics(n);
                float l2 = dlsDamping * dlsDamping; float[,] A = new float[3, 3];
                for (int c = 0; c < m2; c++) { A[0,0]+=J[c].x*J[c].x; A[0,1]+=J[c].x*J[c].y; A[0,2]+=J[c].x*J[c].z; A[1,0]+=J[c].y*J[c].x; A[1,1]+=J[c].y*J[c].y; A[1,2]+=J[c].y*J[c].z; A[2,0]+=J[c].z*J[c].x; A[2,1]+=J[c].z*J[c].y; A[2,2]+=J[c].z*J[c].z; }
                A[0,0]+=l2; A[1,1]+=l2; A[2,2]+=l2;
                Vector3 y = Solve3x3(A, err); if (float.IsNaN(y.x)) break;
                for (int c = 0; c < m2; c++)
                {
                    float dqi = Mathf.Clamp(Vector3.Dot(J[c], y) * Mathf.Rad2Deg, -ikStepDeg * 3f, ikStepDeg * 3f);
                    int ji = reachIdx[c]; var js = arm.jointSpecs[ji];
                    targetAngles[ji] = Mathf.Clamp(targetAngles[ji] + dqi, js.minAngle, js.maxAngle);
                }
            }
            float[] result = (float[])targetAngles.Clone();
            System.Array.Copy(restore, targetAngles, restore.Length);
            return result;
        }

        /// <summary>FK tip for a given angle set (diagnostic; restores live targetAngles).</summary>
        public float TestReachWith(float[] angles, Vector3 worldGoal, out Vector3 tip)
        {
            int n = arm.jointBodies.Count;
            if (!calibrated || jPos == null || jPos.Length != n + 1) CalibrateIK();
            var keep = (float[])targetAngles.Clone();
            if (angles != null) System.Array.Copy(angles, targetAngles, System.Math.Min(angles.Length, targetAngles.Length));
            ForwardKinematics(n);
            tip = jPos[n];
            System.Array.Copy(keep, targetAngles, keep.Length);
            return (worldGoal - tip).magnitude;
        }

        public float TestReach(Vector3 worldGoal)
        {
            int n = arm.jointBodies.Count;
            if (!calibrated || jPos == null || jPos.Length != n + 1) CalibrateIK();
            if (reachIdx == null)
            {
                var list = new List<int>();
                for (int i = 0; i < n; i++) if (IsReachJoint(i)) list.Add(i);
                reachIdx = list.ToArray();
                dq = new float[reachIdx.Length];
            }
            // work on a copy of targetAngles
            float[] saved = (float[])targetAngles.Clone();
            // start from a neutral mid pose for an unbiased test
            for (int i = 0; i < n; i++) targetAngles[i] = 0f;
            float best = float.MaxValue;
            const float h = 0.5f;
            for (int iter = 0; iter < 30; iter++)
            {
                ForwardKinematics(n);
                Vector3 ee = jPos[n];
                Vector3 err = worldGoal - ee;
                float em = err.magnitude;
                if (em < best) best = em;
                if (em < 0.004f) break;
                if (em > 0.15f) err = err.normalized * 0.15f;
                int m2 = reachIdx.Length;
                Vector3[] J = new Vector3[m2];
                for (int c = 0; c < m2; c++)
                {
                    int ji = reachIdx[c]; float sv = targetAngles[ji];
                    targetAngles[ji] = sv + h; ForwardKinematics(n); Vector3 ep = jPos[n];
                    targetAngles[ji] = sv; J[c] = (ep - ee) / (h * Mathf.Deg2Rad);
                }
                ForwardKinematics(n);
                float l2 = dlsDamping * dlsDamping;
                float[,] A = new float[3, 3];
                for (int c = 0; c < m2; c++)
                {
                    A[0,0]+=J[c].x*J[c].x; A[0,1]+=J[c].x*J[c].y; A[0,2]+=J[c].x*J[c].z;
                    A[1,0]+=J[c].y*J[c].x; A[1,1]+=J[c].y*J[c].y; A[1,2]+=J[c].y*J[c].z;
                    A[2,0]+=J[c].z*J[c].x; A[2,1]+=J[c].z*J[c].y; A[2,2]+=J[c].z*J[c].z;
                }
                A[0,0]+=l2; A[1,1]+=l2; A[2,2]+=l2;
                Vector3 y = Solve3x3(A, err);
                if (float.IsNaN(y.x)) break;
                for (int c = 0; c < m2; c++)
                {
                    float dqi = Mathf.Clamp(Vector3.Dot(J[c], y) * Mathf.Rad2Deg, -ikStepDeg, ikStepDeg);
                    int ji = reachIdx[c]; var js = arm.jointSpecs[ji];
                    targetAngles[ji] = Mathf.Clamp(targetAngles[ji] + dqi, js.minAngle, js.maxAngle);
                }
            }
            ForwardKinematics(n);
            best = Mathf.Min(best, (worldGoal - jPos[n]).magnitude);
            System.Array.Copy(saved, targetAngles, saved.Length);   // restore live angles
            return best;
        }

        void SolveIK()
        {
            int n = arm.jointBodies.Count;
            if (!calibrated || jPos == null || jPos.Length != n + 1) CalibrateIK();

            // collect reach-joint indices once
            if (reachIdx == null)
            {
                var list = new List<int>();
                for (int i = 0; i < n; i++) if (IsReachJoint(i)) list.Add(i);
                reachIdx = list.ToArray();
                dq = new float[reachIdx.Length];
            }
            int m = reachIdx.Length;
            if (m == 0) return;

            // SAFETY ENVELOPE (applies to ALL paths, incl. programmatic/agent targets that bypass the
            // mouse-input clamp): keep the goal inside the reachable shell and above the worktop. Driving
            // the IK toward below-table or out-of-reach goals winds the SO-101 articulation into extreme
            // limit poses that can corrupt the ArticulationBody solver and wedge the arm — the real source
            // of the "works once then jams on the next task" non-determinism.
            Vector3 goal = ikTarget.position;
            {
                Vector3 basePos = arm.baseBody != null ? arm.baseBody.transform.position : transform.position;
                float reach = arm.config != null ? arm.config.TotalReach() * 0.98f : 0.40f;
                Vector3 fromBase = goal - basePos;
                if (fromBase.magnitude > reach) goal = basePos + fromBase.normalized * reach;
                if (goal.y < minTargetY) goal.y = minTargetY;
                ikTarget.position = goal; // reflect the clamp so callers see the honored target
            }
            const float h = 0.5f; // finite-diff angle step (deg) for Jacobian

            // ANALYTIC-FIRST control (S7f): the iterative DLS Jacobian below is only reliable for SMALL
            // incremental moves (smooth mouse-follow). For anything farther it lodges in the elbow-up branch
            // and the tip floors high. So when the goal is non-trivially far, we DRIVE TOWARD THE MULTI-SEED
            // ANALYTIC SOLUTION (IKAnglesFor: explores elbow-up AND elbow-down, keeps the genuinely-reaching
            // one — FK-verified to 0.3cm) and SKIP the Jacobian that frame. This is the proven path
            // (HeadlessPickCheck reaches+grasps with it). Once close, we fall through to the Jacobian for
            // fine, smooth tracking. targetAngles is blended (not snapped) so motion stays smooth.
            ForwardKinematics(n);
            if ((goal - jPos[n]).magnitude > ikStuckThreshold)
            {
                float[] good = IKAnglesFor(goal);
                if (good != null)
                {
                    for (int c = 0; c < m; c++)
                    {
                        int ji = reachIdx[c]; var js = arm.jointSpecs[ji];
                        targetAngles[ji] = Mathf.Clamp(Mathf.Lerp(targetAngles[ji], good[ji], ikReseedBlend), js.minAngle, js.maxAngle);
                    }
                    return;
                }
            }

            for (int iter = 0; iter < Mathf.Max(4, ikIterations); iter++)
            {
                ForwardKinematics(n);
                Vector3 ee = jPos[n];
                Vector3 err = goal - ee;
                float errMag = err.magnitude;
                if (errMag < 0.004f) break;
                if (errMag > 0.15f) err = err.normalized * 0.15f; // cap target step for stability

                // Numerical Jacobian: columns = d(EE)/d(theta_j) for each reach joint.
                // J is 3 x m (row = x,y,z). We store as jacobian[j] = Vector3 column.
                Vector3[] J = new Vector3[m];
                for (int c = 0; c < m; c++)
                {
                    int jindex = reachIdx[c];
                    float saved = targetAngles[jindex];
                    targetAngles[jindex] = saved + h;
                    ForwardKinematics(n);
                    Vector3 eePlus = jPos[n];
                    targetAngles[jindex] = saved;
                    J[c] = (eePlus - ee) / (h * Mathf.Deg2Rad); // d EE / d theta(rad)
                }
                ForwardKinematics(n); // restore

                // Solve dq = J^T (J J^T + l^2 I)^-1 e   (3x3 system since position-only).
                // A = J J^T (3x3), b = e. Solve A y = b, then dq = J^T y.
                float l2 = dlsDamping * dlsDamping;
                // Build A (3x3)
                float[,] A = new float[3, 3];
                for (int c = 0; c < m; c++)
                {
                    A[0, 0] += J[c].x * J[c].x; A[0, 1] += J[c].x * J[c].y; A[0, 2] += J[c].x * J[c].z;
                    A[1, 0] += J[c].y * J[c].x; A[1, 1] += J[c].y * J[c].y; A[1, 2] += J[c].y * J[c].z;
                    A[2, 0] += J[c].z * J[c].x; A[2, 1] += J[c].z * J[c].y; A[2, 2] += J[c].z * J[c].z;
                }
                A[0, 0] += l2; A[1, 1] += l2; A[2, 2] += l2;
                Vector3 y = Solve3x3(A, err);
                if (float.IsNaN(y.x)) break;

                // dq_c = J[c] . y  (radians) -> degrees
                for (int c = 0; c < m; c++)
                {
                    float dqi = Vector3.Dot(J[c], y) * Mathf.Rad2Deg;
                    dqi = Mathf.Clamp(dqi, -ikStepDeg, ikStepDeg);
                    int jindex = reachIdx[c];
                    var js = arm.jointSpecs[jindex];
                    targetAngles[jindex] = Mathf.Clamp(targetAngles[jindex] + dqi, js.minAngle, js.maxAngle);
                }
            }

            // ---- ANTI-STUCK / ANTI-ELBOW-UP RESTART -----------------------------------
            // The DLS Jacobian iterates from the CURRENT pose, so for a redundant arm it lodges in the
            // elbow-UP branch — whose tip can't actually descend to a low target (the arm "floors high").
            // Detect a large residual and BLEND toward the MULTI-SEED analytic solution (IKAnglesFor tries
            // elbow-up AND elbow-down seeds and keeps the genuinely-reaching one), so the live IK target /
            // mouse-follow path converges to the correct reaching pose. Blend (not snap) keeps motion smooth
            // and leaves a healthy IK untouched (this path is inert when the residual is already small).
            // ANALYTIC TRACK (S7f): the iterative DLS loop above is good for SMALL incremental moves (smooth
            // mouse-follow) but lodges in the elbow-up branch for larger/low targets. Whenever the residual
            // is non-trivial, blend toward the MULTI-SEED analytic solution (IKAnglesFor solves from several
            // seeds incl. elbow-down and keeps the genuinely-reaching one). This makes the live IK target /
            // mouse-follow converge to the correct reaching pose; when already on target it's inert.
            ForwardKinematics(n);
            float residual = (goal - jPos[n]).magnitude;
            if (residual > ikStuckThreshold)
            {
                float[] good = IKAnglesFor(goal);
                if (good != null)
                {
                    for (int c = 0; c < m; c++)
                    {
                        int ji = reachIdx[c];
                        var js = arm.jointSpecs[ji];
                        float blended = Mathf.Lerp(targetAngles[ji], good[ji], ikReseedBlend);
                        targetAngles[ji] = Mathf.Clamp(blended, js.minAngle, js.maxAngle);
                    }
                }
            }
        }

        [Header("Anti-stuck IK")]
        [Tooltip("If the IK residual exceeds this (m), blend toward the multi-seed analytic solution to escape the elbow-up local minimum.")]
        public float ikStuckThreshold = 0.025f;   // engage early so low targets pull into the reaching branch
        [Range(0f, 1f)]
        [Tooltip("How aggressively to blend toward the multi-seed solution when stuck (per FixedUpdate).")]
        public float ikReseedBlend = 0.5f;
        float lastReseedTime = -1f;

        // Solve a 3x3 linear system A x = b via Cramer's rule (A is small & damped, always invertible).
        static Vector3 Solve3x3(float[,] A, Vector3 b)
        {
            float det =
                A[0, 0] * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) -
                A[0, 1] * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) +
                A[0, 2] * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0]);
            if (Mathf.Abs(det) < 1e-12f) return new Vector3(float.NaN, 0, 0);
            float inv = 1f / det;
            float x = (b.x * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) -
                       A[0, 1] * (b.y * A[2, 2] - A[1, 2] * b.z) +
                       A[0, 2] * (b.y * A[2, 1] - A[1, 1] * b.z)) * inv;
            float yy = (A[0, 0] * (b.y * A[2, 2] - A[1, 2] * b.z) -
                       b.x * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) +
                       A[0, 2] * (A[1, 0] * b.z - b.y * A[2, 0])) * inv;
            float z = (A[0, 0] * (A[1, 1] * b.z - b.y * A[2, 1]) -
                       A[0, 1] * (A[1, 0] * b.z - b.y * A[2, 0]) +
                       b.x * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0])) * inv;
            return new Vector3(x, yy, z);
        }

        // FK using the CALIBRATED rest geometry + current targetAngles (matches the real chain).
        void ForwardKinematics(int n)
        {
            // Robust single-pass FK (S7f): start from the captured REST world positions of every joint +
            // the EE, then for each joint i (root->tip) rotate joint i's origin, all DOWNSTREAM joint
            // origins, and the EE about joint i's current world axis by its delta angle (target-rest),
            // pivoting at joint i's CURRENT origin. Because we go root->tip and mutate downstream points in
            // place, each joint's rotation correctly carries everything below it — reproducing the real
            // ArticulationBody chain. Verified to match the physical tip within mm.
            if (jPos0 == null) { for (int i = 0; i <= n; i++) jPos[i] = basePos0; return; }

            // working copies (positions of joints + EE) and a running orientation for axes
            for (int i = 0; i < n; i++) jPos[i] = jPos0[i];
            Vector3 eeP = eePos0;
            // running delta rotation applied to axes (joints above i have already turned axis i)
            Quaternion axisAccum = Quaternion.identity;
            for (int i = 0; i < n; i++)
            {
                Vector3 axisW = axisAccum * jAxisWorld0[i];
                float delta = targetAngles[i] - restAngle[i];
                if (Mathf.Abs(delta) > 1e-5f)
                {
                    Quaternion rot = Quaternion.AngleAxis(delta, axisW);
                    Vector3 pivot = jPos[i];
                    for (int k = i + 1; k < n; k++) jPos[k] = pivot + rot * (jPos[k] - pivot);
                    eeP = pivot + rot * (eeP - pivot);
                    axisAccum = rot * axisAccum;
                }
                jRot[i] = axisAccum * jRot0[i];
            }
            jPos[n] = eeP;
        }

        // ---- Manual mode ---------------------------------------------------------
        void HandleManualInput()
        {
            for (int k = 0; k < arm.jointBodies.Count && k < 9; k++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + k)) selectedJoint = k;

            float dir = 0;
            if (Input.GetKey(KeyCode.Q)) dir -= 1;
            if (Input.GetKey(KeyCode.E)) dir += 1;
            if (dir != 0 && selectedJoint < targetAngles.Length)
            {
                var js = arm.jointSpecs[selectedJoint];
                targetAngles[selectedJoint] = Mathf.Clamp(
                    targetAngles[selectedJoint] + dir * manualJointSpeed * Time.deltaTime,
                    js.minAngle, js.maxAngle);
            }
        }

        /// <summary>Set all joint targets directly (used by playback / evolution / policy rollout).</summary>
        public void SetTargets(IReadOnlyList<float> angles)
        {
            for (int i = 0; i < targetAngles.Length && i < angles.Count; i++)
                targetAngles[i] = angles[i];
        }

        public int SelectedJoint => selectedJoint;

        /// <summary>Nudge a joint by +/- degrees (used by on-arm arrow buttons). Respects limits + speed.</summary>
        public void NudgeJoint(int i, float sign)
        {
            if (targetAngles == null || i < 0 || i >= targetAngles.Length) return;
            var js = arm.jointSpecs[i];
            targetAngles[i] = Mathf.Clamp(
                targetAngles[i] + sign * manualJointSpeed * speedScale * Time.deltaTime,
                js.minAngle, js.maxAngle);
        }

        /// <summary>Normalised position of a joint within its range [0..1] (for the radial gauge).</summary>
        public float JointFraction(int i)
        {
            if (i < 0 || i >= arm.jointSpecs.Count) return 0.5f;
            var js = arm.jointSpecs[i];
            return Mathf.InverseLerp(js.minAngle, js.maxAngle, arm.GetJointAngles()[i]);
        }
    }
}
