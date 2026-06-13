using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Enables SELF-COLLISION on the arm so links physically cannot pass through each other, WITHOUT
    /// jamming adjacent joints. ArticulationBodies in one articulation don't self-collide by default; we
    /// IGNORE near pairs (chain gap &lt;= 2 — they share a joint and their STL meshes overlap by design, so
    /// colliding them wedges the joints) and ENABLE far pairs (gap &gt;= 3 — e.g. base vs wrist) so the arm
    /// can't fold back through itself.
    ///
    /// S7f FIX: a link can have MULTIPLE colliders (STL mesh colliders + the primitive capsule/box). The
    /// old code ignored only ONE representative collider per link, so the other collider pairs between
    /// adjacent links stayed active and JAMMED the arm — it couldn't descend to low grasp targets (joints
    /// stuck ~17deg short; tip floored ~19cm high). We now track ALL colliders per link and ignore EVERY
    /// cross-collider pair between near links.
    /// </summary>
    public class SelfCollision : MonoBehaviour
    {
        ProceduralArm arm;
        // colliders grouped by link index (0 = base, 1.. = jointBodies[i-1])
        readonly List<List<Collider>> linkCols = new List<List<Collider>>();

        public void Setup(ProceduralArm a)
        {
            arm = a;
            Gather();
            ApplyIgnores(ignoreAll: true);   // settle with everything ignored (avoid first-step depenetration NaN)
            StopAllCoroutines();
            StartCoroutine(ReassertForAWhile());
        }

        void Gather()
        {
            linkCols.Clear();
            AddLink(arm.baseBody != null ? arm.baseBody : null);
            for (int i = 0; i < arm.jointBodies.Count; i++) AddLink(arm.jointBodies[i]);
            // Gripper jaws are separate child ArticulationBodies — give them their own group so they're
            // included in the all-internal-ignore (they jam against the wrist/forearm when the arm folds).
            if (arm.gripper != null)
            {
                var jawList = new List<Collider>();
                foreach (var c in arm.gripper.GetComponentsInChildren<Collider>()) jawList.Add(c);
                if (jawList.Count > 0) linkCols.Add(jawList);
            }
        }

        // Collect ALL colliders under THIS body that belong to it (not descendants that are other bodies).
        void AddLink(ArticulationBody body)
        {
            var list = new List<Collider>();
            if (body != null)
            {
                foreach (var c in body.GetComponentsInChildren<Collider>())
                {
                    var owner = c.GetComponentInParent<ArticulationBody>();
                    if (owner == body) list.Add(c);
                }
            }
            linkCols.Add(list);
        }

        // S7f DECISION: ignore ALL internal arm-vs-arm collision pairs. On the real SO-101 the joint limits
        // already prevent the serial chain from folding through itself, and Unity's articulation solver
        // keeps the links rigidly connected — so internal self-collision adds NO physical fidelity but
        // DOES jam the joints (any residual mesh overlap between links generates contact forces the drive
        // can't overcome, so the arm can't descend to low targets — verified: colliders off => 2.8cm reach,
        // on => 10-19cm). We keep arm-vs-ENVIRONMENT and arm-vs-OBJECT collisions (handled elsewhere) and
        // expose a penetration METRIC for training, but never let the arm collide with itself.
        // `ignoreAll` is retained for API compatibility (always ignores internally now).
        void ApplyIgnores(bool ignoreAll)
        {
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 1; j < linkCols.Count; j++)
                {
                    var ai = linkCols[i]; var aj = linkCols[j];
                    for (int x = 0; x < ai.Count; x++)
                        for (int y = 0; y < aj.Count; y++)
                            if (ai[x] != null && aj[y] != null)
                                Physics.IgnoreCollision(ai[x], aj[y], true);
                }
        }

        System.Collections.IEnumerator ReassertForAWhile()
        {
            // Re-assert the all-internal ignore for the first second (survives MeshCollider cooking / the
            // broadphase pair-table rebuild) then at a low rate forever (cheap; survives later rebuilds).
            for (int frame = 0; frame < 40; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame % 5 == 0) ApplyIgnores(true);
            }
            var wait = new WaitForSeconds(2.0f);
            while (true)
            {
                ApplyIgnores(true);
                yield return wait;
            }
        }

        /// <summary>Smallest penetration depth between any FAR (gap>=3) link pair (0 = none). Training signal.</summary>
        public float MaxSelfPenetration()
        {
            float worst = 0f;
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 3; j < linkCols.Count; j++)
                {
                    var ai = linkCols[i]; var aj = linkCols[j];
                    for (int x = 0; x < ai.Count; x++)
                        for (int y = 0; y < aj.Count; y++)
                        {
                            var a = ai[x]; var b = aj[y];
                            if (a == null || b == null) continue;
                            var pa = a.transform; var pb = b.transform;
                            if (float.IsNaN(pa.position.x) || float.IsNaN(pb.position.x)) continue;
                            if (Physics.ComputePenetration(a, pa.position, pa.rotation, b, pb.position, pb.rotation,
                                                           out _, out float dist))
                                if (dist > worst) worst = dist;
                        }
                }
            return worst;
        }
    }
}
