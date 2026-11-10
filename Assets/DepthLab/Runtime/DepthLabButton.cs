using UnityEngine;
using UnityEngine.EventSystems;

namespace DepthLab
{
    public class DepthLabButton : UnityEngine.UI.Button
    {
        public UnityEngine.UI.Image halo;
        public string tooltip;
        public Color accentColor = new Color(0f, .898f, 1f, 1f);
        public float transitionSeconds = .12f;
        private bool hovering;
        private Vector3 targetScale = Vector3.one;
        private Color targetHalo;
        protected override void Awake()
        {
            base.Awake();
            onClick.AddListener(ConfirmSound);
        }
        private void ConfirmSound()
        {
            if (DepthLabApp.Instance != null) DepthLabApp.Instance.PlaySound(true);
        }
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            bool accent = state == SelectionState.Highlighted || state == SelectionState.Selected;
            targetScale = Vector3.one * (state == SelectionState.Pressed ? .98f : accent ? 1.015f : 1f);
            targetHalo = accentColor;
            targetHalo.a = state == SelectionState.Disabled ? .06f :
                state == SelectionState.Pressed ? .8f : accent ? .65f : .2f;
            if (instant || !Application.isPlaying)
            {
                transform.localScale = targetScale;
                if (halo != null) halo.color = targetHalo;
            }
        }
        private void Update()
        {
            float blend = transitionSeconds <= 0f ? 1f :
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f / transitionSeconds);
            if ((transform.localScale - targetScale).sqrMagnitude > .000001f)
                transform.localScale = Vector3.Lerp(transform.localScale, targetScale, blend);
            if (halo != null && halo.color != targetHalo)
                halo.color = Color.Lerp(halo.color, targetHalo, blend);
        }
        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (!IsInteractable()) return;
            hovering = true;
            if (DepthLabApp.Instance != null)
            {
                DepthLabApp.Instance.PlaySound(false);
                DepthLabApp.Instance.SetTooltip(tooltip);
                DepthLabApp.Instance.ReportTutorialAction("ray");
            }
        }
        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            hovering = false;
            if (DepthLabApp.Instance != null) DepthLabApp.Instance.SetTooltip("");
        }
        protected override void OnDisable()
        {
            if (hovering && DepthLabApp.Instance != null) DepthLabApp.Instance.SetTooltip("");
            hovering = false;
            base.OnDisable();
            targetScale = Vector3.one;
            transform.localScale = Vector3.one;
        }
    }
}
