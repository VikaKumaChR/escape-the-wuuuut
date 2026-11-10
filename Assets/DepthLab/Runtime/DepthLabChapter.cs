using UnityEngine;

namespace DepthLab
{
    public abstract class DepthLabChapter : MonoBehaviour
    {
        public int chapterIndex;
        public Transform spawnPoint;
        public float threeStarSeconds = 180f;
        public float twoStarSeconds = 360f;
        public int threeStarMistakes = 0;
        public int twoStarMistakes = 3;
        public abstract string Objective { get; }
        public abstract string Hint { get; }
        public abstract int Progress { get; }
        public abstract int Goal { get; }
        public abstract void Begin();
        public virtual void SetPaused(bool paused) { }
        protected bool CanInteract => DepthLabApp.Instance != null &&
            DepthLabApp.Instance.IsPlaying && !DepthLabApp.Instance.IsPaused &&
            DepthLabApp.Instance.ActiveChapter == chapterIndex;
        protected void Mistake() { if (CanInteract) DepthLabApp.Instance.AddMistake(); }
        protected void Complete() { if (CanInteract) DepthLabApp.Instance.CompleteChapter(); }
    }
}
