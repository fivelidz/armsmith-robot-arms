using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Multi-camera console: a main orbit camera plus a wrist CV camera and a fixed environment camera,
    /// each rendering to a RenderTexture shown as a HUD panel. Intrinsics match the real rig
    /// (wrist UVC ~80deg, C922 ~78deg) so trained vision crosses to real life.
    /// See design/specs/CAMERA_VISION_SPEC.md.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera mainCam;
        public Camera wristCam;
        public Camera envCam;

        public RenderTexture wristRT;
        public RenderTexture envRT;

        [Header("Orbit (main cam)")]
        public Transform pivot;
        public float distance = 1.1f;
        public float yaw = 30f, pitch = 28f;
        public float minPitch = -10f, maxPitch = 80f;
        public Vector3 pivotPoint = new Vector3(0f, 0.15f, 0.3f);

        public RawImage wristPanel;
        public RawImage envPanel;

        public void Setup(Transform gripper, Vector3 envPos, Vector3 envLookAt)
        {
            // Wrist cam parented to the gripper (robot's-eye view). It must look DOWN the gripper toward
            // where the jaws grasp (forward), not backward. We parent it, place it slightly behind/above
            // the tip, then aim it at a point beyond the end-effector tip so it always faces the work.
            if (wristCam != null && gripper != null)
            {
                var t = wristCam.transform;
                t.SetParent(gripper, false);
                // Mount the wrist cam BEHIND and slightly ABOVE the gripper so the JAWS are in frame
                // (the player sees the claw fingers + what they're grasping), looking forward down the tool.
                t.localPosition = new Vector3(0f, -0.10f, -0.05f);
                Vector3 forwardPoint = gripper.position + gripper.up * 0.25f;   // aim past the jaws
                t.LookAt(forwardPoint, gripper.forward);
                wristCam.fieldOfView = 90f;          // wider so jaws + object both fit
                wristCam.nearClipPlane = 0.01f;
                wristRT = new RenderTexture(256, 256, 16) { name = "WristRT" };
                wristCam.targetTexture = wristRT;
                if (wristPanel) wristPanel.texture = wristRT;
            }
            if (envCam != null)
            {
                envCam.transform.position = envPos;
                envCam.transform.LookAt(envLookAt);
                envCam.fieldOfView = 78f;
                envCam.nearClipPlane = 0.05f;
                envRT = new RenderTexture(320, 240, 16) { name = "EnvRT" };
                envCam.targetTexture = envRT;
                if (envPanel) envPanel.texture = envRT;
            }
        }

        void LateUpdate()
        {
            if (mainCam == null) return;

            // RMB orbit, MMB pan, scroll zoom (only when not dragging IK with LMB).
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch -= Input.GetAxis("Mouse Y") * 3f;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            if (Input.GetMouseButton(2))
            {
                Vector3 right = mainCam.transform.right;
                Vector3 up = mainCam.transform.up;
                pivotPoint -= (right * Input.GetAxis("Mouse X") + up * Input.GetAxis("Mouse Y")) * 0.01f * distance;
            }
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            // Only zoom with scroll if RMB held or Ctrl, so scroll can also move IK depth.
            if (Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftControl))
                distance = Mathf.Clamp(distance - scroll * 1.5f, 0.4f, 4f);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            mainCam.transform.position = pivotPoint + rot * (Vector3.back * distance);
            mainCam.transform.LookAt(pivotPoint);

            if (Input.GetKeyDown(KeyCode.V)) ToggleHud();
        }

        void ToggleHud()
        {
            if (wristPanel) wristPanel.enabled = !wristPanel.enabled;
            if (envPanel) envPanel.enabled = !envPanel.enabled;
        }

        /// <summary>Read a camera's RT into a Texture2D (for dataset recording / vision obs).</summary>
        public Texture2D Capture(Camera cam, RenderTexture rt)
        {
            if (cam == null || rt == null) return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
    }
}
