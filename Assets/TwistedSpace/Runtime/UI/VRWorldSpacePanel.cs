using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.UI
{
    /// <summary>
    /// A world-space UGUI panel with comfortable, unscaled head-follow and interruptible fades.
    /// Put this on the Canvas, outside the camera hierarchy, and assign the XR camera explicitly.
    /// Use one EventSystem with XR UI Input Module, NOT StandaloneInputModule or
    /// InputSystemUIInputModule. On each XRI 3.x XRRayInteractor enable UI Interaction and
    /// bind its UI Press input to the controller trigger. Confirmation uses release-click events.
    /// Use a small world-space Canvas scale (for example 0.001), not a screen-space Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup), typeof(TrackedDeviceGraphicRaycaster))]
    [AddComponentMenu("Twisted Space/UI/VR World Space Panel")]
    public sealed class VRWorldSpacePanel : MonoBehaviour
    {
        [Header("Camera and follow")]
        [SerializeField, Tooltip("XR head camera. The Canvas camera is also accepted; Camera.main is queried only once as a last resort.")]
        private Camera headCamera;
        [SerializeField] private bool followHead = true;
        [SerializeField, Min(0.25f)] private float distance = 1.5f;
        [SerializeField, Tooltip("World-up offset in metres, relative to the head.")]
        private float verticalOffset = -0.1f;
        [SerializeField, Range(0.1f, 2f)] private float positionSmoothTime = 0.6f;
        [SerializeField, Range(0.1f, 2f), Tooltip("Exponential rotation time constant, in seconds.")]
        private float rotationDampingTime = 0.5f;
        [SerializeField, Range(0f, 80f)] private float maximumPitch = 25f;

        [Header("Visibility")]
        [SerializeField] private bool startVisible = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.45f;
        [SerializeField, Tooltip("Sampled from 0 to 1; endpoints are explicitly enforced.")]
        private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Optional button forwarding")]
        [SerializeField] private ConfirmedXRButton resetPuzzleButton;
        [SerializeField] private ConfirmedXRButton backButton;
        [SerializeField] private UnityEvent onResetPuzzle = new UnityEvent();
        [SerializeField] private UnityEvent onBack = new UnityEvent();

        private readonly List<ConfirmedXRButton> childButtons = new List<ConfirmedXRButton>(8);
        private Canvas panelCanvas;
        private CanvasGroup panelGroup;
        private ConfirmedXRButton boundResetButton;
        private ConfirmedXRButton boundBackButton;
        private Coroutine fadeRoutine;
        private Vector3 positionVelocity;
        private Vector3 smoothedPosition;
        private float stableYaw;
        private float smoothedYaw;
        private float smoothedPitch;
        private bool poseInitialized;
        private bool initialized;
        private bool interactionReady;

        /// <summary>Forwarded once for each confirmed Reset button click; existing button events remain intact.</summary>
        public UnityEvent OnResetPuzzle => onResetPuzzle;

        /// <summary>Forwarded once for each confirmed Back button click; does not automatically hide the panel.</summary>
        public UnityEvent OnBack => onBack;

        /// <summary>False during either fade, while hidden, or while the panel component is disabled.</summary>
        public bool IsInteractionReady =>
            initialized && isActiveAndEnabled && interactionReady && panelGroup != null &&
            panelGroup.enabled && panelGroup.interactable && panelGroup.blocksRaycasts &&
            panelGroup.alpha >= 0.999f;

        /// <summary>Disable to leave the panel at its current world pose. Resuming follow does not snap.</summary>
        public bool FollowHead
        {
            get => followHead;
            set
            {
                if (followHead == value)
                    return;

                followHead = value;
                positionVelocity = Vector3.zero;
                if (value && poseInitialized)
                    smoothedPosition = transform.position;
            }
        }

        private void Reset()
        {
            GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        }

        private void Awake()
        {
            panelCanvas = GetComponent<Canvas>();
            panelGroup = GetComponent<CanvasGroup>();
            if (panelCanvas.renderMode != RenderMode.WorldSpace)
            {
                FailSetup("requires a World Space Canvas. Change Canvas Render Mode before playing.");
                return;
            }

            if (headCamera == null)
                headCamera = panelCanvas.worldCamera;
            if (headCamera == null)
            {
                headCamera = Camera.main;
                if (headCamera != null)
                    Debug.LogWarning($"{nameof(VRWorldSpacePanel)} used Camera.main once. Assign the XR head camera explicitly.", this);
            }

            if (headCamera == null)
            {
                FailSetup("has no head camera. Assign an XR Camera or Canvas World Camera; the one-time Camera.main fallback failed.");
                return;
            }

            if (!ValidateCameraHierarchy())
                return;

            panelCanvas.worldCamera = headCamera;
            initialized = true;
            interactionReady = startVisible;
            panelGroup.alpha = startVisible ? 1f : 0f;
            SetInteraction(startVisible);

            // Store the actual subscribed references so inspector changes cannot leak listeners.
            boundResetButton = resetPuzzleButton;
            boundBackButton = backButton;
            if (boundResetButton != null)
                boundResetButton.onClick.AddListener(ForwardResetPuzzle);
            if (boundBackButton != null)
                boundBackButton.onClick.AddListener(ForwardBack);
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;
            if (headCamera == null)
            {
                FailSetup("lost its assigned head camera. Assign a valid camera before reinitializing the panel.");
                return;
            }
            if (!ValidateCameraHierarchy() || !followHead)
                return;

            GetTargetPose(out Vector3 targetPosition, out float targetPitch, out float targetYaw);
            if (!poseInitialized)
            {
                // First tracked pose is applied directly: never fly in from the scene origin.
                smoothedPosition = targetPosition;
                smoothedYaw = targetYaw;
                smoothedPitch = targetPitch;
                positionVelocity = Vector3.zero;
                poseInitialized = true;
            }
            else
            {
                float deltaTime = Time.unscaledDeltaTime;
                if (deltaTime <= 0f)
                    return;
                // Smooth the entire world position, including head translation in every axis.
                smoothedPosition = Vector3.SmoothDamp(smoothedPosition, targetPosition,
                    ref positionVelocity, Mathf.Max(0.01f, positionSmoothTime), Mathf.Infinity, deltaTime);
                float rotationBlend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, rotationDampingTime));
                smoothedYaw = Mathf.LerpAngle(smoothedYaw, targetYaw, rotationBlend);
                smoothedPitch = Mathf.Lerp(smoothedPitch, targetPitch, rotationBlend);
            }

            // +Z points away from the viewer, as required for readable world-space UGUI.
            // Damping yaw/pitch separately, rather than slerping arbitrary poses, prevents roll.
            transform.SetPositionAndRotation(smoothedPosition, Quaternion.Euler(smoothedPitch, smoothedYaw, 0f));
        }

        private void GetTargetPose(out Vector3 position, out float pitch, out float yaw)
        {
            Vector3 headForward = headCamera.transform.forward;
            Vector3 horizontalForward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (horizontalForward.sqrMagnitude > 0.01f)
                stableYaw = Mathf.Atan2(horizontalForward.x, horizontalForward.z) * Mathf.Rad2Deg;
            else if (!poseInitialized)
            {
                // At a vertical startup pose, camera right provides an initial horizontal heading.
                Vector3 horizontalRight = Vector3.ProjectOnPlane(headCamera.transform.right, Vector3.up);
                if (horizontalRight.sqrMagnitude > 0.01f)
                    stableYaw = Mathf.Atan2(-horizontalRight.z, horizontalRight.x) * Mathf.Rad2Deg;
            }

            yaw = stableYaw;
            float pitchLimit = Mathf.Clamp(maximumPitch, 0f, 80f);
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(headForward.y, -1f, 1f)) * Mathf.Rad2Deg,
                -pitchLimit, pitchLimit);
            Quaternion facing = Quaternion.Euler(pitch, yaw, 0f);
            position = headCamera.transform.position + facing * Vector3.forward * Mathf.Max(0.25f, distance)
                + Vector3.up * verticalOffset;
        }

        /// <summary>Fade from the current alpha. Interaction becomes available only at successful completion.</summary>
        public void FadeIn()
        {
            BeginFade(true);
        }

        /// <summary>Immediately cancel pending confirmation and block input, then fade from the current alpha.</summary>
        public void FadeOut()
        {
            BeginFade(false);
        }

        private void BeginFade(bool show)
        {
            if (!initialized || !isActiveAndEnabled)
                return;

            StopFade();
            interactionReady = false;
            SetInteraction(false);
            CancelPendingConfirmations();
            float targetAlpha = show ? 1f : 0f;
            if (fadeDuration <= 0f || Mathf.Approximately(panelGroup.alpha, targetAlpha))
            {
                CompleteFade(show);
                return;
            }

            fadeRoutine = StartCoroutine(FadeTo(show));
        }

        private IEnumerator FadeTo(bool show)
        {
            float startingAlpha = panelGroup.alpha;
            float targetAlpha = show ? 1f : 0f;
            float duration = Mathf.Max(0.001f, fadeDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // Yield first so a newly started coroutine cannot finish before its handle is assigned.
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float curved = fadeCurve != null && fadeCurve.length > 0
                    ? Mathf.Clamp01(fadeCurve.Evaluate(progress))
                    : progress * progress * (3f - 2f * progress);
                panelGroup.alpha = Mathf.Lerp(startingAlpha, targetAlpha, curved);
            }

            fadeRoutine = null;
            CompleteFade(show);
        }

        private void CompleteFade(bool show)
        {
            panelGroup.alpha = show ? 1f : 0f;
            interactionReady = show;
            SetInteraction(show);
        }

        private void SetInteraction(bool allowed)
        {
            panelGroup.interactable = allowed;
            panelGroup.blocksRaycasts = allowed;
        }

        private void StopFade()
        {
            if (fadeRoutine == null)
                return;
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        private void CancelPendingConfirmations()
        {
            // Populate a reused list only on visibility changes, including dynamically added descendants.
            GetComponentsInChildren(true, childButtons);
            for (int i = 0; i < childButtons.Count; i++)
                childButtons[i].CancelPendingConfirmation();
            childButtons.Clear();
            if (boundResetButton != null)
                boundResetButton.CancelPendingConfirmation();
            if (boundBackButton != null)
                boundBackButton.CancelPendingConfirmation();
        }

        private bool ValidateCameraHierarchy()
        {
            if (transform.IsChildOf(headCamera.transform))
            {
                FailSetup("must not be parented to the head camera. Move the Canvas outside the camera hierarchy.");
                return false;
            }
            return true;
        }

        private void FailSetup(string reason)
        {
            Debug.LogError($"{nameof(VRWorldSpacePanel)} {reason}", this);
            enabled = false;
        }

        private void ForwardResetPuzzle()
        {
            if (IsInteractionReady)
                onResetPuzzle.Invoke();
        }

        private void ForwardBack()
        {
            if (IsInteractionReady)
                onBack.Invoke();
        }

        private void OnDisable()
        {
            StopFade();
            interactionReady = false;
            if (panelGroup != null)
            {
                SetInteraction(false);
                panelGroup.alpha = 0f;
            }
            CancelPendingConfirmations();
            positionVelocity = Vector3.zero;
            poseInitialized = false;
            // Re-enabling intentionally stays hidden; call FadeIn explicitly to show it again.
        }

        private void OnDestroy()
        {
            if (boundResetButton != null)
                boundResetButton.onClick.RemoveListener(ForwardResetPuzzle);
            if (boundBackButton != null)
                boundBackButton.onClick.RemoveListener(ForwardBack);
        }
    }
}
