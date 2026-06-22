using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Evaluation
{
    /// <summary>
    /// EV1 — composable success PREDICATES (RoboLab-inspired). A predicate is a pure boolean query over
    /// world state (positions, distances, contact, rest). Scenario success is then expressed as a small
    /// TREE of predicates (AND/OR/NOT) instead of a bespoke inline `switch`. The SAME predicate tree drives:
    ///   - success detection (ScenarioManager.SuccessNow),
    ///   - shaped fitness terms (a predicate exposes a continuous Margin),
    ///   - curriculum difficulty labels (count of predicates / spatial tolerances),
    ///   - the human-readable objective string (Describe()).
    ///
    /// Design goals: ZERO per-frame allocation in Evaluate (predicates are built once, re-evaluated each
    /// tick against a TaskContext snapshot); headless-testable (no MonoBehaviour dependency in the math).
    /// </summary>
    public interface IPredicate
    {
        /// <summary>True if the predicate currently holds.</summary>
        bool Evaluate(in TaskContext ctx);

        /// <summary>Signed "satisfaction margin" in metres-ish units: >=0 when satisfied, the magnitude is
        /// how far from the boundary. Negative = how far from being satisfied (drives shaped reward).
        /// For composite predicates this is the worst/limiting child margin.</summary>
        float Margin(in TaskContext ctx);

        /// <summary>Human-readable description, e.g. "cube inside trayB (&lt; 6 cm) AND at rest".</summary>
        string Describe();
    }

    /// <summary>
    /// Immutable snapshot of the bits of the world a predicate can ask about. Built once per evaluation
    /// from the live scene (ScenarioManager) OR synthesised in a headless test. Keeping this a struct with
    /// resolver delegates means predicates never touch Unity types directly -> trivially unit-testable.
    /// </summary>
    public readonly struct TaskContext
    {
        public readonly Vector3 EndEffector;     // gripper tip world pos
        public readonly float GripperClose;      // 0 open .. 1 closed
        public readonly Func<string, Vector3> Pos;       // named object -> world position
        public readonly Func<string, Vector3> Vel;       // named object -> linear velocity (zero if static)
        public readonly Func<string, bool> Exists;       // is the named object active/present

        public TaskContext(Vector3 ee, float gripperClose,
                           Func<string, Vector3> pos, Func<string, Vector3> vel, Func<string, bool> exists)
        {
            EndEffector = ee; GripperClose = gripperClose; Pos = pos; Vel = vel ?? (_ => Vector3.zero);
            Exists = exists ?? (_ => true);
        }

        public Vector3 P(string name) => Pos != null ? Pos(name) : Vector3.zero;
        public Vector3 V(string name) => Vel != null ? Vel(name) : Vector3.zero;
        public bool Has(string name) => Exists == null || Exists(name);
    }

    // ---------------------------------------------------------------------------------------------------
    // Leaf predicates
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Horizontal (XZ) distance between two named objects is below a tolerance — the workhorse for
    /// "in container" / "on pad" / "in zone" checks where height is handled separately.</summary>
    public sealed class NearXZ : IPredicate
    {
        readonly string a, b; readonly float tol;
        public NearXZ(string a, string b, float tol) { this.a = a; this.b = b; this.tol = tol; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx)
        {
            Vector3 pa = ctx.P(a), pb = ctx.P(b);
            float d = Vector3.Distance(new Vector3(pa.x, 0, pa.z), new Vector3(pb.x, 0, pb.z));
            return tol - d;
        }
        public string Describe() => $"{a} within {tol * 100f:F0}cm of {b} (horizontal)";
    }

    /// <summary>Full 3D distance below tolerance (e.g. EE reaches a target point, or a stack is aligned).</summary>
    public sealed class Near : IPredicate
    {
        readonly string a, b; readonly float tol;
        public Near(string a, string b, float tol) { this.a = a; this.b = b; this.tol = tol; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx) => tol - Vector3.Distance(ctx.P(a), ctx.P(b));
        public string Describe() => $"{a} within {tol * 100f:F0}cm of {b}";
    }

    /// <summary>End-effector reaches a named target within tolerance (no grasp needed).</summary>
    public sealed class EeReaches : IPredicate
    {
        readonly string target; readonly float tol;
        public EeReaches(string target, float tol) { this.target = target; this.tol = tol; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx) => tol - Vector3.Distance(ctx.EndEffector, ctx.P(target));
        public string Describe() => $"gripper reaches {target} (< {tol * 100f:F0}cm)";
    }

    /// <summary>Object height (world Y) is below a ceiling — "resting low / set down / inside a tray".</summary>
    public sealed class BelowHeight : IPredicate
    {
        readonly string a; readonly float maxY;
        public BelowHeight(string a, float maxY) { this.a = a; this.maxY = maxY; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx) => maxY - ctx.P(a).y;
        public string Describe() => $"{a} set down (y < {maxY * 100f:F0}cm)";
    }

    /// <summary>Object A is above object B by at least minDy and horizontally aligned (stacking).</summary>
    public sealed class AboveAligned : IPredicate
    {
        readonly string a, b; readonly float minDy, xzTol;
        public AboveAligned(string a, string b, float minDy, float xzTol) { this.a = a; this.b = b; this.minDy = minDy; this.xzTol = xzTol; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx)
        {
            Vector3 pa = ctx.P(a), pb = ctx.P(b);
            float dyMargin = (pa.y - pb.y) - minDy;
            float xz = Vector3.Distance(new Vector3(pa.x, 0, pa.z), new Vector3(pb.x, 0, pb.z));
            float xzMargin = xzTol - xz;
            return Mathf.Min(dyMargin, xzMargin);
        }
        public string Describe() => $"{a} stacked on {b} (>{minDy * 100f:F0}cm up, <{xzTol * 100f:F0}cm aligned)";
    }

    /// <summary>Object's linear speed is below a threshold — it has come to rest (no longer being flung).</summary>
    public sealed class AtRest : IPredicate
    {
        readonly string a; readonly float maxSpeed;
        public AtRest(string a, float maxSpeed = 0.05f) { this.a = a; this.maxSpeed = maxSpeed; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx) => maxSpeed - ctx.V(a).magnitude;
        public string Describe() => $"{a} at rest";
    }

    /// <summary>The gripper is closed past a threshold AND near an object — a proxy for "grasping" it.</summary>
    public sealed class Grasping : IPredicate
    {
        readonly string obj; readonly float reach, minClose;
        public Grasping(string obj, float reach = 0.05f, float minClose = 0.5f) { this.obj = obj; this.reach = reach; this.minClose = minClose; }
        public bool Evaluate(in TaskContext ctx) => Margin(ctx) >= 0f;
        public float Margin(in TaskContext ctx)
        {
            float reachMargin = reach - Vector3.Distance(ctx.EndEffector, ctx.P(obj));
            float closeMargin = ctx.GripperClose - minClose;
            return Mathf.Min(reachMargin, closeMargin);
        }
        public string Describe() => $"grasping {obj}";
    }

    // ---------------------------------------------------------------------------------------------------
    // Composite predicates (AND / OR / NOT / quantifier)
    // ---------------------------------------------------------------------------------------------------

    public sealed class And : IPredicate
    {
        readonly IPredicate[] kids;
        public And(params IPredicate[] kids) { this.kids = kids; }
        public bool Evaluate(in TaskContext ctx) { foreach (var k in kids) if (!k.Evaluate(ctx)) return false; return true; }
        public float Margin(in TaskContext ctx) { float m = float.PositiveInfinity; foreach (var k in kids) m = Mathf.Min(m, k.Margin(ctx)); return m; }
        public string Describe() { return string.Join(" AND ", Array.ConvertAll(kids, k => k.Describe())); }
    }

    public sealed class Or : IPredicate
    {
        readonly IPredicate[] kids;
        public Or(params IPredicate[] kids) { this.kids = kids; }
        public bool Evaluate(in TaskContext ctx) { foreach (var k in kids) if (k.Evaluate(ctx)) return true; return false; }
        public float Margin(in TaskContext ctx) { float m = float.NegativeInfinity; foreach (var k in kids) m = Mathf.Max(m, k.Margin(ctx)); return m; }
        public string Describe() { return "(" + string.Join(" OR ", Array.ConvertAll(kids, k => k.Describe())) + ")"; }
    }

    public sealed class Not : IPredicate
    {
        readonly IPredicate k;
        public Not(IPredicate k) { this.k = k; }
        public bool Evaluate(in TaskContext ctx) => !k.Evaluate(ctx);
        public float Margin(in TaskContext ctx) => -k.Margin(ctx);
        public string Describe() => "NOT (" + k.Describe() + ")";
    }

    /// <summary>ForAll over a named set of objects: every member must satisfy a per-member predicate built
    /// by the factory. Used by SortIntoTray ("ALL cubes in the tray").</summary>
    public sealed class ForAll : IPredicate
    {
        readonly IReadOnlyList<string> members;
        readonly Func<string, IPredicate> factory;
        readonly string label;
        public ForAll(IReadOnlyList<string> members, Func<string, IPredicate> factory, string label)
        { this.members = members; this.factory = factory; this.label = label; }
        public bool Evaluate(in TaskContext ctx)
        {
            for (int i = 0; i < members.Count; i++) { if (!ctx.Has(members[i])) continue; if (!factory(members[i]).Evaluate(ctx)) return false; }
            return true;
        }
        public float Margin(in TaskContext ctx)
        {
            float m = float.PositiveInfinity;
            for (int i = 0; i < members.Count; i++) { if (!ctx.Has(members[i])) continue; m = Mathf.Min(m, factory(members[i]).Margin(ctx)); }
            return m == float.PositiveInfinity ? 0f : m;
        }
        public string Describe() => $"ALL {label}";

        /// <summary>How many members currently satisfy the predicate (for progress reward / UI).</summary>
        public int CountSatisfied(in TaskContext ctx)
        {
            int n = 0;
            for (int i = 0; i < members.Count; i++) { if (ctx.Has(members[i]) && factory(members[i]).Evaluate(ctx)) n++; }
            return n;
        }
        public int Total => members.Count;
    }
}
