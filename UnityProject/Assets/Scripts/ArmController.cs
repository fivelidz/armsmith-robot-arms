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

            // Natural "ready" pose: bend shoulder forward and elbow down so the gripper hovers
            // over the worktop in reach of the trays, instead of standing bolt upright.
            // Joints (starter): 0=BaseYaw, 1=Shoulder(pitch), 2=Elbow(pitch), 3=Wrist(pitch).
            // Tuned so the gripper hovers ~5-8 cm ABOVE the worktop (y=0), not into it.
            float[] home = { 0f, 40f, -78f, -5f };
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < home.Length
                    ? Mathf.Clamp(home[i], arm.jointSpecs[i].minAngle, arm.jointSpecs[i].maxAngle)
                    : 0f;
            arm.SeedServoState(targetAngles);   // start servo rate-limiter at the home pose
            arm.SetJointTargets(targetAngles);

            if (ikTarget != null)
                ikTarget.position = new Vector3(0.18f, 0.10f, 0.34f); // above Tray A
        }

        void Update()
        {
            if (arm == null || arm.jointBodies.Count == 0) return;
            HandleModeToggle();
            HandleGripper();
            if (mode == Mode.IK) HandleIKInput();
            else HandleManualInput();
        }

        void FixedUpdate()
        {
            if (arm == null || targetAngles == null) return;
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
        // We forward-simulate joint frames from the live base using the current targetAngles, run CCD to
        // update those angles toward the goal, and only write the resulting angle TARGETS to the drives
        // (via SetJointTargets in FixedUpdate). Robust: reaches forward instead of folding down.
        Vector3[] jPos;       // virtual joint world positions
        Quaternion[] jRot;    // virtual joint world rotations
        Vector3[] jAxisLocal; // each joint's local rotation axis

        void SolveIK()
        {
            int n = arm.jointBodies.Count;
            if (jPos == null || jPos.Length != n + 1)
            {
                jPos = new Vector3[n + 1];
                jRot = new Quaternion[n + 1];
                jAxisLocal = new Vector3[n];
                for (int i = 0; i < n; i++) jAxisLocal[i] = arm.config.AxisVector(arm.jointSpecs[i].axis);
            }

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
                    ForwardKinematics(n); // re-evaluate chain after this joint moved
                }
                if (!improved) break;
                if (Vector3.Distance(jPos[n], goal) < 0.005f) break;
            }
        }

        // Build virtual joint frames from the live base using current targetAngles + the link offsets.
        void ForwardKinematics(int n)
        {
            // Root: base body's top (first joint's parent frame).
            Transform baseT = arm.baseBody.transform;
            Quaternion rot = baseT.rotation;
            Vector3 pos = arm.jointBodies[0].transform.position; // first joint origin (stable)

            for (int i = 0; i < n; i++)
            {
                jPos[i] = pos;
                // apply this joint's rotation about its local axis by targetAngles[i]
                rot = rot * Quaternion.AngleAxis(targetAngles[i], jAxisLocal[i]);
                jRot[i] = rot;
                // advance along the link (local +Y by link length) to the next joint
                float len = arm.jointSpecs[i].linkLength;
                pos = pos + rot * (Vector3.up * len);
            }
            // end-effector tip (gripper offset ~ palm + finger)
            jPos[n] = pos + rot * (Vector3.up * (0.02f + arm.config.gripperLength));
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
