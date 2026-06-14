using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Conditions interface (design/specs/TRAINING_REGIMEN.md §5). Edits the shared TrainingConfig: the
    /// curriculum difficulty + RANDOMIZATION STRENGTH ("scrambled world"), the reward-shaping weights,
    /// which SENSOR MODULES feed the policy (model inclusion/exclusion of information), the scenario, and
    /// GA hyperparameters. OnGUI immediate-mode; toggle with F4. Changes apply to the trainer live.
    /// </summary>
    public class ConditionsPanel : MonoBehaviour
    {
        public EvolutionTrainer trainer;
        public ScenarioManager scenarios;
        public bool show = true;

        Texture2D px;
        GUIStyle hdr, lbl, small;
        Vector2 scroll;

        public void Bind(EvolutionTrainer t, ScenarioManager s) { trainer = t; scenarios = s; }

        void EnsureStyles()
        {
            if (px == null) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
            if (hdr == null)
            {
                hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13, normal = { textColor = new Color(1f, 0.8f, 0.5f) } };
                lbl = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.white } };
                small = new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            }
        }

        void Update() { if (Input.GetKeyDown(KeyCode.F4)) show = !show; }

        void OnGUI()
        {
            if (!show || trainer == null) return;
            EnsureStyles();
            var cfg = trainer.config;
            const float W = 320f;
            float X = Screen.width - W - 12f, Y = 60f, H = Screen.height - 120f;
            GUI.color = new Color(0.07f, 0.06f, 0.05f, 0.92f);
            GUI.DrawTexture(new Rect(X, Y, W, H), px); GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(X + 10, Y + 8, W - 20, H - 16));
            GUILayout.Label("CONDITIONS  (F4)", hdr);

            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("Curriculum", lbl);
            cfg.difficulty = Slider("difficulty  " + cfg.LevelName(), cfg.difficulty, 0f, 1f);
            cfg.autoCurriculum = GUILayout.Toggle(cfg.autoCurriculum, " auto-advance difficulty");
            GUILayout.Space(4);

            GUILayout.Label("Scrambled world", lbl);
            cfg.randomization = Slider("randomization strength", cfg.randomization, 0f, 1f);
            GUILayout.Space(4);

            GUILayout.Label("Reward weights", lbl);
            cfg.wReach   = Slider("reach",   cfg.wReach,   0f, 3f);
            cfg.wGrasp   = Slider("grasp",   cfg.wGrasp,   0f, 5f);
            cfg.wPlace   = Slider("place",   cfg.wPlace,   0f, 3f);
            cfg.wSuccess = Slider("success", cfg.wSuccess, 0f, 10f);
            cfg.wEnergy  = Slider("energy",  cfg.wEnergy,  0f, 0.02f);
            cfg.wSelfPen = Slider("self-pen",cfg.wSelfPen, 0f, 3f);
            cfg.wOob     = Slider("oob",     cfg.wOob,     0f, 10f);
            GUILayout.Space(4);

            GUILayout.Label("Sensors (observation = model input)", lbl);
            cfg.useMotorEncoders = GUILayout.Toggle(cfg.useMotorEncoders, " MotorEncoders");
            cfg.useTaskState     = GUILayout.Toggle(cfg.useTaskState,     " TaskState (EE/target/vel)");
            cfg.useImu           = GUILayout.Toggle(cfg.useImu,           " IMU");
            cfg.useRangeFinder   = GUILayout.Toggle(cfg.useRangeFinder,   " RangeFinder");
            cfg.useLidar         = GUILayout.Toggle(cfg.useLidar,         " Lidar2D");
            cfg.useDepthCamera   = GUILayout.Toggle(cfg.useDepthCamera,   " DepthCamera (heavy)");
            cfg.useTactile       = GUILayout.Toggle(cfg.useTactile,       " Tactile");
            GUILayout.Space(4);

            GUILayout.Label("GA / policy", lbl);
            cfg.populationSize = Mathf.RoundToInt(Slider("population " + cfg.populationSize, cfg.populationSize, 4, 48));
            cfg.elite          = Mathf.RoundToInt(Slider("elite " + cfg.elite, cfg.elite, 1, 10));
            cfg.mutationRate   = Slider("mutation rate", cfg.mutationRate, 0f, 1f);
            cfg.mutationSigma  = Slider("mutation sigma", cfg.mutationSigma, 1f, 60f);
            cfg.keysPerGenome  = Mathf.RoundToInt(Slider("keys/genome " + cfg.keysPerGenome, cfg.keysPerGenome, 2, 10));
            cfg.policyHidden   = Mathf.RoundToInt(Slider("policy hidden " + cfg.policyHidden, cfg.policyHidden, 4, 64));
            cfg.evalResets     = Mathf.RoundToInt(Slider("eval resets " + cfg.evalResets, cfg.evalResets, 1, 6));
            cfg.rolloutSpeedup = Slider("rollout speedup", cfg.rolloutSpeedup, 1f, 10f);

            GUILayout.Space(6);
            if (GUILayout.Button("Apply to trainer")) trainer.ApplyConfig();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        float Slider(string label, float v, float lo, float hi)
        {
            GUILayout.Label($"{label}: {v:0.###}", small);
            return GUILayout.HorizontalSlider(v, lo, hi);
        }
    }
}
