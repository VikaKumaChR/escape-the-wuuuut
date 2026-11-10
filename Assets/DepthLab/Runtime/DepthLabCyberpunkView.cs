using TMPro;
using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab
{
    [DefaultExecutionOrder(100)]
    public class DepthLabCyberpunkView : MonoBehaviour
    {
        public DepthLabApp app;
        public TMP_Text researcherLabel, progressLabel;
        public UnityEngine.UI.Image experienceFill;
        public TMP_Text[] archiveDetails = new TMP_Text[4];

        private double nextProgressPoll;

        private bool MenuEnabled
        {
            get
            {
                if (app == null || app.menuCanvas == null ||
                    !app.menuCanvas.gameObject.activeInHierarchy) return false;
                return !app.menuCanvas.TryGetComponent<Canvas>(out var menu) || menu.isActiveAndEnabled;
            }
        }

        private void OnEnable()
        {
            nextProgressPoll = 0;
            if (!MenuEnabled) return;
            RefreshProgress();
            RefreshPreviewLayout();
        }

        private void LateUpdate()
        {
            if (!MenuEnabled) return;
            // ShowPage restores the old layout, so correct the same transforms every late frame.
            RefreshPreviewLayout();
            if (Time.unscaledTimeAsDouble >= nextProgressPoll) RefreshProgress();
        }

        private void RefreshProgress()
        {
            nextProgressPoll = Time.unscaledTimeAsDouble + 0.5;
            int completed = 0;
            int stars = 0;
            for (int i = 0; i < 4; i++)
            {
                ChapterRecord record = DepthLabProgress.Read(i);
                if (record.completed)
                {
                    completed++;
                    stars += record.stars;
                }

                if (archiveDetails == null || i >= archiveDetails.Length || archiveDetails[i] == null)
                    continue;

                string details = record.completed
                    ? Loc.F("用时 {0:0.##} 秒  /  错误 {1}  /  提示 {2}\n重试 {3}  /  尝试 {4}", record.seconds, record.mistakes, record.hints, record.retries, record.attempts)
                    : Loc.F("未完成  /  尝试 {0}", record.attempts);
                SetTextIfChanged(archiveDetails[i], details);
            }

            SetTextIfChanged(researcherLabel, Loc.F("研究员  /  等级 {0}  /  经验 {1}", completed + 1, completed * 100));
            SetTextIfChanged(progressLabel, Loc.F("已完成 {0}/4  /  星星 {1}/12", completed, stars));
            float fill = completed / 4f;
            if (experienceFill != null && experienceFill.fillAmount != fill)
                experienceFill.fillAmount = fill;
        }

        private static void SetTextIfChanged(TMP_Text label, string value)
        {
            if (label != null && label.text != value) label.text = value;
        }

        // Callable during authoring; coordinates assume a 2260 x 1330, centre-pivot menu canvas.
        public void RefreshPreviewLayout()
        {
            if (app == null || app.menuCanvas == null || app.previews == null) return;
            bool detail = app.detailPage != null && app.detailPage.activeSelf;
            bool chapters = !detail && app.chapterPage != null && app.chapterPage.activeSelf;
            bool home = !detail && !chapters && app.homePage != null && app.homePage.activeSelf;

            for (int i = 0; i < app.previews.Length; i++)
            {
                Transform preview = app.previews[i];
                if (preview == null) continue;
                if (!app.showChapterPreviews)
                {
                    if (preview.gameObject.activeSelf) preview.gameObject.SetActive(false);
                    continue;
                }
                if (preview.parent != app.menuCanvas) preview.SetParent(app.menuCanvas, false);

                // Only the app knows which detail preview is selected. Never enable one here.
                if (!detail && preview.gameObject.activeSelf != (home || chapters))
                    preview.gameObject.SetActive(home || chapters);

                if (!detail && !chapters && !home) continue;
                float x = 1730;
                float y = 465 + i * 160;
                float scale = 1.35f;
                if (chapters)
                {
                    x = 930 + i % 2 * 1090;
                    y = 410 + i / 2 * 340;
                    scale = 2.2f;
                }
                else if (detail)
                {
                    x = 1840;
                    y = 620;
                    scale = 4;
                }

                Vector3 position = new Vector3(x - 1130, 665 - y, -85);
                Vector3 localScale = Vector3.one * scale;
                if (preview.localPosition != position) preview.localPosition = position;
                if (preview.localScale != localScale) preview.localScale = localScale;
            }
        }
    }
}
