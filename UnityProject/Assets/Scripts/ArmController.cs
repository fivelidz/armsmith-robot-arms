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
            float[] home4 = { 0f, 0f, 0f, 0f };
            float[] home6 = { 0f, 0f, 0f, 0f, 0f, 0f };
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

        void HandleModeToggle()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                mode = mode == Mode.IK ? Mode.Manual : Mode.IK;
        }

        public int wristRollJoint = -1;   // index of the claw-rotation joint (auto-found: "wrist_roll" / last Roll)

        void HandleGripper()
        {
            if (arm.gripper == null) return;
            if (Input.GetKeyDown(KeyCode.Space)) arm.gripper.Toggle();
            // Comma = open, Period = close (hold to actuate continuously).
            if (Input.GetKey(KeyCode.Comma))  arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 0f, 3f * Time.deltaTime));
            if (Input.GetKey(KeyCode.Period)) arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 1f, 3f * Time.deltaTime));
            if (Input.GetKeyDown(KeyCode.M)) mouseFollow = !mouseFollow;  // toggle mouse-follow

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
        bool calibrated;

        // Capture the real chain geometry once (call after the home pose is applied & physics settled).
        public void CalibrateIK()
        {
            int n = arm.jointBodies.Count;
            jPos = new Vector3[n + 1];
            jRot = new Quaternion[n + 1];
            jAxisLocal = new Vector3[n];
            restLocalRot = new Quaternion[n];
            restLocalOff = new Vector3[n];

            // Use the REAL revolute twist axis of each ArticulationBody (local frame), not the simplified
            // config enum. For a revolute AB the twist axis is anchorRotation * +X (Unity drive axis).
            // This makes FK match the real SO-101 chain (the enum axes were wrong -> bad FK -> bad IK).
            for (int i = 0; i < n; i++)
            {
                var ab = arm.jointBodies[i];
                if (ab.jointType == ArticulationJointType.RevoluteJoint)
                    jAxisLocal[i] = (ab.anchorRotation * Vector3.right).normalized;
                else
                    jAxisLocal[i] = arm.config.AxisVector(arm.jointSpecs[i].axis);
            }

            baseRot0 = arm.jointBodies[0].transform.rotation
                       * Quaternion.Inverse(Quaternion.AngleAxis(targetAngles[0], jAxisLocal[0]));
            basePos0 = arm.jointBodies[0].transform.position;

            // For each consecutive pair, record the local transform (undoing the current joint angles)
            // so FK can re-apply arbitrary angles.
            for (int i = 0; i < n; i++)
            {
                Transform a = arm.jointBodies[i].transform;
                // frame of joint i WITHOUT its own angle applied:
                Quaternion aFrame0 = a.rotation * Quaternion.Inverse(Quaternion.AngleAxis(targetAngles[i], jAxisLocal[i]));
                Transform b = (i + 1 < n) ? arm.jointBodies[i + 1].transform
                                          : (arm.endEffector != null ? arm.endEffector : a);
                Vector3 worldOff = b.position - a.position;
                restLocalOff[i] = Quaternion.Inverse(aFrame0) * worldOff;
                restLocalRot[i] = Quaternion.Inverse(aFrame0) * (b.rotation
                                   * (i + 1 < n ? Quaternion.Inverse(Quaternion.AngleAxis(targetAngles[i + 1], jAxisLocal[i + 1])) : Quaternion.identity));
            }
            // EE relative to last joint frame (without last joint's angle)
            if (arm.endEffector != null && n > 0)
            {
                Transform last = arm.jointBodies[n - 1].transform;
                Quaternion lastFrame0 = last.rotation * Quaternion.Inverse(Quaternion.AngleAxis(targetAngles[n - 1], jAxisLocal[n - 1]));
                eeLocalOff = Quaternion.Inverse(lastFrame0) * (arm.endEffector.position - last.position);
                eeLocalRot = Quaternion.Inverse(lastFrame0) * arm.endEffector.rotation;
            }
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
        public float dlsDamping = 0.08f;   // DLS lambda (higher = more stable, slower)
        public float ikStepDeg = 18f;      // max deg change per joint per IK update (smooth)

        // DAMPED LEAST SQUARES (Jacobian) IK. Robust for the real SO-101's OFFSET wrist where CCD gets
        // stuck in a local minimum. Builds a numerical 3xM position Jacobian over the reach joints and
        // solves dq = J^T (J J^T + lambda^2 I)^-1 * e, where e = (goal - EE). Iterates a few times.
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

            Vector3 goal = ikTarget.position;
            const float h = 0.5f; // finite-diff angle step (deg) for Jacobian

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
        }

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
            Quaternion rot = baseRot0;
            Vector3 pos = basePos0;
            for (int i = 0; i < n; i++)
            {
                jPos[i] = pos;
                rot = rot * Quaternion.AngleAxis(targetAngles[i], jAxisLocal[i]); // apply joint angle
                jRot[i] = rot;
                pos = pos + rot * restLocalOff[i];        // step to next joint origin (real offset)
                rot = rot * restLocalRot[i];              // and into next joint's frame (real twist)
            }
            jPos[n] = pos; // last computed pos is the EE (restLocalOff[n-1] already points to EE/endEffector)
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
    }
}
