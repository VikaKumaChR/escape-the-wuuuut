using UnityEngine;

namespace DepthLab
{
    public sealed class DepthLabAmbient : MonoBehaviour
    {
        public Vector3 rotation = new Vector3(0, 8, 0);
        public float bobAmount;
        private Vector3 origin;
        private void Awake() { origin = transform.localPosition; }
        private void Update()
        {
            transform.Rotate(rotation * Time.unscaledDeltaTime, Space.Self);
            if (bobAmount > 0)
                transform.localPosition = origin + Vector3.up * (Mathf.Sin(Time.unscaledTime * .6f) * bobAmount);
        }
    }
}
