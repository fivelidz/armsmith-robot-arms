#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless gate proving ALL training CONDITIONS + global settings round-trip through SaveSystem:
    /// save → mutate everything → load → verify the loaded values match what was saved (not the mutated
    /// state). Covers: TrainingConfig (reward weights + per-term enables, domain-randomization ranges,
    /// termination/success, curriculum difficulty, GA hyperparameters, sensor mask), usePredicateSuccess,
    /// SensorRealism (enabled/noise/latency), simSpeed, policyMode. Also checks the schema bumped to v2.
    ///
    /// Run: -executeMethod ArmSmith.EditorTools.ConditionsPersistenceCheck.RunHeadless
    /// </summary>
    public static class ConditionsPersistenceCheck
    {
        [MenuItem("ARMSMITH/Run Conditions Persistence Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(16); }

        public static bool Run()
        {
            int pass = 0, fail = 0;
            void Check(string label, bool cond) { if (cond) pass++; else { fail++; Debug.LogError($"[ConditionsPersistenceCheck] FAIL: {label}"); } }

            var spawned = new System.Collections.Generic.List<GameObject>();
            string slot = "test_conditions_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                // minimal real graph
                string kin = Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                var armGo = new GameObject("Arm"); spawned.Add(armGo);
                var arm = armGo.AddComponent<ProceduralArm>();
                if (File.Exists(kin)) arm.BuildFromKinematics(kin); else arm.Build(ArmConfig.CreateStarter());

                var tgt = new GameObject("ikt"); spawned.Add(tgt);
                var ctrl = armGo.AddComponent<ArmController>(); ctrl.Bind(arm, tgt.transform, null);

                var scenGo = new GameObject("Scen"); spawned.Add(scenGo);
                var scen = scenGo.AddComponent<ScenarioManager>();
                scen.Init(arm, ctrl, () => new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));

                var hubGo = new GameObject("Hub"); spawned.Add(hubGo);
                var hub = hubGo.AddComponent<SensorHub>(); hub.Init(arm, null);

                var trGo = new GameObject("Tr"); spawned.Add(trGo);
                var tr = trGo.AddComponent<EvolutionTrainer>(); tr.Init(arm, ctrl, scen); tr.sensorHub = hub;

                var ssGo = new GameObject("Save"); spawned.Add(ssGo);
                var ss = ssGo.AddComponent<SaveSystem>(); ss.autoSaveEnabled = false;   // don't trigger Start autoload
                ss.Bind(arm, ctrl, scen, hub, null, tr);

                // ── set DISTINCTIVE condition values, then SAVE ──
                var c = tr.config;
                c.wReach = 3.14f; c.wEnergy = 0.0077f; c.wSuccess = 7.5f;
                c.eGrasp = false; c.eOob = false; c.eReach = true;
                c.difficulty = 0.72f; c.randomization = 0.55f; c.autoCurriculum = false; c.advanceSuccessRate = 0.83f;
                c.drSpawnPosM = 0.09f; c.drYawDeg = 33f; c.drMass = false; c.drFriction = true;
                c.timeoutSec = 42f; c.successHoldSec = 0.9f; c.termOnOob = false;
                c.populationSize = 28; c.elite = 6; c.mutationRate = 0.41f; c.evalResets = 5;
                c.useImu = false; c.useLidar = false; c.useDepthCamera = true; c.useTactile = true;
                c.ApplySensorMask(hub);   // real flow: applying conditions pushes the mask to the hub before save
                tr.policyMode = true;
                scen.usePredicateSuccess = true;
                SensorRealism.enabled = true; SensorRealism.noiseRelative = 0.077f; SensorRealism.noiseAbsolute = 0.012f; SensorRealism.latencyFrames = 3;
                Time.timeScale = 2.5f;

                string path = ss.Save(slot);
                Check("save file written", File.Exists(path));
                Check("schema is v2", File.ReadAllText(path).Contains("armsmith.save.v2"));

                // ── MUTATE everything to wrong values ──
                c.wReach = 0f; c.wEnergy = 0f; c.wSuccess = 0f; c.eGrasp = true; c.eOob = true; c.eReach = false;
                c.difficulty = 0f; c.randomization = 0f; c.autoCurriculum = true; c.advanceSuccessRate = 0.1f;
                c.drSpawnPosM = 0f; c.drYawDeg = 0f; c.drMass = true; c.drFriction = false;
                c.timeoutSec = 1f; c.successHoldSec = 0f; c.termOnOob = true;
                c.populationSize = 1; c.elite = 1; c.mutationRate = 0f; c.evalResets = 1;
                c.useImu = true; c.useLidar = true; c.useDepthCamera = false; c.useTactile = false;
                tr.policyMode = false; scen.usePredicateSuccess = false;
                SensorRealism.enabled = false; SensorRealism.noiseRelative = 0f; SensorRealism.latencyFrames = 0;
                Time.timeScale = 1f;

                // ── LOAD (full) and VERIFY restored values ──
                Check("load returns true", ss.Load(slot));
                var lc = tr.config;
                Check("wReach restored", Mathf.Abs(lc.wReach - 3.14f) < 1e-3f);
                Check("wEnergy restored", Mathf.Abs(lc.wEnergy - 0.0077f) < 1e-4f);
                Check("wSuccess restored", Mathf.Abs(lc.wSuccess - 7.5f) < 1e-3f);
                Check("eGrasp restored (false)", lc.eGrasp == false);
                Check("eOob restored (false)", lc.eOob == false);
                Check("difficulty restored", Mathf.Abs(lc.difficulty - 0.72f) < 1e-3f);
                Check("randomization restored", Mathf.Abs(lc.randomization - 0.55f) < 1e-3f);
                Check("autoCurriculum restored (false)", lc.autoCurriculum == false);
                Check("advanceSuccessRate restored", Mathf.Abs(lc.advanceSuccessRate - 0.83f) < 1e-3f);
                Check("drSpawnPosM restored", Mathf.Abs(lc.drSpawnPosM - 0.09f) < 1e-3f);
                Check("drYawDeg restored", Mathf.Abs(lc.drYawDeg - 33f) < 1e-2f);
                Check("drMass restored (false)", lc.drMass == false);
                Check("drFriction restored (true)", lc.drFriction == true);
                Check("timeoutSec restored", Mathf.Abs(lc.timeoutSec - 42f) < 1e-2f);
                Check("successHoldSec restored", Mathf.Abs(lc.successHoldSec - 0.9f) < 1e-3f);
                Check("termOnOob restored (false)", lc.termOnOob == false);
                Check("populationSize restored", lc.populationSize == 28);
                Check("elite restored", lc.elite == 6);
                Check("mutationRate restored", Mathf.Abs(lc.mutationRate - 0.41f) < 1e-3f);
                Check("evalResets restored", lc.evalResets == 5);
                Check("useImu restored (false)", lc.useImu == false);
                Check("useDepthCamera restored (true)", lc.useDepthCamera == true);
                Check("policyMode restored (true)", tr.policyMode == true);
                Check("usePredicateSuccess restored", scen.usePredicateSuccess == true);
                Check("sensorRealism restored", SensorRealism.enabled == true);
                Check("noiseRelative restored", Mathf.Abs(SensorRealism.noiseRelative - 0.077f) < 1e-4f);
                Check("latencyFrames restored", SensorRealism.latencyFrames == 3);
                Check("simSpeed restored", Mathf.Abs(Time.timeScale - 2.5f) < 1e-3f);

                // sensor mask actually applied to the hub on load
                Check("hub IMU disabled by mask", hub.Get("IMU") != null && hub.Get("IMU").Enabled == false);
                Check("hub Depth enabled by mask", hub.Get("DepthCamera") != null && hub.Get("DepthCamera").Enabled == true);

                // ── conditionsOnly load must NOT change scenario ──
                scen.LoadScenario(ScenarioType.ReachTouch);
                var before = scen.current;
                ss.Load(slot, true);
                Check("conditionsOnly keeps scenario", scen.current == before);
                Check("conditionsOnly still restores config", Mathf.Abs(tr.config.wReach - 3.14f) < 1e-3f);
            }
            catch (System.Exception e) { Debug.LogError("[ConditionsPersistenceCheck] " + e); fail++; }
            finally
            {
                Time.timeScale = 1f; SensorRealism.enabled = false;
                try { string p = Path.Combine(Application.persistentDataPath, "Saves", slot + ".save.json"); if (File.Exists(p)) File.Delete(p); } catch { }
                for (int i = spawned.Count - 1; i >= 0; i--) if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            }

            bool ok = fail == 0;
            Debug.Log(ok
                ? $"[ConditionsPersistenceCheck] PASSED — {pass} assertions (all conditions + settings round-trip through SaveSystem v2)."
                : $"[ConditionsPersistenceCheck] FAILED — {fail} of {pass + fail} assertions failed.");
            return ok;
        }
    }
}
#endif
