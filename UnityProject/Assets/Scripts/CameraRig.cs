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

        // ── named camera VIEW PRESETS (cycle through to inspect the arm + attachments) ──────────────────
        public enum View { Orbit, Front, Side, Top, CloseUp, Wide }
        public static readonly View[] AllViews = { View.Orbit, View.Front, View.Side, View.Top, View.CloseUp, View.Wide };
        public View currentView = View.Orbit;

        public static string ViewName(View v)
        {
            switch (v)
            {
                case View.Orbit:   return "Orbit";
                case View.Front:   return "Front";
                case View.Side:    return "Side";
                case View.Top:     return "Top-down";
                case View.CloseUp: return "Close-up";
                case View.Wide:    return "Workspace";
                default:           return v.ToString();
            }
        }

        /// <summary>Snap the orbit camera to a named viewpoint (yaw/pitch/distance + pivot). The user can
        /// still drag from there; the framing is just re-seeded.</summary>
        public void SetView(View v)
        {
            currentView = v;
            switch (v)
            {
                case View.Orbit:   yaw = 30f;  pitch = 28f; distance = 1.1f; pivotPoint = new Vector3(0f, 0.15f, 0.30f); break;
                case View.Front:   yaw = 0f;   pitch = 8f;  distance = 1.0f; pivotPoint = new Vector3(0f, 0.18f, 0.30f); break;
                case View.Side:    yaw = 90f;  pitch = 8f;  distance = 1.0f; pivotPoint = new Vector3(0f, 0.18f, 0.30f); break;
                case View.Top:     yaw = 0f;   pitch = 78f; distance = 1.1f; pivotPoint = new Vector3(0f, 0.10f, 0.30f); break;
                case View.CloseUp: yaw = 25f;  pitch = 20f; distance = 0.5f; pivotPoint = new Vector3(0f, 0.22f, 0.32f); break;
                case View.Wide:    yaw = 35f;  pitch = 35f; distance = 2.0f; pivotPoint = new Vector3(0f, 0.12f, 0.28f); break;
            }
        }

        /// <summary>Advance to the next/previous view preset (dir = +1 / -1).</summary>
        public View CycleView(int dir)
        {
            int idx = System.Array.IndexOf(AllViews, currentView);
            idx = ((idx + dir) % AllViews.Length + AllViews.Length) % AllViews.Length;
            SetView(AllViews[idx]);
            return currentView;
        }

        public void Setup(Transform gripper, Vector3 envPos, Vector3 envLookAt)
        {
            Setup(gripper, null, null, null, envPos, envLookAt);
        }

        /// <summary>Wrist-camera setup that derives its framing from the actual JAW geometry so it reliably
        /// shows BOTH jaws and looks OUT past them toward the work — independent of the twisted EE local
        /// frame. Pass the two jaw transforms + the EE; falls back to the EE if jaws are null.</summary>
        public void Setup(Transform endEffector, Transform jawA, Transform jawB, Transform gripperBody, Vector3 envPos, Vector3 envLookAt)
        {
            if (wristCam != null && endEffector != null)
            {
                var t = wristCam.transform;
                t.SetParent(null, true);                         // world-space rig (WristCamAim places it)
                // CLAW CAMERA: a wrist UVC-style view that frames BOTH jaws and the object ahead. The view
                // basis is built from the jaw geometry (mid-point + the line between the jaws), so the camera
                // sits behind/above the grasp point along the gripper's OUT direction and looks outward — the
                // jaws sit in the near frame, the target object is centred beyond them. (Previously the rig
                // got the SAME transform for tip+body, so its approach was zero and it fell back to a fixed
                // top-down world view that ignored the gripper and didn't frame the jaws.)
                // FOV 80deg + nearClip 0.01m MATCH THE REAL RIG: the SO-101 wrist UVC module (reBot UVC32
                // mount, ~60-90deg FOV) per design/specs/CAMERA_VISION_SPEC.md, so a vision policy trained on
                // this stream transfers to the physical camera. The real mount sits just behind the gripper
                // frame (TCP at gripper_link [-0.0079,-0.0002,-0.0981], rpy[0,180,0]) looking down the tool —
                // which is exactly the OUT direction the jaw-derived basis produces.
                wristCam.fieldOfView = 80f;
                wristCam.nearClipPlane = 0.01f;
                var aim = wristCam.gameObject.GetComponent<WristCamAim>() ?? wristCam.gameObject.AddComponent<WristCamAim>();
                aim.gripperTip = endEffector;                    // EE tip = grasp point
                aim.gripper = gripperBody != null ? gripperBody : endEffector;
                aim.jawA = jawA;
                aim.jawB = jawB;
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
        public Transform gripper;        // gripper/wrist body (root of the approach axis)
        public Transform jawA;           // the two jaw transforms — define the grasp basis (separation axis)
        public Transform jawB;
        public float back = 0.085f;      // how far BACK along the approach axis to mount the camera
        public float up = 0.02f;         // small lift along the camera-up so both jaws are seen from slightly above
        public float lookAhead = 0.05f;  // aim a touch beyond the grasp point so the target object centres

        void LateUpdate()
        {
            if (gripperTip == null) return;

            // GRASP BASIS from jaw geometry (robust to the twisted EE local frame):
            //   graspPt   = midpoint between the jaws (or EE tip if jaws unknown)
            //   sepAxis   = line between the two jaws (the open/close direction)
            //   outAxis   = direction from the wrist body OUT through the grasp point (where the claw faces)
            //   camUp     = perpendicular to BOTH sep and out -> keeps both jaws side-by-side, level in frame
            Vector3 graspPt, sepAxis;
            if (jawA != null && jawB != null)
            {
                graspPt = 0.5f * (jawA.position + jawB.position);
                sepAxis = (jawA.position - jawB.position);
            }
            else
            {
                graspPt = gripperTip.position;
                sepAxis = gripperTip.right;   // best guess
            }
            if (sepAxis.sqrMagnitude < 1e-8f) sepAxis = gripperTip.right;
            sepAxis.Normalize();

            // OUT axis: from the wrist body toward the grasp point, extended outward. If the body and grasp
            // point coincide, fall back to the EE's "down the tool" direction.
            Vector3 outAxis = gripper != null ? (graspPt - gripper.position) : (gripperTip.position - gripperTip.parent.position);
            if (outAxis.sqrMagnitude < 1e-8f) outAxis = -gripperTip.up;
            outAxis.Normalize();

            // camera up = perpendicular to the plane spanned by out & sep, so the two jaws are framed
            // left/right and the object sits centred ahead.
            Vector3 camUp = Vector3.Cross(outAxis, sepAxis);
            if (camUp.sqrMagnitude < 1e-8f) camUp = Vector3.up;
            camUp.Normalize();

            // mount the camera BACK along -out (behind the jaws) plus a small lift, look OUT past the jaws.
            transform.position = graspPt - outAxis * back + camUp * up;
            Vector3 look = graspPt + outAxis * lookAhead;
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude < 1e-8f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, camUp);
        }
    }
}
