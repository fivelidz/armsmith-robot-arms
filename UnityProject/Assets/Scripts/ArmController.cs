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
            float[] home = { 0f, 48f, -88f, -20f };
            for (int i = 0; i < targetAngles.Length; i++)
                targetAngles[i] = i < home.Length
                    ? Mathf.Clamp(home[i], arm.jointSpecs[i].minAngle, arm.jointSpecs[i].maxAngle)
                    : 0f;
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
            if (Input.GetKeyDown(KeyCode.Space) && arm.gripper != null)
                arm.gripper.Toggle();
        }

        // ---- IK mode -------------------------------------------------------------
        void HandleIKInput()
        {
            if (ikTarget == null) return;
            Vector3 p = ikTarget.position;

            // Keyboard nudge in world axes (W/S forward-back along Z, A/D along X, R/F up-down)
            Vector3 d = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) d += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) d += Vector3.back;
            if (Input.GetKey(KeyCode.A)) d += Vector3.left;
            if (Input.GetKey(KeyCode.D)) d += Vector3.right;
            if (Input.GetKey(KeyCode.R)) d += Vector3.up;
            if (Input.GetKey(KeyCode.F)) d += Vector3.down;
            p += d.normalized * keyMoveSpeed * Time.deltaTime;

            // Mouse drag (LMB) moves target on a plane facing the camera; scroll = depth.
            if (Input.GetMouseButton(0) && mainCamera != null)
            {
                Plane plane = new Plane(-mainCamera.transform.forward, ikTarget.position);
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                if (plane.Raycast(ray, out float enter))
                    p = ray.GetPoint(enter);
            }
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f && mainCamera != null)
                p += mainCamera.transform.forward * scroll * 0.5f;

            ikTarget.position = p;
        }

        void SolveIK()
        {
            int n = arm.jointBodies.Count;
            pts.Clear(); lens.Clear();

            // Build the current chain in world space from joint origins -> end effector.
            for (int i = 0; i < n; i++) pts.Add(arm.jointBodies[i].transform.position);
            pts.Add(arm.endEffector != null ? arm.endEffector.position
                                            : arm.jointBodies[n - 1].transform.position);
            for (int i = 0; i < pts.Count - 1; i++)
                lens.Add(Vector3.Distance(pts[i], pts[i + 1]));

            FabrikIK.Solve(pts, lens, ikTarget.position, ikIterations, 0.002f);

            // Convert solved point directions -> per-joint angle about that joint's axis.
            // We compute the signed angle between the current link dir and the solved link dir,
            // projected onto the joint's world rotation axis, and accumulate onto the target.
            for (int i = 0; i < n; i++)
            {
                var ab = arm.jointBodies[i];
                Vector3 axisWorld = ab.transform.TransformDirection(
                    arm.config.AxisVector(arm.jointSpecs[i].axis)).normalized;

                Vector3 curDir = (pts.Count > i + 1)
                    ? (arm.endEffector != null && i == n - 1
                        ? (arm.endEffector.position - ab.transform.position)
                        : (arm.jointBodies[Mathf.Min(i + 1, n - 1)].transform.position - ab.transform.position))
                    : ab.transform.up;
                Vector3 solvedDir = pts[i + 1] - pts[i];

                // project onto plane perpendicular to axis
                curDir = Vector3.ProjectOnPlane(curDir, axisWorld);
                solvedDir = Vector3.ProjectOnPlane(solvedDir, axisWorld);
                if (curDir.sqrMagnitude < 1e-6f || solvedDir.sqrMagnitude < 1e-6f) continue;

                float delta = Vector3.SignedAngle(curDir, solvedDir, axisWorld);
                var js = arm.jointSpecs[i];
                targetAngles[i] = Mathf.Clamp(targetAngles[i] + delta, js.minAngle, js.maxAngle);
            }
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
