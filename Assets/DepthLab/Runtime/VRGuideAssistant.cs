using TMPro;
using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab
{
    public sealed class VRGuideAssistant : MonoBehaviour
    {
        public const string WelcomeText = "我是诺娃！抓起彩色实体，放进同色、同形的线框。\n从侧面看前后距离，对准后松手。三个都放好，出口自动打开。";

        public DepthLabApp app;
        public LabChapter chapter;
        public DepthLabNova nova;
        public Canvas speechCanvas;
        public TMP_Text speechText;
        public UnityEngine.UI.Button hintButton, talkButton;
        private string Greeting => chapter == null || chapter.chapterIndex == 0 ? Loc.T(WelcomeText) :
            chapter.chapterIndex == 1
                ? Loc.T("这次寻找藏在树后的路。先向左、再向右观察，让平台显形；然后按青色编号依次前进。") :
            chapter.chapterIndex == 2
                ? Loc.T("看前方的两个物体。先比较方块大小，再比较线段长度；在操作台选择答案。不要被背景骗了！")
                : Loc.T("先点光源「+25°」，再转动镜面，让光照到橙色接收球。对准后保持片刻，球就会亮起。");

        private const float IdleSeconds = 35f;
        private bool validationAttempted, configurationValid, initialized;
        private bool observedSolved, idleReminderSent;
        private int observedProgress, observedMistakes;
        private float idleSeconds;
        private string displayedSpeech;
        private uint displayedVersion;
        private float pageSeconds, readingSeconds;

        private void OnValidate()
        {
            if (!Application.isPlaying && speechText != null)
                speechText.text = Greeting;
        }

        private void OnEnable()
        {
            // StartChapter activates the hierarchy before IsPlaying and Begin are set.
            initialized = false;
            observedProgress = observedMistakes = 0;
            observedSolved = false;
            RearmIdle();
            displayedSpeech = null;
            pageSeconds = readingSeconds = 0f;
            SetButtons(false);
            if (speechText != null) speechText.text = Greeting;
        }

        private void OnDisable()
        {
            SetButtons(false);
            if (nova != null && nova.externallyDriven) nova.SetExternalPaused(true);
            if (speechCanvas != null) speechCanvas.enabled = false;
        }

        private bool IsCurrentChapter => app.IsPlaying && app.ActiveChapter >= 0 &&
            app.chapters != null && app.ActiveChapter < app.chapters.Length &&
            ReferenceEquals(app.chapters[app.ActiveChapter], chapter) &&
            chapter.isActiveAndEnabled;

        private bool CanAct => isActiveAndEnabled && configurationValid && initialized &&
            IsCurrentChapter && !app.IsPaused && chapter.InteractionAllowed && !chapter.ExitReady;

        private void LateUpdate()
        {
            if (!ValidateConfiguration()) return;
            bool current = IsCurrentChapter;
            bool active = current && !app.IsPaused && chapter.InteractionAllowed;
            SetButtons(active && initialized && !chapter.ExitReady);
            speechCanvas.enabled = current && !app.IsPaused;
            nova.SetExternalPaused(!active);
            if (!active) return;

            if (!initialized)
            {
                initialized = true;
                nova.ResetExternalPresentation();
                nova.SetExternalPaused(false);
                nova.PresentExternal(Greeting, DepthLabNova.Emotion.Curious, true);
                SetButtons(!chapter.ExitReady);
            }

            float delta = Time.unscaledDeltaTime;
            FacePlayer(delta);
            nova.TickExternal(delta);
            ObserveState();
            UpdateSpeechPages(delta);

            if (!chapter.ExitReady)
            {
                idleSeconds += delta;
                if (!nova.ExternalPresentationActive && readingSeconds <= 0f &&
                    !idleReminderSent && idleSeconds >= IdleSeconds)
                {
                    idleReminderSent = true;
                    nova.PresentExternal(chapter.Objective,
                        DepthLabNova.Emotion.Thinking);
                }
            }
            SetButtons(CanAct);
        }

        private void ObserveState()
        {
            int progress = chapter.Progress;
            int mistakes = app.Mistakes;
            bool solved = chapter.ExitReady;
            bool progressChanged = progress != observedProgress;
            bool mistakesChanged = mistakes != observedMistakes;
            bool newlySolved = solved && !observedSolved;

            if (progressChanged || mistakesChanged || solved != observedSolved) RearmIdle();
            if (newlySolved)
            {
                string next = chapter.chapterIndex == 0 ? Loc.T("下一关，我们一起找藏在树后的路。") :
                    chapter.chapterIndex == 1 ? Loc.T("下一关，看看背景怎样骗过眼睛。") :
                    chapter.chapterIndex == 2 ? Loc.T("下一关，用镜面把光送到接收球。") : Loc.T("四项实验完成，研究报告已准备好。");
                nova.PresentExternal(Loc.F("做得很好！走过亮起的出口。{0}", next),
                    DepthLabNova.Emotion.Excited);
            }
            else if (!solved)
            {
                if (progressChanged)
                    nova.PresentExternal(chapter.chapterIndex == 1
                            ? Loc.F("到达 {0:00} 号平台！继续选择下一个亮起的编号。", progress)
                            : chapter.chapterIndex == 0 ? Loc.F("放好了！已匹配 {0}/{1}。继续观察剩余实体与线框的位置。", progress, chapter.Goal)
                            : chapter.chapterIndex == 2 ? Loc.T("答对了！点击继续，完成下一项比较。")
                            : Loc.T("光源调好了！现在转动镜面，让反射光照向接收球。"),
                        progress > observedProgress ? DepthLabNova.Emotion.Happy : DepthLabNova.Emotion.Curious);
                else if (mistakes > observedMistakes)
                    nova.PresentExternal(chapter.chapterIndex == 1
                            ? Loc.T("这块平台还不安全。找青色编号，按顺序前进。")
                            : chapter.chapterIndex == 0 ? Loc.T("没关系！掉下的物体可以重新抓取。先看形状，再从侧面观察前后距离。")
                            : Loc.T("别急，只比较目标本身。可以点提示，让我陪你找线索。"),
                        DepthLabNova.Emotion.Encouraging);
            }

            observedProgress = progress;
            observedMistakes = mistakes;
            observedSolved = solved;
        }

        public void Hint()
        {
            if (!CanAct) return;
            ObserveState();
            RearmIdle();
            app.Execute(LabAction.Hint);
            nova.PresentExternal(chapter.Hint, DepthLabNova.Emotion.Thinking);
        }

        public void Talk()
        {
            if (!CanAct) return;
            ObserveState();
            RearmIdle();
            nova.PresentExternal(chapter.Objective, DepthLabNova.Emotion.Curious, true);
        }

        private void UpdateSpeechPages(float delta)
        {
            if (displayedSpeech != speechText.text || displayedVersion != nova.ExternalPresentationVersion)
            {
                displayedSpeech = speechText.text;
                displayedVersion = nova.ExternalPresentationVersion;
                speechText.pageToDisplay = 1;
                speechText.ForceMeshUpdate();
                pageSeconds = 0f;
                readingSeconds = Mathf.Max(1, speechText.textInfo.pageCount) * 9f;
            }
            readingSeconds = Mathf.Max(0f, readingSeconds - delta);
            pageSeconds += delta;
            if (pageSeconds >= 9f && speechText.pageToDisplay < speechText.textInfo.pageCount)
            {
                speechText.pageToDisplay++;
                pageSeconds = 0f;
            }
        }

        private void RearmIdle()
        {
            idleSeconds = 0f;
            idleReminderSent = false;
        }

        private void FacePlayer(float delta)
        {
            Vector3 direction = app.Head.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) return;
            float yaw = Quaternion.LookRotation(-direction, Vector3.up).eulerAngles.y;
            float smoothYaw = Mathf.LerpAngle(transform.eulerAngles.y, yaw, 1f - Mathf.Exp(-4f * delta));
            transform.rotation = Quaternion.Euler(0f, smoothYaw, 0f);
        }

        private void SetButtons(bool available)
        {
            if (hintButton != null) hintButton.interactable = available;
            if (talkButton != null) talkButton.interactable = available;
        }

        private bool ValidateConfiguration()
        {
            if (validationAttempted)
            {
                if (!configurationValid) enabled = false;
                return configurationValid;
            }
            validationAttempted = true;
            string error = null;
            if (app == null || chapter == null || nova == null || speechCanvas == null ||
                speechText == null || hintButton == null || talkButton == null)
                error = "Assign app, chapter, nova, speechCanvas, speechText, hintButton and talkButton.";
            else if (app.chapters == null || chapter.chapterIndex < 0 || chapter.chapterIndex >= app.chapters.Length ||
                !ReferenceEquals(app.chapters[chapter.chapterIndex], chapter))
                error = "chapter must match its index in app.chapters.";
            else if (app.headCamera == null)
                error = "app.headCamera is required for yaw-only facing.";
            else if (transform == chapter.transform || !transform.IsChildOf(chapter.transform) || nova.transform == transform ||
                !nova.transform.IsChildOf(transform))
                error = "Place the guide root under CH1 and Nova on a separate model child of the guide.";
            else if (!nova.externallyDriven || !ReferenceEquals(nova.app, app) || !nova.isActiveAndEnabled)
                error = "Nova must be enabled, externallyDriven, and reference the same app.";
            else if (nova.dialogueCanvas != speechCanvas || nova.subtitle != speechText || nova.subtitlePanel == null)
                error = "Assign Nova.dialogueCanvas/subtitle/subtitlePanel to the new speech UI.";
            else if (!nova.subtitlePanel.transform.IsChildOf(speechCanvas.transform) ||
                !speechText.transform.IsChildOf(nova.subtitlePanel.transform))
                error = "Nova.subtitlePanel must belong to speechCanvas and contain speechText.";
            else if (hintButton == talkButton || !speechText.transform.IsChildOf(speechCanvas.transform) ||
                !hintButton.transform.IsChildOf(speechCanvas.transform) ||
                !talkButton.transform.IsChildOf(speechCanvas.transform))
                error = "Use distinct hint/talk buttons and speech text inside speechCanvas.";
            else if (speechCanvas.renderMode != RenderMode.WorldSpace ||
                speechCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                error = "speechCanvas must be WorldSpace with a GraphicRaycaster (XR subclass supported).";
            else if (!nova.ValidateExternalConfiguration(out string novaError))
                error = novaError;

            if (error == null)
            {
                configurationValid = true;
                speechCanvas.worldCamera = app.headCamera;
                return true;
            }

            Debug.LogError("VRGuideAssistant configuration error: " + error, this);
            enabled = false;
            return false;
        }
    }
}
