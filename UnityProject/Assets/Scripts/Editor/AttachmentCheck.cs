#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using ArmSmith.Modules;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless gate for the KSP-style 3D ATTACHMENT system:
    ///   - the part catalog is non-empty and every def builds a 3D GameObject (procedural mesh, no collider).
    ///   - Place() parents the part to the chosen link, enables the matching sensor, and (for cameras) makes
    ///     a Camera + RenderTexture.
    ///   - Move() repositions/rescales; Remove() destroys + disables the sensor if unused.
    ///   - ToJson/FromJson round-trips a placed loadout (so it survives save/load).
    ///
    /// Run: -executeMethod ArmSmith.EditorTools.AttachmentCheck.RunHeadless
    /// </summary>
    public static class AttachmentCheck
    {
        [MenuItem("ARMSMITH/Run Attachment Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(17); }

        public static bool Run()
        {
            int pass = 0, fail = 0;
            void Check(string label, bool cond) { if (cond) pass++; else { fail++; Debug.LogError($"[AttachmentCheck] FAIL: {label}"); } }

            var spawned = new List<GameObject>();
            try
            {
                // catalog + procedural mesh build
                Check("catalog non-empty", AttachmentSystem.Catalog.Length >= 6);
                System.Func<Color, Material> mat = c => new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = c };
                foreach (var def in AttachmentSystem.Catalog)
                {
                    var go = ModuleParts.Build(def, mat);
                    bool ok = go != null && go.GetComponentInChildren<MeshRenderer>() != null;
                    bool noCollider = go != null && go.GetComponentInChildren<Collider>() == null;
                    Check($"part {def.id} builds a mesh", ok);
                    Check($"part {def.id} has no collider", noCollider);
                    if (go != null) Object.DestroyImmediate(go);
                }

                // real arm + hub to place onto
                string kin = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                var armGo = new GameObject("Arm"); spawned.Add(armGo);
                var arm = armGo.AddComponent<ProceduralArm>();
                if (System.IO.File.Exists(kin)) arm.BuildFromKinematics(kin); else arm.Build(ArmConfig.CreateStarter());
                Check("arm built", arm.baseBody != null);

                var hubGo = new GameObject("Hub"); spawned.Add(hubGo);
                var hub = hubGo.AddComponent<SensorHub>(); hub.Init(arm, null);

                var atGo = new GameObject("Attach"); spawned.Add(atGo);
                var at = atGo.AddComponent<AttachmentSystem>();
                at.Bind(arm, null, hub, mat);

                // ensure the camera sensor starts disabled so we can prove Place enables it
                if (hub.Get("DepthCamera") != null) hub.Get("DepthCamera").Enabled = false;

                int wrist = Mathf.Max(0, arm.jointSpecs.Count - 2);
                var cam = at.Place("cam_wrist", wrist, new Vector3(0, 0.03f, 0.04f), Vector3.zero, 1f);
                Check("camera placed", cam != null && cam.go != null);
                Check("camera parented to link", cam != null && cam.go.transform.parent == arm.jointBodies[wrist].transform);
                Check("camera has Camera+RT", cam != null && cam.camera != null && cam.rt != null);
                Check("placing camera enabled DepthCamera sensor", hub.Get("DepthCamera") != null && hub.Get("DepthCamera").Enabled);

                var range = at.Place("range", wrist, new Vector3(0.02f, 0.03f, 0.02f), Vector3.zero, 1f);
                Check("rangefinder placed", range != null);
                Check("two parts placed", at.placed.Count == 2);
                Check("total mass sums", Mathf.Abs(at.TotalAddedMass() - (0.03f + 0.01f)) < 1e-3f);

                // move
                at.Move(cam, new Vector3(0.06f, 0.06f, 0.08f), new Vector3(20, 0, 0), 1.8f);
                Check("move applies scale", Mathf.Abs(cam.go.transform.localScale.x - 1.8f) < 1e-3f);
                Check("move applies position", Mathf.Abs(cam.go.transform.localPosition.z - 0.08f) < 1e-3f);

                // json round-trip
                string json = at.ToJson();
                at.Clear();
                Check("clear removes all", at.placed.Count == 0);
                at.FromJson(json);
                Check("restored count", at.placed.Count == 2);
                Check("restored defId", at.placed[0].defId == "cam_wrist");

                // remove disables sensor when no part uses it
                var depthParts = at.placed.FindAll(p => p.defId == "cam_wrist");
                foreach (var p in new List<PlacedPart>(depthParts)) at.Remove(p);
                Check("removing camera disabled DepthCamera", hub.Get("DepthCamera") == null || !hub.Get("DepthCamera").Enabled);

                // MountNodeViz: builds collider-free markers when shown, clears when hidden
                var mmGo = new GameObject("MM"); spawned.Add(mmGo);
                var mm = mmGo.AddComponent<ModuleMount>(); mm.Setup(arm);
                var mvGo = new GameObject("MV"); spawned.Add(mvGo);
                var mv = mvGo.AddComponent<MountNodeViz>(); mv.Bind(arm, mm, mat);
                mv.SetShown(true);
                int markers = 0; foreach (Transform t in EnumerateChildren(arm)) if (t.name.StartsWith("MountNode_")) markers++;
                Check("mount-node markers shown", markers > 0);
                bool markerNoCollider = true; foreach (Transform t in EnumerateChildren(arm)) if (t.name.StartsWith("MountNode_") && t.GetComponent<Collider>() != null) markerNoCollider = false;
                Check("markers have no collider", markerNoCollider);
                mv.SetShown(false);
                int after = 0; foreach (Transform t in EnumerateChildren(arm)) if (t.name.StartsWith("MountNode_")) after++;
                Check("markers cleared on hide", after == 0);
            }
            catch (System.Exception e) { Debug.LogError("[AttachmentCheck] " + e); fail++; }
            finally { for (int i = spawned.Count - 1; i >= 0; i--) if (spawned[i] != null) Object.DestroyImmediate(spawned[i]); }

            bool ok2 = fail == 0;
            Debug.Log(ok2
                ? $"[AttachmentCheck] PASSED — {pass} assertions (parts build + place/parent/sensor + move + json round-trip + remove + mount-nodes)."
                : $"[AttachmentCheck] FAILED — {fail} of {pass + fail} assertions failed.");
            return ok2;
        }

        static IEnumerable<Transform> EnumerateChildren(ProceduralArm arm)
        {
            if (arm == null) yield break;
            var roots = new List<Transform>();
            if (arm.baseBody != null) roots.Add(arm.baseBody.transform);
            foreach (var jb in arm.jointBodies) if (jb != null) roots.Add(jb.transform);
            foreach (var r in roots)
                foreach (Transform t in r.GetComponentsInChildren<Transform>())
                    yield return t;
        }
    }
}
#endif
