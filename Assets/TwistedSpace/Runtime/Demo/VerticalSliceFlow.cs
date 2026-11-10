using System.Collections;
using TwistedSpace.Puzzles;
using TwistedSpace.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TwistedSpace.Demo
{
    public enum DemoState { MainMenu, Playing, ResetConfirmation, Solved }

    [DisallowMultipleComponent]
    public class VerticalSliceFlow : MonoBehaviour
    {
        [Header("Puzzle")]
        [SerializeField, Tooltip("Keep enabled at scene startup so its Start can validate and initialize.")]
        private ForcedPerspectivePuzzle puzzle;
        [SerializeField, Tooltip("The same targets assigned to the puzzle; never disable their grab components.")]
        private PerspectivePuzzleTarget[] targets;
        [SerializeField] private PuzzleObservationVolume observationVolume;

        [Header("Active world-space panels")]
        [SerializeField] private VRWorldSpacePanel mainMenuPanel;
        [SerializeField, Tooltip("Author at a fixed side position. Head following is disabled by this flow.")]
        private VRWorldSpacePanel hintPanel;
        [SerializeField] private VRWorldSpacePanel resetPanel;
        [SerializeField] private VRWorldSpacePanel resultPanel;
        [SerializeField] private SlidingExitDoor exitDoor;

        [Header("World-space readouts")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text hintText;
        [SerializeField] private Text resultText;
        [SerializeField] private Slider alignmentSlider;
        [SerializeField] private Slider stableHoldSlider;

        private static readonly string[] Hints =
        {
            "第一步：回到地面标记的观测区，面向三个几何框。",
            "第二步：依次抓住框体并移动，让三个外轮廓在视野中重合。",
            "第三步：微调物体和头部视角，看到对齐后保持视角，等待稳定进度完成。"
        };

        public DemoState State { get; private set; } = DemoState.MainMenu;
        public float ElapsedSeconds { get; private set; }
        public int HintsUsed { get; private set; }

        private ForcedPerspectivePuzzle subscribedPuzzle;
        private bool initialized;
        private bool restarting;
        private int hintIndex;
        private int displayedSecond = -1;
        private int displayedPercent = -1;
        private string displayedStatus;

        private IEnumerator Start()
        {
            if (!ValidateSetup())
            {
                if (puzzle)
                    puzzle.enabled = false;
                enabled = false;
                yield break;
            }

            subscribedPuzzle = puzzle;
            subscribedPuzzle.OnPuzzleSolved.AddListener(HandleSolved);
            hintPanel.FollowHead = false;
            ConfigureSlider(alignmentSlider);
            ConfigureSlider(stableHoldSlider);
            hintText.text = Hints[0];
            resultText.text = string.Empty;

            // All panel Awakes have run. Suppress startup flashes even if startVisible was true.
            HideInitially(hintPanel);
            HideInitially(resetPanel);
            HideInitially(resultPanel);
            mainMenuPanel.FadeIn();
            exitDoor.Close();

            // Update gates the first LateUpdate, but leaves every component's Start free to run.
            // Never call ResetPuzzle before this initialization barrier.
            yield return null;
            puzzle.enabled = false;
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                // Every initial Start runs before Update, regardless of component ordering.
                if (puzzle)
                    puzzle.enabled = false;
                return;
            }

            if (State != DemoState.Playing || restarting)
                return;

            ElapsedSeconds += Time.unscaledDeltaTime;
            RefreshProgress();
        }

        public void StartGame()
        {
            if (CanHandleCallback() && State == DemoState.MainMenu)
                RestartRound();
        }

        public void RequestReset()
        {
            if (!CanHandleCallback() || State != DemoState.Playing)
                return;
            State = DemoState.ResetConfirmation;
            puzzle.enabled = false;
            ShowStatePanels();
        }

        public void CancelReset()
        {
            if (!CanHandleCallback() || State != DemoState.ResetConfirmation)
                return;
            State = DemoState.Playing;
            puzzle.enabled = true;
            ShowStatePanels();
            RefreshProgress();
        }

        public void ConfirmReset()
        {
            if (CanHandleCallback() && State == DemoState.ResetConfirmation)
                RestartRound();
        }

        public void ShowNextHint()
        {
            if (!CanHandleCallback() || State != DemoState.Playing)
                return;
            hintIndex = (hintIndex + 1) % Hints.Length;
            HintsUsed++;
            hintText.text = Hints[hintIndex];
        }

        public void ReturnToMenu()
        {
            if (!CanHandleCallback() || State == DemoState.MainMenu)
                return;
            State = DemoState.MainMenu;
            puzzle.enabled = false;
            exitDoor.Close();
            ShowStatePanels();
        }

        public void Replay()
        {
            if (CanHandleCallback() && State == DemoState.Solved)
                RestartRound();
        }

        private bool CanHandleCallback()
        {
            if (!initialized)
            {
                Debug.LogWarning("VerticalSliceFlow: UI callback ignored until startup initialization completes.", this);
                return false;
            }
            return isActiveAndEnabled && !restarting;
        }

        private void RestartRound()
        {
            restarting = true;
            puzzle.enabled = false;
            // Only the puzzle queues poses. Held targets finish their reset after XRI detaches.
            puzzle.ResetPuzzle();
            ElapsedSeconds = 0f;
            HintsUsed = 0;
            hintIndex = 0;
            hintText.text = Hints[0];
            resultText.text = string.Empty;
            displayedSecond = -1;
            displayedPercent = -1;
            displayedStatus = null;
            exitDoor.Close();
            State = DemoState.Playing;
            puzzle.enabled = true;
            ShowStatePanels();
            restarting = false;
            RefreshProgress();
        }

        private void HandleSolved()
        {
            if (!initialized || !isActiveAndEnabled || restarting ||
                State != DemoState.Playing || !puzzle.IsSolved)
                return;

            State = DemoState.Solved;
            int seconds = Mathf.FloorToInt(ElapsedSeconds);
            resultText.text = $"挑战完成！\n用时：{seconds / 60}分{seconds % 60:00}秒\n使用提示：{HintsUsed}次";
            alignmentSlider.SetValueWithoutNotify(1f);
            stableHoldSlider.SetValueWithoutNotify(1f);
            exitDoor.Open();
            ShowStatePanels();
            // Leave puzzle enabled: OnDisable would cancel its success emission fade.
        }

        private void RefreshProgress()
        {
            float alignment = Mathf.Clamp01(puzzle.Progress);
            alignmentSlider.SetValueWithoutNotify(alignment);
            stableHoldSlider.SetValueWithoutNotify(Mathf.Clamp01(puzzle.StableHoldProgress));

            bool pendingReset = false;
            for (int i = 0; i < targets.Length; i++)
                pendingReset |= targets[i].IsResetPending;

            string status = pendingReset ? "松开手柄以完成重置" :
                !observationVolume.IsHeadInside ? "请进入观测区" :
                puzzle.StableHoldProgress > 0f ? "保持视角" : "调整物体与视角";
            int second = Mathf.FloorToInt(ElapsedSeconds);
            int percent = Mathf.RoundToInt(alignment * 100f);
            if (status == displayedStatus && second == displayedSecond && percent == displayedPercent)
                return;

            displayedStatus = status;
            displayedSecond = second;
            displayedPercent = percent;
            statusText.text = $"{status}\n对齐：{percent}%　用时：{second / 60}:{second % 60:00}";
        }

        private void ShowStatePanels()
        {
            SetVisible(mainMenuPanel, State == DemoState.MainMenu);
            SetVisible(hintPanel, State == DemoState.Playing);
            SetVisible(resetPanel, State == DemoState.ResetConfirmation);
            SetVisible(resultPanel, State == DemoState.Solved);
        }

        private static void SetVisible(VRWorldSpacePanel panel, bool visible)
        {
            if (visible)
                panel.FadeIn();
            else
                panel.FadeOut();
        }

        private static void HideInitially(VRWorldSpacePanel panel)
        {
            panel.GetComponent<CanvasGroup>().alpha = 0f;
            panel.FadeOut();
        }

        private static void ConfigureSlider(Slider slider)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.interactable = false;
            slider.SetValueWithoutNotify(0f);
        }

        private bool ValidateSetup()
        {
            if (!puzzle || !puzzle.isActiveAndEnabled || !observationVolume ||
                !observationVolume.isActiveAndEnabled || !observationVolume.IsConfigured ||
                !exitDoor || !exitDoor.isActiveAndEnabled ||
                !statusText || !hintText || !resultText ||
                !alignmentSlider || !stableHoldSlider || alignmentSlider == stableHoldSlider ||
                targets == null || targets.Length < 2)
            {
                Debug.LogError("VerticalSliceFlow: assign an initially enabled puzzle, configured observation " +
                    "volume, active door, all readouts, two distinct sliders and the puzzle's targets.", this);
                return false;
            }

            VRWorldSpacePanel[] panels = { mainMenuPanel, hintPanel, resetPanel, resultPanel };
            for (int i = 0; i < panels.Length; i++)
            {
                if (!panels[i] || !panels[i].isActiveAndEnabled ||
                    panels[i].GetComponent<Canvas>().renderMode != RenderMode.WorldSpace ||
                    !panels[i].GetComponent<CanvasGroup>().enabled)
                {
                    Debug.LogError("VerticalSliceFlow: all four panels must be active, enabled World Space canvases.", this);
                    return false;
                }
                for (int j = 0; j < i; j++)
                    if (panels[i] == panels[j])
                    {
                        Debug.LogError("VerticalSliceFlow: assign four distinct panels.", this);
                        return false;
                    }
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i] || !targets[i].isActiveAndEnabled || !targets[i].IsConfigured)
                {
                    Debug.LogError("VerticalSliceFlow: each target must be active, enabled and configured.", this);
                    return false;
                }
                for (int j = 0; j < i; j++)
                    if (targets[i] == targets[j])
                    {
                        Debug.LogError("VerticalSliceFlow: target references must be unique.", this);
                        return false;
                    }
            }
            return true;
        }

        private void OnEnable()
        {
            if (!initialized)
                return;
            puzzle.enabled = State == DemoState.Playing || State == DemoState.Solved;
            ShowStatePanels();
        }

        private void OnDisable()
        {
            if (puzzle)
                puzzle.enabled = false;
        }

        private void OnDestroy()
        {
            if (subscribedPuzzle)
                subscribedPuzzle.OnPuzzleSolved.RemoveListener(HandleSolved);
        }
    }
}
