// StlArmSkin.cs — applies SO-ARM100 real STL meshes to a ProceduralArm at runtime.
//
// Usage:
//   1. Attach ProceduralArm to a GameObject.
//   2. In the Inspector set `useStlMeshes = true` and optionally override `stlMeshDir`.
//   3. Hit Play — Build() calls StlArmSkin.Apply() at the end, hiding procedural
//      cylinders and adding "stl_vis" children with the real geometry.
//   4. Fine-tune per-link offset/rotation/scale in the StlLinkMap component that is
//      auto-added to the arm's root GameObject.
//
// Attribution: SO-ARM100/SO-101 robot arm meshes © The Robot Studio, Apache-2.0
//              https://github.com/TheRobotStudio/SO-ARM100

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Per-link visual offset descriptor (tweakable in the Inspector)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializable descriptor for one STL visual override on a joint/link body.
    /// Tweak <c>localPosition</c>, <c>localEuler</c>, and <c>localScale</c> in the
    /// Inspector until the mesh is visually aligned with the physics body.
    /// </summary>
    [Serializable]
    public class StlLinkEntry
    {
        [Tooltip("Human-readable label — matches the ArticulationBody's GameObject name.")]
        public string  linkName     = "";

        [Tooltip("Filename inside the mesh directory (e.g. base_so101_v2.stl).")]
        public string  stlFile      = "";

        [Tooltip("Local position offset relative to the joint's ArticulationBody transform.")]
        public Vector3 localPosition = Vector3.zero;

        [Tooltip("Local Euler-angle rotation (degrees) of the STL visual child.")]
        public Vector3 localEuler    = Vector3.zero;

        [Tooltip("Uniform scale applied to the loaded mesh GameObject (usually 1).")]
        public float   uniformScale  = 1f;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  MonoBehaviour that holds the map — added to the arm root so it's Inspector-editable
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores the per-link STL mapping on the arm's root GameObject.
    /// StlArmSkin.Apply() creates this component (or reuses an existing one) and
    /// populates it with sensible defaults for the SO-101 6-DOF arm.
    /// </summary>
    public class StlLinkMap : MonoBehaviour
    {
        [Tooltip("One entry per joint/link body. Edit offsets here to align meshes.")]
        public List<StlLinkEntry> entries = new List<StlLinkEntry>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Main static helper
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Static helper that wires up STL visuals onto an already-built ProceduralArm.
    /// Call once from ProceduralArm.Build() after the physics hierarchy is finished.
    /// </summary>
    public static class StlArmSkin
    {
        // ── Default mesh directory (resolved at runtime) ──────────────────────────
        // Application.dataPath in the Editor resolves to  <project>/Assets/
        // so this resolves to  <project>/Assets/Meshes/SOARM100/
        public static string DefaultMeshDir =>
            Path.Combine(Application.dataPath, "Meshes", "SOARM100");

        // ── URP Lit material (shared across all STL visuals) ──────────────────────
        static Material _stlMaterial;
        static Material StlMaterial
        {
            get
            {
                if (_stlMaterial != null) return _stlMaterial;

                Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

                _stlMaterial = new Material(sh)
                {
                    name  = "STL_Metallic_Grey",
                    color = new Color(0.82f, 0.82f, 0.84f, 1f)
                };

                // Enable metallic/smoothness properties if the shader has them.
                if (_stlMaterial.HasProperty("_Metallic"))
                    _stlMaterial.SetFloat("_Metallic", 0.55f);
                if (_stlMaterial.HasProperty("_Smoothness"))
                    _stlMaterial.SetFloat("_Smoothness", 0.40f);
                if (_stlMaterial.HasProperty("_Glossiness")) // Standard shader name
                    _stlMaterial.SetFloat("_Glossiness", 0.40f);

                return _stlMaterial;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Public entry point
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Hides procedural visual children (MeshRenderer disabled, colliders kept) and
        /// attaches loaded STL meshes as new "stl_vis" children on each body.
        /// <para>
        /// <paramref name="meshDir"/> — absolute path to the folder containing the .stl
        /// files.  Pass null/empty to use <see cref="DefaultMeshDir"/>.
        /// </para>
        /// </summary>
        public static void Apply(ProceduralArm arm, string meshDir)
        {
            if (arm == null)
            {
                Debug.LogWarning("[StlArmSkin] Apply called with null arm.");
                return;
            }

            if (string.IsNullOrEmpty(meshDir))
                meshDir = DefaultMeshDir;

            // Ensure (or reuse) the StlLinkMap component on the arm root.
            var linkMap = arm.GetComponent<StlLinkMap>();
            if (linkMap == null)
                linkMap = arm.gameObject.AddComponent<StlLinkMap>();

            // Build the default mapping for the SO-101 arm if the map is empty
            // (i.e., first run or entries cleared in Inspector).
            if (linkMap.entries == null || linkMap.entries.Count == 0)
                linkMap.entries = BuildDefaultMap(arm);

            // Mesh cache to avoid loading the same STL twice.
            var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

            // Process every entry in the map.
            foreach (var entry in linkMap.entries)
            {
                if (string.IsNullOrEmpty(entry.stlFile)) continue;

                // Resolve the ArticulationBody transform by name.
                Transform bodyTf = FindBodyTransform(arm, entry.linkName);
                if (bodyTf == null)
                {
                    Debug.LogWarning($"[StlArmSkin] Could not find body transform '{entry.linkName}' on arm '{arm.name}'.");
                    continue;
                }

                // Load mesh (cached).
                Mesh mesh = GetOrLoadMesh(meshCache, meshDir, entry.stlFile);
                if (mesh == null) continue; // warning already emitted by StlImporter

                // Disable/hide ALL procedural visual children (named vis_*).
                HideProceduralVisuals(bodyTf);

                // Attach the STL visual.
                AttachStlVisual(bodyTf, mesh, entry);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Default SO-101 mapping  (edit offsets here for global first-run defaults)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the default per-link mapping for the SO-101 6-DOF arm.
        /// Joint naming follows ProceduralArm: "Base", then cfg.joints[i].name
        /// ("BaseYaw", "Shoulder", "Elbow", "ForearmRoll", "WristPitch", "WristRoll"),
        /// "LeftJaw", "RightJaw", "Gripper".
        ///
        /// The per-link offsets below are starting estimates:
        ///   • Position is centred on the physics body origin (0,0,0).
        ///   • Rotation corrects for the STL mesh's own rest orientation so it points
        ///     "up" (along +Y) to match the procedural link direction in Unity.
        ///   • The meshes are in metres after the mm→m conversion in StlImporter, so
        ///     scale stays 1.  Adjust in the Inspector after first run.
        /// </summary>
        static List<StlLinkEntry> BuildDefaultMap(ProceduralArm arm)
        {
            var list = new List<StlLinkEntry>();

            // ── Base (fixed pedestal) ──────────────────────────────────────────────
            // base_so101_v2.stl sits flat; the STL rest pose is Z-up so after conversion
            // the mesh already stands upright.  Shift down by half the base height so the
            // bottom of the mesh sits at the world origin of the base body.
            float bh = arm.config != null ? arm.config.baseHeight : 0.10f;
            list.Add(new StlLinkEntry
            {
                linkName     = "Base",
                stlFile      = "base_so101_v2.stl",
                localPosition = new Vector3(0f, -(bh * 0.5f), 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 0 – BaseYaw (rotation at top of base) ───────────────────────
            // rotation_pitch_so101_v1.stl: this is the yaw turntable/shoulder bracket.
            // The mesh natural pose has the rotation axis pointing up; no extra rotation needed.
            list.Add(new StlLinkEntry
            {
                linkName     = "BaseYaw",
                stlFile      = "rotation_pitch_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 1 – Shoulder (pitch, first long link) ───────────────────────
            // upper_arm_so101_v1.stl: long segment from shoulder to elbow.
            // The STL rest pose is horizontal (along Z in the original frame), which after
            // Y↔Z swap ends up along Y in Unity — exactly the link direction.
            // Shift forward (−Z) to centre the mesh on the joint pivot.
            list.Add(new StlLinkEntry
            {
                linkName     = "Shoulder",
                stlFile      = "upper_arm_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 2 – Elbow (pitch, second long link) ─────────────────────────
            // under_arm_so101_v1.stl: forearm from elbow to wrist.
            list.Add(new StlLinkEntry
            {
                linkName     = "Elbow",
                stlFile      = "under_arm_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 3 – ForearmRoll (roll DOF at end of forearm) ────────────────
            // wrist_roll_pitch_so101_v2.stl: dual-axis wrist block.
            list.Add(new StlLinkEntry
            {
                linkName     = "ForearmRoll",
                stlFile      = "wrist_roll_pitch_so101_v2.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 4 – WristPitch ──────────────────────────────────────────────
            // wrist_roll_follower_so101_v1.stl: final roll / follower body.
            list.Add(new StlLinkEntry
            {
                linkName     = "WristPitch",
                stlFile      = "wrist_roll_follower_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Joint 5 – WristRoll ───────────────────────────────────────────────
            // Reuse the moving jaw mesh as a visual stand-in for the palm / last roll DOF
            // when there are only 6 joints; the real jaw will be on the Gripper children.
            // (If the arm has fewer joints, this entry is simply skipped when no matching
            //  body is found — no crash.)
            list.Add(new StlLinkEntry
            {
                linkName     = "WristRoll",
                stlFile      = "wrist_roll_follower_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 90f, 0f),
                uniformScale  = 1f
            });

            // ── Gripper – Moving jaw (LeftJaw ArticulationBody) ───────────────────
            list.Add(new StlLinkEntry
            {
                linkName     = "LeftJaw",
                stlFile      = "moving_jaw_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 1f
            });

            // ── Gripper – Fixed jaw side (RightJaw ArticulationBody) ─────────────
            // Mirror of the moving jaw; flip 180° around Y so it faces the other way.
            list.Add(new StlLinkEntry
            {
                linkName     = "RightJaw",
                stlFile      = "moving_jaw_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 180f, 0f),
                uniformScale  = 1f
            });

            // ── Gripper palm body ──────────────────────────────────────────────────
            // The "Gripper" GameObject is the palm that holds left/right jaws.
            // Use the wrist-roll-pitch combined block as a visual palm.
            list.Add(new StlLinkEntry
            {
                linkName     = "Gripper",
                stlFile      = "wrist_roll_pitch_so101_v2.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = new Vector3(0f, 0f, 0f),
                uniformScale  = 0.6f   // slightly smaller so it fits within the gripper body
            });

            return list;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the Transform of the named body within the arm hierarchy.
        /// Searches: baseBody, all jointBodies, leftJaw, rightJaw, and the Gripper.
        /// </summary>
        static Transform FindBodyTransform(ProceduralArm arm, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Base
            if (arm.baseBody != null &&
                arm.baseBody.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.baseBody.transform;

            // Joint chain
            if (arm.jointBodies != null)
            {
                foreach (var ab in arm.jointBodies)
                {
                    if (ab != null &&
                        ab.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return ab.transform;
                }
            }

            // Jaw bodies
            if (arm.leftJaw != null &&
                arm.leftJaw.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.leftJaw.transform;

            if (arm.rightJaw != null &&
                arm.rightJaw.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.rightJaw.transform;

            // Gripper palm — no ArticulationBody field; find child named "Gripper"
            var gripperTf = arm.transform.Find("Gripper");
            if (gripperTf == null)
            {
                // Deep search (in case of nesting)
                gripperTf = FindDescendant(arm.transform, "Gripper");
            }
            if (gripperTf != null &&
                gripperTf.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return gripperTf;

            return null;
        }

        /// <summary>Breadth-first search for a named descendant.</summary>
        static Transform FindDescendant(Transform root, string childName)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (t.gameObject.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    return t;
                for (int i = 0; i < t.childCount; i++)
                    queue.Enqueue(t.GetChild(i));
            }
            return null;
        }

        /// <summary>
        /// Disables MeshRenderer on every child whose name starts with "vis_".
        /// Colliders are NOT touched so physics continues working.
        /// </summary>
        static void HideProceduralVisuals(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("vis_", StringComparison.OrdinalIgnoreCase))
                {
                    var mr = child.GetComponent<MeshRenderer>();
                    if (mr != null) mr.enabled = false;
                }
            }
        }

        /// <summary>
        /// Loads a mesh from cache or disk, returning null on failure.
        /// </summary>
        static Mesh GetOrLoadMesh(Dictionary<string, Mesh> cache, string dir, string file)
        {
            string key = file.ToLowerInvariant();
            if (cache.TryGetValue(key, out Mesh cached)) return cached;

            string path = Path.Combine(dir, file);
            Mesh mesh = StlImporter.Load(path);
            if (mesh != null)
                cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Creates (or replaces) the "stl_vis" child on <paramref name="parent"/> with
        /// a MeshFilter + MeshRenderer using <paramref name="mesh"/> and the offsets
        /// described in <paramref name="entry"/>.
        /// </summary>
        static void AttachStlVisual(Transform parent, Mesh mesh, StlLinkEntry entry)
        {
            // Remove any pre-existing stl_vis so re-running Apply() is idempotent.
            var existing = parent.Find("stl_vis");
            if (existing != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(existing.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject("stl_vis");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = entry.localPosition;
            go.transform.localRotation = Quaternion.Euler(entry.localEuler);
            go.transform.localScale    = Vector3.one * entry.uniformScale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = StlMaterial;
            mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows     = true;
        }
    }
}
