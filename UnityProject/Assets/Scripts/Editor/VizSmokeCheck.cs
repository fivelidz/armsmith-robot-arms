#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ArmSmith.Visualization;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless smoke test for the path-visualization data layer (S7d). Verifies the providers generate
    /// sane TrajectorySets and the TrajectoryData helpers behave, WITHOUT the GUI (rendering itself needs
    /// a camera, but the DATA correctness — point counts, finiteness, multimodality, cost ordering,
    /// resampling — is fully testable here). Good CI gate for the viz code.
    ///
    /// Run: Unity -batchmode -nographics -projectPath . \
    ///        -executeMethod ArmSmith.EditorTools.VizSmokeCheck.RunHeadless -quit -logFile -
    /// </summary>
    public static class VizSmokeCheck
    {
        [MenuItem("ARMSMITH/Run Viz Smoke Check")]
        public static void RunMenu() { Debug.Log(Run() ? "Viz smoke PASSED" : "Viz smoke FAILED"); }

        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(4); }

        public static bool Run()
        {
            int fails = 0;
            GameObject go = null;
            try
            {
                go = new GameObject("VizSmoke");

                // 1) TrajectorySample helpers
                var s = new TrajectorySample();
                for (int i = 0; i < 10; i++) s.points.Add(new Vector3(i * 0.1f, 0, 0));
                float len = s.Length();
                if (Mathf.Abs(len - 0.9f) > 1e-3f) { Debug.LogError($"[Viz] Length wrong: {len}"); fails++; }
                var rs = s.Resampled(5);
                if (rs.Count != 5) { Debug.LogError($"[Viz] Resampled count {rs.Count} != 5"); fails++; }
                if (!Finite(rs)) { Debug.LogError("[Viz] Resampled has non-finite points"); fails++; }

                // 2) DiffusionPathDemo — multimodal, finite, costed, one chosen
                var diff = go.AddComponent<DiffusionPathDemo>();
                diff.autoResolveScene = false;     // use the default start/goal/obstacle
                var set = diff.GetTrajectories();
                if (set == null || set.Count < 3) { Debug.LogError($"[Viz] diffusion set count {(set==null?0:set.Count)} < 3"); fails++; }
                else
                {
                    int chosen = 0; bool allFinite = true; bool anyCost = false;
                    foreach (var smp in set.samples)
                    {
                        if (smp.chosen) chosen++;
                        if (!Finite(smp)) allFinite = false;
                        if (smp.cost > 0f) anyCost = true;
                        if (smp.Count < 2) { Debug.LogError("[Viz] diffusion sample too short"); fails++; }
                    }
                    if (chosen != 1) { Debug.LogError($"[Viz] expected exactly 1 chosen, got {chosen}"); fails++; }
                    if (!allFinite) { Debug.LogError("[Viz] diffusion sample non-finite"); fails++; }
                    if (!anyCost) { Debug.LogError("[Viz] diffusion costs all zero"); fails++; }
                    if (!set.hasStartGoal) { Debug.LogError("[Viz] diffusion set missing start/goal"); fails++; }
                    // chosen should be the min-cost one
                    float minC = float.MaxValue; TrajectorySample minS = null;
                    foreach (var smp in set.samples) if (smp.cost < minC) { minC = smp.cost; minS = smp; }
                    if (minS != null && !minS.chosen) { Debug.LogError("[Viz] chosen is not the min-cost path"); fails++; }
                }

                // 3) DenoisePathDemo — produces a finite path that converges toward clean over steps
                var den = go.AddComponent<DenoisePathDemo>();   // OnEnable builds clean + noise
                den.autoResolveScene = false;
                var d0 = den.GetTrajectories();
                if (d0 == null || d0.Count < 1 || d0.samples[0].Count < 2) { Debug.LogError("[Viz] denoise empty"); fails++; }
                else if (!Finite(d0.samples[0])) { Debug.LogError("[Viz] denoise non-finite"); fails++; }

                // 4) TrajectorySet.MarkBestChosen
                var ts = new TrajectorySet();
                ts.Add(new TrajectorySample { cost = 5f });
                ts.Add(new TrajectorySample { cost = 2f });
                ts.Add(new TrajectorySample { cost = 9f });
                ts.MarkBestChosen();
                if (!ts.samples[1].chosen || ts.samples[0].chosen || ts.samples[2].chosen)
                { Debug.LogError("[Viz] MarkBestChosen picked wrong sample"); fails++; }

                Debug.Log(fails == 0 ? "[VizSmokeCheck] PASSED — providers + data helpers all sane."
                                     : $"[VizSmokeCheck] FAILED — {fails} check(s) failed.");
                return fails == 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[VizSmokeCheck] Exception: " + e);
                return false;
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        static bool Finite(TrajectorySample s)
        {
            foreach (var p in s.points)
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                    float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z)) return false;
            return true;
        }
    }
}
#endif
