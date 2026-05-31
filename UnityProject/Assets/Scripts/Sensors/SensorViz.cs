using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Visualises what the range sensors "see": draws the RangeFinder single point + its ray, and the
    /// Lidar2D fan of rays with hit points, and the DepthCamera grid rays. Toggle the whole overlay with
    /// 'L'; cycle which sensor is shown with Shift+L. Uses GL lines so it renders in play mode.
    /// Lets the player see + toggle the sensor footprints (I: "see what the lidar sees and the single points").
    /// </summary>
    public class SensorViz : MonoBehaviour
    {
        public SensorHub hub;
        public bool show = true;
        public enum View { All, RangeFinder, Lidar2D, DepthCamera }
        public View view = View.All;

        static Material lineMat;

        public void Bind(SensorHub h) { hub = h; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.L) && !Input.GetKey(KeyCode.LeftShift)) show = !show;
            if (Input.GetKeyDown(KeyCode.L) && Input.GetKey(KeyCode.LeftShift))
                view = (View)(((int)view + 1) % 4);
        }

        void EnsureMat()
        {
            if (lineMat != null) return;
            lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_ZWrite", 0); lineMat.SetInt("_Cull", 0);
        }

        void OnRenderObject()
        {
            if (!show || hub == null) return;
            EnsureMat(); lineMat.SetPass(0);
            GL.PushMatrix(); GL.Begin(GL.LINES);

            if (view == View.All || view == View.RangeFinder) DrawRangeFinder();
            if (view == View.All || view == View.Lidar2D) DrawLidar();
            if (view == View.All || view == View.DepthCamera) DrawDepth();

            GL.End(); GL.PopMatrix();
        }

        void DrawRangeFinder()
        {
            var s = hub.Get("RangeFinder") as RangeFinderSensor;
            if (s == null || !s.Enabled || s.origin == null) return;
            Vector3 dir = s.origin.TransformDirection(s.localDir).normalized;
            float d = s.Observe()[0];
            Vector3 hit = s.origin.position + dir * d;
            GL.Color(new Color(1f, 0.2f, 0.2f, 0.9f));
            Line(s.origin.position, hit);
            // crosshair at the single point
            GL.Color(Color.red);
            Cross(hit, 0.02f);
        }

        void DrawLidar()
        {
            var s = hub.Get("Lidar2D") as Lidar2DSensor;
            if (s == null || !s.Enabled || s.origin == null) return;
            float[] r = s.Observe();
            for (int i = 0; i < r.Length; i++)
            {
                float ang = -s.fovDeg * 0.5f + s.fovDeg * (i / (float)Mathf.Max(1, r.Length - 1));
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * s.origin.forward;
                Vector3 hit = s.origin.position + dir * r[i];
                GL.Color(new Color(0.2f, 0.8f, 1f, 0.7f));
                Line(s.origin.position, hit);
                GL.Color(new Color(0.4f, 1f, 1f, 1f));
                Cross(hit, 0.012f);
            }
        }

        void DrawDepth()
        {
            var s = hub.Get("DepthCamera") as DepthCameraSensor;
            if (s == null || !s.Enabled || s.cam == null) return;
            float[] d = s.Observe();
            int g = Mathf.RoundToInt(Mathf.Sqrt(d.Length));
            GL.Color(new Color(0.6f, 1f, 0.4f, 0.5f));
            for (int y = 0; y < g; y++)
                for (int x = 0; x < g; x++)
                {
                    Vector3 vp = new Vector3((x + 0.5f) / g, (y + 0.5f) / g, 0f);
                    Ray ray = s.cam.ViewportPointToRay(vp);
                    Vector3 hit = ray.origin + ray.direction * d[y * g + x];
                    Cross(hit, 0.008f);
                }
        }

        static void Line(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }
        static void Cross(Vector3 p, float s)
        {
            GL.Vertex(p - Vector3.right * s); GL.Vertex(p + Vector3.right * s);
            GL.Vertex(p - Vector3.up * s); GL.Vertex(p + Vector3.up * s);
            GL.Vertex(p - Vector3.forward * s); GL.Vertex(p + Vector3.forward * s);
        }

        public string Status() => show ? $"sensor viz: {view}" : "sensor viz: off";
    }
}
