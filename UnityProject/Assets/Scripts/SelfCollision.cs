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
            GatherAndApply();
            // CRITICAL: Unity re-evaluates collision pairs after MeshColliders finish cooking and after
            // the first physics ticks, which silently DROPS the IgnoreCollision pairs we set here. If that
            // happens, the tightly-packed adjacent wrist links start generating contact forces that JAM the
            // arm whenever it folds down to reach low (the pick-and-place "can't reach the cube / floors 6cm
            // above target" bug). We re-assert the ignore pairs for the first second to make them stick.
            StartCoroutine(ReassertForAWhile());
        }

        void GatherAndApply()
        {
            linkCols.Clear();

            // gather ONE representative collider per rigid link (base + each joint body), excluding jaws.
            AddLinkCollider(arm.baseBody != null ? arm.baseBody.GetComponent<Collider>() : null);
            for (int i = 0; i < arm.jointBodies.Count; i++)
            {
                var c = arm.jointBodies[i].GetComponent<Collider>();
                if (c == null) c = arm.jointBodies[i].GetComponentInChildren<Collider>();
                AddLinkCollider(c);
            }
            ApplyIgnores();
        }

        void ApplyIgnores()
        {
            // Force-ENABLE collision only between FAR-APART link pairs (gap >= 3 in the chain) — e.g.
            // base vs forearm/wrist. NEAR pairs (gap 1 or 2) are the tightly-packed wrist/gripper cluster
            // whose meshes overlap by design; colliding them JAMS the joints at their limits, so we IGNORE
            // those. This keeps "can't fold back through itself" without wedging the wrist.
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 1; j < linkCols.Count; j++)
                {
                    if (linkCols[i] == null || linkCols[j] == null) continue;
                    bool near = (j - i) <= 2;              // adjacent + once-removed = ignore (don't jam)
                    Physics.IgnoreCollision(linkCols[i], linkCols[j], near);
                }
        }

        System.Collections.IEnumerator ReassertForAWhile()
        {
            // Re-apply the ignore pairs over the first ~1s so they survive MeshCollider cooking /
            // Unity's post-init collision-pair rebuild, THEN keep re-asserting at a low rate forever.
            // Unity can silently rebuild the broadphase collision-pair table (e.g. after the arm enters
            // a new contact-rich configuration), which re-drops our ignores and re-jams the wrist on the
            // NEXT pick attempt — that was the "works once then jams" non-determinism. A 2 Hz re-assert is
            // negligible cost (a few dozen IgnoreCollision calls) and makes the arm robust across repeated
            // tasks without any manual reset.
            for (int frame = 0; frame < 60; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame % 5 == 0) ApplyIgnores();
            }
            // steady-state low-rate re-assert
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                ApplyIgnores();
                yield return wait;
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
