using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Modules
{
    /// <summary>
    /// KSP-style in-scene ATTACH-NODE markers: a small glowing ring at every valid mount socket on the arm,
    /// shown only while the player is in the Build/Modules attachment mode. Makes it obvious WHERE parts can
    /// go (the way KSP highlights green attach nodes). Markers are non-colliding and parented to their link
    /// so they track the arm as it moves. Toggle with SetShown(true/false).
    /// </summary>
    public class MountNodeViz : MonoBehaviour
    {
        public ProceduralArm arm;
        public ModuleMount mount;
        System.Func<Color, Material> _mat;
        readonly List<GameObject> _markers = new List<GameObject>();
        bool _shown;

        public void Bind(ProceduralArm a, ModuleMount m, System.Func<Color, Material> matFactory)
        { arm = a; mount = m; _mat = matFactory; }

        public bool Shown => _shown;

        public void SetShown(bool on)
        {
            if (on == _shown && (_markers.Count > 0 || !on)) { if (_shown) Refresh(); return; }
            _shown = on;
            if (on) Build(); else ClearMarkers();
        }

        void Build()
        {
            ClearMarkers();
            if (arm == null || mount == null) return;
            var teal = _mat != null ? _mat(new Color(0f, 0.83f, 0.71f)) : Fallback(new Color(0f, 0.83f, 0.71f));
            foreach (var mp in mount.mountPoints)
            {
                var link = mp.Link(arm);
                if (link == null) continue;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = marker.GetComponent<Collider>(); if (col != null) DestroyImmediate(col);
                marker.name = "MountNode_" + mp.name;
                marker.transform.SetParent(link, false);
                marker.transform.localPosition = mp.localPosition;
                marker.transform.localRotation = Quaternion.Euler(mp.localEuler) * Quaternion.Euler(90, 0, 0);
                marker.transform.localScale = new Vector3(0.03f, 0.002f, 0.03f);   // flat disc
                marker.GetComponent<MeshRenderer>().sharedMaterial = teal;
                _markers.Add(marker);
            }
        }

        void Refresh() { }   // markers are parented, so they follow the arm automatically

        void ClearMarkers()
        {
            foreach (var m in _markers) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
            _markers.Clear();
        }

        static Material Fallback(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(sh) { color = c };
        }

        void OnDestroy() { ClearMarkers(); }
    }
}
