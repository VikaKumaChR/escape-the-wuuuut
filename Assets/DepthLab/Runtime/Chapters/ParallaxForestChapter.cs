using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace DepthLab
{
    public class ParallaxForestChapter : LabChapter
    {
        public Transform[] safePlatforms;
        public GameObject[] hiddenPlatformVisuals;
        public Button[] safeButtons;
        public TMP_Text discoveryLabel;
        public float revealSpan = 0.22f;

        private int steps;
        private bool discovered;
        private bool paused;
        private bool hasCheckpoint;
        private Vector3 checkpoint;
        private bool hasHeadSample;
        private Transform sampledHead;
        private Transform sampledRoot;
        private float previousHeadX;
        private float inspectionX;
        private float minimumX;
        private float maximumX;
        private Vector3 previousRootPosition;
        private Quaternion previousRootRotation;
        private Vector3 previousRootScale;
        private Transform inspectionHead;
        private Transform inspectionRoot;
        private float inspectionRestX;
        private float inspectionTargetX;
        private bool desktopInspection;

        public bool PathDiscovered => discovered;
        public override string Objective => PuzzleSolved ? "走进亮起的出口，前往下一关。" :
            discovered ? $"点击亮起的 {steps + 1:00} 号平台，前往下一个落脚点。" :
            XRSettings.isDeviceActive ? "保持原地，左右侧倾头部，让隐藏平台显形。" :
                "先点「向左观察」，再点「向右观察」，让隐藏平台显形。";
        public override string Hint => discovered
            ? "2. 按顺序选择四个安全平台；选错会返回最近的检查点。\n3. 全部通过后，步行穿过已开启的出口。"
            : "1. 桌面：点击“向左观察”并等视角停稳，再点“向右观察”并等视角停稳。\nVR（启用 XR 后）：保持原地，左右侧倾头部，不用移动或转向操作；转向、传送不计入观察距离。";
        public override int Progress => steps;
        public override int Goal => 4;
        protected override bool PuzzleSolved => steps == Goal;

        public override void Begin()
        {
            RestoreDesktopView();
            steps = 0;
            discovered = false;
            paused = false;
            hasHeadSample = false;
            sampledHead = null;
            sampledRoot = null;
            inspectionX = 0f;
            minimumX = 0f;
            maximumX = 0f;

            var app = DepthLabApp.Instance;
            Transform start = spawnPoint != null ? spawnPoint : app != null ? app.PlayerRoot : null;
            hasCheckpoint = start != null;
            checkpoint = hasCheckpoint ? start.position : Vector3.zero;
            SetPlatformVisibility(false);
            ResetExit();
            RefreshStatus();
            RefreshControls();
        }

        protected override void Update()
        {
            base.Update();
            if (!CanInteract || paused)
            {
                hasHeadSample = false;
                RefreshControls();
                return;
            }

            UpdateDesktopInspection();
            if (!discovered)
                TrackInspection();
            RefreshControls();
        }

        public override void SetPaused(bool value)
        {
            paused = value;
            hasHeadSample = false;
            base.SetPaused(value);
            RefreshControls();
        }

        public void StepOne() => Step(0);
        public void StepTwo() => Step(1);
        public void StepThree() => Step(2);
        public void StepFour() => Step(3);
        public void LookLeft() => SetDesktopInspection(-.15f);
        public void LookRight() => SetDesktopInspection(.15f);

        private void SetDesktopInspection(float offset)
        {
            if (!CanInteract || paused || XRSettings.isDeviceActive || discovered) return;
            var app = DepthLabApp.Instance;
            if (app.Head == null || app.PlayerRoot == null) return;
            if (!desktopInspection)
            {
                inspectionHead = app.Head;
                inspectionRoot = app.PlayerRoot;
                inspectionRestX = inspectionRoot.InverseTransformPoint(inspectionHead.position).x;
                desktopInspection = true;
            }
            inspectionTargetX = inspectionRestX + offset;
        }

        private void UpdateDesktopInspection()
        {
            if (!desktopInspection || inspectionHead == null || inspectionRoot == null) return;
            if (XRSettings.isDeviceActive)
            {
                desktopInspection = false;
                return;
            }
            Vector3 position = inspectionRoot.InverseTransformPoint(inspectionHead.position);
            position.x = Mathf.MoveTowards(position.x, inspectionTargetX, .45f * Time.deltaTime);
            inspectionHead.position = inspectionRoot.TransformPoint(position);
        }

        private void OnDisable() => RestoreDesktopView();

        private void RestoreDesktopView()
        {
            if (desktopInspection && !XRSettings.isDeviceActive && inspectionHead != null && inspectionRoot != null)
            {
                Vector3 position = inspectionRoot.InverseTransformPoint(inspectionHead.position);
                position.x = inspectionRestX;
                inspectionHead.position = inspectionRoot.TransformPoint(position);
            }
            desktopInspection = false;
        }

        public void WrongPlatform()
        {
            if (!CanInteract || paused || PuzzleSolved)
                return;

            Mistake();
            if (hasCheckpoint)
                DepthLabApp.Instance.TeleportTo(checkpoint);
            hasHeadSample = false;
            RefreshStatus();
            RefreshControls();
        }

        private void Step(int index)
        {
            if (!CanInteract || paused || !discovered || PuzzleSolved)
                return;
            if (index != steps)
            {
                WrongPlatform();
                return;
            }
            if (safePlatforms == null || index >= safePlatforms.Length || safePlatforms[index] == null)
                return;

            checkpoint = safePlatforms[index].position;
            hasCheckpoint = true;
            DepthLabApp.Instance.TeleportTo(checkpoint);
            hasHeadSample = false;
            steps++;
            RefreshStatus();
            RefreshControls();
        }

        private void TrackInspection()
        {
            var app = DepthLabApp.Instance;
            Transform head = app.Head;
            Transform root = app.PlayerRoot;
            if (head == null || root == null)
            {
                hasHeadSample = false;
                return;
            }

            float headX = root.InverseTransformPoint(head.position).x;
            bool rigMoved = !hasHeadSample || sampledHead != head || sampledRoot != root ||
                (root.position - previousRootPosition).sqrMagnitude > 0.00000001f ||
                Quaternion.Angle(root.rotation, previousRootRotation) > 0.001f ||
                (root.lossyScale - previousRootScale).sqrMagnitude > 0.00000001f;

            // Stitch only stationary-rig samples; rig motion must never create inspection distance.
            if (!rigMoved)
            {
                inspectionX += headX - previousHeadX;
                minimumX = Mathf.Min(minimumX, inspectionX);
                maximumX = Mathf.Max(maximumX, inspectionX);
            }

            previousHeadX = headX;
            previousRootPosition = root.position;
            previousRootRotation = root.rotation;
            previousRootScale = root.lossyScale;
            sampledHead = head;
            sampledRoot = root;
            hasHeadSample = true;

            if (maximumX - minimumX < Mathf.Max(0.01f, revealSpan))
                return;

            discovered = true;
            SetPlatformVisibility(true);
            app.ReportTutorialAction("parallax");
            RefreshStatus();
        }

        private void SetPlatformVisibility(bool visible)
        {
            if (hiddenPlatformVisuals == null)
                return;
            foreach (GameObject visual in hiddenPlatformVisuals)
                if (visual != null)
                    visual.SetActive(visible);
        }

        private void RefreshControls()
        {
            if (safeButtons != null)
            {
                for (int i = 0; i < safeButtons.Length; i++)
                {
                    Button button = safeButtons[i];
                    if (button == null)
                        continue;
                    button.gameObject.SetActive(discovered && i == steps && i < Goal);
                    button.interactable = CanInteract && !paused && discovered && i == steps &&
                        i < Goal && safePlatforms != null && i < safePlatforms.Length && safePlatforms[i] != null;
                }
            }

            if (discoveryLabel != null)
                discoveryLabel.text = discovered
                    ? PuzzleSolved ? "3. 路径已通过，请步行穿过已开启的出口。" : "2. 路径已发现，请依次选择四个安全平台。"
                    : maximumX - minimumX < revealSpan * .45f
                        ? "先点向左观察，等视角停稳"
                        : "再点向右观察，平台就会显形";
        }
    }
}
