using UnityEngine;
using UnityEngine.EventSystems;

namespace DepthLab
{
    public sealed class DepthLabUiMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        private Vector3 restScale;
        private float target = 1f;
        private bool hovering;
        private CanvasGroup group;
        private UnityEngine.UI.Selectable selectable;

        private void Awake()
        {
            restScale = transform.localScale;
            selectable = GetComponent<UnityEngine.UI.Selectable>();
            if (selectable == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            target = 1f;
            if (group != null) group.alpha = 0;
        }

        private void Update()
        {
            if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, 1, Time.unscaledDeltaTime * 5);
            if (selectable != null && !selectable.IsInteractable()) target = 1;
            transform.localScale = Vector3.Lerp(transform.localScale, restScale * target,
                1 - Mathf.Exp(-18 * Time.unscaledDeltaTime));
        }

        public void OnPointerEnter(PointerEventData data)
        {
            hovering = true;
            if (selectable != null && selectable.IsInteractable()) target = 1.025f;
        }
        public void OnPointerExit(PointerEventData data) { hovering = false; target = 1; }
        public void OnPointerDown(PointerEventData data)
        {
            if (selectable != null && selectable.IsInteractable()) target = .98f;
        }
        public void OnPointerUp(PointerEventData data) { target = hovering ? 1.025f : 1; }
        private void OnDisable()
        {
            hovering = false;
            target = 1;
            transform.localScale = restScale;
        }
    }
}
