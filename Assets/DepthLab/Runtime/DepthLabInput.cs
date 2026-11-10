using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace DepthLab
{
    public sealed class DepthLabInput : MonoBehaviour
    {
        public Camera headCamera;
        public Transform playerRoot;
        public DepthLabSettings settings;
        public CharacterController motor;
        public Renderer[] controllerVisuals;
        public XRInteractorLineVisual[] controllerLines;

        public bool MovementEnabled { get; set; }

        private const float StepInterval = .3f;
        private const float StickDeadzone = .2f;
        private const float GrabSpeed = 4f;
        private RaycastHit[] grabHits = new RaycastHit[16];
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<Vector3> uiRayPoints = new List<Vector3>(2);
        private static readonly List<XRInputSubsystem> XRSubsystems = new List<XRInputSubsystem>();
        private TrackedPoseDriver poseDriver;
        private bool disabledPoseDriver;
        private bool poseInitialized;
        private float pitch;
        private float yaw;
        private float nextMoveTime;
        private bool turnLatched;
        private bool mouseLookCaptured;
        private XRGrabInteractable held;
        private Rigidbody heldBody;
        private bool heldWasKinematic;
        private bool heldUsedGravity;
        private CollisionDetectionMode heldCollisionMode;
        private DepthLabGrabMotion heldMotion;
        private ChapterPiece heldPiece;
        private float grabDistance;
        private Vector3 grabOffset;
        private Vector3 grabPosition;
        private DepthLabTutorial heldTutorial;
        private bool menuButtonHeld;
        private bool[] visualStates;
        private bool[] lineStates;

        public static bool IsXRActive
        {
            get
            {
                if (XRSettings.isDeviceActive)
                    return true;
                SubsystemManager.GetSubsystems(XRSubsystems);
                foreach (XRInputSubsystem subsystem in XRSubsystems)
                    if (subsystem.running)
                        return true;
                return false;
            }
        }

        private void Update()
        {
            ResolveReferences();
            bool xrActive = IsXRActive;
            ConfigureTracking(xrActive);
            if (!poseInitialized && headCamera != null && playerRoot != null)
                ResetPose();
            AlignMotor();

            DepthLabApp app = DepthLabApp.Instance;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && app != null)
                app.TogglePause();
            bool menuPressed = false;
            if (xrActive)
                InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.menuButton, out menuPressed);
            if (menuPressed && !menuButtonHeld && app != null) app.TogglePause();
            menuButtonHeld = menuPressed;

            UpdateInput(app, xrActive, Application.isFocused, Time.deltaTime, Time.time);
        }

        private void UpdateInput(DepthLabApp app, bool xrActive, bool focused, float deltaTime, float time)
        {
            bool home = app != null && !xrActive && !app.IsPlaying && !app.IsTutorial &&
                (IsPageActive(app.homePage) || IsPageActive(app.chapterPage) ||
                 IsPageActive(app.detailPage) || IsPageActive(app.creditsPage) ||
                 IsPageActive(app.finalPage));
            bool canInteract = app != null && (app.IsPlaying || app.IsTutorial || home) &&
                !app.IsPaused && focused && !IsPageActive(app.settingsPage) &&
                !IsPageActive(app.modalPage) && !IsPageActive(app.pausePage) &&
                (xrActive || !IsTyping());
            if (!canInteract || headCamera == null || playerRoot == null)
            {
                CancelInput();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Vector2 move;
            float turn;
            if (xrActive)
            {
                mouseLookCaptured = false;
                EndGrab(true);
                UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                left.TryGetFeatureValue(CommonUsages.primary2DAxis, out move);
                right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rightAxis);
                turn = rightAxis.x;
            }
            else
            {
                UpdateMouseLook();
                if (home)
                    EndGrab(true);
                else
                    UpdateGrab();
                move = Vector2.zero;
                turn = 0f;
                if (keyboard != null)
                {
                    move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                    move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                    turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                }
            }

            if (!MovementEnabled && !home)
            {
                nextMoveTime = 0f;
                turnLatched = false;
                return;
            }
            Move(move, xrActive, deltaTime, time);
            Turn(turn, xrActive, deltaTime);
        }

        private static bool IsPageActive(GameObject page)
        {
            return page != null && page.activeInHierarchy;
        }

        private void CancelInput()
        {
            EndGrab(true);
            mouseLookCaptured = false;
            nextMoveTime = 0f;
            turnLatched = false;
        }

        private void ResolveReferences()
        {
            DepthLabApp app = DepthLabApp.Instance;
            if (playerRoot == null && app != null)
                playerRoot = app.PlayerRoot;
            if (headCamera == null)
                headCamera = app != null && app.Head != null ? app.Head.GetComponent<Camera>() : Camera.main;
            if (playerRoot != null && (motor == null || motor.transform != playerRoot))
                motor = playerRoot.GetComponent<CharacterController>();
        }

        private bool AlignMotor()
        {
            if (playerRoot == null || headCamera == null) return false;
            if (motor == null || motor.transform != playerRoot)
                motor = playerRoot.GetComponent<CharacterController>();
            if (motor == null || !motor.enabled || !motor.gameObject.activeInHierarchy || !motor.detectCollisions)
                return false;

            // Move the capsule, never the tracked camera. The rig origin can be metres from the HMD.
            Vector3 head = playerRoot.InverseTransformPoint(headCamera.transform.position);
            float height = Mathf.Max(motor.radius * 2f, head.y + .1f);
            if (!Mathf.Approximately(motor.height, height)) motor.height = height;
            Vector3 center = new Vector3(head.x, height * .5f, head.z);
            if ((motor.center - center).sqrMagnitude > .000001f) motor.center = center;
            return true;
        }

        private void ConfigureTracking(bool xrActive)
        {
            if (controllerLines != null)
            {
                if (lineStates == null)
                {
                    lineStates = new bool[controllerLines.Length];
                    for (int i = 0; i < controllerLines.Length; i++)
                        lineStates[i] = controllerLines[i] != null && controllerLines[i].enabled;
                }
                for (int i = 0; i < controllerLines.Length; i++)
                    if (controllerLines[i] != null) controllerLines[i].enabled = xrActive && lineStates[i];
            }
            if (controllerVisuals != null)
            {
                if (visualStates == null)
                {
                    visualStates = new bool[controllerVisuals.Length];
                    for (int i = 0; i < controllerVisuals.Length; i++)
                        visualStates[i] = controllerVisuals[i] != null && controllerVisuals[i].enabled;
                }
                for (int i = 0; i < controllerVisuals.Length; i++)
                    if (controllerVisuals[i] != null)
                        controllerVisuals[i].enabled = xrActive && visualStates[i];
            }
            TrackedPoseDriver current = headCamera != null ? headCamera.GetComponent<TrackedPoseDriver>() : null;
            if (poseDriver != current)
            {
                RestoreTracking();
                poseDriver = current;
                poseInitialized = false;
            }
            if (poseDriver == null)
                return;
            if (xrActive)
            {
                poseDriver.enabled = true;
                disabledPoseDriver = false;
            }
            else if (poseDriver.enabled)
            {
                poseDriver.enabled = false;
                disabledPoseDriver = true;
            }
        }

        private void RestoreTracking()
        {
            if (poseDriver != null && disabledPoseDriver)
                poseDriver.enabled = true;
            disabledPoseDriver = false;
        }

        public void ResetPose()
        {
            CancelInput();
            ResolveReferences();
            poseInitialized = headCamera != null && playerRoot != null;
            if (!poseInitialized)
                return;
            AlignMotor();
            Vector3 angles = headCamera.transform.localEulerAngles;
            pitch = Mathf.DeltaAngle(0f, angles.x);
            yaw = angles.y;
            nextMoveTime = 0f;
            turnLatched = false;
        }

        private void UpdateMouseLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                mouseLookCaptured = false;
                return;
            }
            // RMB is not a UI click. Capture once, including over UI, until release or cancellation.
            if (mouse.rightButton.wasPressedThisFrame)
                mouseLookCaptured = true;
            if (!mouseLookCaptured)
                return;
            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude < .01f)
                return;
            yaw += delta.x * .12f;
            pitch = Mathf.Clamp(pitch - delta.y * .12f, -80f, 80f);
            Quaternion before = headCamera.transform.localRotation;
            headCamera.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            if (Quaternion.Angle(before, headCamera.transform.localRotation) > .0001f)
                Report("controllers");
        }

        private void Move(Vector2 input, bool xrActive, float deltaTime, float time)
        {
            // Missing/disabled scene collision must fail closed, not silently teleport the rig.
            if (!AlignMotor()) return;
            if (input.magnitude <= StickDeadzone)
            {
                nextMoveTime = 0f;
                return;
            }
            input = Vector2.ClampMagnitude(input, 1f);
            bool stepped = xrActive && (settings == null || settings.moveMode == 0);
            if (stepped && time < nextMoveTime)
                return;
            float speed = settings != null ? Mathf.Clamp(settings.moveSpeed, .1f, 10f) : 1.5f;
            float distance = stepped ? Mathf.Min(speed * StepInterval, .5f) : speed * deltaTime;
            Vector3 forward = Vector3.ProjectOnPlane(headCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f)
                forward = Vector3.ProjectOnPlane(playerRoot.forward, Vector3.up).normalized;
            Vector3 direction = forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x;
            Vector3 before = playerRoot.position;
            Physics.SyncTransforms();
            motor.Move(direction * distance);
            nextMoveTime = stepped ? time + StepInterval : 0f;
            if ((playerRoot.position - before).sqrMagnitude > .000001f)
                Report("movement");
        }

        private void Turn(float input, bool xrActive, float deltaTime)
        {
            if (!AlignMotor()) return;
            if (Mathf.Abs(input) < .25f)
            {
                turnLatched = false;
                return;
            }
            bool snap = xrActive && (settings == null || settings.turnMode == 0);
            if (snap && (turnLatched || Mathf.Abs(input) < .65f))
                return;
            float angle = snap
                ? Mathf.Sign(input) * (settings != null ? Mathf.Clamp(settings.snapAngle, 15f, 90f) : 30f)
                : input * 60f * deltaTime;
            Vector3 pivot = headCamera.transform.position;
            // A vertical, head-centred capsule occupies the same world volume during this turn.
            playerRoot.RotateAround(pivot, Vector3.up, angle);
            turnLatched = true;
        }

        private bool IsPointerOverUI()
        {
            EventSystem events = EventSystem.current;
            Mouse mouse = Mouse.current;
            if (events == null || mouse == null)
                return false;
            PointerEventData pointer = new PointerEventData(events)
            {
                position = mouse.position.ReadValue(),
                button = PointerEventData.InputButton.Left
            };
            uiHits.Clear();
            events.RaycastAll(pointer, uiHits);
            foreach (RaycastResult hit in uiHits)
                if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Canvas>() != null)
                    return true;
            // Tracked-only world canvases ignore ordinary mouse PointerEventData.
            if (headCamera != null)
            {
                Ray ray = headCamera.ScreenPointToRay(pointer.position);
                uiRayPoints.Clear();
                uiRayPoints.Add(ray.origin);
                uiRayPoints.Add(ray.GetPoint(headCamera.farClipPlane));
                TrackedDeviceEventData trackedPointer = new TrackedDeviceEventData(events)
                {
                    pointerId = int.MinValue,
                    position = pointer.position,
                    rayPoints = uiRayPoints,
                    layerMask = headCamera.cullingMask
                };
                uiHits.Clear();
                events.RaycastAll(trackedPointer, uiHits);
                foreach (RaycastResult hit in uiHits)
                    if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Canvas>() != null)
                        return true;
            }
            return false;
        }

        private static bool IsTyping()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null &&
                (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null);
        }

        private void UpdateGrab()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                EndGrab(true);
                return;
            }
            if ((heldBody != null || held != null) &&
                (held == null || heldBody == null || !mouse.leftButton.isPressed || !held.isActiveAndEnabled || held.isSelected))
            {
                if (held == null || heldBody == null || !held.isActiveAndEnabled || held.isSelected)
                    EndGrab(true);
                else
                    ReleaseGrab();
            }
            Ray ray = headCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (mouse.leftButton.wasPressedThisFrame && held == null && !IsPointerOverUI() &&
                TryGetGrabHit(ray, out RaycastHit hit))
            {
                Rigidbody body = hit.collider.attachedRigidbody;
                XRGrabInteractable candidate = body != null ? body.GetComponent<XRGrabInteractable>() : null;
                if (candidate != null && candidate.isActiveAndEnabled && !candidate.isSelected &&
                    candidate.colliders.Contains(hit.collider))
                {
                    BeginGrab(candidate, hit.point, hit.distance);
                }
            }
            if (held != null)
            {
                grabDistance = Mathf.Clamp(grabDistance + mouse.scroll.ReadValue().y * .0025f, .3f, 8f);
                grabPosition = ray.GetPoint(grabDistance) + grabOffset;
            }
        }

        private bool TryGetGrabHit(Ray ray, out RaycastHit nearest)
        {
            Physics.SyncTransforms();
            PhysicsScene scene = headCamera.gameObject.scene.GetPhysicsScene();
            int count;
            // Physical occlusion must include solids on Ignore Raycast too, not just selectable layers.
            while ((count = scene.Raycast(ray.origin, ray.direction, grabHits, 8f, ~0,
                QueryTriggerInteraction.Ignore)) == grabHits.Length)
                System.Array.Resize(ref grabHits, grabHits.Length * 2);
            nearest = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = grabHits[i];
                if (hit.collider == motor || hit.distance >= distance) continue;
                nearest = hit;
                distance = hit.distance;
            }
            return distance < float.PositiveInfinity;
        }

        private void FixedUpdate()
        {
            if (heldBody == null || held == null) return;
            DepthLabApp app = DepthLabApp.Instance;
            if (app == null || app.IsPaused || (!app.IsPlaying && !app.IsTutorial) ||
                IsPageActive(app.settingsPage) || IsPageActive(app.modalPage) || IsPageActive(app.pausePage) ||
                !held.isActiveAndEnabled || held.isSelected ||
                (heldPiece != null && (heldPiece.chapter == null || !heldPiece.chapter.InteractionAllowed)))
            {
                EndGrab(true);
                return;
            }
            Physics.SyncTransforms();
            if (heldMotion != null)
            {
                Vector3 target = Vector3.MoveTowards(heldBody.position, grabPosition, GrabSpeed * Time.fixedDeltaTime);
                heldBody.MovePosition(heldMotion.Constrain(target));
            }
        }

        private void BeginGrab(XRGrabInteractable candidate, Vector3 point, float distance)
        {
            Rigidbody body = candidate.GetComponent<Rigidbody>();
            if (body == null || !body.detectCollisions || !candidate.isActiveAndEnabled || candidate.isSelected) return;
            ChapterPiece piece = candidate.GetComponent<ChapterPiece>();
            if (piece != null && (piece.IsPlaced || piece.chapter == null || !piece.chapter.InteractionAllowed)) return;
            DepthLabGrabMotion motion = new DepthLabGrabMotion(body);
            if (!motion.CanSweep) { motion.Dispose(); return; }
            held = candidate;
            heldBody = body;
            heldPiece = piece;
            heldMotion = motion;
            heldWasKinematic = body.isKinematic;
            heldUsedGravity = body.useGravity;
            heldCollisionMode = body.collisionDetectionMode;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;
            grabDistance = distance;
            grabOffset = body.position - point + Vector3.up * motion.PickupLift();
            grabPosition = point + grabOffset;
            if (heldPiece != null) heldPiece.NotifyGrab();
            foreach (DepthLabTutorial tutorial in FindObjectsByType<DepthLabTutorial>())
            {
                if (tutorial.practiceObject != null &&
                    tutorial.practiceObject.GetComponentInChildren<XRGrabInteractable>() == held)
                {
                    heldTutorial = tutorial;
                    heldTutorial.SetHeld(true);
                    break;
                }
            }
            Report("grab");
        }

        private void ReleaseGrab()
        {
            EndGrab(false);
        }

        private void EndGrab(bool cancelled)
        {
            bool hadGrab = held != null || heldBody != null || heldTutorial != null;
            bool xrOwnsBody = held != null && held.isSelected;
            LabChapter chapter = heldPiece != null ? heldPiece.chapter :
                held != null ? held.GetComponentInParent<LabChapter>() : null;
            bool chapterOwnsBody = (chapter != null && !chapter.InteractionAllowed) ||
                (heldPiece != null && heldPiece.IsPlaced);
            if (heldBody != null && !xrOwnsBody && !chapterOwnsBody)
            {
                heldBody.isKinematic = heldWasKinematic;
                heldBody.useGravity = heldUsedGravity;
                heldBody.collisionDetectionMode = heldCollisionMode;
                if (!heldWasKinematic)
                {
                    heldBody.linearVelocity = Vector3.zero;
                    heldBody.angularVelocity = Vector3.zero;
                    heldBody.WakeUp();
                }
            }
            if (heldPiece != null && !xrOwnsBody && !chapterOwnsBody)
            {
                if (cancelled) heldPiece.SuspendInteraction(true);
                else heldPiece.NotifyRelease();
            }
            // A paused chapter owns its frozen pose/kinematic state, but gravity must survive resume.
            if (heldBody != null && chapterOwnsBody && !xrOwnsBody)
            {
                heldBody.useGravity = heldUsedGravity;
                if (heldCollisionMode == CollisionDetectionMode.Discrete ||
                    heldCollisionMode == CollisionDetectionMode.ContinuousSpeculative)
                    heldBody.collisionDetectionMode = heldCollisionMode;
                else if (heldPiece != null)
                    heldPiece.RestoreCollisionModeWhenDynamic(heldCollisionMode);
            }
            held = null;
            heldBody = null;
            heldPiece = null;
            heldMotion?.Dispose();
            heldMotion = null;
            if (heldTutorial != null)
                heldTutorial.SetHeld(false);
            heldTutorial = null;
            if (hadGrab)
                Report("release");
        }

        private static void Report(string action)
        {
            if (DepthLabApp.Instance != null)
                DepthLabApp.Instance.ReportTutorialAction(action);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                CancelInput();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                CancelInput();
        }

        private void OnDisable()
        {
            CancelInput();
            RestoreTracking();
            poseInitialized = false;
            if (controllerVisuals != null && visualStates != null)
                for (int i = 0; i < controllerVisuals.Length; i++)
                    if (controllerVisuals[i] != null) controllerVisuals[i].enabled = visualStates[i];
            visualStates = null;
            if (controllerLines != null && lineStates != null)
                for (int i = 0; i < controllerLines.Length; i++)
                    if (controllerLines[i] != null) controllerLines[i].enabled = lineStates[i];
            lineStates = null;
        }
    }
}
