#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using ArmSmith.Evaluation;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// EV1 headless gate — proves the composable PREDICATE library is correct and self-consistent, with NO
    /// scene / arm / physics dependency (pure math over synthesised TaskContexts). It checks:
    ///   (a) each scenario's predicate tree fires TRUE on a hand-built "solved" world state, and
    ///   (b) fires FALSE on a deliberately "unsolved" state (object far / not at rest / not stacked),
    ///   (c) the And/Or/Not/ForAll combinators behave, and Margin() crosses zero at the boundary.
    ///
    /// Run: -executeMethod ArmSmith.EditorTools.PredicateEvalCheck.RunHeadless
    /// </summary>
    public static class PredicateEvalCheck
    {
        [MenuItem("ARMSMITH/Run Predicate Eval Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(11); }

        // Build a TaskContext from a name->position dictionary (+ optional velocities), gripper state.
        static TaskContext Ctx(Vector3 ee, float close, Dictionary<string, Vector3> pos,
                               Dictionary<string, Vector3> vel = null)
        {
            return new TaskContext(ee, close,
                n => pos.TryGetValue(n, out var p) ? p : Vector3.zero,
                n => vel != null && vel.TryGetValue(n, out var v) ? v : Vector3.zero,
                n => pos.ContainsKey(n));
        }

        public static bool Run()
        {
            int pass = 0, fail = 0;
            void Check(string label, bool cond) { if (cond) pass++; else { fail++; Debug.LogError($"[PredicateEvalCheck] FAIL: {label}"); } }

            // ----- ReachTouch -----
            {
                var p = TaskEvaluator.Build(ScenarioType.ReachTouch, null);
                var target = new Vector3(0.1f, 0.12f, 0.32f);
                Check("ReachTouch hit", p.Evaluate(Ctx(target + new Vector3(0.02f, 0, 0.01f), 0f,
                    new Dictionary<string, Vector3> { ["reachTarget"] = target })));
                Check("ReachTouch miss", !p.Evaluate(Ctx(target + new Vector3(0.20f, 0, 0), 0f,
                    new Dictionary<string, Vector3> { ["reachTarget"] = target })));
            }

            // ----- PickPlaceCube (NearXZ pad + AtRest) -----
            {
                var p = TaskEvaluator.Build(ScenarioType.PickPlaceCube, null);
                var pad = new Vector3(-0.15f, 0.001f, 0.32f);
                var solved = new Dictionary<string, Vector3> { ["cube"] = new Vector3(pad.x + 0.02f, 0.03f, pad.z), ["pad"] = pad };
                Check("PickPlace solved", p.Evaluate(Ctx(Vector3.zero, 0f, solved)));
                var moving = new Dictionary<string, Vector3> { ["cube"] = new Vector3(1, 0, 0) };
                Check("PickPlace not-at-rest fails", !p.Evaluate(Ctx(Vector3.zero, 0f, solved, moving)));
                var far = new Dictionary<string, Vector3> { ["cube"] = new Vector3(0.2f, 0.03f, 0.3f), ["pad"] = pad };
                Check("PickPlace far fails", !p.Evaluate(Ctx(Vector3.zero, 0f, far)));
            }

            // ----- TrayToTray (NearXZ trayB + BelowHeight + AtRest) -----
            {
                var p = TaskEvaluator.Build(ScenarioType.TrayToTray, null);
                var trayB = new Vector3(-0.16f, 0f, 0.30f);
                var solved = new Dictionary<string, Vector3> { ["cube"] = new Vector3(trayB.x, 0.04f, trayB.z), ["trayB"] = trayB };
                Check("TrayToTray solved", p.Evaluate(Ctx(Vector3.zero, 0f, solved)));
                var high = new Dictionary<string, Vector3> { ["cube"] = new Vector3(trayB.x, 0.20f, trayB.z), ["trayB"] = trayB };
                Check("TrayToTray still-held(high) fails", !p.Evaluate(Ctx(Vector3.zero, 1f, high)));
            }

            // ----- StackTwo (AboveAligned + AtRest) -----
            {
                var p = TaskEvaluator.Build(ScenarioType.StackTwo, null);
                var b = new Vector3(-0.12f, 0.025f, 0.32f);
                var solved = new Dictionary<string, Vector3> { ["cube"] = b + new Vector3(0.005f, 0.05f, 0.0f), ["cubeB"] = b };
                Check("StackTwo solved", p.Evaluate(Ctx(Vector3.zero, 0f, solved)));
                var beside = new Dictionary<string, Vector3> { ["cube"] = b + new Vector3(0.10f, 0.0f, 0.0f), ["cubeB"] = b };
                Check("StackTwo beside fails", !p.Evaluate(Ctx(Vector3.zero, 0f, beside)));
            }

            // ----- DropInBin -----
            {
                var p = TaskEvaluator.Build(ScenarioType.DropInBin, null);
                var bin = new Vector3(-0.16f, 0f, 0.34f);
                var solved = new Dictionary<string, Vector3> { ["cube"] = new Vector3(bin.x, 0.02f, bin.z), ["bin"] = bin };
                Check("DropInBin solved", p.Evaluate(Ctx(Vector3.zero, 0f, solved)));
            }

            // ----- SortIntoTray (ForAll) -----
            {
                var names = new List<string> { "sortCube0", "sortCube1", "sortCube2" };
                var p = TaskEvaluator.Build(ScenarioType.SortIntoTray, names);
                var trayB = new Vector3(-0.16f, 0f, 0.34f);
                var all = new Dictionary<string, Vector3>
                {
                    ["trayB"] = trayB, ["cube"] = trayB,
                    ["sortCube0"] = new Vector3(trayB.x + 0.02f, 0.04f, trayB.z),
                    ["sortCube1"] = new Vector3(trayB.x - 0.02f, 0.04f, trayB.z + 0.01f),
                    ["sortCube2"] = new Vector3(trayB.x, 0.04f, trayB.z - 0.02f),
                };
                Check("SortIntoTray all-in solved", p.Evaluate(Ctx(Vector3.zero, 0f, all)));
                var twoIn = new Dictionary<string, Vector3>(all);
                twoIn["sortCube2"] = new Vector3(0.2f, 0.03f, 0.4f);  // one left outside
                Check("SortIntoTray 2/3 fails", !p.Evaluate(Ctx(Vector3.zero, 0f, twoIn)));

                // ForAll progress counter
                var forAll = new ForAll(names,
                    n => new And(new NearXZ(n, "trayB", 0.07f), new BelowHeight(n, 0.07f)), "x");
                Check("ForAll CountSatisfied=2", forAll.CountSatisfied(Ctx(Vector3.zero, 0f, twoIn)) == 2);
            }

            // ----- combinator + margin boundary -----
            {
                var near = new NearXZ("a", "b", 0.06f);
                var atBoundaryInside = new Dictionary<string, Vector3> { ["a"] = new Vector3(0.05f, 0, 0), ["b"] = Vector3.zero };
                var atBoundaryOutside = new Dictionary<string, Vector3> { ["a"] = new Vector3(0.07f, 0, 0), ["b"] = Vector3.zero };
                Check("NearXZ margin sign inside", near.Margin(Ctx(Vector3.zero, 0, atBoundaryInside)) > 0f);
                Check("NearXZ margin sign outside", near.Margin(Ctx(Vector3.zero, 0, atBoundaryOutside)) < 0f);
                var and = new And(near, new AtRest("a"));
                Check("And true", and.Evaluate(Ctx(Vector3.zero, 0, atBoundaryInside)));
                Check("Not flips", new Not(near).Evaluate(Ctx(Vector3.zero, 0, atBoundaryOutside)));
                Check("Or true if any", new Or(near, new Near("a", "b", 0.001f)).Evaluate(Ctx(Vector3.zero, 0, atBoundaryInside)));
            }

            bool ok = fail == 0;
            Debug.Log(ok
                ? $"[PredicateEvalCheck] PASSED — {pass} predicate assertions hold (composable EV1 eval correct)."
                : $"[PredicateEvalCheck] FAILED — {fail} of {pass + fail} assertions failed.");
            return ok;
        }
    }
}
#endif
