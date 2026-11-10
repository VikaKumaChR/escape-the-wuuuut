using TMPro;
using UnityEngine;

namespace DepthLab
{
    public sealed class DepthLabHallPortal : MonoBehaviour
    {
        public DepthLabApp app;
        public int chapterIndex;
        public TMP_Text status;
        public UnityEngine.UI.Button enterButton;
        public Transform gate;
        public Renderer indicator;
        public Material lockedMaterial, readyMaterial;
        private bool unlocked;
        private bool approaching;
        private Vector3 previous;
        private bool sampled;
        private float openAmount;

        private void OnEnable()
        {
            sampled = false;
            approaching = false;
            Refresh();
        }

        private void Refresh()
        {
            unlocked = DepthLabProgress.Unlocked(chapterIndex);
            if (enterButton != null) enterButton.interactable = unlocked;
            if (status != null)
                status.text = unlocked ? "已开放 · 穿过入口开始实验" : "未开放 · 请先完成上一项实验";
            if (indicator != null)
                indicator.sharedMaterial = unlocked ? readyMaterial : lockedMaterial;
        }

        private void Update()
        {
            if (app == null || app.headCamera == null || app.IsPlaying || app.IsTutorial || app.IsPaused)
                return;
            Refresh();
            openAmount = Mathf.MoveTowards(openAmount, unlocked ? 1f : 0f, Time.deltaTime);
            if (gate != null)
                gate.localPosition = new Vector3(0f, 1.4f + openAmount * 3f, .05f);
            if (!unlocked || openAmount < .95f)
                return;
            Vector3 head = app.Head.position;
            Vector3 local = transform.InverseTransformPoint(head);
            Vector3 last = transform.InverseTransformPoint(previous);
            bool continuous = sampled && (head - previous).sqrMagnitude < .5625f;
            previous = head;
            sampled = true;
            if (!continuous || Mathf.Abs(local.x) > .8f || local.y < .2f || local.y > 2.7f)
                approaching = false;
            if (local.z < -.12f) approaching = true;
            if (continuous && approaching && last.z < .18f && local.z >= .18f)
                Enter();
        }

        public void Enter()
        {
            if (app == null)
            {
                Debug.LogError("实验室入口未连接游戏控制器。", this);
                return;
            }
            if (!DepthLabProgress.Unlocked(chapterIndex) || app.IsPaused || app.IsPlaying)
                return;
            approaching = sampled = false;
            app.StartChapter(chapterIndex);
        }
    }
}
