using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.UI
{
    /// <summary>
    /// XR-default UGUI Button: hold UI Press for at least 0.15 seconds, then release over this
    /// button to invoke the inherited onClick once. Leaving, hiding or disabling cancels it.
    /// Use XR UI Input Module and an XRI 3.x ray interactor with Enable UI Interaction and
    /// a trigger-bound UI Press input. Poke click-on-down and Submit are intentionally unsupported.
    /// Replace the ordinary Button component; do not put two Buttons on the same object.
    /// The included custom inspector exposes Face Image/Halo Image and confirmation settings
    /// alongside the inherited On Click event for normal editor drag-and-drop assignment.
    /// Leave Transition at None; do not separately animate these Images' colors.
    /// An optional, slightly larger child/sibling Image behind the face supplies a pale-blue
    /// simulated unlit glow. Give it a soft-alpha sprite and a transparent resting color.
    /// This is NOT URP emission and requires neither bloom nor a custom material.
    /// SimulatorMenuInput may opt into ordinary desktop mouse clicks at runtime. These use
    /// a fixed 0.15-second unscaled delay after clicking, not the XR hold requirement.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Twisted Space/UI/Confirmed XR Button")]
    public sealed class ConfirmedXRButton : Button
    {
        [Header("Release confirmation")]
        [SerializeField, Min(0.15f), Tooltip("Minimum unscaled hold time. A longer hold still requires release over this button.")]
        private float confirmationHoldSeconds = 0.15f;

        [Header("Soft unlit highlight")]
        [SerializeField, Tooltip("Optional override; otherwise the inherited Target Graphic Image is used.")]
        private Image faceImage;
        [SerializeField, Tooltip("Separate soft-alpha Image behind the button. Raycast Target is forced off.")]
        private Image haloImage;
        [SerializeField, Min(1f)] private float hoverBrightness = 1.16f;
        [SerializeField] private Color hoverHaloColor = new Color(0.55f, 0.82f, 1f, 0.4f);
        [SerializeField, Min(0f)] private float hoverInDuration = 0.3f;
        [SerializeField, Min(0f)] private float hoverOutDuration = 0.5f;

        private readonly HashSet<int> hoveringPointers = new HashSet<int>();
        private readonly List<CanvasGroup> canvasGroupBuffer = new List<CanvasGroup>(4);
        private Color baseFaceColor;
        private Color baseHaloColor;
        private bool visualsCached;
        private bool hasPressOwner;
        private int pressOwnerId;
        private double pressStartedAt;
        private bool releasePending;
        private int releaseFrame;
        private float hoverAmount;
        private float hoverStart;
        private float hoverTarget;
        private float hoverElapsed;
        private const float MouseConfirmationSeconds = 0.15f;
        private bool allowDesktopMouseInput;
        private Coroutine mouseConfirmation;
        private bool mouseConfirmationPending;
        private double mouseClickedAt;

        /// <summary>Runtime-only, default false. Switching modes cancels input; desktop mode excludes XR pointers.</summary>
        public bool AllowDesktopMouseInput
        {
            get => allowDesktopMouseInput;
            set
            {
                if (allowDesktopMouseInput == value)
                    return;
                CancelPendingConfirmation();
                hoveringPointers.Clear();
                ResetHover();
                allowDesktopMouseInput = value;
                ApplyVisuals(InteractionAllowed());
            }
        }

        /// <summary>True during an accepted pointer hold or the desktop post-click delay.</summary>
        public bool IsConfirming => mouseConfirmationPending || (hasPressOwner && !releasePending);

        /// <summary>Unscaled progress. XR still requires release; mouse progress starts after a completed click.</summary>
        public float ConfirmationProgress => mouseConfirmationPending
            ? Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - mouseClickedAt) / MouseConfirmationSeconds))
            : IsConfirming && !allowDesktopMouseInput
                ? Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - pressStartedAt) / Mathf.Max(0.15f, confirmationHoldSeconds)))
                : 0f;

        protected override void Awake()
        {
            base.Awake();
            // Our renderer owns colors; never run Selectable's competing tint/sprite/animation transitions.
            transition = Transition.None;
            if (faceImage == null)
                faceImage = targetGraphic as Image;
            if (haloImage != null && (haloImage == faceImage || haloImage == targetGraphic))
            {
                Debug.LogError($"{nameof(ConfirmedXRButton)} requires a separate halo Image, not the button's Target Graphic.", this);
                haloImage = null;
            }
            if (faceImage != null)
                baseFaceColor = faceImage.color;
            if (haloImage != null)
            {
                baseHaloColor = haloImage.color;
                haloImage.raycastTarget = false;
            }
            visualsCached = true;
        }

        protected override void OnEnable()
        {
            transition = Transition.None;
            base.OnEnable();
            CancelPendingConfirmation();
            ResetHover();
            ApplyVisuals(InteractionAllowed());
        }

        protected override void OnDisable()
        {
            CancelPendingConfirmation();
            hoveringPointers.Clear();
            ResetHover();
            base.OnDisable();
            ApplyVisuals(false);
        }

        protected override void OnCanvasGroupChanged()
        {
            base.OnCanvasGroupChanged();
            if (!InteractionAllowed())
            {
                CancelPendingConfirmation();
                ResetHover();
                ApplyVisuals(false);
            }
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            // Called synchronously by the inherited interactable setter, including false -> true
            // changes within one frame. Do not rely solely on polling in Update to cancel a hold.
            if (!Application.isPlaying || !visualsCached)
                return;
            bool allowed = InteractionAllowed();
            if (!allowed)
            {
                CancelPendingConfirmation();
                ResetHover();
            }
            ApplyVisuals(allowed);
        }

        private void Update()
        {
            bool allowed = InteractionAllowed();
            // UIInputModule dispatches Up then Click synchronously. A release not consumed in
            // that frame must never authorize a later unrelated click (for example after dragging).
            if (!allowed || (releasePending && releaseFrame != Time.frameCount))
                CancelPendingConfirmation();
            if (!allowed)
            {
                ResetHover();
                ApplyVisuals(false);
                return;
            }

            float target = hoveringPointers.Count > 0 ? 1f : 0f;
            if (target != hoverTarget)
            {
                hoverStart = hoverAmount;
                hoverTarget = target;
                hoverElapsed = 0f;
            }
            float duration = hoverTarget > 0f ? hoverInDuration : hoverOutDuration;
            hoverElapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(hoverElapsed / duration);
            float smooth = t * t * (3f - 2f * t);
            hoverAmount = Mathf.Lerp(hoverStart, hoverTarget, smooth);
            ApplyVisuals(true);
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            if (IsActive() && IsAllowedPointer(eventData))
                hoveringPointers.Add(eventData.pointerId);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            if (!IsAllowedPointer(eventData))
                return;
            hoveringPointers.Remove(eventData.pointerId);
            if (hasPressOwner && eventData.pointerId == pressOwnerId)
                CancelPendingConfirmation();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || !InteractionAllowed() || !RayHitsThisButton(eventData))
                return;
            if (hasPressOwner || mouseConfirmationPending)
                return;

            // XRI may dispatch Down before Enter on the first frame over a new target.
            hoveringPointers.Add(eventData.pointerId);
            hasPressOwner = true;
            pressOwnerId = eventData.pointerId;
            pressStartedAt = Time.unscaledTimeAsDouble;
            releasePending = false;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || !hasPressOwner || eventData.pointerId != pressOwnerId)
                return;
            if (releasePending)
                return;

            if (!InteractionAllowed() || !hoveringPointers.Contains(pressOwnerId) ||
                !RayHitsThisButton(eventData) || eventData.dragging || !eventData.eligibleForClick ||
                (!allowDesktopMouseInput &&
                 Time.unscaledTimeAsDouble - pressStartedAt < Mathf.Max(0.15f, confirmationHoldSeconds)))
            {
                CancelPendingConfirmation();
                return;
            }

            // Preserve ownership and timestamp until the immediately following OnPointerClick.
            releasePending = true;
            releaseFrame = Time.frameCount;
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || !hasPressOwner || eventData.pointerId != pressOwnerId)
                return;

            bool confirmed = releasePending && releaseFrame == Time.frameCount && InteractionAllowed() &&
                hoveringPointers.Contains(pressOwnerId) && RayHitsThisButton(eventData) &&
                eventData.eligibleForClick && !eventData.dragging;
            // Clear before callbacks: listeners may disable, hide, or destroy this button.
            CancelPendingConfirmation();
            if (confirmed)
            {
                if (allowDesktopMouseInput)
                {
                    mouseConfirmationPending = true;
                    mouseClickedAt = Time.unscaledTimeAsDouble;
                    mouseConfirmation = StartCoroutine(ConfirmMouseClick());
                }
                else
                    onClick.Invoke();
            }
            // Never call Button.OnPointerClick: its ordinary click path bypasses confirmation.
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            // Keyboard/gamepad Submit has no tracked-pointer hold/release evidence.
            // Do not call the base implementation, which would invoke onClick immediately.
        }

        private IEnumerator ConfirmMouseClick()
        {
            while (Time.unscaledTimeAsDouble - mouseClickedAt < MouseConfirmationSeconds)
            {
                yield return null;
                if (!allowDesktopMouseInput || !InteractionAllowed())
                {
                    mouseConfirmation = null;
                    CancelPendingConfirmation();
                    yield break;
                }
            }

            mouseConfirmation = null;
            CancelPendingConfirmation();
            onClick.Invoke();
        }

        /// <summary>Invalidate a hold, unconsumed release or delayed mouse click. Safe to call repeatedly.</summary>
        public void CancelPendingConfirmation()
        {
            if (mouseConfirmation != null)
                StopCoroutine(mouseConfirmation);
            mouseConfirmation = null;
            mouseConfirmationPending = false;
            mouseClickedAt = 0d;
            hasPressOwner = false;
            releasePending = false;
            pressOwnerId = 0;
            pressStartedAt = 0d;
            releaseFrame = -1;
        }

        private bool IsAllowedPointer(PointerEventData eventData)
        {
            if (!allowDesktopMouseInput)
                return eventData is TrackedDeviceEventData;
            if (eventData == null || eventData is TrackedDeviceEventData)
                return false;
            if (eventData is ExtendedPointerEventData extended)
                return extended.device is Mouse;

            // Unity's conventional mouse IDs are negative. XRI 3.3.2 instead constructs
            // PointerModel(0), also used by its touch path, so ID zero needs module provenance.
            if (eventData.pointerId == -1)
                return true;
            return eventData.pointerId == 0 &&
                eventData.currentInputModule is XRUIInputModule module &&
                module.enableMouseInput && !module.enableTouchInput && HasMouseOnlyPointerSource(module);
        }

        private bool IsPrimaryPointer(PointerEventData eventData)
        {
            return IsAllowedPointer(eventData) && eventData.button == PointerEventData.InputButton.Left;
        }

        internal static bool HasMouseOnlyPointerSource(XRUIInputModule module)
        {
            bool hasReferences = module.pointAction || module.leftClickAction || module.rightClickAction ||
                module.middleClickAction || module.scrollWheelAction || module.navigateAction ||
                module.submitAction || module.cancelAction;
            if (!hasReferences)
                return module.enableBuiltinActionsAsFallback;
            return IsMouseAction(module.pointAction) && IsMouseAction(module.leftClickAction);
        }

        private static bool IsMouseAction(InputActionReference reference)
        {
            if (reference == null || reference.action == null || !reference.action.enabled)
                return false;
            bool hasBinding = false;
            foreach (InputBinding binding in reference.action.bindings)
            {
                if (binding.isComposite)
                    continue;
                if (string.IsNullOrEmpty(binding.effectivePath) ||
                    !binding.effectivePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
                    return false;
                hasBinding = true;
            }
            return hasBinding;
        }

        private bool RayHitsThisButton(PointerEventData eventData)
        {
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            return hit != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) == gameObject;
        }

        private bool InteractionAllowed()
        {
            if (!IsActive() || !IsInteractable())
                return false;

            bool ignoreAncestorGroups = false;
            for (Transform current = transform; current != null; current = current.parent)
            {
                // A descendant's ignoreParentGroups must not bypass the panel's fade/disable gate.
                if (current.TryGetComponent(out VRWorldSpacePanel panel) && !panel.IsInteractionReady)
                    return false;
                if (ignoreAncestorGroups)
                    continue;

                current.GetComponents(canvasGroupBuffer);
                bool ignoreAboveThisLevel = false;
                for (int i = 0; i < canvasGroupBuffer.Count; i++)
                {
                    CanvasGroup group = canvasGroupBuffer[i];
                    if (!group.enabled)
                        continue;
                    if (!group.interactable || !group.blocksRaycasts || group.alpha <= 0.001f)
                        return false;
                    ignoreAboveThisLevel |= group.ignoreParentGroups;
                }
                ignoreAncestorGroups = ignoreAboveThisLevel;
            }
            return true;
        }

        private void ResetHover()
        {
            hoverAmount = 0f;
            hoverStart = 0f;
            hoverTarget = 0f;
            hoverElapsed = 0f;
        }

        private void ApplyVisuals(bool allowed)
        {
            if (!visualsCached)
                return;
            if (faceImage != null)
            {
                Color tint = colors.normalColor;
                if (!allowed)
                    tint = colors.disabledColor;
                else if (IsConfirming)
                    tint = colors.pressedColor;
                else if (currentSelectionState == SelectionState.Selected)
                    tint = colors.selectedColor;
                else
                {
                    float brightness = Mathf.Max(1f, hoverBrightness);
                    Color brightTint = new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, tint.a);
                    tint = Color.Lerp(tint, brightTint, hoverAmount);
                }
                Color result = baseFaceColor * tint;
                if (faceImage.color != result)
                    faceImage.color = result;
            }
            if (haloImage != null)
            {
                Color result = allowed ? Color.Lerp(baseHaloColor, hoverHaloColor, hoverAmount) : Color.clear;
                if (haloImage.color != result)
                    haloImage.color = result;
            }
        }
    }
}
