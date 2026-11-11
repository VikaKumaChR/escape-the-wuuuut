using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;
using DistortedSpace.Localization;

namespace DepthLab
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class DepthLabHudPresentation : MonoBehaviour
    {
        public DepthLabApp app;
        public RectTransform panel;
        public TMP_Text stepLabel;
        public TMP_Text controlsLabel;
        public TMP_Text experimentLabel;
        public TMP_Text objectiveLabel;
        public TMP_Text timerLabel;
        public TMP_Text mistakesLabel;
        public TMP_Text progressLabel;
        public TMP_Text feedbackLabel;
        public UnityEngine.UI.Image hintBacking;
        public UnityEngine.UI.Image[] feedbackBorders;
        public RectTransform progressFill;
        public bool compactGuidance = true;
        public GameObject[] detailedElements;
        [Min(.5f)] public float viewingDistance = 1.85f;
        [Min(.5f)] public float repositionDistance = 1.25f;
        [Min(.0001f)] public float worldScale = .001f;
        public Color correctColor = new Color32(0, 229, 255, 255);
        public Color errorColor = new Color32(255, 53, 184, 255);
        public Color hintColor = new Color32(168, 85, 247, 255);
        public Color idleBorderColor = new Color32(22, 119, 255, 90);

        private Canvas canvas;
        private UnityEngine.UI.CanvasScaler scaler;
        private UnityEngine.UI.GraphicRaycaster mouseRaycaster;
        private TrackedDeviceGraphicRaycaster trackedRaycaster;
        private Transform head;
        private bool configured;
        private bool lastXR;
        private bool refreshLabels;
        private bool parked;
        private int chapter = -2;
        private int progress = -1;
        private int goal = -1;
        private int second = -1;
        private int mistakes = -1;
        private int hints = -1;
        private bool exitReady;
        private string objective;
        private Vector3 parkedPosition;
        private Quaternion parkedRotation;
        private Vector3 placementHead;
        private Vector3 lastHeadPosition;
        private float farSince = -1f;
        private float feedbackStarted = -1f;
        private float hintUntil = -1f;
        private Color feedbackColor;
        private const float FeedbackDuration = 1.8f;

        private void OnEnable()
        {
            Cache();
            configured = false;
            refreshLabels = true;
            feedbackStarted = -1f;
            hintUntil = -1f;
            ApplyGuidanceLayout();
            SetFeedback(0f);
            Refresh();
            Loc.LanguageChanged += MarkLabelsDirty;
        }

        private void OnDisable() => Loc.LanguageChanged -= MarkLabelsDirty;

        // Rebuild every label in the new language on the next refresh.
        private void MarkLabelsDirty() => refreshLabels = true;

        private void Cache()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (scaler == null) scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
            if (mouseRaycaster == null) mouseRaycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (trackedRaycaster == null) trackedRaycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            if (app == null) app = GetComponentInParent<DepthLabApp>();
            if (app == null) app = DepthLabApp.Instance;
            if (app != null && app.headCamera != null) head = app.headCamera.transform;
        }

        private void LateUpdate() => Refresh();

        public void ApplyGuidanceLayout()
        {
            if (detailedElements != null)
                foreach (GameObject element in detailedElements)
                    if (element != null) element.SetActive(!compactGuidance);
            if (feedbackBorders != null)
                foreach (var border in feedbackBorders)
                    if (border != null) border.gameObject.SetActive(!compactGuidance);
            if (controlsLabel != null) controlsLabel.gameObject.SetActive(!compactGuidance);
            if (stepLabel != null)
            {
                stepLabel.rectTransform.anchoredPosition = new Vector2(24, compactGuidance ? -570 : -510);
                stepLabel.rectTransform.sizeDelta = new Vector2(982, compactGuidance ? 68 : 38);
                stepLabel.fontSize = compactGuidance ? 28 : 26;
            }
        }

        private void Refresh()
        {
            if (app == null || canvas == null || panel == null || head == null ||
                app.playerRoot == null || !app.IsPlaying || app.IsPaused)
                return;

            bool xr = DepthLabInput.IsXRActive;
            bool modeChanged = !configured || lastXR != xr;
            if (modeChanged)
            {
                lastXR = xr;
                ConfigureCanvas(xr);
                configured = true;
            }

            bool chapterChanged = chapter != app.ActiveChapter;
            UpdatePlacement(chapterChanged);

            int currentProgress = 0;
            int currentGoal = 0;
            DepthLabChapter current = null;
            if (app.chapters != null && app.ActiveChapter >= 0 && app.ActiveChapter < app.chapters.Length)
            {
                current = app.chapters[app.ActiveChapter];
                if (current != null)
                {
                    currentProgress = current.Progress;
                    currentGoal = current.Goal;
                }
            }

            bool currentExitReady = current is LabChapter lab ? lab.ExitReady :
                currentGoal > 0 && currentProgress >= currentGoal;
            string currentObjective = current == null ? Loc.T("观察目标，开始实验") :
                currentExitReady ? Loc.T("实验完成，沿地面导向穿过出口") :
                app.ActiveChapter == 0 ? Loc.T("把三个实体放进同色同形线框") : current.Objective;
            bool force = refreshLabels || chapterChanged;
            if (force && experimentLabel != null)
            {
                string title = app.ActiveChapter >= 0 && app.ActiveChapter < DepthLabProgress.Titles.Length
                    ? Loc.T(DepthLabProgress.Titles[app.ActiveChapter]) : Loc.T("科学实验");
                experimentLabel.text = Loc.F("实验 {0:00}  ·  {1}", app.ActiveChapter + 1, title);
            }
            if (force || objective != currentObjective)
            {
                if (objectiveLabel != null) objectiveLabel.text = currentObjective;
                if (compactGuidance && stepLabel != null) stepLabel.text = currentObjective;
                objective = currentObjective;
            }
            if (force || progress != currentProgress || goal != currentGoal || exitReady != currentExitReady)
            {
                if (progressLabel != null) progressLabel.text = Loc.F("进度  {0}/{1}", currentProgress, currentGoal);
                if (stepLabel != null && !compactGuidance)
                    stepLabel.text = currentExitReady
                        ? Loc.T("目标已完成 · 步行前往出口")
                        : app.ActiveChapter == 0 ? Loc.T("抓取实体 → 对齐线框 → 松开归位") : Loc.T("观察线索 · 按目标完成实验");
                if (progressFill != null)
                    progressFill.anchorMax = new Vector2(currentGoal > 0
                        ? Mathf.Clamp01((float)currentProgress / currentGoal) : 0f, 1f);
            }
            int currentSecond = (int)app.ElapsedSeconds;
            if (force || second != currentSecond)
            {
                if (timerLabel != null) timerLabel.text = Loc.F("用时  {0}", DepthLabProgress.TimeLabel(app.ElapsedSeconds));
                second = currentSecond;
            }
            if (force || mistakes != app.Mistakes)
            {
                if (mistakesLabel != null) mistakesLabel.text = Loc.F("错误  {0}", app.Mistakes);
            }
            if (!force)
            {
                if (app.Mistakes > mistakes) BeginFeedback(errorColor, Loc.T("操作有误 · 调整后重试"));
                else if (app.Hints > hints) BeginFeedback(hintColor, Loc.T("重要提示 · 请看下方"));
                else if (currentProgress > progress) BeginFeedback(correctColor, Loc.T("正确 · 进度已更新"));
            }
            if (controlsLabel != null && (force || modeChanged))
                controlsLabel.text = xr
                    ? Loc.T("握持抓取 · 松开释放 · 射线点选 · 摇杆移动")
                    : Loc.T("左键按住抓取 / 松开释放 · 滚轮远近\n右键观察 · WASD 移动 · Esc 暂停");
            // The app still owns both legacy text values; only their presentation changes.
            if (force && app.hudText != null && experimentLabel != null) app.hudText.enabled = false;
            if (!force && app.Hints > hints) hintUntil = Time.unscaledTime + 12f;
            if (!force && currentProgress > progress) hintUntil = -1f;
            bool showHint = Time.unscaledTime < hintUntil;
            if (app.hintText != null && objectiveLabel != null) app.hintText.enabled = showHint;
            if (hintBacking != null) hintBacking.enabled = showHint;
            chapter = app.ActiveChapter;
            progress = currentProgress;
            goal = currentGoal;
            mistakes = app.Mistakes;
            hints = app.Hints;
            exitReady = currentExitReady;
            refreshLabels = false;
            AnimateFeedback();
        }

        private void ConfigureCanvas(bool xr)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            canvas.sortingOrder = 5;
            if (mouseRaycaster != null) mouseRaycaster.enabled = !xr;
            if (trackedRaycaster != null) trackedRaycaster.enabled = true;
            if (scaler != null)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.dynamicPixelsPerUnit = 1f;
            }

            RectTransform root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = panel.sizeDelta;
            root.localScale = Vector3.one * worldScale;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition3D = Vector3.zero;
            panel.localScale = Vector3.one;
            panel.localRotation = Quaternion.identity;
        }

        private void UpdatePlacement(bool chapterChanged)
        {
            Vector3 headPosition = head.position;
            // Snap turning can move the rig origin around a stationary roomscale head.
            bool teleported = parked && (headPosition - lastHeadPosition).sqrMagnitude > .75f * .75f;
            // Repair App.PlaceHud's overhead write once, rather than continually tracking the head.
            bool appMovedHud = parked && ((transform.position - parkedPosition).sqrMagnitude > .0001f ||
                Quaternion.Angle(transform.rotation, parkedRotation) > .1f);
            Vector3 displacement = head.position - placementHead;
            displacement.y = 0f;
            bool far = displacement.sqrMagnitude > repositionDistance * repositionDistance ||
                Mathf.Abs(head.position.y - placementHead.y) > .65f;
            if (!parked || chapterChanged || appMovedHud || teleported)
                ParkConsole();
            else if (far)
            {
                if (farSince < 0f) farSince = Time.unscaledTime;
                if (Time.unscaledTime - farSince >= .65f) ParkConsole();
            }
            else farSince = -1f;
            lastHeadPosition = headPosition;
        }

        private void ParkConsole()
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            placementHead = head.position;
            parkedPosition = placementHead + forward * viewingDistance;
            // Keep the open center below the eye line, also for seated XR; never at ceiling height.
            parkedPosition.y = Mathf.Min(placementHead.y - .16f, app.playerRoot.position.y + 1.6f);
            parkedRotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.SetPositionAndRotation(parkedPosition, parkedRotation);
            parked = true;
            farSince = -1f;
        }

        private void BeginFeedback(Color color, string message)
        {
            feedbackColor = color;
            feedbackStarted = Time.unscaledTime;
            if (feedbackLabel != null) feedbackLabel.text = message;
        }

        private void AnimateFeedback()
        {
            if (feedbackStarted < 0f) return;
            float elapsed = Time.unscaledTime - feedbackStarted;
            float opacity = elapsed < .18f ? elapsed / .18f :
                1f - Mathf.SmoothStep(0f, 1f, (elapsed - .18f) / (FeedbackDuration - .18f));
            SetFeedback(opacity);
            if (elapsed >= FeedbackDuration) feedbackStarted = -1f;
        }

        private void SetFeedback(float opacity)
        {
            if (feedbackLabel != null)
            {
                Color color = feedbackColor;
                color.a = opacity;
                feedbackLabel.color = color;
            }
            if (feedbackBorders == null) return;
            Color border = Color.Lerp(idleBorderColor, feedbackColor, opacity * .65f);
            foreach (UnityEngine.UI.Image image in feedbackBorders)
                if (image != null) image.color = border;
        }
    }
}
