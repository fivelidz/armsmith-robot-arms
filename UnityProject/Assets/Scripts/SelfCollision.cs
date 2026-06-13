using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Manages the arm's collision so it interacts correctly with the WORLD and OBJECTS but never JAMS
    /// itself. ArticulationBodies in one articulation don't self-collide by default; Unity can re-enable
    /// pairs after MeshCollider cooking. We IGNORE every internal arm-vs-arm collider pair.
    ///
    /// Why ignore ALL internal pairs (S7f): the SO-101's joint limits already prevent the serial chain from
    /// folding through itself, and the articulation solver keeps links rigidly connected — so internal
    /// self-collision adds NO physical fidelity but DOES jam the joints (any residual mesh overlap between
    /// adjacent links / the gripper jaws and the forearm generates contact forces the drive can't overcome,
    /// so the wrist can't pitch down and the arm can't descend to low grasp targets — verified: with arm
    /// colliders off the arm reaches to 0.4cm; with them on and only partially ignored it floored ~8-12cm
    /// high). Earlier per-body grouping missed multi-collider links + the jaws (98 pairs left colliding);
    /// this flat all-pairs ignore is bulletproof. Arm-vs-environment + arm-vs-object collisions are kept
    /// (handled in GameBootstrap.IgnoreArmVsEnvironment and the grasp logic).
    /// </summary>
    public class SelfCollision : MonoBehaviour
    {
        ProceduralArm arm;
        readonly List<Collider> allArmCols = new List<Collider>();

        // Dedicated layer for all arm colliders; we make this layer NOT collide with itself, which is the
        // bulletproof way to disable internal self-collision (per-pair IgnoreCollision kept missing some of
        // the many colliders per link + the jaws). Arm-vs-other-layers (worktop, objects) is unaffected.
        public int armLayer = 8;   // first free user layer (after Default/TransparentFX/IgnoreRaycast/Water/UI)

        public void Setup(ProceduralArm a)
        {
            arm = a;
            Gather();
            // 1) layer-based self-collision OFF (robust, can't miss a collider)
            Physics.IgnoreLayerCollision(armLayer, armLayer, true);
            foreach (var c in allArmCols) if (c != null) c.gameObject.layer = armLayer;
            // 2) belt-and-braces per-pair ignore too (covers anything still on another layer this frame)
            ApplyIgnores();
            StopAllCoroutines();
            StartCoroutine(ReassertForAWhile());
        }

        void Gather()
        {
            allArmCols.Clear();
            if (arm.baseBody != null) CollectBodyCols(arm.baseBody);
            if (arm.jointBodies != null) foreach (var ab in arm.jointBodies) CollectBodyCols(ab);
            CollectBodyCols(arm.leftJaw);
            CollectBodyCols(arm.rightJaw);
        }

        void CollectBodyCols(ArticulationBody body)
        {
            if (body == null) return;
            foreach (var c in body.GetComponentsInChildren<Collider>())
                if (c != null && !allArmCols.Contains(c)) allArmCols.Add(c);
        }

        void ApplyIgnores()
        {
            for (int i = 0; i < allArmCols.Count; i++)
                for (int j = i + 1; j < allArmCols.Count; j++)
                    if (allArmCols[i] != null && allArmCols[j] != null)
                        Physics.IgnoreCollision(allArmCols[i], allArmCols[j], true);
        }

        System.Collections.IEnumerator ReassertForAWhile()
        {
            // Re-assert the all-internal ignore for the first second (survives MeshCollider cooking / the
            // broadphase pair-table rebuild) then at a low rate forever (cheap; survives later rebuilds).
            for (int frame = 0; frame < 40; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame % 5 == 0) { Physics.IgnoreLayerCollision(armLayer, armLayer, true); ApplyIgnores(); }
            }
            var wait = new WaitForSeconds(2.0f);
            while (true)
            {
                Physics.IgnoreLayerCollision(armLayer, armLayer, true);
                ApplyIgnores();
                yield return wait;
            }
        }

        /// <summary>
        /// Self-proximity METRIC for training (the closest approach between NON-adjacent links, gap>=3).
        /// Internal collision is ignored physically, but a policy can still be penalised for near-self-
        /// intersection using this. Returns the largest such penetration depth (0 = none).
        /// </summary>
        public float MaxSelfPenetration()
        {
            if (arm == null || arm.jointBodies == null) return 0f;
            float worst = 0f;
            var bodies = new List<ArticulationBody>();
            if (arm.baseBody != null) bodies.Add(arm.baseBody);
            bodies.AddRange(arm.jointBodies);
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 3; j < bodies.Count; j++)
                {
                    var a = OneCol(bodies[i]); var b = OneCol(bodies[j]);
                    if (a == null || b == null) continue;
                    var pa = a.transform; var pb = b.transform;
                    if (float.IsNaN(pa.position.x) || float.IsNaN(pb.position.x)) continue;
                    if (Physics.ComputePenetration(a, pa.position, pa.rotation, b, pb.position, pb.rotation,
                                                   out _, out float dist))
                        if (dist > worst) worst = dist;
                }
            return worst;
        }

        static Collider OneCol(ArticulationBody body)
        {
            if (body == null) return null;
            var c = body.GetComponent<Collider>();
            return c != null ? c : body.GetComponentInChildren<Collider>();
        }
    }
}
