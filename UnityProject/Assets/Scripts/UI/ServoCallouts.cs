using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Interactive servo callouts. Each joint gets a clickable hotspot (a small collider sphere at the
    /// joint). Click it -> a leader line is drawn from the joint to a floating panel showing that motor's
    /// command + output: name, key, current angle, target angle, servo tick (0-4096), axis direction, and
    /// limits. Multiple can be pinned at once. This helps see how to activate each motor and diagnose
    /// wrong/impossible directions (the axis arrow shows which way + is). Toggle all with F12... (key in
    /// GameBootstrap). Click empty space to clear.
    /// </summary>
    public class ServoCallouts : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public Camera cam;
        Canvas canvas;

        class Callout
        {
            public int joint;
            public GameObject hotspot;     // 3D clickable sphere at the joint
            public RectTransform panel;    // UI panel
            public Text text;
            public UILine line;            // leader line
            public bool pinned;
        }

        readonly List<Callout> callouts = new List<Callout>();
        public bool enabledCallouts = false;   // off by default; press \ to show clickable servo hotspots

        public void Build(ProceduralArm a, ArmController c, Camera camera, Canvas hudCanvas)
        {
            arm = a; controller = c; cam = camera; canvas = hudCanvas;
            for (int i = 0; i < arm.jointBodies.Count; i++)
                callouts.Add(MakeCallout(i));
        }

        Callout MakeCallout(int joint)
        {
            var co = new Callout { joint = joint };

            // 3D clickable hotspot at the joint
            var hs = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hs.name = $"ServoHotspot_{joint}";
            hs.transform.localScale = Vector3.one * 0.03f;
            var mr = hs.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(1f, 0.8f, 0.1f, 1f) };
            var col = hs.GetComponent<SphereCollider>(); col.isTrigger = false; // raycastable
            co.hotspot = hs;

            // UI panel (hidden until clicked)
            var pgo = new GameObject($"Callout_{joint}");
            pgo.transform.SetParent(canvas.transform, false);
            var img = pgo.AddComponent<Image>();
            img.color = new Color(0.06f, 0.09f, 0.12f, 0.92f);
            co.panel = img.rectTransform;
            co.panel.anchorMin = Vector2.zero; co.panel.anchorMax = Vector2.zero;  // (0,0) so screen px maps
            co.panel.sizeDelta = new Vector2(230, 96);
            co.panel.pivot = new Vector2(0, 0.5f);

            var tgo = new GameObject("t"); tgo.transform.SetParent(pgo.transform, false);
            co.text = tgo.AddComponent<Text>();
            co.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            co.text.fontSize = 13; co.text.color = Color.white; co.text.supportRichText = true;
            var trt = co.text.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 4); trt.offsetMax = new Vector2(-6, -4);

            // leader line (UI) — full-screen rect anchored at (0,0) so screen coords map directly.
            var lgo = new GameObject($"Line_{joint}"); lgo.transform.SetParent(canvas.transform, false);
            co.line = lgo.AddComponent<UILine>();
            co.line.color = new Color(1f, 0.8f, 0.1f, 0.9f);
            var lrt = co.line.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.zero; lrt.pivot = Vector2.zero;
            lrt.anchoredPosition = Vector2.zero; lrt.sizeDelta = Vector2.zero;

            pgo.SetActive(false); lgo.SetActive(false);
            return co;
        }

        void Update()
        {
            if (cam == null) return;

            // toggle servo-callout hotspots (\ key)
            if (Input.GetKeyDown(KeyCode.Backslash)) enabledCallouts = !enabledCallouts;

            // hotspots follow joints
            for (int i = 0; i < callouts.Count; i++)
                callouts[i].hotspot.transform.position = arm.jointBodies[i].transform.position;

            // click handling (left click, only when not dragging IK / shift etc.)
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftShift))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 6f))
                {
                    for (int i = 0; i < callouts.Count; i++)
                        if (hit.collider.gameObject == callouts[i].hotspot)
                        { callouts[i].pinned = !callouts[i].pinned; break; }
                }
            }

            // update visuals
            foreach (var co in callouts)
            {
                bool vis = enabledCallouts && co.pinned;
                co.panel.gameObject.SetActive(vis);
                co.line.gameObject.SetActive(vis);
                co.hotspot.SetActive(enabledCallouts);
                if (!vis) continue;

                Vector3 worldJoint = arm.jointBodies[co.joint].transform.position;
                Vector3 screen = cam.WorldToScreenPoint(worldJoint);
                if (screen.z < 0) { co.panel.gameObject.SetActive(false); co.line.gameObject.SetActive(false); continue; }

                // place the panel offset to the right of the joint
                Vector2 panelPos = new Vector2(screen.x + 40, screen.y);
                co.panel.position = panelPos;
                co.line.SetPoints(new Vector2(screen.x, screen.y), panelPos);
                co.text.text = Info(co.joint);
            }
        }

        /// <summary>Pin/unpin a joint's callout by index (for scripting/agent use).</summary>
        public void SetPinned(int joint, bool on)
        {
            foreach (var co in callouts) if (co.joint == joint) co.pinned = on;
            enabledCallouts = true;
        }

        string Info(int i)
        {
            var js = arm.jointSpecs[i];
            float cur = arm.GetJointAngles()[i];
            float tgt = controller != null && controller.TargetAngles != null && i < controller.TargetAngles.Length ? controller.TargetAngles[i] : cur;
            int tick = i < arm.servos.Count ? arm.servos[i].AngleToTick(cur) : 0;
            string key = ArmController.JointKeyLabel(i);
            var sb = new StringBuilder();
            sb.AppendLine($"<b><color=#fc6>#{i + 1} {js.name}</color></b>  key {key}");
            sb.AppendLine($"angle {cur:F1}\u00b0 \u2192 target {tgt:F1}\u00b0");
            sb.AppendLine($"<color=#8f8>tick {tick}/4096</color>  limits [{js.minAngle:F0}..{js.maxAngle:F0}]\u00b0");
            return sb.ToString();
        }
    }

    /// <summary>Minimal UI line renderer (a thin rotated Image) for leader lines on the HUD canvas.</summary>
    public class UILine : Graphic
    {
        Vector2 a, b;
        public void SetPoints(Vector2 p0, Vector2 p1) { a = p0; b = p1; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 dir = (b - a);
            float len = dir.magnitude;
            if (len < 0.01f) return;
            dir /= len;
            Vector2 nrm = new Vector2(-dir.y, dir.x) * 1.5f; // half-thickness 1.5px
            // rect anchored at (0,0) on a ScreenSpaceOverlay canvas -> screen coords == local coords.
            var v = UIVertex.simpleVert; v.color = color;
            v.position = a - nrm; vh.AddVert(v);
            v.position = a + nrm; vh.AddVert(v);
            v.position = b + nrm; vh.AddVert(v);
            v.position = b - nrm; vh.AddVert(v);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }
}
