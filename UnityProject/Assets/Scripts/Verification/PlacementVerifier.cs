using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ArmSmith.Verification
{
    /// <summary>Severity of a placement finding.</summary>
    public enum Severity { Info, Warning, Error }

    /// <summary>One result from a placement rule.</summary>
    public struct PlacementResult
    {
        public string rule;
        public bool pass;
        public Severity severity;
        public string message;
        public Transform subject;   // the part/module this finding is about (optional)

        public static PlacementResult Pass(string rule, string msg = "ok", Transform subj = null)
            => new PlacementResult { rule = rule, pass = true, severity = Severity.Info, message = msg, subject = subj };
        public static PlacementResult Fail(string rule, string msg, Severity sev = Severity.Error, Transform subj = null)
            => new PlacementResult { rule = rule, pass = false, severity = sev, message = msg, subject = subj };
    }

    /// <summary>
    /// A declarative placement rule. Implement this to add a new check WITHOUT touching the verifier or
    /// other rules (open/closed principle) — this is the extension point for future CAD parts, new sensor
    /// modules, new arm types, etc. Each rule inspects a VerificationContext and returns 0+ results.
    /// </summary>
    public interface IPlacementRule
    {
        string Name { get; }
        IEnumerable<PlacementResult> Check(VerificationContext ctx);
    }

    /// <summary>
    /// Everything a rule might need to verify placement. Extensible: add fields as new subsystems appear
    /// (CAD parts list, module mounts, multi-arm registry, ...). Rules read only what they care about.
    /// </summary>
    public class VerificationContext
    {
        public ProceduralArm arm;
        public Transform worktop;                 // the table surface the base should fasten to
        public float worktopTopY = 0f;            // world Y of the worktop surface
        public readonly List<Transform> modules = new List<Transform>();  // player-placed sensor/camera modules
        public readonly List<Transform> cadParts = new List<Transform>(); // future CAD-imported parts

        // tolerances (tunable)
        public float jointGapTolerance = 0.03f;   // max gap between connected links (m)
        public float baseFastenTolerance = 0.02f; // base must sit within this of the worktop (m)
        public float penetrationTolerance = 0.01f;
    }

    /// <summary>
    /// Runs a registry of IPlacementRule checks and aggregates results. Verifies that arms + systems are
    /// correctly placed (base fastened to table, links connected, modules on valid surfaces, no
    /// penetration, ...). Foundational for the CAD + module-mounting systems: any new part/module type
    /// adds its own rule and is automatically validated. Returns a structured report.
    /// </summary>
    public class PlacementVerifier
    {
        readonly List<IPlacementRule> rules = new List<IPlacementRule>();

        public PlacementVerifier RegisterDefaults()
        {
            Register(new BaseFastenedRule());
            Register(new LinksConnectedRule());
            Register(new NoSelfPenetrationRule());
            Register(new AboveWorktopRule());
            Register(new ModuleMountRule());
            return this;
        }

        public PlacementVerifier Register(IPlacementRule rule) { rules.Add(rule); return this; }

        public List<PlacementResult> Verify(VerificationContext ctx)
        {
            var all = new List<PlacementResult>();
            foreach (var r in rules)
            {
                IEnumerable<PlacementResult> res = null;
                try { res = r.Check(ctx); }
                catch (System.Exception e) { all.Add(PlacementResult.Fail(r.Name, "rule threw: " + e.Message, Severity.Warning)); continue; }
                if (res != null) all.AddRange(res);
            }
            return all;
        }

        /// <summary>Human-readable report (for the verification panel / console).</summary>
        public static string Report(List<PlacementResult> results)
        {
            int err = 0, warn = 0, pass = 0;
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                if (r.pass) { pass++; continue; }
                if (r.severity == Severity.Error) err++; else warn++;
                string tag = r.severity == Severity.Error ? "<color=#f66>ERR </color>" : "<color=#fc6>WARN</color>";
                sb.AppendLine($"{tag} [{r.rule}] {r.message}");
            }
            string head = err == 0 && warn == 0
                ? "<color=#6f6>PLACEMENT OK</color>"
                : $"<color=#fc6>{err} errors, {warn} warnings</color>";
            return head + $"  ({pass} checks passed)\n" + sb;
        }

        public static bool AllPass(List<PlacementResult> results)
        {
            foreach (var r in results) if (!r.pass && r.severity == Severity.Error) return false;
            return true;
        }
    }
}
