using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Higher-level mouse interactions layered ON TOP of the approved mouse-follow IK control
    /// (ArmController) — it does NOT modify that control, it just scripts the IK target + gripper.
    ///   - Double-click an object  -> arm moves over it, descends, closes gripper (click-to-grab)
    ///   - Click-grab then click a spot -> carries + releases there (drag-to-place)
    ///   - Hold Shift + drag        -> records a traced path; release -> arm follows it (draw-a-path),
    ///                                 and the path is captured as a trajectory (training seed).
    /// See ROADMAP "More mouse control". The user confirmed the base mouse-follow feels great; this
    /// extends it and lets us observe how mouse motion translates into recorded trajectories.
    /// </summary>
    public class MouseInteraction : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public Transform ikTarget;
        public Camera cam;
        public BehaviourRecorder recorder;

        public float grabDescend = 0.03f;     // height above object to descend to before closing
        public float carryHeight = 0.16f;     // lift height while carrying

        enum State { Idle, Carrying }
        State state = State.Idle;
        float lastClickTime; int clickCount;

        // draw-a-path
        bool drawing;
        readonly List<Vector3> path = new List<Vector3>();

        public string status = "";

        public void Bind(ArmController c, ProceduralArm a, Transform target, Camera camera, BehaviourRecorder rec)
        {
            controller = c; arm = a; ikTarget = target; cam = camera; recorder = rec;
        }

        void Update()
        {
            if (controller == null || cam == null) return;

            // --- Draw-a-path: hold Shift, drag with LMB ---
            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (Input.GetMouseButtonDown(0)) { drawing = true; path.Clear(); controller.mouseFollow = false; status = "drawing path..."; }
                if (drawing && Input.GetMouseButton(0))
                {
                    if (WorkPlaneHit(out Vector3 p)) { if (path.Count == 0 || Vector3.Distance(path[path.Count - 1], p) > 0.01f) path.Add(p); }
                }
                if (drawing && Input.GetMouseButtonUp(0)) { drawing = false; StartCoroutine(FollowPath()); }
                return;
            }

            // --- Double-click to grab / place ---
            if (Input.GetMouseButtonDown(0))
            {
                float t = Time.time;
                clickCount = (t - lastClickTime < 0.35f) ? clickCount + 1 : 1;
                lastClickTime = t;
                if (clickCount >= 2) { clickCount = 0; OnDoubleClick(); }
            }
        }

        void OnDoubleClick()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 5f)) return;

            if (state == State.Idle)
            {
                var rb = hit.collider.attachedRigidbody;
                if (rb != null) StartCoroutine(GrabAt(rb.transform));   // grab a movable object
            }
            else
            {
                StartCoroutine(PlaceAt(hit.point));                     // place where clicked
            }
        }

        IEnumerator GrabAt(Transform obj)
        {
            status = "grabbing " + obj.name;
            controller.mouseFollow = false;
            if (arm.gripper != null) arm.gripper.SetClose(0f);
            // approach above
            yield return MoveTo(obj.position + Vector3.up * carryHeight, 0.8f);
            // descend
            yield return MoveTo(obj.position + Vector3.up * grabDescend, 0.7f);
            // close
            if (arm.gripper != null) arm.gripper.SetClose(1f);
            yield return new WaitForSeconds(0.5f);
            // lift
            yield return MoveTo(obj.position + Vector3.up * carryHeight, 0.6f);
            state = State.Carrying;
            status = "carrying (double-click a spot to place)";
        }

        IEnumerator PlaceAt(Vector3 point)
        {
            status = "placing";
            yield return MoveTo(point + Vector3.up * carryHeight, 0.8f);
            yield return MoveTo(point + Vector3.up * 0.04f, 0.7f);
            if (arm.gripper != null) arm.gripper.SetClose(0f);
            yield return new WaitForSeconds(0.4f);
            yield return MoveTo(point + Vector3.up * carryHeight, 0.5f);
            state = State.Idle;
            status = "placed";
            controller.mouseFollow = true; // hand control back to the player
        }

        IEnumerator FollowPath()
        {
            if (path.Count < 2) { controller.mouseFollow = true; yield break; }
            status = $"following drawn path ({path.Count} pts)";
            if (recorder != null) recorder.StartRecording();   // capture as a trajectory (training seed)
            foreach (var p in path)
                yield return MoveTo(new Vector3(p.x, Mathf.Max(controller.minTargetY, p.y), p.z), 0.12f);
            if (recorder != null) recorder.StopRecording();
            status = "path done (recorded as trajectory)";
            controller.mouseFollow = true;
        }

        // Smoothly drive the IK target to a world point over `dur` seconds (IK + servos do the rest).
        IEnumerator MoveTo(Vector3 worldGoal, float dur)
        {
            Vector3 start = ikTarget.position;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                ikTarget.position = Vector3.Lerp(start, worldGoal, Mathf.SmoothStep(0, 1, t / dur));
                yield return null;
            }
            ikTarget.position = worldGoal;
        }

        bool WorkPlaneHit(out Vector3 p)
        {
            Plane work = new Plane(Vector3.up, new Vector3(0, controller.workPlaneY, 0));
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (work.Raycast(ray, out float e)) { p = ray.GetPoint(e); return true; }
            p = Vector3.zero; return false;
        }
    }
}
