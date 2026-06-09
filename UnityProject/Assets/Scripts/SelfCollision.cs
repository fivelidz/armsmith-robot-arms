using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Enables SELF-COLLISION on the arm so links physically cannot pass through each other (which
    /// produced non-physical poses + erratic IK). ArticulationBodies in one articulation don't self-
    /// collide by default; we force-enable collision between NON-ADJACENT link colliders and explicitly
    /// IGNORE adjacent pairs (i, i+1) + base (they share a joint and would jam). Also reports the closest
    /// approach so training can penalise near-self-collision.
    /// </summary>
    public class SelfCollision : MonoBehaviour
    {
        ProceduralArm arm;
        readonly List<Collider> linkCols = new List<Collider>();

        public void Setup(ProceduralArm a)
        {
            arm = a;
            linkCols.Clear();

            // gather ONE representative collider per rigid link (base + each joint body), excluding jaws.
            AddLinkCollider(arm.baseBody != null ? arm.baseBody.GetComponent<Collider>() : null);
            for (int i = 0; i < arm.jointBodies.Count; i++)
            {
                var c = arm.jointBodies[i].GetComponent<Collider>();
                if (c == null) c = arm.jointBodies[i].GetComponentInChildren<Collider>();
                AddLinkCollider(c);
            }

            // By default Unity ignores collisions within an articulation. Force-ENABLE for non-adjacent
            // pairs, and explicitly IGNORE adjacent pairs so the joints don't jam at their shared anchor.
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 1; j < linkCols.Count; j++)
                {
                    if (linkCols[i] == null || linkCols[j] == null) continue;
                    bool adjacent = (j == i + 1);
                    Physics.IgnoreCollision(linkCols[i], linkCols[j], adjacent);
                }
        }

        void AddLinkCollider(Collider c) { linkCols.Add(c); } // keep index alignment even if null

        /// <summary>Smallest penetration depth between any non-adjacent link pair (0 = no self-collision).
        /// Useful as a training penalty signal.</summary>
        public float MaxSelfPenetration()
        {
            float worst = 0f;
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 2; j < linkCols.Count; j++)
                {
                    var a = linkCols[i]; var b = linkCols[j];
                    if (a == null || b == null) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                                   b, b.transform.position, b.transform.rotation,
                                                   out _, out float dist))
                        if (dist > worst) worst = dist;
                }
            return worst;
        }
    }
}
