using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Builds a physical robot arm from an ArmConfig using Unity ArticulationBody
    /// (reduced-coordinate jointed physics = stable, accurate, no drift). Meshes are
    /// procedural (capsule links, sphere joints, box gripper) so morphology is just numbers
    /// and the whole arm can be regenerated live from the Designer UI or the evolution layer.
    ///
    /// Hierarchy created:
    ///   Arm (this) -> Base (fixed ArticulationBody)
    ///                  -> Joint0 (revolute) -> Joint1 -> ... -> Wrist
    ///                                                            -> Gripper (LeftJaw, RightJaw)
    /// </summary>
    public partial class ProceduralArm : MonoBehaviour
    {
        public ArmConfig config;
        public Material linkMaterial;
        public Material jointMaterial;
        public Material gripperMaterial;

        // ── STL mesh skin (opt-in) ─────────────────────────────────────────────────
        [Tooltip("When true, Build() replaces procedural visuals with real SO-ARM100 STL meshes.")]
        public bool useStlMeshes = false;

        [Tooltip("Absolute path to the folder containing the .stl files. " +
                 "Leave empty to use Application.dataPath/Meshes/SOARM100/")]
        public string stlMeshDir = "";
        // ──────────────────────────────────────────────────────────────────────────

        public ArticulationBody baseBody;
        public readonly List<ArticulationBody> jointBodies = new List<ArticulationBody>();
        public readonly List<JointSpec> jointSpecs = new List<JointSpec>();

        // Digital twin: one servo model per joint. Commands are rate-limited and tick-quantised like
        // real STS3215 servos, so what the arm does in-game == what the real motor would do.
        public readonly List<ServoModel> servos = new List<ServoModel>();
        public bool servoFidelity = true;          // route commands through the servo model
        float[] servoCommandedDeg;                 // last rate-limited command per joint (deg)

        // Consistent per-servo colour used everywhere (on-arm hotspots, panels, callouts, gauges).
        static readonly Color[] ServoPalette = {
            new Color(0.95f,0.30f,0.30f), // red    - J0
            new Color(0.98f,0.62f,0.20f), // orange - J1
            new Color(0.95f,0.85f,0.25f), // yellow - J2
            new Color(0.35f,0.80f,0.40f), // green  - J3
            new Color(0.30f,0.65f,0.95f), // blue   - J4
            new Color(0.70f,0.45f,0.95f), // purple - J5
            new Color(0.95f,0.45f,0.75f), // pink   - J6
        };
        public static Color ServoColor(int i) => ServoPalette[i % ServoPalette.Length];
        public Transform endEffector;        // tip point between the jaws
        public ArticulationBody leftJaw, rightJaw;

        public Gripper gripper;

        public void Build(ArmConfig cfg)
        {
            if (cfg == null || cfg.joints == null || cfg.joints.Count == 0)
                cfg = ArmConfig.CreateStarter();
            config = cfg;
            Clear();

            EnsureMaterials();

            // --- Base (fixed root articulation body) -------------------------------
            var baseGo = new GameObject("Base");
            baseGo.transform.SetParent(transform, false);
            baseBody = baseGo.AddComponent<ArticulationBody>();
            baseBody.immovable = true;
            AddCylinderVisual(baseGo.transform, cfg.baseRadius, cfg.baseHeight, Vector3.up * (cfg.baseHeight * 0.5f), jointMaterial);
            AddCapsuleCollider(baseGo, cfg.baseRadius, cfg.baseHeight, Vector3.up * (cfg.baseHeight * 0.5f));

            Transform parent = baseGo.transform;
            Vector3 localAttach = Vector3.up * cfg.baseHeight; // top of base, in base-local space

            jointBodies.Clear();
            jointSpecs.Clear();

            // --- Joints + links ----------------------------------------------------
            for (int i = 0; i < cfg.joints.Count; i++)
            {
                JointSpec js = cfg.joints[i];
                var go = new GameObject(js.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localAttach;

                var ab = go.AddComponent<ArticulationBody>();
                ConfigureRevolute(ab, js, cfg.AxisVector(js.axis));

                // Visuals: sphere at joint, capsule for the link going +Y (local up = link dir).
                AddSphereVisual(go.transform, js.linkRadius * 1.4f, Vector3.zero, jointMaterial);
                float len = Mathf.Max(0.001f, js.linkLength);
                Vector3 linkCenter = Vector3.up * (len * 0.5f);
                AddCylinderVisual(go.transform, js.linkRadius, len, linkCenter, linkMaterial);
                AddCapsuleCollider(go, js.linkRadius, len, linkCenter);

                jointBodies.Add(ab);
                jointSpecs.Add(js);

                // One servo per joint (digital twin). ID 1..N maps to the real bus.
                servos.Add(new ServoModel
                {
                    servoId = i + 1,
                    minDeg = js.minAngle, maxDeg = js.maxAngle,
                    maxTorqueNm = Mathf.Max(0.5f, js.maxTorque * 0.05f),
                    maxSpeedDegPerSec = 300f
                });

                parent = go.transform;
                localAttach = Vector3.up * len; // next joint sits at the end of this link
            }

            servoCommandedDeg = new float[jointBodies.Count];

            // --- Gripper -----------------------------------------------------------
            var gripGo = new GameObject("Gripper");
            gripGo.transform.SetParent(parent, false);
            gripGo.transform.localPosition = localAttach;

            // palm (with collider so the gripper body itself collides with the table/objects)
            Vector3 palmSize = new Vector3(cfg.gripperWidth + 0.02f, 0.02f, cfg.gripperWidth + 0.02f);
            Vector3 palmCenter = Vector3.up * 0.01f;
            AddBoxVisual(gripGo.transform, palmSize, palmCenter, gripperMaterial);
            var palmCol = gripGo.AddComponent<BoxCollider>();
            palmCol.size = palmSize; palmCol.center = palmCenter;

            float jawHalf = cfg.gripperWidth * 0.5f;
            leftJaw  = BuildJaw(gripGo.transform, "LeftJaw",  -jawHalf, cfg);
            rightJaw = BuildJaw(gripGo.transform, "RightJaw", +jawHalf, cfg);

            // end-effector reference point (between jaws, at finger tip)
            var ee = new GameObject("EndEffector");
            ee.transform.SetParent(gripGo.transform, false);
            ee.transform.localPosition = Vector3.up * (0.02f + cfg.gripperLength * 0.5f);
            endEffector = ee.transform;

            gripper = gripGo.AddComponent<Gripper>();
            gripper.Init(this, leftJaw, rightJaw, cfg.gripperWidth, jawHalf);

            gameObject.name = string.IsNullOrEmpty(cfg.armName) ? "Arm" : cfg.armName;

            // ── STL mesh skin (opt-in) ─────────────────────────────────────────────
            // Applied AFTER the full physics hierarchy is built so it can safely
            // iterate jointBodies / leftJaw / rightJaw.  Physics, servos, and IK are
            // completely unaffected — only visual MeshRenderers are swapped.
            if (useStlMeshes)
                StlArmSkin.Apply(this, stlMeshDir);
            // ──────────────────────────────────────────────────────────────────────
        }

        ArticulationBody BuildJaw(Transform palm, string name, float xOffset, ArmConfig cfg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(palm, false);
            go.transform.localPosition = new Vector3(xOffset, 0.02f, 0f);
            var ab = go.AddComponent<ArticulationBody>();

            // Prismatic jaw sliding along the gripper's LOCAL X (sideways open/close).
            // anchorRotation identity => the prismatic free axis (X) is the jaw's local X (horizontal,
            // across the gripper), fixing the "slides vertically" bug. matchAnchors stays true so the
            // drive TARGET is the jaw's signed DISPLACEMENT from its build position along X.
            ab.jointType = ArticulationJointType.PrismaticJoint;
            ab.anchorRotation = Quaternion.identity;
            ab.linearLockX = ArticulationDofLock.LimitedMotion;
            ab.linearLockY = ArticulationDofLock.LockedMotion;
            ab.linearLockZ = ArticulationDofLock.LockedMotion;
            var drive = ab.xDrive;
            // Each jaw can travel inward up to ~gripperWidth (toward and past centre) and outward a bit.
            drive.lowerLimit = -cfg.gripperWidth;
            drive.upperLimit =  cfg.gripperWidth;
            drive.stiffness = 9000f;
            drive.damping = 150f;
            drive.forceLimit = 80f;     // enough to clamp the cube, not bulldoze it through the table
            ab.xDrive = drive;
            ab.mass = 0.03f;

            Vector3 fingerSize = new Vector3(0.012f, cfg.gripperLength, 0.03f);
            Vector3 fingerCenter = Vector3.up * (cfg.gripperLength * 0.5f);
            AddBoxVisual(go.transform, fingerSize, fingerCenter, gripperMaterial);
            var col = go.AddComponent<BoxCollider>();
            col.size = fingerSize;
            col.center = fingerCenter;
            var mat = new PhysicsMaterial("grip") { dynamicFriction = 1.2f, staticFriction = 1.4f, frictionCombine = PhysicsMaterialCombine.Maximum };
            col.material = mat;
            return ab;
        }

        void ConfigureRevolute(ArticulationBody ab, JointSpec js, Vector3 axisLocal)
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;
            ab.anchorRotation = Quaternion.FromToRotation(Vector3.right, axisLocal); // map drive axis(X) to desired axis
            ab.twistLock = ArticulationDofLock.LimitedMotion;
            var drive = ab.xDrive;
            drive.lowerLimit = js.minAngle;
            drive.upperLimit = js.maxAngle;
            drive.stiffness = js.stiffness;
            drive.damping = js.damping;
            drive.forceLimit = js.maxTorque;
            drive.target = 0f;
            ab.xDrive = drive;
            ab.mass = Mathf.Max(0.05f, js.linkLength * js.linkRadius * 50f);
        }

        public void SetJointTargets(IReadOnlyList<float> anglesDeg)
        {
            float dt = Mathf.Max(1e-4f, Time.fixedDeltaTime);
            for (int i = 0; i < jointBodies.Count && i < anglesDeg.Count; i++)
            {
                var ab = jointBodies[i];
                var drive = ab.xDrive;
                float cmd = Mathf.Clamp(anglesDeg[i], drive.lowerLimit, drive.upperLimit);

                // Digital-twin: pass the command through the servo model (rate-limit + tick quantise),
                // exactly as the real STS3215 would receive it.
                if (servoFidelity && i < servos.Count && servoCommandedDeg != null)
                {
                    float rl = servos[i].RateLimit(servoCommandedDeg[i], cmd, dt);
                    int tick = servos[i].AngleToTick(rl);     // quantise to a real servo tick
                    rl = servos[i].TickToAngle(tick);         // and back -> what the motor actually holds
                    servoCommandedDeg[i] = rl;
                    cmd = rl;
                }
                drive.target = cmd;
                ab.xDrive = drive;
            }
        }

        /// <summary>
        /// HARD-reset the whole articulation to the given joint angles (deg), teleporting joint positions
        /// and zeroing all velocities + drive targets. This is the equivalent of "homing" a real robot
        /// between tasks: it clears any accumulated bad articulation state (the extreme limit poses that
        /// can wedge the SO-101 after a contact-rich pick). Pass null for a straight zero pose.
        /// Must be called from the main thread (it writes jointPosition directly).
        /// </summary>
        public void HardResetJoints(IReadOnlyList<float> anglesDeg = null)
        {
            if (jointBodies == null || jointBodies.Count == 0) return;

            // Find the articulation ROOT (the only body where SetJointPositions actually teleports the
            // whole reduced-coordinate chain — writing child .jointPosition individually gets overwritten
            // by the solver the same frame, which is why a naive per-body teleport silently no-ops).
            ArticulationBody root = baseBody;
            while (root != null && !root.isRoot)
            {
                var p = root.transform.parent;
                root = p != null ? p.GetComponentInParent<ArticulationBody>() : null;
            }
            if (root == null || !root.isRoot) return;

            // Drive targets first (so when the solver re-evaluates, it holds the new pose).
            for (int i = 0; i < jointBodies.Count; i++)
            {
                var ab = jointBodies[i];
                if (ab == null) continue;
                float deg = (anglesDeg != null && i < anglesDeg.Count) ? anglesDeg[i] : 0f;
                var drive = ab.xDrive;
                deg = Mathf.Clamp(deg, drive.lowerLimit, drive.upperLimit);
                drive.target = deg;
                ab.xDrive = drive;
            }

            // Teleport the full reduced-coordinate state via the root, then zero all velocities.
            var positions = new List<float>();
            var velocities = new List<float>();
            root.GetJointPositions(positions);
            root.GetJointVelocities(velocities);
            // Map our revolute jointBodies (index order) onto the reduced DOF list. The reduced list is in
            // articulation DOF order; for this 1-DOF-per-joint chain it aligns with jointBodies order, with
            // any extra DOFs (gripper prismatics) left as-is unless we have an angle for them.
            int dof = 0;
            for (int i = 0; i < jointBodies.Count && dof < positions.Count; i++)
            {
                var ab = jointBodies[i];
                if (ab == null) continue;
                int n = ab.dofCount;
                if (n <= 0) continue;
                float deg = (anglesDeg != null && i < anglesDeg.Count) ? anglesDeg[i] : 0f;
                var drive = ab.xDrive;
                deg = Mathf.Clamp(deg, drive.lowerLimit, drive.upperLimit);
                positions[dof] = deg * Mathf.Deg2Rad;
                velocities[dof] = 0f;
                dof += n;
            }
            root.SetJointPositions(positions);
            root.SetJointVelocities(velocities);

            // Re-seed the servo rate-limiter so it doesn't snap-rate-limit away from the new pose.
            SeedServoState(anglesDeg ?? new float[jointBodies.Count]);
        }

        /// <summary>Seed the servo rate-limiter state (call after setting an initial/home pose).</summary>
        public void SeedServoState(IReadOnlyList<float> anglesDeg)
        {
            if (servoCommandedDeg == null) servoCommandedDeg = new float[jointBodies.Count];
            for (int i = 0; i < servoCommandedDeg.Length && i < anglesDeg.Count; i++)
                servoCommandedDeg[i] = anglesDeg[i];
        }

        /// <summary>Current servo bus state: per-joint (id, tick, deg) — what the real bus would show.</summary>
        public string ServoBusString()
        {
            var sb = new System.Text.StringBuilder();
            float[] ang = GetJointAngles();
            for (int i = 0; i < servos.Count && i < ang.Length; i++)
                sb.Append($"#{servos[i].servoId}:{servos[i].AngleToTick(ang[i])} ");
            return sb.ToString();
        }

        public float[] GetJointAngles()
        {
            var a = new float[jointBodies.Count];
            for (int i = 0; i < jointBodies.Count; i++)
            {
                var pos = jointBodies[i].jointPosition;
                float deg = pos.dofCount > 0 ? pos[0] * Mathf.Rad2Deg : 0f;
                // Wrap to the nearest equivalent inside the joint range so wide joints (wrist_roll) don't
                // display as e.g. 561° (which is 561-360=201, or -159 within range).
                if (i < jointSpecs.Count)
                {
                    float lo = jointSpecs[i].minAngle, hi = jointSpecs[i].maxAngle;
                    while (deg > hi && deg - 360f >= lo - 1f) deg -= 360f;
                    while (deg < lo && deg + 360f <= hi + 1f) deg += 360f;
                }
                a[i] = deg;
            }
            return a;
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
            }
            jointBodies.Clear();
            jointSpecs.Clear();
        }

        // ----- procedural mesh helpers --------------------------------------------
        void EnsureMaterials()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (linkMaterial == null)   linkMaterial   = new Material(sh) { color = new Color(0.85f, 0.85f, 0.88f) };
            if (jointMaterial == null)  jointMaterial  = new Material(sh) { color = new Color(0.25f, 0.45f, 0.85f) };
            if (gripperMaterial == null) gripperMaterial = new Material(sh) { color = new Color(0.95f, 0.55f, 0.15f) };
        }

        static GameObject Prim(PrimitiveType t, Transform parent)
        {
            var go = GameObject.CreatePrimitive(t);
            var col = go.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            go.transform.SetParent(parent, false);
            return go;
        }

        void AddCylinderVisual(Transform parent, float radius, float length, Vector3 center, Material m)
        {
            var go = Prim(PrimitiveType.Cylinder, parent); // default cylinder is 2 units tall along Y
            go.name = "vis_link";
            go.transform.localPosition = center;
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        void AddSphereVisual(Transform parent, float radius, Vector3 center, Material m)
        {
            var go = Prim(PrimitiveType.Sphere, parent);
            go.name = "vis_joint";
            go.transform.localPosition = center;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        void AddBoxVisual(Transform parent, Vector3 size, Vector3 center, Material m)
        {
            var go = Prim(PrimitiveType.Cube, parent);
            go.name = "vis_box";
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        void AddCapsuleCollider(GameObject go, float radius, float height, Vector3 center)
        {
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = radius;
            col.height = height + radius * 2f;
            col.direction = 1; // Y
            col.center = center;
        }
    }
}
