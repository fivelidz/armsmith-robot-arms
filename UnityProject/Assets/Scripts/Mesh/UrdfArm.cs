// UrdfArm.cs — partial class extension of ProceduralArm that builds the SO-101 follower arm
// from kinematics.json using real URDF joint origins, STL meshes, and ArticulationBody physics.
//
// Adds one public method:
//   public void BuildFromKinematics(string kinematicsJsonPath)
//
// After it returns, the SAME public fields that Build(ArmConfig) fills are populated:
//   baseBody, jointBodies (6), jointSpecs (6), servos (6),
//   endEffector, leftJaw, rightJaw, gripper, config
// ArmController, ScenarioManager, EvolutionTrainer, BehaviourRecorder all continue to work
// without modification because they depend exclusively on those public members.
//
// URDF → Unity coordinate conversion (consistent with StlImporter's (x,y,z)→(x,z,y) vertex swap):
//   position : (x, y, z)_urdf  →  (x, z, y)_unity           [swap Y↔Z, no negation]
//   rpy→quat : M·Rz(y)·Ry(p)·Rx(r)·M^T  =  Ry(−y)·Rz(−p)·Rx(−r)  where M=[[1,0,0],[0,0,1],[0,1,0]]
//             In Unity: Euler(0,−y,0)*Euler(0,0,−p)*Euler(−r,0,0)  (r/p/y all in URDF degrees)
//   joint axis: URDF Z=[0,0,1] → Unity Y=[0,1,0]; ArticulationBody drive-X→Y via anchorRotation=Euler(0,0,90)
//   root yaw  : arm root rotated −90° around Y (baseYawOffsetDeg=−90) maps URDF+X to Unity+Z (forward)
//   mesh scale: StlImporter.Load() applies Y↔Z swap and NO mm→m scale (STLs already in metres).
//
// Author: ArmSmith / SO-ARM100 builder   (generated 2026-05-30)
// Attribution: SO-ARM100/SO-101 meshes © The Robot Studio, Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  JSON DTO types  (JsonUtility requires [Serializable] plain classes)
    // ─────────────────────────────────────────────────────────────────────────────

    [Serializable]
    internal class KinMeshEntry
    {
        public string role   = "";
        public string file   = "";
        /// <summary>mesh_xyz_m in URDF/link frame (metres).</summary>
        public float[] mesh_xyz_m   = new float[3];
        /// <summary>mesh_rpy_deg in URDF/link frame.</summary>
        public float[] mesh_rpy_deg = new float[3];
    }

    [Serializable]
    internal class KinInertial
    {
        public float   mass_kg    = 0.1f;
        public float[] com_xyz_m  = new float[3];
    }

    [Serializable]
    internal class KinLink
    {
        public string         name        = "";
        public string         urdf_name   = "";
        public List<KinMeshEntry> meshes  = new List<KinMeshEntry>();
        public KinInertial    inertial    = new KinInertial();
    }

    [Serializable]
    internal class KinJoint
    {
        public string   name            = "";
        public string   type            = "revolute";  // "revolute" or "fixed"
        public string   parent          = "";
        public string   child           = "";
        public float[]  origin_xyz_m    = new float[3];
        public float[]  origin_rpy_deg  = new float[3];
        public float[]  axis_local      = new float[3];
        /// <summary>limit_deg[0]=lower, [1]=upper. Null means fixed joint.</summary>
        public float[]  limit_deg       = null;
    }

    [Serializable]
    internal class KinematicsJson
    {
        public List<KinLink>  links  = new List<KinLink>();
        public List<KinJoint> joints = new List<KinJoint>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Partial extension of ProceduralArm
    // ─────────────────────────────────────────────────────────────────────────────

    public partial class ProceduralArm
    {
        // ── Inspector-tweakable per-link visual offsets ───────────────────────────
        // Applied to the "stl_vis" child after the URDF mesh_xyz/rpy.  Use these
        // in the Inspector if a mesh needs fine-tuning without recompiling.

        [Serializable]
        public class LinkVisualTweak
        {
            [Tooltip("Match the link/joint name exactly (e.g. 'shoulder_link').")]
            public string  linkName   = "";
            public Vector3 posOffset  = Vector3.zero;
            public Vector3 eulerOffset = Vector3.zero;
        }

        [Header("URDF Builder Visual Tweaks")]
        [Tooltip("Fine-tune per-link STL visual offsets without recompiling.")]
        public List<LinkVisualTweak> visualTweaks = new List<LinkVisualTweak>();

        [Header("URDF Builder Orientation")]
        [Tooltip("Extra world-space Y rotation (degrees) applied to the arm root so the arm faces forward (+Z). " +
                 "The SO-101 URDF natural reach direction is +X; −90° (Unity Ry(−90)=RH Ry(−90)) rotates " +
                 "+X to +Z so the arm points toward the table. Default −90.")]
        public float baseYawOffsetDeg = -90f;

        // ─────────────────────────────────────────────────────────────────────────
        //  Main entry point
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the SO-101 follower arm from a kinematics.json file.
        /// Populates baseBody, jointBodies, jointSpecs, servos, endEffector,
        /// leftJaw, rightJaw, gripper, and config so that ArmController /
        /// ScenarioManager / EvolutionTrainer all work unchanged.
        /// </summary>
        public void BuildFromKinematics(string kinematicsJsonPath)
        {
            if (!File.Exists(kinematicsJsonPath))
            {
                Debug.LogError($"[UrdfArm] kinematics.json not found: {kinematicsJsonPath}");
                return;
            }

            // --- Parse JSON ---------------------------------------------------
            string json = File.ReadAllText(kinematicsJsonPath);
            var kin = ParseKinematics(json);
            if (kin == null || kin.joints == null || kin.joints.Count == 0)
            {
                Debug.LogError("[UrdfArm] Failed to parse kinematics.json or no joints found.");
                return;
            }

            // --- Clear any previous arm hierarchy -----------------------------
            Clear();
            EnsureMaterials();

            // Apply base yaw so the arm faces forward (+Z in Unity world).
            // The SO-101 URDF natural reach direction is +X; rotating the root by
            // baseYawOffsetDeg (default 90°) swings it to face +Z (toward the table).
            transform.localRotation = Quaternion.Euler(0f, baseYawOffsetDeg, 0f);

            string meshDir = Path.GetDirectoryName(kinematicsJsonPath);

            // Build a link lookup by name
            var linkByName = new Dictionary<string, KinLink>(StringComparer.OrdinalIgnoreCase);
            foreach (var lk in kin.links) linkByName[lk.name] = lk;

            // --- Separate revolute joints from fixed TCP joint ----------------
            var revoluteJoints = new List<KinJoint>();
            KinJoint tcpJoint  = null;
            KinJoint gripperJoint = null;   // the revolute gripper joint

            foreach (var j in kin.joints)
            {
                if (j.type == "fixed")
                    tcpJoint = j;
                else if (j.name == "gripper")
                    gripperJoint = j;
                else
                    revoluteJoints.Add(j);
            }
            // Add gripper joint last among revolute joints
            if (gripperJoint != null)
                revoluteJoints.Add(gripperJoint);

            // --- Build ArmConfig so ArmController.SolveIK / TotalReach() work -
            config = BuildArmConfig(revoluteJoints, tcpJoint);

            // ── Base link (base_link) ─────────────────────────────────────────
            // The base is a fixed, immovable ArticulationBody at the arm's world origin.
            var baseGo = new GameObject("base_link");
            baseGo.transform.SetParent(transform, false);
            baseGo.transform.localPosition = Vector3.zero;
            baseGo.transform.localRotation = Quaternion.identity;

            baseBody           = baseGo.AddComponent<ArticulationBody>();
            baseBody.immovable = true;
            baseBody.mass      = linkByName.TryGetValue("base_link", out var baseLk)
                                     ? baseLk.inertial.mass_kg : 0.147f;

            // Attach base link STL meshes
            if (linkByName.TryGetValue("base_link", out var baseLinkData))
                AttachLinkMeshes(baseGo.transform, "base_link", baseLinkData.meshes, meshDir);

            // Add a minimal capsule collider so the base has physical presence
            var baseCol = baseGo.AddComponent<CapsuleCollider>();
            baseCol.center    = new Vector3(0f, 0.037f, 0f);
            baseCol.radius    = 0.035f;
            baseCol.height    = 0.075f;
            baseCol.direction = 1; // Y

            // ── Revolute chain ────────────────────────────────────────────────
            jointBodies.Clear();
            jointSpecs.Clear();
            servos.Clear();

            Transform parentTf = baseGo.transform;

            for (int i = 0; i < revoluteJoints.Count; i++)
            {
                var j = revoluteJoints[i];
                bool isGripper = j.name == "gripper";

                // Unity local position: (x,y,z)_urdf → (x,z,y)_unity
                Vector3 localPos = UrdfPosToUnity(j.origin_xyz_m);

                // Unity local rotation from URDF RPY
                Quaternion localRot = UrdfRpyDegToUnity(j.origin_rpy_deg);

                var go = new GameObject(j.child);   // name after the child link
                go.transform.SetParent(parentTf, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = localRot;

                var ab = go.AddComponent<ArticulationBody>();

                // Joint limits
                float limLo = (j.limit_deg != null && j.limit_deg.Length >= 2) ? j.limit_deg[0] : -180f;
                float limHi = (j.limit_deg != null && j.limit_deg.Length >= 2) ? j.limit_deg[1] :  180f;

                // Each SO-101 joint rotates about local-Z (URDF) = local-Y (Unity after M-swap).
                // In ArticulationBody the revolute drive is about the ANCHOR's X-axis.
                // Each joint has a different joint-frame orientation so we need a per-joint
                // anchorRotation that maps anchor-X to the correct physical rotation axis
                // (the child frame's Y in the parent's local coordinates).
                Quaternion anchorRot = JointAnchorRotation(j.name);
                // Higher stiffness + force so each drive actually reaches its commanded angle against the
                // arm's inertia (esp. shoulder_pan turning the whole arm sideways). forceLimit 40->150.
                ConfigureUrdfRevolute(ab, limLo, limHi,
                    stiffness: 14000f, damping: 250f, forceLimit: 150f,
                    massKg: linkByName.TryGetValue(j.child, out var cLk) ? cLk.inertial.mass_kg : 0.1f,
                    anchorRotation: anchorRot);

                // JointSpec for IK / ArmController
                float linkLength = (i + 1 < revoluteJoints.Count)
                    ? UrdfPosToUnity(revoluteJoints[i + 1].origin_xyz_m).magnitude
                    : (tcpJoint != null ? UrdfPosToUnity(tcpJoint.origin_xyz_m).magnitude : 0.05f);

                JointAxis axis = JointAxisForJointName(j.name);
                var spec = new JointSpec
                {
                    name       = j.name,
                    axis       = axis,
                    linkLength = linkLength,
                    linkRadius = 0.022f,
                    minAngle   = limLo,
                    maxAngle   = limHi,
                    maxTorque  = 40f,
                    stiffness  = 9000f,
                    damping    = 150f
                };
                jointSpecs.Add(spec);
                jointBodies.Add(ab);

                // ServoModel (digital twin of STS3215)
                servos.Add(new ServoModel
                {
                    servoId          = i + 1,
                    minDeg           = limLo,
                    maxDeg           = limHi,
                    maxTorqueNm      = 1.6f,
                    maxSpeedDegPerSec= 300f
                });

                // STL visuals for this link. SKIP the moving-jaw link mesh here — the visible jaw is
                // built once by the hand-built `moving_jaw` body below. Attaching it on the gripper
                // revolute body too caused the "doubled-up" jaw look.
                if (linkByName.TryGetValue(j.child, out var childLinkData)
                    && !j.child.ToLower().Contains("jaw"))
                    AttachLinkMeshes(go.transform, j.child, childLinkData.meshes, meshDir);

                // Fallback collider (capsule oriented along -X in local space, which is the link's
                // primary direction in the URDF frame before anchor rotation)
                var col = go.AddComponent<CapsuleCollider>();
                col.radius    = 0.018f;
                col.height    = Mathf.Max(0.04f, linkLength + 0.036f);
                col.direction = 0; // X  (link runs along local X in URDF joint frame)
                col.center    = new Vector3(-linkLength * 0.5f, 0f, 0f);

                parentTf = go.transform;

                // ── Gripper sub-tree ──────────────────────────────────────────
                // After the gripper joint (joint 5, index 5) we build the parallel-jaw gripper.
                // leftJaw = the moving jaw (driven ArticulationBody matching real gripper joint)
                // rightJaw = a static reference jaw opposite, so Gripper.SetClose() works
                if (isGripper)
                {
                    BuildUrdfGripper(go.transform, j, tcpJoint, meshDir);
                    break;  // gripper is the last joint; stop the chain here
                }
            }

            servoCommandedDeg = new float[jointBodies.Count];
            gameObject.name = "SO-101-Follower";

            Debug.Log($"[UrdfArm] Built SO-101 arm: {jointBodies.Count} DOF, " +
                      $"endEffector={(endEffector != null ? endEffector.name : "null")}, " +
                      $"gripper={(gripper != null ? "ok" : "null")}");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Gripper sub-tree builder
        // ─────────────────────────────────────────────────────────────────────────

        void BuildUrdfGripper(Transform gripperLinkTf, KinJoint gripperJoint,
                               KinJoint tcpJoint, string meshDir)
        {
            // ── Moving jaw  (leftJaw) ─────────────────────────────────────────
            // The real gripper joint origin is already the pivot for the moving jaw.
            // We drive it as a RevoluteJoint (same as in URDF), but ArmController
            // only cares that leftJaw / rightJaw are ArticulationBodies under the
            // gripper palm, and that Gripper.SetClose() can drive them.
            // To keep Gripper.cs working (it uses prismatic xDrive), we create a
            // prismatic "moving jaw" driven by the jaw servo, mirroring Build().

            float jawWidth = 0.04f;   // real SO-101 jaw opening ≈ 40 mm
            float jawLen   = 0.025f;  // finger stub length

            // ── Moving jaw ────────────────────────────────────────────────────
            {
                var go = new GameObject("moving_jaw");
                go.transform.SetParent(gripperLinkTf, false);
                // Place jaw in front of the gripper link, offset slightly
                go.transform.localPosition = new Vector3(0f, 0f, jawWidth * 0.5f);
                go.transform.localRotation = Quaternion.identity;

                leftJaw = go.AddComponent<ArticulationBody>();
                leftJaw.jointType     = ArticulationJointType.PrismaticJoint;
                leftJaw.anchorRotation = Quaternion.identity;
                leftJaw.linearLockX   = ArticulationDofLock.LimitedMotion;
                leftJaw.linearLockY   = ArticulationDofLock.LockedMotion;
                leftJaw.linearLockZ   = ArticulationDofLock.LockedMotion;
                var ld = leftJaw.xDrive;
                ld.lowerLimit = -jawWidth;
                ld.upperLimit =  jawWidth;
                ld.stiffness  = 9000f;
                ld.damping    = 150f;
                ld.forceLimit = 80f;
                leftJaw.xDrive = ld;
                leftJaw.mass   = 0.012f;

                // STL visual: moving_jaw_so101_v1.stl  (StlImporter converts mm→m + Z-up→Y-up)
                AttachSingleMesh(go.transform, "moving_jaw_so101_v1.stl", meshDir,
                    meshPosUrdf: new float[]{0f, 0f, 0.0189f},
                    meshRpyDeg: new float[]{0f, 0f, 0f});

                // Collider
                var col = go.AddComponent<BoxCollider>();
                col.size   = new Vector3(0.012f, jawLen, 0.012f);
                col.center = new Vector3(0f, jawLen * 0.5f, 0f);
                var mat = new PhysicsMaterial("grip")
                {
                    dynamicFriction = 1.2f, staticFriction = 1.4f,
                    frictionCombine = PhysicsMaterialCombine.Maximum
                };
                col.material = mat;
            }

            // ── Static reference jaw  (rightJaw) ─────────────────────────────
            // Mirror jaw; also prismatic so Gripper.cs xDrive calls succeed
            {
                var go = new GameObject("fixed_jaw");
                go.transform.SetParent(gripperLinkTf, false);
                go.transform.localPosition = new Vector3(0f, 0f, -jawWidth * 0.5f);
                go.transform.localRotation = Quaternion.identity;

                rightJaw = go.AddComponent<ArticulationBody>();
                rightJaw.jointType     = ArticulationJointType.PrismaticJoint;
                rightJaw.anchorRotation = Quaternion.identity;
                rightJaw.linearLockX   = ArticulationDofLock.LimitedMotion;
                rightJaw.linearLockY   = ArticulationDofLock.LockedMotion;
                rightJaw.linearLockZ   = ArticulationDofLock.LockedMotion;
                var rd = rightJaw.xDrive;
                rd.lowerLimit = -jawWidth;
                rd.upperLimit =  jawWidth;
                rd.stiffness  = 9000f;
                rd.damping    = 150f;
                rd.forceLimit = 80f;
                rightJaw.xDrive = rd;
                rightJaw.mass   = 0.012f;

                // NO mesh on the fixed jaw: the real SO-101 follower has only ONE moving jaw closing
                // against the wrist body (there is no separate static-jaw mesh). Adding the moving_jaw
                // mesh here caused the "doubled-up" look at the claw. This body is a physics reference only.

                var col = go.AddComponent<BoxCollider>();
                col.size   = new Vector3(0.012f, jawLen, 0.012f);
                col.center = new Vector3(0f, jawLen * 0.5f, 0f);
                var mat = new PhysicsMaterial("grip")
                {
                    dynamicFriction = 1.2f, staticFriction = 1.4f,
                    frictionCombine = PhysicsMaterialCombine.Maximum
                };
                col.material = mat;
            }

            // ── End-effector (TCP = the GRASP POINT) ──────────────────────────
            // CRITICAL for grasping: the EE is the point the IK aims at, so it must sit BETWEEN the jaws
            // (at the grasp centre) — otherwise closing the jaws never contacts an object at the IK target.
            // The jaws were created as children of gripperLinkTf at local (±0.019, 0, ±0.020), so their
            // midpoint is the gripper-link origin. We place the EE at that midpoint, nudged slightly
            // along the finger (+local? ) so it's at the finger-tip grasp zone.
            var eeGo = new GameObject("EndEffector");
            eeGo.transform.SetParent(gripperLinkTf, false);
            Vector3 jawMidLocal = (leftJaw.transform.localPosition + rightJaw.transform.localPosition) * 0.5f;
            eeGo.transform.localPosition = jawMidLocal + new Vector3(0f, 0.02f, 0f); // grasp zone between fingers
            eeGo.transform.localRotation = Quaternion.identity;
            endEffector = eeGo.transform;

            // ── Gripper component ─────────────────────────────────────────────
            // Attach to the gripper_link's parent GameObject so it nests correctly.
            var gripPalmGo = gripperLinkTf.gameObject;
            gripper = gripPalmGo.AddComponent<Gripper>();
            gripper.Init(this, leftJaw, rightJaw, jawWidth * 2f, jawWidth * 0.5f);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  ArticulationBody configuration
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Configure a RevoluteJoint that spins about URDF local-Z.
        /// ArticulationBody's revolute "twist" is about its anchor's local X-axis.
        /// Each joint has a different anchorRotation because the joint frame orientations
        /// differ across the SO-101 kinematic chain (computed from child_Y_in_parent analysis).
        /// </summary>
        static void ConfigureUrdfRevolute(ArticulationBody ab,
            float lowerLimitDeg, float upperLimitDeg,
            float stiffness, float damping, float forceLimit,
            float massKg,
            Quaternion anchorRotation)
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;

            // anchorRotation is computed per-joint so drive-X maps to the correct
            // physical rotation axis (the URDF joint Z in the parent frame).
            ab.anchorRotation = anchorRotation;

            ab.twistLock = ArticulationDofLock.LimitedMotion;

            var drive = ab.xDrive;
            drive.lowerLimit = lowerLimitDeg;
            drive.upperLimit = upperLimitDeg;
            drive.stiffness  = stiffness;
            drive.damping    = damping;
            drive.forceLimit = forceLimit;
            drive.target     = 0f;
            ab.xDrive = drive;

            ab.mass = Mathf.Max(0.01f, massKg);
            // Safety vs mobility balance: cap velocity to prevent singularity explosions, but keep damping
            // LOW so the drive can actually reach its commanded angle (angularDamping=2 was fighting the
            // drive — shoulder_pan stalled at 14deg when asked for 31deg, causing all off-centre reach fails).
            ab.maxAngularVelocity = 6f;          // rad/s cap (still prevents explosions)
            ab.angularDamping = 0.2f;            // low: don't fight the drive
            ab.jointFriction = 0.02f;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Mesh attachment helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attach all STL meshes for a link.  Each mesh in the JSON has its own
        /// mesh_xyz_m and mesh_rpy_deg (in the link's URDF frame).  We convert
        /// those using the same (x,z,y) position swap and RPY→Euler mapping.
        /// StlImporter.Load() already converts mm→m and Z-up→Y-up internally, so
        /// after conversion the mesh sits correctly in the link frame.
        /// </summary>
        void AttachLinkMeshes(Transform linkTf, string linkName,
                               List<KinMeshEntry> meshEntries, string meshDir)
        {
            if (meshEntries == null || meshEntries.Count == 0) return;

            // Find per-link visual tweak (if any)
            LinkVisualTweak tweak = null;
            if (visualTweaks != null)
                foreach (var vt in visualTweaks)
                    if (vt.linkName == linkName) { tweak = vt; break; }

            // Shared grey-metallic material (reuse StlArmSkin's cached material or create one)
            Material mat = GetOrCreateStlMaterial();

            // Mesh load cache (keyed by lowercase filename) — avoids loading sts3215 5×
            var cache = GetMeshCache();

            for (int mi = 0; mi < meshEntries.Count; mi++)
            {
                var entry = meshEntries[mi];
                if (string.IsNullOrEmpty(entry.file)) continue;

                Mesh mesh = GetOrLoadMesh(cache, meshDir, entry.file);
                if (mesh == null) continue;

                // StlImporter already converted the vertex coordinates (mm→m, Z-up→Y-up).
                // The mesh_xyz_m and mesh_rpy_deg from the JSON are in the LINK's URDF frame,
                // so we apply the same URDF→Unity conversion to place the visual.
                Vector3    mPos = UrdfPosToUnity(entry.mesh_xyz_m);
                Quaternion mRot = UrdfRpyDegToUnity(entry.mesh_rpy_deg);

                // Apply inspector tweak offset on the FIRST (primary) mesh only
                if (mi == 0 && tweak != null)
                {
                    mPos += tweak.posOffset;
                    mRot  = mRot * Quaternion.Euler(tweak.eulerOffset);
                }

                string visName = $"stl_vis_{mi}_{Path.GetFileNameWithoutExtension(entry.file)}";
                // Remove any previous version (idempotent re-run)
                var existing = linkTf.Find(visName);
                if (existing != null)
                {
                    if (Application.isPlaying) Destroy(existing.gameObject);
                    else DestroyImmediate(existing.gameObject);
                }

                var go = new GameObject(visName);
                go.transform.SetParent(linkTf, false);
                go.transform.localPosition = mPos;
                go.transform.localRotation = mRot;
                go.transform.localScale    = Vector3.one; // mesh is already in metres

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows    = true;
            }
        }

        /// <summary>
        /// Attach a single named STL file as a visual child of <paramref name="parent"/>,
        /// using raw URDF position/RPY arrays (no JSON entry needed).
        /// </summary>
        void AttachSingleMesh(Transform parent, string file, string meshDir,
                               float[] meshPosUrdf, float[] meshRpyDeg)
        {
            var cache = GetMeshCache();
            Mesh mesh = GetOrLoadMesh(cache, meshDir, file);
            if (mesh == null) return;

            var go = new GameObject($"stl_vis_{Path.GetFileNameWithoutExtension(file)}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = UrdfPosToUnity(meshPosUrdf);
            go.transform.localRotation = UrdfRpyDegToUnity(meshRpyDeg);
            go.transform.localScale    = Vector3.one;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateStlMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows    = true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  ArmConfig builder  (for IK + config.TotalReach())
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal ArmConfig from the revolute joint list so that
        /// ArmController.SolveIK (ForwardKinematics) works:
        ///   • joint[i].linkLength  = |unity-space offset to next joint|
        ///   • config.gripperLength = distance from gripper joint to TCP
        /// The IK forward model walks +Y * linkLength per joint, which is an
        /// approximation of the real zig-zag URDF offsets — good enough for CCD IK.
        /// </summary>
        static ArmConfig BuildArmConfig(List<KinJoint> revoluteJoints, KinJoint tcpJoint)
        {
            var cfg = new ArmConfig
            {
                armName       = "SO-101-Follower",
                baseHeight    = 0.0624f,   // shoulder_pan z-offset from base = 62.4 mm
                baseRadius    = 0.04f,
                gripperLength = tcpJoint != null
                                ? UrdfPosToUnity(tcpJoint.origin_xyz_m).magnitude
                                : 0.0984f,
                gripperWidth  = 0.04f
            };

            for (int i = 0; i < revoluteJoints.Count; i++)
            {
                var j = revoluteJoints[i];
                bool isLast = (i == revoluteJoints.Count - 1);

                float linkLength;
                if (!isLast)
                    linkLength = UrdfPosToUnity(revoluteJoints[i + 1].origin_xyz_m).magnitude;
                else
                    linkLength = tcpJoint != null
                                 ? UrdfPosToUnity(tcpJoint.origin_xyz_m).magnitude
                                 : 0.035f;

                float limLo = (j.limit_deg != null && j.limit_deg.Length >= 2) ? j.limit_deg[0] : -180f;
                float limHi = (j.limit_deg != null && j.limit_deg.Length >= 2) ? j.limit_deg[1] :  180f;

                cfg.joints.Add(new JointSpec
                {
                    name       = j.name,
                    axis       = JointAxisForJointName(j.name),
                    linkLength = linkLength,
                    linkRadius = 0.022f,
                    minAngle   = limLo,
                    maxAngle   = limHi,
                    maxTorque  = 40f,
                    stiffness  = 9000f,
                    damping    = 150f
                });
            }

            return cfg;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  JSON parsing  (JsonUtility can't deserialize arrays-of-floats as float[]
        //  directly inside a nested class via a top-level wrapper; we use a
        //  two-pass approach: raw text replacement + JsonUtility on simple structs)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses kinematics.json.
        /// JsonUtility requires [Serializable] classes with exact field names.
        /// The JSON contains null values for fixed joints' limit/axis — JsonUtility
        /// treats missing/null numeric arrays as empty, which is fine here.
        ///
        /// NOTE: JsonUtility does NOT support polymorphic types or Dictionary.
        /// For the flat structure of kinematics.json this is sufficient.
        /// </summary>
        static KinematicsJson ParseKinematics(string json)
        {
            // JsonUtility cannot handle JSON with C++ comments or trailing commas.
            // The file uses "_comment" keys (valid JSON) so no stripping needed.
            // We do strip null literal values for float[] fields since JsonUtility
            // maps them to empty arrays rather than crashing.
            json = json.Replace(": null", ": []")
                       .Replace(":null",  ":[]");

            try
            {
                return JsonUtility.FromJson<KinematicsJson>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UrdfArm] JSON parse error: {ex.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Coordinate conversion helpers  (static, callable from BuildArmConfig)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// URDF position (metres, Z-up RH) → Unity position (metres, Y-up LH).
        /// Formula: urdf(x,y,z) → unity(x, z, y).
        /// </summary>
        static Vector3 UrdfPosToUnity(float[] xyz)
        {
            if (xyz == null || xyz.Length < 3) return Vector3.zero;
            return new Vector3(xyz[0], xyz[2], xyz[1]);
        }

        /// <summary>
        /// URDF roll/pitch/yaw (degrees) → Unity local Quaternion.
        ///
        /// Derivation (consistent with StlImporter's (x,y,z)→(x,z,y) vertex swap):
        ///   The axis-swap matrix M maps URDF→Unity:  M = [[1,0,0],[0,0,1],[0,1,0]]
        ///   For elementary rotations: M·Rx(r)·M^T = Rx(−r),  M·Ry(p)·M^T = Rz(−p),
        ///                             M·Rz(y)·M^T = Ry(−y)
        ///   URDF RPY = Rz(y)·Ry(p)·Rx(r)  so
        ///   M·R_urdf·M^T = Ry(−y) · Rz(−p) · Rx(−r)
        ///
        ///   In Unity's convention (left-hand coords, right-hand Euler angles):
        ///     Quaternion.Euler(0, y_u, 0)  ≡  right-hand Ry( y_u)   [Y same sign]
        ///     Quaternion.Euler(0, 0, z_u)  ≡  right-hand Rz( z_u)   [Z same sign]
        ///     Quaternion.Euler(x_u, 0, 0)  ≡  right-hand Rx( x_u)   [X same sign]
        ///   Therefore:
        ///     Ry(−y_urdf) = Euler(0, −y_urdf, 0)
        ///     Rz(−p_urdf) = Euler(0, 0, −p_urdf)
        ///     Rx(−r_urdf) = Euler(−r_urdf, 0, 0)
        ///   Combined (applied right-to-left):
        ///     Q = Euler(0, −y, 0) * Euler(0, 0, −p) * Euler(−r, 0, 0)
        /// </summary>
        static Quaternion UrdfRpyDegToUnity(float[] rpyDeg)
        {
            if (rpyDeg == null || rpyDeg.Length < 3) return Quaternion.identity;
            float r = rpyDeg[0];   // URDF roll
            float p = rpyDeg[1];   // URDF pitch
            float y = rpyDeg[2];   // URDF yaw
            // M·Rz(y)·Ry(p)·Rx(r)·M^T  =  Ry(−y)·Rz(−p)·Rx(−r)
            // In Unity: Euler angles map to same-sign right-hand matrices
            return Quaternion.Euler(0f, -y, 0f)
                 * Quaternion.Euler(0f, 0f, -p)
                 * Quaternion.Euler(-r, 0f, 0f);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Map a URDF joint name to the closest JointAxis enum value.</summary>
        static JointAxis JointAxisForJointName(string jname)
        {
            if (jname == null) return JointAxis.Pitch;
            string n = jname.ToLowerInvariant();
            if (n.Contains("pan")  || n.Contains("yaw"))  return JointAxis.Yaw;
            if (n.Contains("roll"))                        return JointAxis.Roll;
            return JointAxis.Pitch;  // lift, flex, gripper
        }

        /// <summary>
        /// Returns the ArticulationBody anchorRotation for a given SO-101 URDF joint.
        ///
        /// ArticulationBody revolute drives about the anchor's X axis. The anchorRotation
        /// must map anchor-X to the URDF joint's physical rotation axis (URDF Z = Unity Y
        /// of the CHILD frame) expressed in PARENT-local coordinates.
        ///
        /// Computed analytically from M·R_urdf·M^T for each joint's RPY:
        ///
        ///   shoulder_pan  : child_Y_in_parent=(0,−1,0) → Euler(0, 0,−90)  [X→−Y]
        ///   shoulder_lift : child_Y_in_parent=(0, 0,+1) → Euler(0,−90, 0)  [X→+Z]
        ///   elbow_flex    : child_Y_in_parent=(0,+1,0)  → Euler(0, 0,+90)  [X→+Y]
        ///   wrist_flex    : child_Y_in_parent=(0,+1,0)  → Euler(0, 0,+90)  [X→+Y]
        ///   wrist_roll    : child_Y_in_parent=(0, 0,+1) → Euler(0,−90, 0)  [X→+Z]
        ///   gripper       : child_Y_in_parent=(0, 0,−1) → Euler(0,+90, 0)  [X→−Z]
        /// </summary>
        static Quaternion JointAnchorRotation(string jointName)
        {
            if (jointName == null) return Quaternion.Euler(0f, 0f, 90f);
            string n = jointName.ToLowerInvariant();
            if (n == "shoulder_pan")  return Quaternion.Euler(0f,   0f, -90f);
            // shoulder_lift must PITCH forward/back (swing in Y-Z), so its drive axis = world X.
            // Euler(0,0,90) gives that; the previous Euler(0,-90,0) made it swing sideways (wrong axis,
            // links appeared to detach because the upper arm rotated about its own length).
            if (n == "shoulder_lift") return Quaternion.Euler(0f,   0f,  90f);
            if (n == "elbow_flex")    return Quaternion.Euler(0f,   0f,  90f);
            if (n == "wrist_flex")    return Quaternion.Euler(0f,   0f,  90f);
            if (n == "wrist_roll")    return Quaternion.Euler(0f, -90f,   0f);
            if (n == "gripper")       return Quaternion.Euler(0f,  90f,   0f);
            // Fallback for unknown joints: X→+Y (generic pitch-type assumption)
            return Quaternion.Euler(0f, 0f, 90f);
        }

        // Per-session mesh cache (keyed by lowercase filename)
        // Stored on the MonoBehaviour instance to survive across calls to BuildFromKinematics.
        [NonSerialized] Dictionary<string, Mesh> _meshCache;
        Dictionary<string, Mesh> GetMeshCache() =>
            _meshCache ?? (_meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase));

        static Mesh GetOrLoadMesh(Dictionary<string, Mesh> cache, string dir, string file)
        {
            string key = file.ToLowerInvariant();
            if (cache.TryGetValue(key, out Mesh m)) return m;

            // Primary path
            string path = Path.Combine(dir, file);
            m = StlImporter.Load(path);

            // Fallback: try alt_file naming (mixed-case) if primary not found
            if (m == null)
            {
                // Try lowercase variant
                string lower = Path.Combine(dir, file.ToLowerInvariant());
                if (lower != path) m = StlImporter.Load(lower);
            }

            if (m != null) cache[key] = m;
            return m;
        }

        [NonSerialized] Material _stlMat;
        Material GetOrCreateStlMaterial()
        {
            if (_stlMat != null) return _stlMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _stlMat = new Material(sh)
            {
                name  = "UrdfArm_STL_Mat",
                color = new Color(0.82f, 0.82f, 0.84f, 1f)
            };
            if (_stlMat.HasProperty("_Metallic"))    _stlMat.SetFloat("_Metallic",    0.55f);
            if (_stlMat.HasProperty("_Smoothness"))  _stlMat.SetFloat("_Smoothness",  0.40f);
            if (_stlMat.HasProperty("_Glossiness"))  _stlMat.SetFloat("_Glossiness",  0.40f);
            return _stlMat;
        }
    }
}
