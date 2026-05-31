using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith.Verification
{
    /// <summary>
    /// Runs the PlacementVerifier and shows a live report panel (top-right under the camera feeds).
    /// Verifies the arm + any registered modules/CAD parts are correctly placed (base fastened, links
    /// connected, no self-penetration, above worktop, modules mounted). Auto-refreshes periodically;
    /// toggle with the 'P'... key set in GameBootstrap. Extensible: register extra rules via Verifier.
    /// </summary>
    public class VerificationPanel : MonoBehaviour
    {
        public ProceduralArm arm;
        public Transform worktop;
        public float worktopTopY = 0f;
        public bool show = true;
        public float refreshEvery = 0.75f;

        readonly PlacementVerifier verifier = new PlacementVerifier();
        readonly VerificationContext ctx = new VerificationContext();
        Text text;
        GameObject root;
        float timer;

        public void Build(Transform canvas, ProceduralArm a, Transform worktopT)
        {
            arm = a; worktop = worktopT;
            verifier.RegisterDefaults();

            root = new GameObject("VerificationPanel");
            root.transform.SetParent(canvas, false);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.09f, 0.85f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-10, -640);   // below the sensor-modules panel
            rt.sizeDelta = new Vector2(310, 150);

            var tgo = new GameObject("t"); tgo.transform.SetParent(root.transform, false);
            text = tgo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12; text.color = Color.white; text.supportRichText = true;
            var trt = text.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 6); trt.offsetMax = new Vector2(-8, -6);
        }

        /// <summary>Register an extra rule at runtime (e.g. when a CAD/module subsystem loads).</summary>
        public void AddRule(IPlacementRule rule) => verifier.Register(rule);

        /// <summary>Run the verification now and return the results (also usable by training/CI).</summary>
        public List<PlacementResult> RunNow()
        {
            ctx.arm = arm;
            ctx.worktop = worktop;
            ctx.worktopTopY = worktopTopY;
            return verifier.Verify(ctx);
        }

        void Update()
        {
            if (root == null) return;
            if (Input.GetKeyDown(KeyCode.F3)) show = !show;
            root.SetActive(show);
            if (!show) return;

            timer += Time.unscaledDeltaTime;
            if (timer < refreshEvery) return;
            timer = 0f;

            var results = RunNow();
            text.text = "<b><color=#6cf>PLACEMENT CHECK</color></b> (F3)\n" + PlacementVerifier.Report(results);
        }
    }
}
