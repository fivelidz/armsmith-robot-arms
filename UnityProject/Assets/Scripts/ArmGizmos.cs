using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Robotics visual conventions (research/manipulation_repos/TEST_ENVIRONMENTS.md sec 5):
    ///  - per-joint axis line (the rotation axis) so the player sees how each joint moves
    ///  - end-effector RGB frame triad (X=red, Y=green, Z=blue)
    ///  - translucent workspace reach hemisphere
    ///  - line from end-effector to the IK target
    /// Uses GL line drawing in OnRenderObject so it works in play mode without gizmos.
    /// </summary>
    public class ArmGizmos : MonoBehaviour
    {
        public ProceduralArm arm;
        public Transform ikTarget;
        public bool showAxes = true;
        public bool showWorkspace = false;
        public bool showTargetLine = true;

        static Material lineMat;

        void EnsureMat()
        {
            if (lineMat != null) return;
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_Cull", 0);
            lineMat.SetInt("_ZWrite", 0);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.B)) showWorkspace = !showWorkspace;
            if (Input.GetKeyDown(KeyCode.X)) showAxes = !showAxes;
        }

        void OnRenderObject()
        {
            if (arm == null || arm.jointBodies.Count == 0) return;
            EnsureMat();
            lineMat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            if (showAxes)
            {
                for (int i = 0; i < arm.jointBodies.Count; i++)
                {
                    var ab = arm.jointBodies[i];
                    Vector3 axis = ab.transform.TransformDirection(arm.config.AxisVector(arm.jointSpecs[i].axis));
                    Vector3 p = ab.transform.position;
                    GL.Color(new Color(1f, 0.9f, 0.1f, 0.9f));
                    Line(p - axis * 0.04f, p + axis * 0.04f);
                }
                // EE frame triad
                if (arm.endEffector != null)
                {
                    var t = arm.endEffector;
                    GL.Color(Color.red);   Line(t.position, t.position + t.right * 0.05f);
                    GL.Color(Color.green); Line(t.position, t.position + t.up * 0.05f);
                    GL.Color(Color.blue);  Line(t.position, t.position + t.forward * 0.05f);
                }
            }

            if (showTargetLine && ikTarget != null && arm.endEffector != null)
            {
                GL.Color(new Color(0.2f, 0.9f, 0.3f, 0.5f));
                Line(arm.endEffector.position, ikTarget.position);
            }

            if (showWorkspace)
            {
                Vector3 c = arm.baseBody != null ? arm.baseBody.transform.position : transform.position;
                float r = arm.config.TotalReach();
                GL.Color(new Color(1f, 0.5f, 0.1f, 0.25f));
                DrawCircle(c, r, Vector3.up, 48);
                DrawCircle(c, r, Vector3.right, 48);
                DrawCircle(c, r, Vector3.forward, 48);
            }

            GL.End();
            GL.PopMatrix();
        }

        static void Line(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }

        static void DrawCircle(Vector3 c, float r, Vector3 normal, int seg)
        {
            Vector3 t = Vector3.Cross(normal, Vector3.up);
            if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(normal, Vector3.right);
            t.Normalize();
            Vector3 b = Vector3.Cross(normal, t);
            Vector3 prev = c + t * r;
            for (int i = 1; i <= seg; i++)
            {
                float ang = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 p = c + (t * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r;
                GL.Vertex(prev); GL.Vertex(p);
                prev = p;
            }
        }
    }
}
