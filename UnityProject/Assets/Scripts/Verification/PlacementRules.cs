using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Verification
{
    /// <summary>The base must be fastened to (resting on) the worktop surface, not floating or sunk.</summary>
    public class BaseFastenedRule : IPlacementRule
    {
        public string Name => "BaseFastened";
        public IEnumerable<PlacementResult> Check(VerificationContext ctx)
        {
            if (ctx.arm == null || ctx.arm.baseBody == null)
            { yield return PlacementResult.Fail(Name, "no arm/base to verify"); yield break; }

            var baseT = ctx.arm.baseBody.transform;
            var col = baseT.GetComponentInChildren<Collider>();
            float baseBottom = col != null ? col.bounds.min.y : baseT.position.y;
            float gap = baseBottom - ctx.worktopTopY;
            if (Mathf.Abs(gap) <= ctx.baseFastenTolerance)
                yield return PlacementResult.Pass(Name, $"base fastened to worktop (gap {gap * 1000:F0} mm)", baseT);
            else if (gap > 0)
                yield return PlacementResult.Fail(Name, $"base FLOATS {gap * 1000:F0} mm above worktop", Severity.Error, baseT);
            else
                yield return PlacementResult.Fail(Name, $"base SUNK {(-gap) * 1000:F0} mm into worktop", Severity.Error, baseT);
        }
    }

    /// <summary>Each link must connect to the next with no large gap (chain is physically continuous).</summary>
    public class LinksConnectedRule : IPlacementRule
    {
        public string Name => "LinksConnected";
        public IEnumerable<PlacementResult> Check(VerificationContext ctx)
        {
            var arm = ctx.arm;
            if (arm == null || arm.jointBodies.Count == 0)
            { yield return PlacementResult.Fail(Name, "no joints"); yield break; }

            // base top -> joint0, then joint i -> joint i+1
            Vector3 prev = arm.baseBody != null ? arm.baseBody.transform.position : arm.jointBodies[0].transform.position;
            for (int i = 0; i < arm.jointBodies.Count; i++)
            {
                Vector3 jp = arm.jointBodies[i].transform.position;
                float gap = Vector3.Distance(prev, jp);
                // expected separation = previous link's length (roughly); just flag absurd jumps
                if (gap > 0.5f)
                    yield return PlacementResult.Fail(Name, $"joint {i} ({arm.jointSpecs[i].name}) is {gap * 100:F0} cm from the previous part — disconnected", Severity.Error, arm.jointBodies[i].transform);
                prev = jp;
            }
            yield return PlacementResult.Pass(Name, "links connected end-to-end");
        }
    }

    /// <summary>No two non-adjacent arm parts should inter-penetrate (self-collision = damage risk).</summary>
    public class NoSelfPenetrationRule : IPlacementRule
    {
        public string Name => "NoSelfPenetration";
        public IEnumerable<PlacementResult> Check(VerificationContext ctx)
        {
            var arm = ctx.arm;
            if (arm == null) yield break;
            var bodies = arm.jointBodies;
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 2; j < bodies.Count; j++)   // skip adjacent (i, i+1) — they share a joint
                {
                    var ci = bodies[i].GetComponentInChildren<Collider>();
                    var cj = bodies[j].GetComponentInChildren<Collider>();
                    if (ci == null || cj == null) continue;
                    if (Physics.ComputePenetration(ci, ci.transform.position, ci.transform.rotation,
                                                   cj, cj.transform.position, cj.transform.rotation,
                                                   out _, out float dist) && dist > ctx.penetrationTolerance)
                        yield return PlacementResult.Fail(Name,
                            $"{arm.jointSpecs[i].name} penetrates {arm.jointSpecs[j].name} by {dist * 1000:F0} mm (self-collision)",
                            Severity.Warning, bodies[i].transform);
                }
            yield return PlacementResult.Pass(Name, "no self-penetration");
        }
    }

    /// <summary>No arm part should be below the worktop surface (passing through the desk).</summary>
    public class AboveWorktopRule : IPlacementRule
    {
        public string Name => "AboveWorktop";
        public IEnumerable<PlacementResult> Check(VerificationContext ctx)
        {
            var arm = ctx.arm;
            if (arm == null) yield break;
            foreach (var b in arm.jointBodies)
            {
                var col = b.GetComponentInChildren<Collider>();
                if (col == null) continue;
                if (col.bounds.min.y < ctx.worktopTopY - 0.02f)
                    yield return PlacementResult.Fail(Name,
                        $"a link is {(ctx.worktopTopY - col.bounds.min.y) * 1000:F0} mm below the worktop (through the desk)",
                        Severity.Warning, b.transform);
            }
            yield return PlacementResult.Pass(Name, "all parts above the worktop");
        }
    }

    /// <summary>Player-placed modules must be parented to a valid robot part and face a sensible direction.
    /// (Foundation for the module-mounting + CAD systems — extend with mount-type specifics later.)</summary>
    public class ModuleMountRule : IPlacementRule
    {
        public string Name => "ModuleMount";
        public IEnumerable<PlacementResult> Check(VerificationContext ctx)
        {
            if (ctx.modules.Count == 0) { yield return PlacementResult.Pass(Name, "no modules to verify"); yield break; }
            foreach (var m in ctx.modules)
            {
                if (m == null) continue;
                bool onArm = ctx.arm != null && m.GetComponentInParent<ProceduralArm>() != null;
                if (!onArm)
                    yield return PlacementResult.Fail(Name, $"module '{m.name}' is not mounted on a robot part", Severity.Warning, m);
                else
                    yield return PlacementResult.Pass(Name, $"module '{m.name}' mounted OK", m);
            }
        }
    }
}
