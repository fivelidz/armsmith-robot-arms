using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Shared scale helper for the immediate-mode (OnGUI) panels (TrainingPanel F3, ConditionsPanel F4).
    ///
    /// WHY: the Canvas-based panels are scaled by a CanvasScaler set to ScaleWithScreenSize at a 1920x1080
    /// reference (see GameBootstrap.BuildHud). OnGUI / IMGUI completely bypasses the CanvasScaler, so on a
    /// 2560x1440 (or any non-1920) display its text/rects render at raw pixels and look tiny and mis-placed
    /// relative to the Canvas UI. To keep BOTH UI systems consistent, the OnGUI panels wrap their drawing in
    /// UiScale.Begin()/End(), which sets GUI.matrix so the panel can be laid out in the SAME 1920x1080
    /// logical coordinate space the Canvas uses, then scales to the real screen.
    ///
    /// Match logic mirrors CanvasScaler MatchWidthOrHeight = 0.5: a log-average of the width and height
    /// scale factors, so the UI scales sensibly on both wider and taller screens.
    /// </summary>
    public static class UiScale
    {
        public const float RefWidth  = 1920f;
        public const float RefHeight = 1080f;

        /// <summary>Current scale factor from logical (1920x1080) units to real screen pixels.</summary>
        public static float Factor
        {
            get
            {
                float sw = Screen.width  / RefWidth;
                float sh = Screen.height / RefHeight;
                if (sw <= 0f) sw = 1f;
                if (sh <= 0f) sh = 1f;
                // match = 0.5 -> geometric mean of width/height scale (same as CanvasScaler's log-lerp at 0.5)
                return Mathf.Sqrt(sw * sh);
            }
        }

        /// <summary>Begin drawing in logical 1920x1080 space. Returns the previous GUI.matrix to restore.</summary>
        public static Matrix4x4 Begin()
        {
            Matrix4x4 prev = GUI.matrix;
            float f = Factor;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(f, f, 1f));
            return prev;
        }

        /// <summary>Restore the matrix saved by Begin().</summary>
        public static void End(Matrix4x4 prev) => GUI.matrix = prev;

        /// <summary>Logical screen width in the 1920x1080 reference space (for right/bottom-edge placement).</summary>
        public static float LogicalWidth  => Screen.width  / Factor;
        /// <summary>Logical screen height in the 1920x1080 reference space.</summary>
        public static float LogicalHeight => Screen.height / Factor;
    }
}
