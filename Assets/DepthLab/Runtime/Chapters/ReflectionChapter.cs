using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using DistortedSpace.Localization;

namespace DepthLab
{
    public sealed class ReflectionChapter : LabChapter
    {
        public Light keyLight;
        public Transform reflector;
        public Transform reflectorAnchor;
        public XRGrabInteractable reflectorGrab;
        public Rigidbody reflectorBody;
        public Transform emitter;
        public Transform receiver;
        public Renderer receiverRenderer;
        public Material receiverIdleMaterial;
        public Material receiverHitMaterial;
        public LineRenderer incomingBeam;
        public LineRenderer reflectedBeam;
        public TMP_Text instrumentLabel;
        public float receiverRadius = .34f;
        public float lockSeconds = 1.5f;
        public float requiredLightYaw = 25f;

        private float lightYaw;
        private float stableSeconds;
        private bool solved;
        private bool rayHits;
        private float mirrorYaw;
        private const float InitialMirrorYaw = -15f;

        public override string Objective => solved ? Loc.T("实验完成：沿地面指引穿过出口。") :
            !CorrectLight ? Loc.T("第一步：点击下方「+25°」，调整光源。") :
            !rayHits ? Loc.T("第二步：点击「镜面 −5°」，让反射光照到橙色接收球。") :
            Loc.T("第三步：保持不动，等接收球亮起青灯。");
        public override string Hint => solved ? Loc.T("3. 光学锁定成功。请步行穿过已开启的出口。") :
            !CorrectLight ? Loc.T("先点「+25°」。暂时不用调整镜面。") :
            !rayHits ? Loc.F("镜面当前 {0:0}°。点击「镜面 −5°」调到 −60°；从 −15°开始需要点九次。", mirrorYaw) :
            Loc.T("已经对准，保持 1.5 秒即可完成。无需计算公式。");
        public override int Progress => solved ? 2 : CorrectLight ? 1 : 0;
        public override int Goal => 2;
        protected override bool PuzzleSolved => solved;
        private bool CorrectLight => Mathf.Abs(Mathf.DeltaAngle(lightYaw, requiredLightYaw)) <= 1f;

        public override void Begin()
        {
            lightYaw = 0f;
            mirrorYaw = InitialMirrorYaw;
            stableSeconds = 0f;
            solved = false;
            rayHits = false;
            ResetExit();
            if (reflector != null && reflectorAnchor != null)
                reflector.SetPositionAndRotation(reflectorAnchor.position,
                    transform.rotation * Quaternion.Euler(0f, mirrorYaw, 0f));
            UpdateOptics();
            RefreshStatus();
        }

        public void LightLeft() => SetLight(-25f);
        public void LightCenter() => SetLight(0f);
        public void LightRight() => SetLight(25f);
        public void RotateLeft() => RotateMirror(-5f);
        public void RotateRight() => RotateMirror(5f);

        protected override bool IsGrabAvailable(XRGrabInteractable grab) => !solved && base.IsGrabAvailable(grab);

        private void SetLight(float angle)
        {
            if (!InteractionAllowed || solved) return;
            lightYaw = angle;
            stableSeconds = 0f;
            DepthLabApp.Instance.ReportTutorialAction("light");
            UpdateOptics();
            RefreshStatus();
        }

        private void RotateMirror(float degrees)
        {
            if (!InteractionAllowed || solved || reflector == null ||
                (reflectorGrab != null && reflectorGrab.isSelected)) return;
            mirrorYaw = Mathf.DeltaAngle(0f, mirrorYaw + degrees);
            reflector.rotation = transform.rotation * Quaternion.Euler(0f, mirrorYaw, 0f);
            stableSeconds = 0f;
            DepthLabApp.Instance.ReportTutorialAction("reflect");
            UpdateOptics();
        }

        protected override void Update()
        {
            base.Update();
            if (!InteractionAllowed) return;
            if (reflector != null && reflectorAnchor != null)
            {
                if (!solved)
                    mirrorYaw = Mathf.DeltaAngle(0f, (Quaternion.Inverse(transform.rotation) * reflector.rotation).eulerAngles.y);
                reflector.SetPositionAndRotation(reflectorAnchor.position,
                    transform.rotation * Quaternion.Euler(0f, mirrorYaw, 0f));
                if (reflectorBody != null)
                {
                    reflectorBody.position = reflector.position;
                    reflectorBody.rotation = reflector.rotation;
                }
            }
            UpdateOptics();
            if (!solved)
            {
                stableSeconds = rayHits && CorrectLight ? stableSeconds + Time.deltaTime : 0f;
                if (stableSeconds >= lockSeconds)
                {
                    solved = true;
                    DepthLabApp.Instance.ReportTutorialAction("reflection");
                    RefreshStatus();
                }
            }
            if (reflectorGrab != null) reflectorGrab.enabled = !solved;
            if (interactionButtons != null && solved)
                foreach (var button in interactionButtons)
                    if (button != null) button.interactable = false;
        }

        private void UpdateOptics()
        {
            if (reflector == null || receiver == null) return;
            Vector3 incident = transform.rotation * Quaternion.Euler(0f, lightYaw, 0f) * Vector3.forward;
            Vector3 origin = reflector.position;
            Vector3 reflected = Vector3.Reflect(incident, reflector.forward).normalized;
            Vector3 target = receiver.position - origin;
            float along = Vector3.Dot(target, reflected);
            float missSquared = target.sqrMagnitude - along * along;
            rayHits = along > 0f && missSquared <= receiverRadius * receiverRadius;
            float length = rayHits
                ? Mathf.Max(.01f, along - Mathf.Sqrt(Mathf.Max(0f, receiverRadius * receiverRadius - missSquared)))
                : 6f;
            Vector3 source = origin - incident * 2.8f;
            if (emitter != null) emitter.position = source;
            if (keyLight != null) keyLight.transform.rotation = Quaternion.LookRotation(incident, transform.up);
            if (incomingBeam != null)
            {
                incomingBeam.SetPosition(0, source);
                incomingBeam.SetPosition(1, origin);
            }
            if (reflectedBeam != null)
            {
                reflectedBeam.SetPosition(0, origin);
                reflectedBeam.SetPosition(1, origin + reflected * length);
                reflectedBeam.startColor = reflectedBeam.endColor =
                    rayHits && CorrectLight ? new Color(.3f, 1f, .76f) : new Color(1f, .71f, .28f);
            }
            if (receiverRenderer != null)
                receiverRenderer.sharedMaterial = solved || (rayHits && CorrectLight) ? receiverHitMaterial : receiverIdleMaterial;
            if (instrumentLabel != null)
                instrumentLabel.text = Loc.F("光源 {0:+0;-0;0}°   /   镜面 {1:+0;-0;0}°", lightYaw, mirrorYaw) + "\n" +
                    (solved ? Loc.T("3. 接收器已锁定 · 请步行穿过已开启的出口") :
                    !CorrectLight ? Loc.T("等待第一步：选择 +25° 光源") :
                    !rayHits ? Loc.F("光源已就绪 · 镜面距离参考角度还有 {0:0}°", Mathf.Abs(Mathf.DeltaAngle(mirrorYaw, -60f))) :
                    Loc.F("已对准接收球 · 正在确认 {0:0.0} / {1:0.0} 秒", stableSeconds, lockSeconds));
        }
    }
}
