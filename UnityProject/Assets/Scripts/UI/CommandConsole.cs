using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Live in-game TEXT COMMAND console (bottom-left, above the servo panel). Type a robot command
    /// (the AgentCommands grammar: move/reach/pick/place/sort/open/close/grip/home/train/say/scenario)
    /// and press Enter to execute it. Shows a scrolling log of recent commands + results. This is the
    /// text->task interface (I59): the same grammar an LLM emits, that you can type by hand and that
    /// exports to the real arm. Toggle the console focus with the backtick (`) key.
    /// </summary>
    public class CommandConsole : MonoBehaviour
    {
        public AgentCommands agent;
        InputField input;
        Text logText;
        GameObject root;
        public bool show = true;

        public void Build(Transform canvas, AgentCommands a)
        {
            agent = a;

            root = new GameObject("CommandConsole");
            root.transform.SetParent(canvas, false);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.05f, 0.07f, 0.92f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(520, 10);   // right of the servo panel
            rt.sizeDelta = new Vector2(440, 200);

            // title
            var title = MakeText(root.transform, 13, FontStyle.Bold, new Color(0f, 0.83f, 0.7f));
            title.text = "COMMAND CONSOLE  (type a command, Enter to run; ` to focus)";
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0, 1);
            trt.offsetMin = new Vector2(8, -22); trt.offsetMax = new Vector2(-8, -4);

            // log
            logText = MakeText(root.transform, 12, FontStyle.Normal, new Color(0.8f, 0.85f, 0.9f));
            logText.alignment = TextAnchor.LowerLeft;
            var lrt = logText.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1); lrt.pivot = new Vector2(0, 0);
            lrt.offsetMin = new Vector2(8, 34); lrt.offsetMax = new Vector2(-8, -26);

            // input field
            var igo = new GameObject("input"); igo.transform.SetParent(root.transform, false);
            var iimg = igo.AddComponent<Image>(); iimg.color = new Color(0.1f, 0.13f, 0.16f, 1f);
            var irt = iimg.rectTransform;
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 0); irt.pivot = new Vector2(0, 0);
            irt.offsetMin = new Vector2(8, 6); irt.offsetMax = new Vector2(-8, 28);
            input = igo.AddComponent<InputField>();
            var itxtGo = new GameObject("text"); itxtGo.transform.SetParent(igo.transform, false);
            var itxt = itxtGo.AddComponent<Text>();
            itxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            itxt.fontSize = 13; itxt.color = Color.white; itxt.alignment = TextAnchor.MiddleLeft;
            var itrt = itxt.rectTransform; itrt.anchorMin = Vector2.zero; itrt.anchorMax = Vector2.one;
            itrt.offsetMin = new Vector2(6, 0); itrt.offsetMax = new Vector2(-6, 0);
            input.textComponent = itxt;
            var ph = new GameObject("ph"); ph.transform.SetParent(igo.transform, false);
            var pht = ph.AddComponent<Text>();
            pht.font = itxt.font; pht.fontSize = 13; pht.color = new Color(0.5f, 0.55f, 0.6f);
            pht.alignment = TextAnchor.MiddleLeft; pht.text = "e.g.  pick nearest into trayB   |   reach 0.1 0.1 0.3   |   sort";
            var phrt = pht.rectTransform; phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(6, 0); phrt.offsetMax = new Vector2(-6, 0);
            input.placeholder = pht;
            input.onEndEdit.AddListener(OnSubmit);
        }

        Text MakeText(Transform parent, int size, FontStyle style, Color col)
        {
            var go = new GameObject("t"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size; t.fontStyle = style; t.color = col; t.supportRichText = true;
            return t;
        }

        void OnSubmit(string cmd)
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return; // only on Enter
            cmd = cmd.Trim();
            if (cmd.Length == 0) return;
            agent.Run(cmd);                 // execute the command line (AgentCommands grammar)
            input.text = "";
            input.ActivateInputField();     // keep focus for the next command
        }

        void Update()
        {
            if (root == null) return;
            if (Input.GetKeyDown(KeyCode.BackQuote)) { show = !show; if (show) input.ActivateInputField(); }
            root.SetActive(show);
            if (!show || logText == null || agent == null) return;

            var sb = new StringBuilder();
            int start = Mathf.Max(0, agent.log.Count - 8);
            for (int i = start; i < agent.log.Count; i++) sb.AppendLine(agent.log[i]);
            logText.text = sb.ToString();
        }
    }
}
