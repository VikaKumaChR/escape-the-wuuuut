using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab
{
    [DefaultExecutionOrder(-100)]
    public sealed class DepthLabApp : MonoBehaviour
    {
        public static DepthLabApp Instance { get; private set; }
        public Camera headCamera;
        public Transform playerRoot;
        public GameObject menuSpace;
        public Transform menuCanvas;
        public GameObject homePage, chapterPage, detailPage, settingsPage, creditsPage;
        public GameObject pausePage, resultPage, finalPage, modalPage, tutorialPage;
        public GameObject hud, tutorialProps;
        public DepthLabChapter[] chapters;
        public DepthLabSettings settings;
        public DepthLabSettingsView settingsView;
        public DepthLabInput input;
        public DepthLabTutorial tutorial;
        public XRUIInputModule uiModule;
        public TMP_Text tooltipText, detailTitle, detailBody, detailStatus, hudText, hintText;
        public TMP_Text resultTitle, resultBody, modalText, tutorialText, finalTotals;
        public TMP_Text[] chapterStatuses, homeStatuses, finalCards;
        public UnityEngine.UI.Button playSelectedButton, tutorialNextButton, nextChapterButton;
        public AudioSource effectsAudio, musicAudio;
        public AudioClip hoverSound, confirmSound;
        public Transform[] previews;
        public bool showChapterPreviews = true;
        public DepthLabHolographicGraphic detailIllustration;

        public Transform Head => headCamera.transform;
        public Transform PlayerRoot => playerRoot;
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsTutorial { get; private set; }
        public int ActiveChapter { get; private set; } = -1;
        public float ElapsedSeconds { get; private set; }
        public int Mistakes { get; private set; }
        public int Hints { get; private set; }
        public int Retries { get; private set; }
        public int PredictedStars => ActiveChapter < 0 ? 3 : DepthLabProgress.CalculateStars(ElapsedSeconds, Mistakes, Hints, Retries,
            chapters[ActiveChapter].threeStarSeconds, chapters[ActiveChapter].twoStarSeconds,
            chapters[ActiveChapter].threeStarMistakes, chapters[ActiveChapter].twoStarMistakes);

        private int selectedChapter;
        private bool settingsFromPause;
        private int displayedSecond = -1, displayedProgress = -1;
        private LabAction modalAction;
        private GameObject modalReturn;
        private GameObject visiblePage;
        private bool lastXR;

        private void Awake()
        {
            Instance = this;
            foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
                canvas.worldCamera = headCamera;
            settings.Load();
            ApplyAudio();
            tutorial.Changed += RefreshTutorial;
            tutorial.Finished += TutorialFinished;
        }
        private void Start() { ShowMenu(); }
        private void OnDestroy()
        {
            tutorial.Changed -= RefreshTutorial;
            tutorial.Finished -= TutorialFinished;
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            bool xr = DepthLabInput.IsXRActive;
            if (uiModule != null && (xr != lastXR || uiModule.enableMouseInput != !xr))
            {
                uiModule.enableXRInput = true;
                uiModule.enableMouseInput = !xr;
                lastXR = xr;
            }
            input.MovementEnabled = (IsPlaying || IsTutorial) && !IsPaused;
            if (!IsPlaying || IsPaused) return;
            ElapsedSeconds += Time.unscaledDeltaTime;
            int progress = chapters[ActiveChapter].Progress;
            if (displayedSecond != (int)ElapsedSeconds || displayedProgress != progress)
            {
                displayedSecond = (int)ElapsedSeconds;
                displayedProgress = progress;
                RefreshHud();
            }
        }

        public void Execute(LabAction action, int argument = 0)
        {
            switch (action)
            {
                case LabAction.Start:
                    StartChapter(0);
                    break;
                case LabAction.Continue:
                    int next = 0;
                    while (next < 3 && DepthLabProgress.Read(next).completed) next++;
                    StartChapter(next);
                    break;
                case LabAction.Chapters: ShowChapterSelect(); break;
                case LabAction.Detail: ShowDetail(argument); break;
                case LabAction.PlaySelected: StartChapter(selectedChapter); break;
                case LabAction.Menu: ShowMenu(); break;
                case LabAction.Tutorial: StartTutorial(); break;
                case LabAction.TutorialNext: if (IsTutorial) tutorial.Next(); break;
                case LabAction.TutorialSkip: tutorial.Skip(); break;
                case LabAction.Settings: ShowSettings(); break;
                case LabAction.SettingsBack:
                    if (settingsFromPause) ShowPage(pausePage); else ShowMenu();
                    break;
                case LabAction.Credits: ShowPage(creditsPage); break;
                case LabAction.Exit: ShowModal(LabAction.ConfirmExit, "离开实验室？\n已完成的章节成绩已保存。"); break;
                case LabAction.ConfirmExit:
                    if (modalAction != LabAction.ConfirmExit || visiblePage != modalPage) break;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
                case LabAction.CancelModal: ShowPage(modalReturn); break;
                case LabAction.Pause: TogglePause(); break;
                case LabAction.Resume: Resume(); break;
                case LabAction.Hint: ShowHint(); break;
                case LabAction.Replay:
                    if (IsTutorial) StartTutorial();
                    else if (ActiveChapter >= 0) StartChapter(ActiveChapter, true);
                    break;
                case LabAction.PlayAgain: StartChapter(0); break;
                case LabAction.NextChapter:
                    if (ActiveChapter == 3) ShowFinal(); else StartChapter(ActiveChapter + 1);
                    break;
                case LabAction.ResetProgress:
                    ShowModal(LabAction.ConfirmReset, "重置所有章节进度？\n仅清除深度实验室的成绩与解锁记录。\n音频与舒适度设置将保留。"); break;
                case LabAction.ConfirmReset:
                    if (modalAction != LabAction.ConfirmReset || visiblePage != modalPage) break;
                    DepthLabProgress.Reset(); ShowPage(settingsPage); RefreshProgress(); break;
                case LabAction.Defaults: settingsView.Defaults(); break;
                case LabAction.Final: ShowFinal(); break;
            }
        }

        public void ShowMenu()
        {
            bool enteringHub = visiblePage == null || IsPlaying || IsTutorial || ActiveChapter >= 0 ||
                !menuSpace.activeSelf;
            LeaveGameplay();
            ActiveChapter = -1;
            menuSpace.SetActive(true);
            if (enteringHub)
            {
                TeleportTo(Vector3.zero);
                menuCanvas.SetPositionAndRotation(new Vector3(0, 1.65f, 2.8f), Quaternion.identity);
            }
            RefreshProgress();
            ShowPage(homePage);
        }
        private void LeaveGameplay()
        {
            input.MovementEnabled = false;
            input.ResetPose();
            foreach (DepthLabChapter chapter in chapters)
            {
                chapter.SetPaused(true);
                chapter.gameObject.SetActive(false);
            }
            IsPlaying = false;
            IsPaused = false;
            IsTutorial = false;
            hud.SetActive(false);
            tutorialProps.SetActive(false);
        }
        public void ShowChapterSelect()
        {
            ShowMenu();
            ShowPage(chapterPage);
        }
        public void ShowDetail(int index)
        {
            if (index < 0 || index >= chapters.Length) return;
            selectedChapter = index;
            detailTitle.text = $"{index + 1:00}  /  {DepthLabProgress.Titles[index]}";
            detailBody.text = $"{DepthLabProgress.Concepts[index]}\n\n{DepthLabProgress.Descriptions[index]}\n\n预计用时    {DepthLabProgress.Times[index]}\n\n{chapters[index].Hint}";
            detailStatus.text = DepthLabProgress.Summary(index);
            if (detailIllustration != null)
            {
                detailIllustration.illustration = (DepthLabHolographicGraphic.Illustration)(index + 1);
                detailIllustration.SetVerticesDirty();
            }
            playSelectedButton.interactable = DepthLabProgress.Unlocked(index);
            ShowPage(detailPage);
        }
        public void StartChapter(int index, bool replay = false)
        {
            if (index < 0 || index >= chapters.Length || !DepthLabProgress.Unlocked(index)) return;
            int retries = replay && ActiveChapter == index ? Retries + 1 : 0;
            LeaveGameplay();
            ActiveChapter = index;
            Retries = retries;
            Mistakes = Hints = 0;
            ElapsedSeconds = 0;
            displayedSecond = displayedProgress = -1;
            menuSpace.SetActive(false);
            ShowPage(null);
            DepthLabChapter chapter = chapters[index];
            chapter.gameObject.SetActive(true);
            TeleportTo(chapter.spawnPoint.position);
            IsPlaying = true;
            chapter.Begin();
            chapter.SetPaused(false);
            DepthLabProgress.BeginAttempt(index);
            PlaceHud(chapter.spawnPoint.position);
            hud.SetActive(true);
            hintText.text = "每次提示增加一次评分惩罚  /  可随时暂停";
            RefreshHud();
        }
        public void TeleportTo(Vector3 position)
        {
            if (ActiveChapter >= 0 && ActiveChapter < chapters.Length &&
                chapters[ActiveChapter] is LabChapter lab && lab.exitDoor != null)
                lab.exitDoor.ResetCrossing();
            if (!DepthLabInput.IsXRActive)
            {
                playerRoot.rotation = Quaternion.identity;
                Head.localRotation = Quaternion.identity;
            }
            Vector3 offset = Head.position - playerRoot.position;
            offset.y = 0;
            CharacterController motor = input.motor;
            bool restoreMotor = motor != null && motor.enabled;
            if (restoreMotor) motor.enabled = false;
            playerRoot.position = position - offset;
            if (restoreMotor) motor.enabled = true;
            Physics.SyncTransforms();
            input.ResetPose();
            if (IsPlaying) PlaceHud(position);
        }
        private void PlaceHud(Vector3 feet)
        {
            hud.transform.SetPositionAndRotation(feet + new Vector3(0, 2.85f, 2.8f), Quaternion.identity);
        }
        public void AddMistake()
        {
            if (!IsPlaying || IsPaused) return;
            Mistakes++;
            RefreshHud();
        }
        public void CompleteChapter()
        {
            if (!TrySaveChapter(out ChapterRecord run)) return;
            resultTitle.text = $"{new string('★', run.stars)}\n第 {ActiveChapter + 1:00} 章已完成";
            resultBody.text = $"{DepthLabProgress.Titles[ActiveChapter]}\n\n用时  {DepthLabProgress.TimeLabel(ElapsedSeconds)}       错误  {Mistakes}\n提示  {Hints}       重试  {Retries}\n\n{(ActiveChapter == 3 ? "已探索全部四个感知实验室" : "下一章已解锁")}\n\n最佳成绩已自动保存。";
            nextChapterButton.GetComponentInChildren<TMP_Text>().text = ActiveChapter == 3 ? "最终总结" : "下一章";
            PlaceModal();
            ShowPage(resultPage);
        }

        public void CompleteChapterFromExit(LabChapter source)
        {
            if (source == null || !source.InteractionAllowed || !source.ExitReady ||
                ActiveChapter < 0 || ActiveChapter >= chapters.Length || chapters[ActiveChapter] != source)
                return;
            int next = ActiveChapter + 1;
            if (!TrySaveChapter(out _)) return;
            if (next < chapters.Length) StartChapter(next);
            else ShowFinal();
        }

        private bool TrySaveChapter(out ChapterRecord run)
        {
            run = null;
            if (!IsPlaying || IsPaused || ActiveChapter < 0 || ActiveChapter >= chapters.Length ||
                chapters[ActiveChapter].Progress < chapters[ActiveChapter].Goal) return false;
            run = new ChapterRecord { seconds = ElapsedSeconds, stars = PredictedStars,
                mistakes = Mistakes, hints = Hints, retries = Retries };
            IsPlaying = false;
            DepthLabProgress.Complete(ActiveChapter, run);
            input.MovementEnabled = false;
            chapters[ActiveChapter].SetPaused(true);
            hud.SetActive(false);
            return true;
        }
        public void TogglePause()
        {
            if (IsPaused) Resume();
            else if (IsPlaying || IsTutorial)
            {
                IsPaused = true;
                input.MovementEnabled = false;
                if (ActiveChapter >= 0) chapters[ActiveChapter].SetPaused(true);
                hud.SetActive(false);
                PlaceModal();
                ShowPage(pausePage);
            }
        }
        private void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            if (IsTutorial) ShowPage(tutorialPage);
            else
            {
                chapters[ActiveChapter].SetPaused(false);
                ShowPage(null);
                hud.SetActive(true);
            }
        }
        private void ShowSettings()
        {
            settingsFromPause = IsPlaying || IsTutorial;
            if (settingsFromPause && !IsPaused) TogglePause();
            settingsView.Refresh();
            ShowPage(settingsPage);
        }
        private void ShowHint()
        {
            if (IsTutorial) { ReportTutorialAction("hint"); return; }
            if (!IsPlaying || IsPaused) return;
            Hints++;
            hintText.text = chapters[ActiveChapter].Hint;
            RefreshHud();
        }
        private void RefreshHud()
        {
            DepthLabChapter c = chapters[ActiveChapter];
            hudText.text = $"实验室 {ActiveChapter + 1:00}   {c.Progress}/{c.Goal}   |   {DepthLabProgress.TimeLabel(ElapsedSeconds)}   |   错误 {Mistakes}   |   {new string('★', PredictedStars)}\n{c.Objective}";
        }
        public void ReportTutorialAction(string action) { if (IsTutorial && !IsPaused) tutorial.Observe(action); }
        private void StartTutorial()
        {
            ShowMenu();
            IsTutorial = true;
            tutorialProps.SetActive(true);
            ShowPage(tutorialPage);
            tutorial.Begin();
        }
        private void RefreshTutorial()
        {
            tutorialText.text = $"操作练习  {tutorial.Step + 1:00} / 07\n\n{tutorial.Instruction}\n\n{(tutorial.Ready ? "已检测到操作  /  可点击下一步" : "请完成操作后继续")}";
            tutorialNextButton.interactable = tutorial.Ready;
        }
        private void TutorialFinished() { ShowMenu(); }
        private void RefreshProgress()
        {
            for (int i = 0; i < 4; i++)
            {
                chapterStatuses[i].text = DepthLabProgress.Summary(i);
                homeStatuses[i].text = DepthLabProgress.Read(i).completed
                    ? $"已完成   {new string('★', DepthLabProgress.Read(i).stars)}"
                    : DepthLabProgress.Unlocked(i) ? "可以探索" : "未解锁";
            }
        }
        private void ShowFinal()
        {
            ShowMenu();
            int stars = 0, completed = 0;
            float seconds = 0;
            for (int i = 0; i < 4; i++)
            {
                ChapterRecord r = DepthLabProgress.Read(i);
                if (r.completed) { completed++; stars += r.stars; seconds += r.seconds; }
                finalCards[i].text = $"{i + 1:00}  {DepthLabProgress.Titles[i]}\n{DepthLabProgress.Summary(i)}";
            }
            finalTotals.text = $"{(completed == 4 ? "深度探索者" : "探索进行中")}\n实验室 {completed}/4    星级 {stars}/12    最佳成绩总用时 {DepthLabProgress.TimeLabel(seconds)}";
            ShowPage(finalPage);
        }
        private void ShowModal(LabAction confirm, string text)
        {
            modalReturn = visiblePage;
            modalAction = confirm;
            modalText.text = text;
            foreach (DepthLabCommand command in modalPage.GetComponentsInChildren<DepthLabCommand>(true))
                if (command.action == LabAction.ConfirmReset || command.action == LabAction.ConfirmExit)
                    command.action = confirm;
            ShowPage(modalPage);
        }
        private void ShowPage(GameObject page)
        {
            foreach (GameObject p in new[] { homePage, chapterPage, detailPage, settingsPage, creditsPage,
                pausePage, resultPage, finalPage, modalPage, tutorialPage }) p.SetActive(p == page);
            visiblePage = page;
            menuCanvas.gameObject.SetActive(page != null);
            if (previews != null) for (int i = 0; i < previews.Length; i++)
            {
                Transform preview = previews[i];
                preview.gameObject.SetActive(showChapterPreviews &&
                    (page == homePage || page == chapterPage || (page == detailPage && i == selectedChapter)));
                float x = 1140, y = 291 + i * 166;
                if (page == chapterPage) { x = 685 + i % 2 * 770; y = 360 + i / 2 * 260; }
                if (page == detailPage) { x = 1400; y = 500; }
                preview.localPosition = new Vector3(x - 800, 520 - y, -80);
                preview.localScale = Vector3.one * (page == detailPage ? 2 : page == homePage ? .48f : 1);
            }
            SetTooltip("");
        }
        private void PlaceModal()
        {
            Vector3 forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            menuCanvas.SetPositionAndRotation(Head.position + forward * 2.8f, Quaternion.LookRotation(forward));
        }
        public void SetTooltip(string text) { tooltipText.text = text; }
        public void PlaySound(bool confirm)
        {
            if (effectsAudio != null) effectsAudio.PlayOneShot(confirm ? confirmSound : hoverSound, settings.sfx);
        }
        public void ApplyAudio()
        {
            if (musicAudio != null) musicAudio.volume = settings.music;
        }
    }
}
