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
        public float keyMoveSpeed = 0.25f;     // m/s for WASD target nudge
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
            float[] home4 = { 0f, 40f, -78f, -5f };                       // procedural: yaw,shoulder,elbow,wrist
            float[] home6 = { 0f, -40f, -30f, -15f, 0f, 0f };             // SO-101: pan,lift,elbow,wristflex,roll,grip
            float[] home = arm.jointBodies.Count >= 6 ? home6 : home4;
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < home.Length
                    ? Mathf.Clamp(home[i], arm.jointSpecs[i].minAngle, arm.jointSpecs[i].maxAngle)
                    : 0f;
            arm.SeedServoState(targetAngles);   // start servo rate-limiter at the home pose
            arm.SetJointTargets(targetAngles);

            if (ikTarget != null)
                ikTarget.position = new Vector3(0.0f, 0.12f, 0.30f); // in front of the arm, above the table
        }

        void Update()
        {
            if (arm == null || arm.jointBodies.Count == 0) return;
            HandleModeToggle();
            HandleGripper();
            if (mode == Mode.IK) HandleIKInput();
            else HandleManualInput();
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
                if (settleFrames == 30) CalibrateIK();
                return;
            }
            if (mode == Mode.IK) SolveIK();
            arm.SetJointTargets(targetAngles);
        }

        void HandleModeToggle()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                mode = mode == Mode.IK ? Mode.Manual : Mode.IK;
        }

        void HandleGripper()
        {
            if (arm.gripper == null) return;
            if (Input.GetKeyDown(KeyCode.Space)) arm.gripper.Toggle();
            // Comma = open, Period = close (hold to actuate continuously).
            if (Input.GetKey(KeyCode.Comma))  arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 0f, 3f * Time.deltaTime));
            if (Input.GetKey(KeyCode.Period)) arm.gripper.SetClose(Mathf.MoveTowards(arm.gripper.closeAmount, 1f, 3f * Time.deltaTime));
            if (Input.GetKeyDown(KeyCode.M)) mouseFollow = !mouseFollow;  // toggle mouse-follow
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
                Plane work = new Plane(Vector3.up, new Vector3(0f, workPlaneY, 0f));
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                if (work.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    // smooth toward the cursor point
                    p = Vector3.Lerp(p, hit, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
                }
            }

            // Scroll (without Ctrl, which is zoom) raises/lowers the work-plane height (pick depth).
            // More sensitive depth control for a better 3D dimension when reaching up/down.
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f && !Input.GetKey(KeyCode.LeftControl) && !Input.GetMouseButton(1))
            {
                // Shift = fine (precise), default = responsive depth.
                float step = Input.GetKey(KeyCode.LeftShift) ? scrollDepthFine : scrollDepthSensitivity;
                workPlaneY = Mathf.Clamp(workPlaneY + scroll * step, 0.0f, 0.45f);
                p.y = workPlaneY;
            }

            // Keyboard fine-nudge (works alongside mouse follow).
            Vector3 d = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) d += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) d += Vector3.back;
            if (Input.GetKey(KeyCode.A)) d += Vector3.left;
            if (Input.GetKey(KeyCode.D)) d += Vector3.right;
            if (Input.GetKey(KeyCode.R)) d += Vector3.up;
            if (Input.GetKey(KeyCode.F)) d += Vector3.down;
            p += d.normalized * keyMoveSpeed * Time.deltaTime;

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

            for (int i = 0; i < n; i++)
                jAxisLocal[i] = arm.config.AxisVector(arm.jointSpecs[i].axis);

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

        void SolveIK()
        {
            int n = arm.jointBodies.Count;
            if (!calibrated || jPos == null || jPos.Length != n + 1) CalibrateIK();

            Vector3 goal = ikTarget.position;
            for (int iter = 0; iter < ikIterations; iter++)
            {
                ForwardKinematics(n);
                bool improved = false;
                for (int i = n - 1; i >= 0; i--)
                {
                    Vector3 jp = jPos[i];
                    Vector3 axis = (jRot[i] * jAxisLocal[i]).normalized;
                    Vector3 ee = jPos[n];

                    Vector3 toEE = Vector3.ProjectOnPlane(ee - jp, axis);
                    Vector3 toGoal = Vector3.ProjectOnPlane(goal - jp, axis);
                    if (toEE.sqrMagnitude < 1e-7f || toGoal.sqrMagnitude < 1e-7f) continue;

                    float delta = Vector3.SignedAngle(toEE, toGoal, axis) * 0.6f; // damping
                    var js = arm.jointSpecs[i];
                    float na = Mathf.Clamp(targetAngles[i] + delta, js.minAngle, js.maxAngle);
                    if (Mathf.Abs(na - targetAngles[i]) > 0.01f) improved = true;
                    targetAngles[i] = na;
                    ForwardKinematics(n);
                }
                if (!improved) break;
                if (Vector3.Distance(jPos[n], goal) < 0.005f) break;
            }
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
