using TMPro;
using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab
{
    public sealed class DepthLabExitDoor : MonoBehaviour
    {
        public LabChapter chapter;
        public Transform leftPanel, rightPanel;
        public TMP_Text statusLabel, conditionLabel;
        public float OpenAmount { get; private set; }
        public bool IsOpen => OpenAmount >= .95f;

        private const float JambHalfWidth = .83f;
        private const float JambHeight = 2.76f;
        // Just beyond the leaves, with room for the capsule before the rear wall.
        private const float ExitPlane = .15f;
        private const float MaxSampleDistance = .75f;
        private Vector3 previousHead;
        private bool hasSample, approaching, crossed;
        private int labelState = -1;

        private void OnEnable() { CloseImmediate(); Loc.LanguageChanged += ForceLabelRefresh; }
        private void OnDisable() { CloseImmediate(); Loc.LanguageChanged -= ForceLabelRefresh; }
        // The label cache only tracks progress/state, so drop it when the language changes.
        private void ForceLabelRefresh() => labelState = -1;

        public void CloseImmediate()
        {
            OpenAmount = 0;
            labelState = -1;
            crossed = false;
            ResetCrossing();
            PositionPanels();
            RefreshLabels();
        }

        public void ResetCrossing()
        {
            hasSample = false;
            approaching = false;
        }

        private void Update()
        {
            if (chapter != null && chapter.InteractionAllowed)
            {
                OpenAmount = Mathf.MoveTowards(OpenAmount, chapter.ExitReady ? 1f : 0f, Time.deltaTime * .8f);
                PositionPanels();
            }
            RefreshLabels();
        }

        private void LateUpdate()
        {
            DepthLabApp app = DepthLabApp.Instance;
            if (crossed || chapter == null || !chapter.InteractionAllowed || !chapter.ExitReady ||
                !IsOpen || app == null || app.headCamera == null)
            {
                ResetCrossing();
                return;
            }

            Vector3 head = app.Head.position;
            Vector3 local = transform.InverseTransformPoint(head);
            Vector3 previousLocal = transform.InverseTransformPoint(previousHead);
            bool continuous = hasSample && (head - previousHead).sqrMagnitude <= MaxSampleDistance * MaxSampleDistance;
            previousHead = head;
            hasSample = true;
            if (!continuous) approaching = false;
            if (Mathf.Abs(local.x) > JambHalfWidth || local.y <= 0f || local.y >= JambHeight)
            {
                approaching = false;
                return;
            }

            if (local.z <= -.1f) approaching = true;
            if (!continuous || !approaching || previousLocal.z >= ExitPlane || local.z < ExitPlane) return;

            crossed = true;
            chapter.WalkThroughExit();
        }

        private void RefreshLabels()
        {
            bool solved = chapter != null && chapter.ExitReady;
            bool paused = solved && !chapter.InteractionAllowed;
            int progress = chapter != null ? chapter.Progress : 0;
            int state = progress * 8 + (solved ? 1 : 0) + (paused ? 2 : 0) + (IsOpen ? 4 : 0);
            if (labelState == state) return;
            labelState = state;
            if (statusLabel != null)
                statusLabel.text = Loc.T(!solved ? "出口未解锁" : paused ? "已暂停" : IsOpen ? "出口已开启" : "正在开门");
            if (conditionLabel != null)
                conditionLabel.text = !solved
                    ? Loc.F("实验进度 {0} / {1}\n完成后门会自动打开", progress, chapter != null ? chapter.Goal : 0)
                    : paused ? Loc.T("继续后穿过出口") : !IsOpen ? Loc.T("请稍候")
                    : chapter.chapterIndex == 3 ? Loc.T("穿过门框 · 查看总结") : Loc.T("穿过门框 · 自动进入下一章");
        }
        private void PositionPanels()
        {
            if (leftPanel != null) leftPanel.localPosition = new Vector3(-.44f - OpenAmount * .9f, 1.35f, .08f);
            if (rightPanel != null) rightPanel.localPosition = new Vector3(.44f + OpenAmount * .9f, 1.35f, .08f);
        }
    }
}
