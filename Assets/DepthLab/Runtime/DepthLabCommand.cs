using UnityEngine;

namespace DepthLab
{
    public enum LabAction
    {
        Start, Chapters, Detail, PlaySelected, Menu, Tutorial, TutorialNext, TutorialSkip,
        Settings, Credits, Exit, CancelModal, ConfirmExit, Pause, Resume, Hint, Replay,
        NextChapter, ResetProgress, ConfirmReset, Defaults, SettingsBack, Final, PlayAgain, Continue
    }

    public sealed class DepthLabCommand : MonoBehaviour
    {
        public LabAction action;
        public int argument;
        public void Execute()
        {
            if (DepthLabApp.Instance != null) DepthLabApp.Instance.Execute(action, argument);
        }
    }
}
