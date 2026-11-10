using UnityEngine;

namespace TwistedSpace.Demo
{
    /// <summary>Small local motion for the guide proxy; never animates the camera or puzzle targets.</summary>
    public sealed class FloatingGuide : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float amplitude = 0.025f;
        [SerializeField, Min(0f)] private float cyclesPerSecond = 0.25f;
        [SerializeField] private float yawDegreesPerSecond = 8f;
        private Vector3 restingPosition;
        private Quaternion restingRotation;
        private float elapsed;

        private void OnEnable()
        {
            restingPosition = transform.localPosition;
            restingRotation = transform.localRotation;
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            transform.localPosition = restingPosition +
                Vector3.up * (Mathf.Sin(elapsed * cyclesPerSecond * Mathf.PI * 2f) * amplitude);
            transform.localRotation = restingRotation * Quaternion.Euler(0f, elapsed * yawDegreesPerSecond, 0f);
        }

        private void OnDisable()
        {
            transform.localPosition = restingPosition;
            transform.localRotation = restingRotation;
        }
    }
}
