using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Modules
{
    /// <summary>
    /// KSP-style modular ATTACHMENT system. The player picks a part from a bin and snaps it onto a mount
    /// socket on the arm; the part appears as a real 3D object parented to that link, can be re-posed, and
    /// (for cameras/sensors) drives the matching sensor. Parts are built procedurally from primitives so no
    /// asset import is needed.
    ///
    /// One AttachmentSystem lives in the scene (created by GameBootstrap), bound to the arm + ModuleMount.
    /// </summary>
    public enum PartKind { Camera, RangeFinder, Lidar, Imu, Tactile, Light, Bracket, Counterweight }

    [Serializable]
    public class PartDef
    {
        public string id;
        public string name;
        public PartKind kind;
        public string blurb;
        public float massKg;
        public Color color;
        public string sensorType;     // links to SensorHub module name (or null for purely structural parts)
    }

    /// <summary>A part placed on the arm: which catalog def, which link/socket, local pose, the live GO,
    /// and (for cameras) its Camera + RenderTexture.</summary>
    [Serializable]
    public class PlacedPart
    {
        public string defId;
        public int linkIndex;          // -1 = base
        public Vector3 localPos;
        public Vector3 localEuler;
        public float scale = 1f;
        [NonSerialized] public GameObject go;
        [NonSerialized] public Camera camera;
        [NonSerialized] public RenderTexture rt;
        [NonSerialized] public PartDef def;
    }

    public class AttachmentSystem : MonoBehaviour
    {
        public ProceduralArm arm;
        public ModuleMount mount;
        public SensorHub sensorHub;

        public readonly List<PlacedPart> placed = new List<PlacedPart>();
        public event Action Changed;

        Func<Color, Material> _mat;

        public void Bind(ProceduralArm a, ModuleMount m, SensorHub hub, Func<Color, Material> matFactory)
        {
            arm = a; mount = m; sensorHub = hub; _mat = matFactory;
        }

        // ── PART CATALOG (the "parts bin") ───────────────────────────────────────────────────────────────
        public static readonly PartDef[] Catalog =
        {
            new PartDef{ id="cam_wrist",  name="Wrist Camera",     kind=PartKind.Camera,      blurb="RGB camera on a mount — point it at the workspace.", massKg=0.03f, color=Hex("4da6f2"), sensorType="DepthCamera" },
            new PartDef{ id="cam_env",    name="Overview Camera",  kind=PartKind.Camera,      blurb="Wide-angle scene camera for a 3rd-person feed.",     massKg=0.04f, color=Hex("59cc66"), sensorType=null },
            new PartDef{ id="range",      name="ToF Rangefinder",  kind=PartKind.RangeFinder, blurb="1-point distance sensor — approach/descent timing.", massKg=0.01f, color=Hex("fa9e33"), sensorType="RangeFinder" },
            new PartDef{ id="lidar",      name="2D Lidar",         kind=PartKind.Lidar,       blurb="Planar fan scan of the surroundings.",               massKg=0.05f, color=Hex("ff6b2b"), sensorType="Lidar2D" },
            new PartDef{ id="imu",        name="IMU",              kind=PartKind.Imu,         blurb="Orientation + acceleration sensing.",                massKg=0.005f,color=Hex("b373f2"), sensorType="IMU" },
            new PartDef{ id="tactile",    name="EFlesh Tactile",   kind=PartKind.Tactile,     blurb="Fingertip contact force — best for grasping.",       massKg=0.008f,color=Hex("39ff82"), sensorType="EFleshTactile" },
            new PartDef{ id="light",      name="LED Spotlight",    kind=PartKind.Light,       blurb="Illuminates the workspace for the cameras.",         massKg=0.02f, color=Hex("ffcc00"), sensorType=null },
            new PartDef{ id="bracket",    name="Riser Bracket",    kind=PartKind.Bracket,     blurb="Structural standoff — raises a mounted part.",       massKg=0.02f, color=Hex("4a6070"), sensorType=null },
            new PartDef{ id="weight",     name="Counterweight",    kind=PartKind.Counterweight,blurb="Adds mass to balance a heavy payload.",             massKg=0.10f, color=Hex("c8d8e4"), sensorType=null },
        };

        public static PartDef GetDef(string id) { foreach (var p in Catalog) if (p.id == id) return p; return null; }
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        // ── PLACE / MOVE / REMOVE ────────────────────────────────────────────────────────────────────────

        /// <summary>Attach a part to a link at a local pose. Returns the PlacedPart (with its live GO).</summary>
        public PlacedPart Place(string defId, int linkIndex, Vector3 localPos, Vector3 localEuler, float scale = 1f)
        {
            var def = GetDef(defId); if (def == null || arm == null) return null;
            Transform parent = LinkTransform(linkIndex);
            if (parent == null) return null;

            var pp = new PlacedPart { defId = defId, def = def, linkIndex = linkIndex, localPos = localPos, localEuler = localEuler, scale = scale };
            pp.go = ModuleParts.Build(def, _mat);
            pp.go.transform.SetParent(parent, false);
            pp.go.transform.localPosition = localPos;
            pp.go.transform.localRotation = Quaternion.Euler(localEuler);
            pp.go.transform.localScale = Vector3.one * scale;

            // cameras get a real Camera + RenderTexture so the player can see their feed
            if (def.kind == PartKind.Camera) AttachCamera(pp);
            if (def.kind == PartKind.Light) AttachLight(pp);

            // enable the matching sensor module so the part actually contributes to the policy
            if (!string.IsNullOrEmpty(def.sensorType) && sensorHub != null) sensorHub.SetEnabled(def.sensorType, true);

            placed.Add(pp);
            Changed?.Invoke();
            return pp;
        }

        public void Move(PlacedPart pp, Vector3 localPos, Vector3 localEuler, float scale)
        {
            if (pp?.go == null) return;
            pp.localPos = localPos; pp.localEuler = localEuler; pp.scale = scale;
            pp.go.transform.localPosition = localPos;
            pp.go.transform.localRotation = Quaternion.Euler(localEuler);
            pp.go.transform.localScale = Vector3.one * Mathf.Max(0.2f, scale);
            Changed?.Invoke();
        }

        public void Remove(PlacedPart pp)
        {
            if (pp == null) return;
            if (pp.rt != null) { pp.rt.Release(); pp.rt = null; }
            if (pp.go != null) Destroy(pp.go);
            placed.Remove(pp);
            // disable the matching sensor if no other placed part uses it
            if (pp.def != null && !string.IsNullOrEmpty(pp.def.sensorType) && sensorHub != null)
            {
                bool stillUsed = false; foreach (var o in placed) if (o.def != null && o.def.sensorType == pp.def.sensorType) { stillUsed = true; break; }
                if (!stillUsed) sensorHub.SetEnabled(pp.def.sensorType, false);
            }
            Changed?.Invoke();
        }

        public void Clear() { for (int i = placed.Count - 1; i >= 0; i--) Remove(placed[i]); }

        public float TotalAddedMass() { float m = 0f; foreach (var p in placed) if (p.def != null) m += p.def.massKg; return m; }

        // ── camera / light helpers ──────────────────────────────────────────────────────────────────────
        void AttachCamera(PlacedPart pp)
        {
            var camGo = new GameObject("PartCam");
            camGo.transform.SetParent(pp.go.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, 0.02f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 5f;
            cam.depth = -5;   // render to texture only
            pp.rt = new RenderTexture(256, 256, 16) { name = "PartCamRT_" + pp.defId };
            cam.targetTexture = pp.rt;
            pp.camera = cam;
        }

        void AttachLight(PlacedPart pp)
        {
            var lgo = new GameObject("PartLight");
            lgo.transform.SetParent(pp.go.transform, false);
            lgo.transform.localPosition = new Vector3(0, 0, 0.02f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 1.2f; l.spotAngle = 50f; l.intensity = 3f; l.color = pp.def.color;
        }

        Transform LinkTransform(int linkIndex)
        {
            if (arm == null) return null;
            if (linkIndex < 0) return arm.baseBody != null ? arm.baseBody.transform : arm.transform;
            return linkIndex < arm.jointBodies.Count ? arm.jointBodies[linkIndex].transform : arm.transform;
        }

        // ── persistence (so a built loadout survives save/load) ──────────────────────────────────────────
        [Serializable] public class SaveBlob { public List<PlacedPart> parts = new List<PlacedPart>(); }

        public string ToJson()
        {
            var blob = new SaveBlob();
            foreach (var p in placed) blob.parts.Add(new PlacedPart { defId = p.defId, linkIndex = p.linkIndex, localPos = p.localPos, localEuler = p.localEuler, scale = p.scale });
            return JsonUtility.ToJson(blob);
        }

        public void FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            Clear();
            var blob = JsonUtility.FromJson<SaveBlob>(json);
            if (blob?.parts == null) return;
            foreach (var p in blob.parts) Place(p.defId, p.linkIndex, p.localPos, p.localEuler, p.scale <= 0 ? 1f : p.scale);
        }
    }
}
