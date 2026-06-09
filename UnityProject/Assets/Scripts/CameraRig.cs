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
                // Realistic wrist-cam mount: like the printed UVC32 bracket on the real SO-101, the camera
                // sits a little BEHIND and ABOVE the gripper tip and looks toward the grasp point. The EE
                // local frame is twisted, so a WristCamAim component re-aims the camera each frame in WORLD
                // space (toward the tip, biased downward) — guaranteeing it always sees the jaws + what's
                // below them, regardless of how the wrist is rotated.
                t.localPosition = new Vector3(0f, -0.06f, 0f);   // slightly back from the tip along the tool
                wristCam.fieldOfView = 70f;                      // realistic UVC FOV; jaws + object fit
                wristCam.nearClipPlane = 0.01f;
                var aim = wristCam.gameObject.GetComponent<WristCamAim>() ?? wristCam.gameObject.AddComponent<WristCamAim>();
                aim.gripperTip = gripper;                        // EE tip = grasp point
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

        // (helper class WristCamAim is defined at the bottom of this file)

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

    /// <summary>Re-aims the wrist camera each frame in WORLD space toward the gripper tip (grasp point),
    /// biased slightly downward, so it reliably frames the jaws + the object below regardless of the
    /// twisted end-effector local frame. Mirrors a real wrist UVC camera looking at the grasp zone.</summary>
    public class WristCamAim : MonoBehaviour
    {
        public Transform gripperTip;
        public float downBias = 0.06f;   // aim a bit below the tip so the workspace under the jaws is framed

        void LateUpdate()
        {
            if (gripperTip == null) return;
            Vector3 look = gripperTip.position - Vector3.up * downBias; // grasp point, slightly below
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
