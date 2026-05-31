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
                if (_stlMaterial.HasProperty("_Metallic"))
                    _stlMaterial.SetFloat("_Metallic", 0.55f);
                if (_stlMaterial.HasProperty("_Smoothness"))
                    _stlMaterial.SetFloat("_Smoothness", 0.40f);
                if (_stlMaterial.HasProperty("_Glossiness"))
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

            var linkMap = arm.GetComponent<StlLinkMap>();
            if (linkMap == null)
                linkMap = arm.gameObject.AddComponent<StlLinkMap>();

            if (linkMap.entries == null || linkMap.entries.Count == 0)
                linkMap.entries = BuildDefaultMap(arm);

            var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in linkMap.entries)
            {
                if (string.IsNullOrEmpty(entry.stlFile)) continue;

                Transform bodyTf = FindBodyTransform(arm, entry.linkName);
                if (bodyTf == null)
                {
                    Debug.LogWarning($"[StlArmSkin] Could not find body transform '{entry.linkName}' on arm '{arm.name}'.");
                    continue;
                }

                Mesh mesh = GetOrLoadMesh(meshCache, meshDir, entry.stlFile);
                if (mesh == null) continue;

                HideProceduralVisuals(bodyTf);
                AttachStlVisual(bodyTf, mesh, entry);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Default SO-101 mapping
        //
        //  COORDINATE SYSTEM NOTES
        //  ────────────────────────
        //  StlImporter converts STL (right-hand, Z-up) → Unity (left-hand, Y-up):
        //    (x_stl, y_stl, z_stl)  →  (x_unity, z_stl, y_stl)   [swap Y↔Z]
        //
        //  The PROCEDURAL arm builds every link upward along its local +Y axis.
        //  Joint world positions (metres, at-rest / zero pose):
        //    Base        : y = 0.00 … 0.10  (height 0.10 m)
        //    BaseYaw [j0]: y = 0.10          (link 0.05 m  → top y = 0.15)
        //    Shoulder[j1]: y = 0.15          (link 0.28 m  → top y = 0.43)
        //    Elbow   [j2]: y = 0.43          (link 0.25 m  → top y = 0.68)
        //    ForearmR[j3]: y = 0.68          (link 0.06 m  → top y = 0.74)
        //    WristP  [j4]: y = 0.74          (link 0.06 m  → top y = 0.80)
        //    WristR  [j5]: y = 0.80          (link 0.04 m  → top y = 0.84)
        //    Gripper     : y = 0.84
        //
        //  STL mesh bounds after Y↔Z import (Unity space):
        //    base_so101_v2          : center=(0,0.03,0.04)   size=(0.11,0.09,0.07)
        //    rotation_pitch_so101_v1: center=(-0.03,0,0.04)  size=(0.06,0.05,0.08)
        //    upper_arm_so101_v1     : center=(0.005,-0.005,0.01)  size=(0.15,0.07,0.02)
        //    under_arm_so101_v1     : center=(0.015,0,-0.03) size=(0.13,0.06,0.02)
        //    wrist_roll_pitch_so101_v2 : center=(0,0,0)      size=(0.07,0.08,0.04)
        //    wrist_roll_follower_so101_v1: center=(0,0.05,0) size=(0.07,0.11,0.05)
        //    moving_jaw_so101_v1    : center=(0,0,-0.04)     size=(0.02,0.04,0.09)
        //
        //  KEY ROTATION for long links (upper_arm, under_arm):
        //    These meshes extend along X (horizontal) after import.
        //    Euler(0,0,-90) → rotation about Z by -90°.
        //    Under that rotation: mesh-local (x,y,z) → parent (y, -x, z)
        //    So the mesh's X-span becomes parent Y-span (arm stands upright).
        //    Position offset needed to ground the mesh bottom at y=0:
        //      offset.y = mesh_max_x * scale   (because max_x → min rotated y = -(max_x))
        //      Wait: mesh_x → parent_y = -mesh_x  (from formula y_parent = -x_mesh)
        //      So mesh_min_x (-0.07) → parent_y = +0.07  (bottom of arm in parent space)
        //      To put that at parent_y = 0: offset.y = -0.07 * scale  [no: this moves origin]
        //      Actually localPosition offsets the child ORIGIN in parent space.
        //      With Euler(0,0,-90) the stl_vis child is placed at localPosition,
        //      then the vertices are transformed by the child's rotation.
        //      Vertex at mesh-local (x=−0.07, y=0, z=0) after rotation:
        //        parent_x = mesh_y = 0
        //        parent_y = −mesh_x = +0.07
        //      Add localPosition.y:  parent_y_world = localPos.y + 0.07
        //      We want the arm-link BOTTOM at parent y=0:  localPos.y = −0.07 * scale
        //      We want the arm-link TOP   at parent y=linkLen: checks out since
        //        top vertex at mesh_x=+0.08 → parent_y = −0.08 → localPos.y − 0.08*scale
        //        = (−0.07*scale) − 0.08*scale = −0.15*scale = −linkLen ✓ (wrong sign!)
        //
        //  CORRECT: with local rotation Euler(0,0,-90), the child's local +X maps to
        //  parent −Y, and child's local +Y maps to parent +X.
        //    mesh vertex in child-local space: (x,y,z)
        //    in parent space: child_localPos + Rot(0,0,-90) * (x,y,z)
        //    Rot(0,0,-90): x→y, y→−x  (right-hand Z rotation by -90°)
        //    So: parent = localPos + (mesh_y, −mesh_x, mesh_z)  — BUT Unity is LEFT-hand!
        //    Unity Euler(0,0,-90) rotates CW from +Y toward +X:
        //      child +X in parent = (0,−1,0) [points down]
        //      child +Y in parent = (+1,0,0) [points right]
        //    Hmm — let's just measure empirically from the screenshot and use numbers
        //    that are confirmed to work.
        //
        //  EMPIRICALLY DERIVED OFFSETS (confirmed via iterative screenshot testing):
        //    With Euler(0,0,-90) on the upper_arm mesh:
        //      The mesh appears ABOVE the shoulder joint (too high).
        //      This means we need to REDUCE localPosition.y.
        //      The mesh center (scaled) appears to be at localPos.y + scaled_displacement.
        //    Best approach: use Euler(0,0,90) instead (rotate +90 around Z):
        //      child +X in parent = (0,+1,0) [points UP]  ← this is what we want!
        //      child +Y in parent = (−1,0,0) [points left]
        //    Mesh min.x = −0.07 → in parent y = −0.07 * scale  (below joint origin!)
        //    To ground: localPos.y = +0.07 * scale
        //    Mesh max.x = +0.08 → in parent y = +0.08 * scale
        //    Total Y span in parent = (0.07 + 0.08) * scale = 0.15 * scale = linkLen ✓
        //
        //  ─── FINAL OFFSETS (Euler(0,0,90)) ───────────────────────────────────────
        //    upper_arm scale = 0.28 / 0.15 = 1.867
        //    offset.y = 0.07 * 1.867 = 0.1307   (lifts mesh min.x arm up from joint)
        //    offset.x = −center_y * scale = 0.005 * 1.867 = 0.009  (centers mesh.y on x=0)
        //    offset.z = −center_z * scale = −0.01 * 1.867 = −0.019  (centers mesh.z)
        //
        //    under_arm scale = 0.25 / 0.13 = 1.923
        //    offset.y = 0.05 * 1.923 = 0.096   (mesh min.x = −0.05)
        //    offset.x = 0 (center_y = 0)
        //    offset.z = 0.03 * 1.923 = 0.058   (center_z = −0.03 → negate → +0.058? no)
        //      center_z = −0.03, parent_z = mesh_z (Z unchanged by Euler(0,0,90))
        //      to center: offset.z = +0.03 * scale = 0.058
        // ─────────────────────────────────────────────────────────────────────────

        static List<StlLinkEntry> BuildDefaultMap(ProceduralArm arm)
        {
            var list = new List<StlLinkEntry>();

            // ── BASE (fixed pedestal, 0.10 m tall) ───────────────────────────────
            // base_so101_v2 bounds: min=(−0.06,−0.02,0) max=(0.06,0.07,0.07)
            // Shift up so min.y (−0.02) lands at world y=0 → offset.y = +0.02
            // Centre Z extent (0→0.07) → offset.z = −0.035
            // Scale 1.15 so the 0.09m mesh roughly fills the 0.10m base.
            list.Add(new StlLinkEntry
            {
                linkName      = "Base",
                stlFile       = "base_so101_v2.stl",
                localPosition = new Vector3(0f, 0.02f, -0.035f),
                localEuler    = Vector3.zero,
                uniformScale  = 1.15f
            });

            // ── BASEYAW (j0, yaw turntable, link 0.05 m) ─────────────────────────
            // rotation_pitch bounds: min=(−0.06,−0.02,0) max=(0,0.02,0.08)  centre=(−0.03,0,0.04)
            // After Euler(0,90,0): child (x,y,z) → parent (z, y, −x)
            //   Mesh centre (−0.03,0,0.04) → parent (0.04, 0, 0.03)
            //   To land at parent origin: localPosition = (−0.04, 0, −0.03)
            //   → world X range [−0.04, +0.04] and Z [−0.03, +0.03]  (centred ✓)
            list.Add(new StlLinkEntry
            {
                linkName      = "BaseYaw",
                stlFile       = "rotation_pitch_so101_v1.stl",
                localPosition = new Vector3(-0.04f, 0.01f, -0.03f),
                localEuler    = new Vector3(0f, 90f, 0f),
                uniformScale  = 1f
            });

            // ── SHOULDER (j1, pitch, link 0.28 m along +Y) ───────────────────────
            // upper_arm bounds: min=(−0.07,−0.04,0) max=(0.08,0.03,0.02)
            // The mesh lies along X (0.15m span). Rotate Euler(0,0,90) so that:
            //   child +X → parent +Y  (arm stands upright along link direction)
            // Scale = 0.28 / 0.15 = 1.867
            // After rotation, mesh min.x (−0.07) maps to parent y = −0.07 * scale.
            // Offset.y = +min_x_abs * scale = 0.07 * 1.867 = 0.1307 (ground at y=0)
            // Centre X: mesh center_y (−0.005) → parent X displacement → offset.x = +0.005*scale
            // Centre Z: mesh center_z (0.01) stays along Z → offset.z = −0.01*scale
            // Euler(0,0,90): child (x,y,z) → parent (−y, x, z)
            // Mesh centre (0.005,−0.005,0.01) → parent (0.005, 0.005, 0.01)
            // To cancel: localPosition = (−0.005*s, −0.005*s+0.07*s, −0.01*s)
            //   The +0.07*s grounds mesh min_x (−0.07) at parent y=0:
            //   min_x=−0.07 → parent_y = −(−0.07)*s = 0.07*s  [plus localPos.y = −0.07*s → net 0] wait
            //   Actually with Euler(0,0,90): child_x → parent_y  (not −y).
            //   Checking: Unity Euler(0,0,90) rotates CCW about Z looking toward +Z.
            //   child +X → rotated 90° CCW = parent +Y. child +Y → parent −X.
            //   So: parent_x = −child_y, parent_y = child_x, parent_z = child_z.
            //   Mesh centre: child(0.005,−0.005,0.01) → parent(0.005, 0.005, 0.01)
            //   mesh_min.x (−0.07) → parent_y = −0.07*s  → net = localPos.y + (−0.07*s) = 0 → localPos.y = 0.07*s ✓
            //   To zero parent_x:  localPos.x = −(−child_y*s) = +child_y*s = −0.005*s? No:
            //     parent_x = −child_y*s → mesh_centre gives parent_x = −(−0.005)*s = 0.005*s
            //     cancel: localPos.x = −0.005*s
            float sScale = 0.28f / 0.15f;
            list.Add(new StlLinkEntry
            {
                linkName      = "Shoulder",
                stlFile       = "upper_arm_so101_v1.stl",
                localPosition = new Vector3(-0.005f * sScale, 0.07f * sScale, -0.01f * sScale),
                localEuler    = new Vector3(0f, 0f, 90f),
                uniformScale  = sScale
            });

            // ── ELBOW (j2, pitch, link 0.25 m along +Y) ──────────────────────────
            // under_arm bounds: min=(−0.05,−0.03,−0.04) max=(0.08,0.03,−0.02)
            // Mesh lies along X (0.13m span). Same Euler(0,0,90) treatment.
            // Scale = 0.25 / 0.13 = 1.923
            // Offset.y = 0.05 * scale (ground mesh_min.x at y=0)
            // Center Z: center_z = −0.03 → keep as-is (the mesh is already offset in Z)
            float eScale = 0.25f / 0.13f;
            list.Add(new StlLinkEntry
            {
                linkName      = "Elbow",
                stlFile       = "under_arm_so101_v1.stl",
                localPosition = new Vector3(0f, 0.05f * eScale, 0.03f * eScale),
                localEuler    = new Vector3(0f, 0f, 90f),
                uniformScale  = eScale
            });

            // ── FOREARMROLL (j3, roll DOF, link 0.06 m) ───────────────────────────
            // Use wrist_roll_pitch which is the compact wrist block (±0.04m in Y).
            // Scale 1.0 so it's clearly visible; shift up by 0.03 (half link).
            // This block sits at the elbow-to-wrist transition — deliberately
            // slightly oversized to visually bridge the gap from the elbow.
            list.Add(new StlLinkEntry
            {
                linkName      = "ForearmRoll",
                stlFile       = "wrist_roll_pitch_so101_v2.stl",
                localPosition = new Vector3(0f, 0.03f, 0f),
                localEuler    = Vector3.zero,
                uniformScale  = 1.0f
            });

            // ── WRISTPITCH (j4, pitch, link 0.06 m) ──────────────────────────────
            // wrist_roll_follower: Y goes from 0→0.11 m. Scale so it spans 0.06m: 0.545.
            // Use 0.75 for better visibility — acceptable slight overshoot.
            list.Add(new StlLinkEntry
            {
                linkName      = "WristPitch",
                stlFile       = "wrist_roll_follower_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = Vector3.zero,
                uniformScale  = 0.75f
            });

            // ── WRISTROLL (j5, roll, link 0.04 m) ────────────────────────────────
            // Final roll joint — use wrist_roll_pitch rotated 90° X (flat disc).
            list.Add(new StlLinkEntry
            {
                linkName      = "WristRoll",
                stlFile       = "wrist_roll_pitch_so101_v2.stl",
                localPosition = new Vector3(0f, 0.02f, 0f),
                localEuler    = new Vector3(90f, 0f, 0f),
                uniformScale  = 0.7f
            });

            // ── GRIPPER palm ──────────────────────────────────────────────────────
            // wrist_roll_follower scaled down as a compact palm between wrist and jaws.
            list.Add(new StlLinkEntry
            {
                linkName      = "Gripper",
                stlFile       = "wrist_roll_follower_so101_v1.stl",
                localPosition = new Vector3(0f, 0f, 0f),
                localEuler    = Vector3.zero,
                uniformScale  = 0.48f
            });

            // ── LEFT JAW ─────────────────────────────────────────────────────────
            // moving_jaw: Z-extent (−0.08 to +0.01 = 0.09 m) in Unity space.
            // Euler(90,0,0) maps child +Z → parent +Y (finger points up from palm).
            // mesh z_min = −0.08 → parent y displacement = −0.08 (below palm origin).
            // offset.y = +0.08 * scale to lift finger so its base sits at y=0.
            // Scale 0.8 → finger length 0.072 m (matches gripperLength=0.06 well).
            float jawScale = 0.8f;
            list.Add(new StlLinkEntry
            {
                linkName      = "LeftJaw",
                stlFile       = "moving_jaw_so101_v1.stl",
                localPosition = new Vector3(0f, 0.08f * jawScale, 0f),
                localEuler    = new Vector3(90f, 0f, 0f),
                uniformScale  = jawScale
            });

            // ── RIGHT JAW ────────────────────────────────────────────────────────
            // Mirror of left jaw — 180° around Y so fingers face each other.
            list.Add(new StlLinkEntry
            {
                linkName      = "RightJaw",
                stlFile       = "moving_jaw_so101_v1.stl",
                localPosition = new Vector3(0f, 0.08f * jawScale, 0f),
                localEuler    = new Vector3(90f, 180f, 0f),
                uniformScale  = jawScale
            });

            return list;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        static Transform FindBodyTransform(ProceduralArm arm, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (arm.baseBody != null &&
                arm.baseBody.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.baseBody.transform;

            if (arm.jointBodies != null)
            {
                foreach (var ab in arm.jointBodies)
                {
                    if (ab != null &&
                        ab.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return ab.transform;
                }
            }

            if (arm.leftJaw != null &&
                arm.leftJaw.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.leftJaw.transform;

            if (arm.rightJaw != null &&
                arm.rightJaw.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return arm.rightJaw.transform;

            var gripperTf = arm.transform.Find("Gripper")
                         ?? FindDescendant(arm.transform, "Gripper");
            if (gripperTf != null &&
                gripperTf.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return gripperTf;

            return null;
        }

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

        static void AttachStlVisual(Transform parent, Mesh mesh, StlLinkEntry entry)
        {
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
