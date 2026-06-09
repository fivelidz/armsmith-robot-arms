using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Reachable-workspace map. Samples a grid over the worktop and tests whether the arm's IK can place
    /// the gripper there at a chosen height, drawing reachable cells GREEN and unreachable RED. Helps the
    /// player (and the designer) see where the arm can reliably work — and where to place trays/targets so
    /// tasks are solvable. Toggle with the 'P'... key (set in GameBootstrap). Computed once on demand.
    /// </summary>
    public class WorkspaceMap : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public float sampleHeight = 0.06f;     // table-level reach test
        public float reachTolerance = 0.05f;   // <= this error counts as "reachable"
        public Vector2 xRange = new Vector2(-0.40f, 0.40f);
        public Vector2 zRange = new Vector2(0.05f, 0.55f);
        public int gridX = 17, gridZ = 13;

        public bool show = false;
        struct Cell { public Vector3 pos; public bool reachable; public float err; }
        readonly List<Cell> cells = new List<Cell>();
        static Material lineMat;

        public void Bind(ArmController c, ProceduralArm a) { controller = c; arm = a; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Backslash) && Input.GetKey(KeyCode.LeftShift)) { show = !show; if (show) Compute(); }
        }

        /// <summary>Sample the grid using the IK's forward-kinematics reachability check (no physics).</summary>
        public void Compute()
        {
            cells.Clear();
            if (controller == null || arm == null) return;
            for (int ix = 0; ix < gridX; ix++)
                for (int iz = 0; iz < gridZ; iz++)
                {
                    float x = Mathf.Lerp(xRange.x, xRange.y, ix / (float)(gridX - 1));
                    float z = Mathf.Lerp(zRange.x, zRange.y, iz / (float)(gridZ - 1));
                    Vector3 p = new Vector3(x, sampleHeight, z);
                    float err = controller.TestReach(p);   // FK-based reach error (no physics drive)
                    cells.Add(new Cell { pos = p, reachable = err <= reachTolerance, err = err });
                }
        }

        void EnsureMat()
        {
            if (lineMat != null) return;
            lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_ZWrite", 0); lineMat.SetInt("_Cull", 0);
        }

        void OnRenderObject()
        {
            if (!show || cells.Count == 0) return;
            EnsureMat(); lineMat.SetPass(0);
            GL.PushMatrix(); GL.Begin(GL.QUADS);
            float hx = (xRange.y - xRange.x) / (gridX - 1) * 0.45f;
            float hz = (zRange.y - zRange.x) / (gridZ - 1) * 0.45f;
            foreach (var c in cells)
            {
                Color col = c.reachable ? new Color(0.2f, 0.9f, 0.3f, 0.35f) : new Color(0.9f, 0.2f, 0.2f, 0.25f);
                GL.Color(col);
                Vector3 p = new Vector3(c.pos.x, 0.002f, c.pos.z);
                GL.Vertex(p + new Vector3(-hx, 0, -hz));
                GL.Vertex(p + new Vector3(-hx, 0, hz));
                GL.Vertex(p + new Vector3(hx, 0, hz));
                GL.Vertex(p + new Vector3(hx, 0, -hz));
            }
            GL.End(); GL.PopMatrix();
        }
    }
}
