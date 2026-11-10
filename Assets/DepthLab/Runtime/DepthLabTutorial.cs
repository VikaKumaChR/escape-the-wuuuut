using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using DistortedSpace.Localization;

namespace DepthLab
{
    public sealed class DepthLabTutorial : MonoBehaviour
    {
        public Transform head;
        public Transform playerRoot;
        public GameObject practiceObject;
        public GameObject target;
        public int Step { get; private set; }
        public bool Ready { get; private set; }
        public bool Completed;
        public event Action Changed;
        public event Action Finished;

        public string Instruction => Completed ? Loc.T("操作练习已完成。") : Loc.T(Instructions[Mathf.Clamp(Step, 0, 6)]);

        // Chinese source text doubles as localization keys (shown through Loc.T in Instruction).
        private static readonly string[] Instructions =
        {
            "1/7 环顾：桌面按住鼠标右键移动鼠标；VR 转头或侧倾。完成后点击“下一步”。\n本项目未安装 OpenXR / XR Management，未配置 XR 加载器；以下 VR 操作仅在另行配置并启用 XR 后适用。",
            "2/7 调整朝向：桌面按住鼠标右键移动鼠标；VR 移动或转动控制器。完成后点击“下一步”。",
            "3/7 指向按钮：桌面将鼠标悬停在可用按钮上；VR 用界面射线指向可用按钮。完成后点击“下一步”。",
            "4/7 抓取彩色实体：桌面在练习物体上按住鼠标左键，滚轮调远近；VR 用控制器抓取。完成后点击“下一步”。\n彩色物体是可抓取实体；线框仅标示目标，不是实体，不能抓取。",
            "5/7 放入目标：将彩色实体移至距线框中心不足 35 厘米处再松手；桌面松开鼠标左键。完成后点击“下一步”。\n线框是无碰撞的目标影像，不会托住物体；彩色物体与底座是有碰撞的实体。",
            "6/7 移动超过 30 厘米，原地转向不算。桌面：WASD 连续移动，Q/E 连续转向；VR：左摇杆移动，右摇杆转向，模式由设置决定。完成后点击“下一步”。\nEsc 或 VR 左手菜单键可暂停。",
            "7/7 查看提示：先点击“提示”，再点击“下一步”完成练习。"
        };

        private XRGrabInteractable grab;
        private GameObject capturedPractice;
        private GameObject capturedTarget;
        private Vector3 practicePosition;
        private Quaternion practiceRotation;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Vector3 headPosition;
        private Quaternion headRotation;
        private Vector3 movementOrigin;
        private bool haveHeadPose;
        private bool haveMovementOrigin;
        private bool begun;
        private bool desktopHeld;
        private bool grabbedPractice;
        private bool releasedPractice;
        private ControllerPose leftPose;
        private ControllerPose rightPose;

        private struct ControllerPose
        {
            public bool valid;
            public UnityEngine.XR.InputDevice device;
            public Vector3 position;
            public Quaternion rotation;
        }

        private bool IsHeld => desktopHeld || (grab != null && grab.isSelected);
        private bool Paused => DepthLabApp.Instance != null && DepthLabApp.Instance.IsPaused;

        public void Begin()
        {
            begun = false;
            ResolveReferences();
            foreach (DepthLabInput input in FindObjectsByType<DepthLabInput>())
                if (input.playerRoot == playerRoot)
                    input.ResetPose();
            BindGrab();
            CaptureAuthoredPoses();
            // Do not move an object out of a live interactor's hand when restarting.
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            desktopHeld = false;
            grabbedPractice = false;
            releasedPractice = false;
            RestorePose(practiceObject, practicePosition, practiceRotation);
            RestorePose(target, targetPosition, targetRotation);
            Step = 0;
            Ready = false;
            Completed = false;
            begun = true;
            CaptureStepBaseline();
            Changed?.Invoke();
        }

        private void ResolveReferences()
        {
            DepthLabApp app = DepthLabApp.Instance;
            if (head == null && app != null)
                head = app.Head;
            if (playerRoot == null && app != null)
                playerRoot = app.PlayerRoot;
        }

        private void CaptureAuthoredPoses()
        {
            if (practiceObject != null && capturedPractice != practiceObject)
            {
                capturedPractice = practiceObject;
                practicePosition = practiceObject.transform.position;
                practiceRotation = practiceObject.transform.rotation;
            }
            if (target != null && capturedTarget != target)
            {
                capturedTarget = target;
                targetPosition = target.transform.position;
                targetRotation = target.transform.rotation;
            }
        }

        private static void RestorePose(GameObject item, Vector3 position, Quaternion rotation)
        {
            if (item == null)
                return;
            item.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null)
                return;
            body.position = position;
            body.rotation = rotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void BindGrab()
        {
            UnbindGrab();
            grab = practiceObject != null ? practiceObject.GetComponentInChildren<XRGrabInteractable>() : null;
            if (grab == null)
                return;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        private void UnbindGrab()
        {
            if (grab == null)
                return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            Observe("grab");
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (!args.isCanceled)
                Observe("release");
        }

        public void SetHeld(bool value)
        {
            bool wasHeld = desktopHeld;
            desktopHeld = value;
            if (value && !wasHeld)
                Observe("grab");
            else if (!value && wasHeld)
                Observe("release");
        }

        public void Observe(string action)
        {
            if (!begun || Completed || Paused)
                return;
            if (action == "grab" && IsHeld)
            {
                grabbedPractice = true;
                releasedPractice = false;
                if (Step == 3)
                    SetReady(true);
                if (Step == 4)
                    SetReady(false);
            }
            else if (action == "release" && grabbedPractice && !IsHeld)
            {
                releasedPractice = true;
                if (Step == 4)
                    CheckRelease();
            }
            else if ((Step == 1 && action == "controllers") ||
                     (Step == 2 && action == "ray") ||
                     (Step == 6 && action == "hint"))
            {
                SetReady(true);
            }
            // Head, release and locomotion require measured poses, not just action names.
        }

        private void Update()
        {
            if (!begun || Completed)
                return;
            ResolveReferences();
            if (Paused)
            {
                // Tracking continues while paused, but cannot satisfy a tutorial step.
                CaptureStepBaseline();
                return;
            }
            switch (Step)
            {
                case 0:
                    if (head == null)
                        break;
                    if (!haveHeadPose)
                        CaptureStepBaseline();
                    Vector3 position = playerRoot != null ? playerRoot.InverseTransformPoint(head.position) : head.position;
                    Quaternion rotation = playerRoot != null ? Quaternion.Inverse(playerRoot.rotation) * head.rotation : head.rotation;
                    if (Vector3.Distance(headPosition, position) > .05f ||
                        Quaternion.Angle(headRotation, rotation) > 8f)
                        SetReady(true);
                    break;
                case 1:
                    if (DepthLabInput.IsXRActive &&
                        (ControllerMoved(XRNode.LeftHand, ref leftPose) |
                         ControllerMoved(XRNode.RightHand, ref rightPose)))
                        SetReady(true);
                    break;
                case 4:
                    CheckRelease();
                    break;
                case 5:
                    if (head == null && playerRoot == null)
                        break;
                    if (!haveMovementOrigin)
                        CaptureStepBaseline();
                    Vector3 feet = head != null ? head.position : playerRoot.position;
                    if (Vector3.ProjectOnPlane(feet - movementOrigin, Vector3.up).magnitude > .3f)
                        SetReady(true);
                    break;
            }
        }

        private static bool ControllerMoved(XRNode node, ref ControllerPose baseline)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid ||
                (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                baseline.valid = false;
                return false;
            }
            if (!baseline.valid || !baseline.device.Equals(device))
            {
                baseline.valid = true;
                baseline.device = device;
                baseline.position = position;
                baseline.rotation = rotation;
                return false;
            }
            return Vector3.Distance(baseline.position, position) > .04f ||
                Quaternion.Angle(baseline.rotation, rotation) > 10f;
        }

        private void CheckRelease()
        {
            SetReady(releasedPractice && !IsHeld && practiceObject != null && target != null &&
                Vector3.Distance(practiceObject.transform.position, target.transform.position) < .35f);
        }

        private void CaptureStepBaseline()
        {
            haveHeadPose = head != null;
            if (haveHeadPose)
            {
                headPosition = playerRoot != null ? playerRoot.InverseTransformPoint(head.position) : head.position;
                headRotation = playerRoot != null ? Quaternion.Inverse(playerRoot.rotation) * head.rotation : head.rotation;
            }
            haveMovementOrigin = head != null || playerRoot != null;
            if (haveMovementOrigin)
                // A turn pivots the root around the head; only head translation counts.
                movementOrigin = head != null ? head.position : playerRoot.position;
            leftPose = default;
            rightPose = default;
            if (DepthLabInput.IsXRActive)
            {
                ControllerMoved(XRNode.LeftHand, ref leftPose);
                ControllerMoved(XRNode.RightHand, ref rightPose);
            }
        }

        private void SetReady(bool value)
        {
            if (Ready == value)
                return;
            Ready = value;
            Changed?.Invoke();
        }

        public void Next()
        {
            if (!begun || Completed || Paused || !Ready)
                return;
            if (Step == 4)
            {
                CheckRelease();
                if (!Ready)
                    return;
            }
            if (Step == 6)
            {
                Finish();
                return;
            }
            Step++;
            Ready = false;
            CaptureStepBaseline();
            if (Step == 3 && IsHeld)
            {
                grabbedPractice = true;
                Ready = true;
            }
            if (Step == 4)
                CheckRelease();
            Changed?.Invoke();
        }

        public void Skip()
        {
            if (!Completed)
                Finish();
        }

        private void Finish()
        {
            Completed = true;
            Ready = false;
            Step = 7;
            Changed?.Invoke();
            Finished?.Invoke();
        }

        private void OnEnable()
        {
            if (begun && !Completed)
            {
                BindGrab();
                CaptureStepBaseline();
            }
        }

        private void OnDisable()
        {
            UnbindGrab();
        }
    }
}
