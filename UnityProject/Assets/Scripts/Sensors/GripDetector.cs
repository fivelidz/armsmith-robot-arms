using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Kinetic grip-detection feedback. Continuously checks whether a graspable object is within the
    /// gripper's grasp range and, if so, HIGHLIGHTS it and shows a "GRIP READY" cue — making objects
    /// easier to pick up and giving the player (and the policy) feedback on grasp opportunity.
    ///
    /// This feedback is REVEALED to the player only if the EFleshTactile (grip/tactile) module is enabled
    /// (I83) — it represents what that sensor tells you. With the module off, the cue is hidden (you grasp
    /// "blind"). Also exposes a normalised "grip readiness" [0..1] that feeds the tactile sensor channels.
    /// </summary>
    public class GripDetector : MonoBehaviour
    {
        public ProceduralArm arm;
        public SensorHub hub;

        Transform candidate;            // the object currently in grasp range
        Renderer candidateRend;
        Color candidateOrig;
        public float readiness;         // 0..1 how aligned/close the gripper is to grasping

        // HUD cue
        GameObject cue;
        UnityEngine.UI.Text cueText;

        public void Bind(ProceduralArm a, SensorHub h, Transform canvas)
        {
            arm = a; hub = h;
            cue = new GameObject("GripCue");
            cue.transform.SetParent(canvas, false);
            cueText = cue.AddComponent<UnityEngine.UI.Text>();
            cueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cueText.fontSize = 20; cueText.fontStyle = FontStyle.Bold;
            cueText.alignment = TextAnchor.MiddleCenter; cueText.color = new Color(0.2f, 1f, 0.4f);
            cueText.supportRichText = true;
            var rt = cueText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -120); rt.sizeDelta = new Vector2(400, 40);
        }

        bool TactileOn()
        {
            var s = hub != null ? hub.Get("EFleshTactile") : null;
            return s != null && s.Enabled;
        }

        void Update()
        {
            if (arm == null || arm.gripper == null) return;
            Vector3 tip = arm.gripper.TipPosition;
            float radius = arm.gripper.graspRadius;

            // find nearest graspable object to the grasp point
            Transform near = null; float nearD = radius;
            foreach (var col in Physics.OverlapSphere(tip, radius))
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (col.GetComponentInParent<ProceduralArm>() != null) continue;
                float d = Vector3.Distance(tip, rb.worldCenterOfMass);
                if (d < nearD) { nearD = d; near = rb.transform; }
            }

            readiness = near != null ? 1f - (nearD / radius) : 0f;

            // swap highlight target
            if (near != candidate)
            {
                Unhighlight();
                candidate = near;
                if (candidate != null) { candidateRend = candidate.GetComponent<Renderer>(); if (candidateRend != null) candidateOrig = candidateRend.material.color; }
            }

            bool reveal = TactileOn();
            // Highlight the in-range object (only when tactile module reveals it).
            if (candidate != null && candidateRend != null)
                candidateRend.material.color = reveal
                    ? Color.Lerp(candidateOrig, new Color(0.3f, 1f, 0.5f), 0.6f)
                    : candidateOrig;

            // HUD cue
            if (cueText != null)
            {
                bool show = reveal && candidate != null && !arm.gripper.IsHolding;
                cueText.enabled = show;
                if (show) cueText.text = $"<color=#3f6>\u25C9 GRIP READY</color>  ({(readiness * 100):F0}%)  \u2014 close to grab";
                if (reveal && arm.gripper.IsHolding) { cueText.enabled = true; cueText.text = "<color=#6cf>\u25C9 HOLDING</color>"; }
            }
        }

        void Unhighlight()
        {
            if (candidate != null && candidateRend != null) candidateRend.material.color = candidateOrig;
        }

        /// <summary>Grip-readiness signal [0..1] for the tactile sensor channels / training.</summary>
        public float Readiness => readiness;
    }
}
