using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TwistedSpace.Puzzles
{
    /// <summary>
    /// Inspector setup: tracked center-eye Camera, a reference transform whose FORWARD is
    /// the required WORLD look direction (not parented to the head), observation volumes,
    /// and at least two unique grabbable targets. This measures projected AABBs, not exact
    /// mesh silhouettes, depth visibility or physical contact. No XR poses are driven here.
    /// Use OnProgressChanged for events-only UI, or assign a World Space UGUI Slider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ForcedPerspectivePuzzle : MonoBehaviour
    {
        [Serializable]
        public sealed class ProgressEvent : UnityEvent<float> { }

        [Header("Drag-and-drop setup")]
        [SerializeField] private Camera headCamera;
        [SerializeField, Tooltip("其世界 forward 是要求的观看方向，不是注视点；不要放在头部层级下。")]
        private Transform requiredLookDirection;
        [SerializeField] private PuzzleObservationVolume[] observationVolumes = new PuzzleObservationVolume[0];
        [SerializeField] private PerspectivePuzzleTarget[] targets = new PerspectivePuzzleTarget[0];

        [Header("Forgiving alignment")]
        [SerializeField, Range(0.01f, 1f), Tooltip("所有目标公共交集 / 所有目标包围矩形；不是相对锚点的平均值。")]
        private float overlapThreshold = 0.65f;
        [SerializeField, Range(0f, 180f)] private float lookToleranceDegrees = 25f;
        [SerializeField, Min(0f)] private float stableHoldSeconds = 0.35f;
        [SerializeField, Range(0.00001f, 0.1f), Tooltip("拒绝过小投影，单位为视口宽/高比例。")]
        private float minimumViewportSize = 0.002f;

        [Header("Progress and success")]
        [SerializeField, Tooltip("可留空。必须属于 World Space Canvas，范围会设为 0..1。")]
        private Slider progressSlider;
        [SerializeField, Min(0f)] private float progressSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float successFadeSeconds = 0.45f;
        [SerializeField] private AnimationCurve successFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private ProgressEvent onProgressChanged = new ProgressEvent();

        [SerializeField, Tooltip("成功只触发一次；调用 ResetPuzzle 后可再次触发。")]
        private UnityEvent onPuzzleSolved = new UnityEvent();

        /// <summary>Invoked once per solve; bind doors/audio in the Inspector or with AddListener.</summary>
        public UnityEvent OnPuzzleSolved => onPuzzleSolved;
        public ProgressEvent OnProgressChanged => onProgressChanged;
        public bool IsSolved { get; private set; }
        public float Progress { get; private set; }
        public float CurrentOverlap { get; private set; }
        public float StableHoldProgress => stableHoldSeconds > 0f
            ? Mathf.Clamp01(stableElapsed / stableHoldSeconds) : (IsSolved ? 1f : 0f);

        private Rect[] projectedRectangles;
        private bool configurationValid;
        private Slider validatedSlider;
        private float stableElapsed;
        private float progressVelocity;
        private Coroutine successRoutine;
        private int stateVersion;
        private bool publishingProgress;

        private void Start()
        {
            configurationValid = ValidateSetup();
            if (!configurationValid)
                return;
            projectedRectangles = new Rect[targets.Length];
            ValidateSlider();
            PublishProgress(0f, true);
        }

        private bool ValidateSetup()
        {
            bool valid = headCamera && requiredLookDirection &&
                !requiredLookDirection.IsChildOf(headCamera.transform) &&
                targets != null && targets.Length >= 2 &&
                observationVolumes != null && observationVolumes.Length > 0;
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!targets[i] || !targets[i].IsConfigured)
                        valid = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (targets[i] && (targets[i] == targets[j] || targets[i].SharesRendererWith(targets[j])))
                            valid = false;
                    }
                }
            }
            if (observationVolumes != null)
            {
                for (int i = 0; i < observationVolumes.Length; i++)
                    if (!observationVolumes[i] || !observationVolumes[i].IsConfigured)
                        valid = false;
            }
            if (!valid)
                Debug.LogError("Puzzle setup invalid: assign a head Camera, independent look-direction " +
                    "transform, configured head observation volumes and at least two unique valid targets. " +
                    "Targets cannot share renderers. Configure references before Start.", this);
            return valid;
        }

        private void ValidateSlider()
        {
            validatedSlider = null;
            if (!progressSlider)
                return;
            Canvas canvas = progressSlider.GetComponentInParent<Canvas>(true);
            if (!canvas || canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
            {
                Debug.LogError("Puzzle progress Slider must be under a World Space Canvas. " +
                    "Slider output disabled; OnProgressChanged events remain available.", this);
                return;
            }
            validatedSlider = progressSlider;
            validatedSlider.minValue = 0f;
            validatedSlider.maxValue = 1f;
            validatedSlider.wholeNumbers = false;
            validatedSlider.interactable = false;
        }

        private void LateUpdate()
        {
            if (!configurationValid || IsSolved)
                return;

            CurrentOverlap = 0f;
            bool inside = false;
            for (int i = 0; i < observationVolumes.Length; i++)
            {
                if (observationVolumes[i] && observationVolumes[i].IsHeadInside)
                {
                    inside = true;
                    break;
                }
            }

            bool correctLook = false;
            bool validProjection = inside && headCamera && headCamera.isActiveAndEnabled && requiredLookDirection;
            if (validProjection)
            {
                correctLook = Vector3.Dot(headCamera.transform.forward, requiredLookDirection.forward) >=
                    Mathf.Cos(lookToleranceDegrees * Mathf.Deg2Rad);

                // No projection work at all outside the region. All targets must be valid;
                // never drop an invalid target from the multi-way intersection.
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!targets[i] || !targets[i].TryGetViewportBounds(headCamera, minimumViewportSize,
                        out projectedRectangles[i]))
                        validProjection = false;
                }
                if (validProjection)
                    CurrentOverlap = ProjectedBoundsUtility.ConservativeOverlap(projectedRectangles);
            }

            bool correct = validProjection && correctLook && CurrentOverlap >= overlapThreshold;
            stableElapsed = correct ? stableElapsed + Time.deltaTime : 0f;
            float desiredProgress = validProjection && correctLook
                ? Mathf.Clamp01(CurrentOverlap / Mathf.Max(0.01f, overlapThreshold)) : 0f;
            // Progress represents alignment; the independent uninterrupted hold gates success.
            float nextProgress = progressSmoothTime <= 0f ? desiredProgress :
                Mathf.SmoothDamp(Progress, desiredProgress, ref progressVelocity,
                    progressSmoothTime, Mathf.Infinity, Time.deltaTime);
            int version = stateVersion;
            PublishProgress(nextProgress);
            // Inspector listeners may synchronously reset or disable the puzzle.
            if (version != stateVersion || !isActiveAndEnabled)
                return;
            if (correct && stableElapsed >= stableHoldSeconds)
                Solve();
        }

        private void Solve()
        {
            if (IsSolved)
                return;
            IsSolved = true;
            progressVelocity = 0f;
            int version = stateVersion;
            // Start the visual before callbacks, so listener-triggered reset/disable can clean it up.
            successRoutine = StartCoroutine(BrightenTargets());
            OnPuzzleSolved?.Invoke();
            if (version == stateVersion && isActiveAndEnabled && IsSolved)
                PublishProgress(1f, true);
        }

        private IEnumerator BrightenTargets()
        {
            float elapsed = 0f;
            while (elapsed < successFadeSeconds)
            {
                float fraction = elapsed / successFadeSeconds;
                float brightness = successFadeCurve == null
                    ? Mathf.SmoothStep(0f, 1f, fraction)
                    : Mathf.Clamp01(successFadeCurve.Evaluate(fraction));
                SetTargetBrightness(brightness);
                elapsed += Time.deltaTime;
                yield return null;
            }
            SetTargetBrightness(1f);
            successRoutine = null;
        }

        private void SetTargetBrightness(float brightness)
        {
            for (int i = 0; i < targets.Length; i++)
                if (targets[i])
                    targets[i].SetSuccessBrightness(brightness);
        }

        private void PublishProgress(float value, bool force = false)
        {
            value = Mathf.Clamp01(value);
            bool changed = Mathf.Abs(Progress - value) > 0.0001f;
            Progress = value;
            if (validatedSlider)
                validatedSlider.SetValueWithoutNotify(value);
            // A progress listener may call ResetPuzzle (including when value is already zero).
            // Update state/slider, but never recursively publish another progress notification.
            if ((changed || force) && !publishingProgress)
            {
                publishingProgress = true;
                try
                {
                    onProgressChanged?.Invoke(value);
                }
                finally
                {
                    publishingProgress = false;
                }
            }
        }

        /// <summary>
        /// Unlatch immediately and queue every target's original world pose/scale/velocities.
        /// Selected targets are NOT teleported: release all hands to complete their reset.
        /// Disabled targets restore on re-enable. Pending targets cannot solve the puzzle.
        /// Observation occupancy remains physical; step out if immediate retry is unwanted.
        /// </summary>
        public void ResetPuzzle()
        {
            stateVersion++;
            StopSuccessVisuals();
            IsSolved = false;
            CurrentOverlap = 0f;
            stableElapsed = 0f;
            progressVelocity = 0f;
            if (targets != null)
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i])
                        targets[i].ResetTarget();
            PublishProgress(0f, true);
        }

        private void StopSuccessVisuals()
        {
            if (successRoutine != null)
            {
                StopCoroutine(successRoutine);
                successRoutine = null;
            }
            if (targets != null)
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i])
                        targets[i].RestoreVisuals();
        }

        private void OnDisable()
        {
            stateVersion++;
            StopSuccessVisuals();
            stableElapsed = 0f;
            progressVelocity = 0f;
            CurrentOverlap = 0f;
            // Keep the solved latch across disable/enable. Only ResetPuzzle may clear it.
            PublishProgress(IsSolved ? 1f : 0f);
        }

        private void OnValidate()
        {
            overlapThreshold = Mathf.Clamp(overlapThreshold, 0.01f, 1f);
            lookToleranceDegrees = Mathf.Clamp(lookToleranceDegrees, 0f, 180f);
            stableHoldSeconds = Mathf.Max(0f, stableHoldSeconds);
            minimumViewportSize = Mathf.Clamp(minimumViewportSize, 0.00001f, 0.1f);
            progressSmoothTime = Mathf.Max(0f, progressSmoothTime);
            successFadeSeconds = Mathf.Max(0f, successFadeSeconds);
        }
    }
}
