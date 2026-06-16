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
                t.SetParent(null, true);                         // world-space rig (WristCamAim places it)
                // CLAW CAMERA (S7f): a wrist UVC-style view that clearly frames BOTH the jaws AND the grasp
                // target. The previous mount sat AT the tip looking at itself, so the claw filled the frame
                // and you couldn't see what was being grasped. WristCamAim now places the camera BACK and
                // slightly ABOVE the grasp point (along the gripper's own approach axis) and looks down the
                // approach line — so the claw is in the lower-near part of the frame and the object it's
                // reaching for is centred below it. Wider FOV so the whole grasp zone fits.
                wristCam.fieldOfView = 62f;
                wristCam.nearClipPlane = 0.005f;
                var aim = wristCam.gameObject.GetComponent<WristCamAim>() ?? wristCam.gameObject.AddComponent<WristCamAim>();
                aim.gripperTip = gripper;                        // EE tip = grasp point
                aim.gripper = gripper;
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
        public Transform gripperTip;     // grasp point (EE tip)
        public Transform gripper;        // gripper body (for the approach axis)
        public float back = 0.075f;      // how far BEHIND/up the grasp point to mount the camera
        public float up = 0.05f;         // extra height so the claw is seen from slightly above
        public float lookAhead = 0.03f;  // aim a touch beyond the tip so the target object centres

        void LateUpdate()
        {
            if (gripperTip == null) return;
            // The gripper's approach axis = the direction from the wrist toward the grasp point. We mount the
            // camera back along that axis + above, and look toward (slightly past) the grasp point — so the
            // jaws sit in the near-bottom of the frame and whatever is being grasped is centred below.
            Vector3 graspPt = gripperTip.position;
            Vector3 approach = gripper != null ? (graspPt - gripper.position) : Vector3.down;
            if (approach.sqrMagnitude < 1e-6f) approach = Vector3.down;
            approach.Normalize();
            // camera position: behind the grasp point along -approach, lifted up in world Y
            transform.position = graspPt - approach * back + Vector3.up * up;
            Vector3 look = graspPt + approach * lookAhead;
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
