using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    [Serializable]
    public class SaveState
    {
        public string schema = "armsmith.save.v2";
        public string armConfigJson;        // ArmConfig
        public string sequenceJson;         // current Sequence
        public string scenario;             // active scenario
        public bool[] sensorEnabled;        // per-module enable flags (order = SensorHub.sensors)
        public string[] sensorNames;
        public float[] zeroPose;            // calibrated zero
        public bool policyMode;

        // v2: ALL training CONDITIONS (reward terms + weights, domain-randomization ranges, termination/
        // success, curriculum difficulty, GA hyperparameters, sensor mask). The single most important thing
        // to persist so a tuned setup survives a restart.
        public string trainingConfigJson;   // JsonUtility(TrainingConfig)

        // v2: global/Options settings so the whole session state round-trips.
        public bool usePredicateSuccess;
        public bool sensorRealism;
        public float sensorNoiseRel, sensorNoiseAbs;
        public int sensorLatencyFrames;
        public float simSpeed;               // Time.timeScale at save time
    }

    /// <summary>
    /// Save / load the whole workspace state: arm config, the current keyframe sequence, active scenario,
    /// which sensor modules are enabled, the calibrated zero pose, and training mode. Saves to named JSON
    /// slots under persistentDataPath/Saves. Keys: F5 quick-save, F9... (F9 is STL) so use Ctrl+S save,
    /// Ctrl+L load (set in GameBootstrap). Implements "saving systems should be possible".
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public ScenarioManager scenarios;
        public SensorHub sensorHub;
        public SequenceEditor sequence;
        public EvolutionTrainer trainer;

        public string slot = "quicksave";

        public void Bind(ProceduralArm a, ArmController c, ScenarioManager s, SensorHub h, SequenceEditor seq, EvolutionTrainer t)
        { arm = a; controller = c; scenarios = s; sensorHub = h; sequence = seq; trainer = t; }

        string Dir => Path.Combine(Application.persistentDataPath, "Saves");
        string PathFor(string name) => Path.Combine(Dir, name + ".save.json");

        public string Save(string name = null)
        {
            name = name ?? slot;
            var st = new SaveState
            {
                armConfigJson = arm != null && arm.config != null ? arm.config.ToJson() : "",
                sequenceJson = sequence != null ? JsonUtility.ToJson(sequence.seq) : "",
                scenario = scenarios != null ? scenarios.current.ToString() : "",
                zeroPose = controller != null ? controller.zeroPose : null,
                policyMode = trainer != null && trainer.policyMode,
                // v2: persist ALL training conditions + global settings
                trainingConfigJson = trainer != null && trainer.config != null ? JsonUtility.ToJson(trainer.config) : "",
                usePredicateSuccess = scenarios != null && scenarios.usePredicateSuccess,
                sensorRealism = SensorRealism.enabled,
                sensorNoiseRel = SensorRealism.noiseRelative,
                sensorNoiseAbs = SensorRealism.noiseAbsolute,
                sensorLatencyFrames = SensorRealism.latencyFrames,
                simSpeed = Time.timeScale,
            };
            if (sensorHub != null)
            {
                int n = sensorHub.sensors.Count;
                st.sensorEnabled = new bool[n]; st.sensorNames = new string[n];
                for (int i = 0; i < n; i++) { st.sensorEnabled[i] = sensorHub.sensors[i].Enabled; st.sensorNames[i] = sensorHub.sensors[i].Name; }
            }
            Directory.CreateDirectory(Dir);
            string p = PathFor(name);
            File.WriteAllText(p, JsonUtility.ToJson(st, true));
            Debug.Log($"[Save] wrote {p}");
            return p;
        }

        public bool Load(string name = null) => Load(name, false);

        /// <summary>Load a slot. When conditionsOnly is true, restore ONLY the training conditions + global
        /// settings (not the scenario/sequence/zero-pose) — used by autoload so a restart keeps your tuned
        /// conditions without yanking the scene to a different scenario.</summary>
        public bool Load(string name, bool conditionsOnly)
        {
            name = name ?? slot;
            string p = PathFor(name);
            if (!File.Exists(p)) { Debug.Log($"[Save] no save at {p}"); return false; }
            var st = JsonUtility.FromJson<SaveState>(File.ReadAllText(p));

            if (!conditionsOnly && scenarios != null && Enum.TryParse(st.scenario, out ScenarioType sc)) scenarios.LoadScenario(sc);
            // v2: restore ALL training conditions FIRST (so the sensor mask + difficulty are in place), then
            // apply the per-module enable flags on top (they reflect the user's explicit last toggles).
            if (trainer != null && !string.IsNullOrEmpty(st.trainingConfigJson))
            {
                var cfg = JsonUtility.FromJson<TrainingConfig>(st.trainingConfigJson);
                if (cfg != null) { trainer.config = cfg; trainer.ApplyConfig(); if (sensorHub != null) cfg.ApplySensorMask(sensorHub); }
            }
            if (sensorHub != null && st.sensorNames != null)
                for (int i = 0; i < st.sensorNames.Length; i++)
                    sensorHub.SetEnabled(st.sensorNames[i], st.sensorEnabled[i]);
            if (!conditionsOnly && sequence != null && !string.IsNullOrEmpty(st.sequenceJson))
                sequence.seq = JsonUtility.FromJson<Sequence>(st.sequenceJson);
            if (!conditionsOnly && controller != null && st.zeroPose != null) controller.zeroPose = st.zeroPose;
            if (trainer != null) trainer.policyMode = st.policyMode;
            // v2: restore global/Options settings
            if (scenarios != null) scenarios.usePredicateSuccess = st.usePredicateSuccess;
            SensorRealism.enabled = st.sensorRealism;
            if (st.sensorNoiseRel > 0f) SensorRealism.noiseRelative = st.sensorNoiseRel;
            if (st.sensorNoiseAbs > 0f) SensorRealism.noiseAbsolute = st.sensorNoiseAbs;
            SensorRealism.latencyFrames = st.sensorLatencyFrames;
            if (st.simSpeed > 0f) Time.timeScale = st.simSpeed;
            Debug.Log($"[Save] loaded {p}");
            return true;
        }

        public List<string> ListSaves()
        {
            var list = new List<string>();
            if (Directory.Exists(Dir))
                foreach (var f in Directory.GetFiles(Dir, "*.save.json"))
                    list.Add(Path.GetFileNameWithoutExtension(f).Replace(".save", ""));
            return list;
        }

        // ── AUTOSAVE of conditions (so a tuned setup survives a restart with no manual action) ──────────
        public const string AutosaveSlot = "autosave";
        public bool autoSaveEnabled = true;
        float _autoSaveTimer;

        /// <summary>Save the conditions/settings to the autosave slot (called on Apply, on interval, on quit).</summary>
        public void AutoSaveConditions()
        {
            if (!autoSaveEnabled) return;
            try { Save(AutosaveSlot); } catch (Exception e) { Debug.LogWarning("[Save] autosave failed: " + e.Message); }
        }

        /// <summary>Load the autosave slot if it exists (called on Start so conditions persist across runs).</summary>
        public bool AutoLoadConditions()
        {
            if (!File.Exists(PathFor(AutosaveSlot))) return false;
            bool ok = Load(AutosaveSlot, true);   // conditions/settings only — don't yank the scene
            if (ok) Debug.Log("[Save] auto-loaded conditions from previous session");
            return ok;
        }

        void Start()
        {
            // Restore the last session's conditions automatically (a beat after bootstrap wires everything).
            if (autoSaveEnabled) Invoke(nameof(AutoLoadConditions), 0.5f);
        }

        void Update()
        {
            // Ctrl+S save, Ctrl+L load (avoid clobbering other keys).
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S)) Save();
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L)) Load();

            // periodic autosave of conditions (every 30s while running) so nothing is lost on a crash.
            if (autoSaveEnabled)
            {
                _autoSaveTimer += Time.unscaledDeltaTime;
                if (_autoSaveTimer >= 30f) { _autoSaveTimer = 0f; AutoSaveConditions(); }
            }
        }

        void OnApplicationQuit() { AutoSaveConditions(); }
        void OnApplicationPause(bool paused) { if (paused) AutoSaveConditions(); }
    }
}
