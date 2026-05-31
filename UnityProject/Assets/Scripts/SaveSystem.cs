using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    [Serializable]
    public class SaveState
    {
        public string schema = "armsmith.save.v1";
        public string armConfigJson;        // ArmConfig
        public string sequenceJson;         // current Sequence
        public string scenario;             // active scenario
        public bool[] sensorEnabled;        // per-module enable flags (order = SensorHub.sensors)
        public string[] sensorNames;
        public float[] zeroPose;            // calibrated zero
        public bool policyMode;
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
                armConfigJson = arm.config != null ? arm.config.ToJson() : "",
                sequenceJson = sequence != null ? JsonUtility.ToJson(sequence.seq) : "",
                scenario = scenarios != null ? scenarios.current.ToString() : "",
                zeroPose = controller != null ? controller.zeroPose : null,
                policyMode = trainer != null && trainer.policyMode,
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

        public bool Load(string name = null)
        {
            name = name ?? slot;
            string p = PathFor(name);
            if (!File.Exists(p)) { Debug.Log($"[Save] no save at {p}"); return false; }
            var st = JsonUtility.FromJson<SaveState>(File.ReadAllText(p));

            if (scenarios != null && Enum.TryParse(st.scenario, out ScenarioType sc)) scenarios.LoadScenario(sc);
            if (sensorHub != null && st.sensorNames != null)
                for (int i = 0; i < st.sensorNames.Length; i++)
                    sensorHub.SetEnabled(st.sensorNames[i], st.sensorEnabled[i]);
            if (sequence != null && !string.IsNullOrEmpty(st.sequenceJson))
                sequence.seq = JsonUtility.FromJson<Sequence>(st.sequenceJson);
            if (controller != null && st.zeroPose != null) controller.zeroPose = st.zeroPose;
            if (trainer != null) trainer.policyMode = st.policyMode;
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

        void Update()
        {
            // Ctrl+S save, Ctrl+L load (avoid clobbering other keys).
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S)) Save();
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L)) Load();
        }
    }
}
