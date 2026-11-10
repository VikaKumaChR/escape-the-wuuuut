using System.Collections;
using UnityEngine;

namespace TwistedSpace.Demo
{
    [DisallowMultipleComponent]
    public class SlidingExitDoor : MonoBehaviour
    {
        [SerializeField, Tooltip("Moving door mesh/root. Its physical collider remains enabled.")]
        private Transform doorLeaf;
        [SerializeField, Tooltip("Stationary sibling of doorLeaf defining its closed world pose.")]
        private Transform closedPose;
        [SerializeField, Tooltip("Stationary sibling of doorLeaf defining its open world pose.")]
        private Transform openPose;
        [SerializeField, Min(0f), Tooltip("Unscaled seconds per move, including a reversal from the current pose.")]
        private float moveSeconds = 1.2f;

        /// <summary>The requested destination, not whether the movement has finished.</summary>
        public bool IsOpen { get; private set; }

        private Coroutine moveRoutine;
        private bool started;

        private void Start()
        {
            started = true;
            if (!ValidateSetup())
            {
                enabled = false;
                return;
            }
            MoveToTarget();
        }

        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            if (!ValidateSetup())
                return;
            bool unchanged = IsOpen == open;
            IsOpen = open;
            if (!isActiveAndEnabled || (unchanged && moveRoutine != null))
                return;
            MoveToTarget();
        }

        private void MoveToTarget()
        {
            StopMovement();
            Transform destination = IsOpen ? openPose : closedPose;
            if (moveSeconds <= 0f)
            {
                doorLeaf.SetPositionAndRotation(destination.position, destination.rotation);
                return;
            }
            moveRoutine = StartCoroutine(Move(destination.position, destination.rotation));
        }

        private IEnumerator Move(Vector3 endPosition, Quaternion endRotation)
        {
            Vector3 startPosition = doorLeaf.position;
            Quaternion startRotation = doorLeaf.rotation;
            float duration = moveSeconds;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // Yield first so even a one-frame move clears its assigned coroutine handle.
                yield return null;
                if (!doorLeaf)
                {
                    Debug.LogError("SlidingExitDoor: doorLeaf was destroyed during movement.", this);
                    moveRoutine = null;
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = t * t * (3f - 2f * t);
                doorLeaf.SetPositionAndRotation(Vector3.Lerp(startPosition, endPosition, smooth),
                    Quaternion.Slerp(startRotation, endRotation, smooth));
            }
            moveRoutine = null;
        }

        private bool ValidateSetup()
        {
            if (!doorLeaf || !closedPose || !openPose ||
                doorLeaf == closedPose || doorLeaf == openPose || closedPose == openPose ||
                closedPose.parent != doorLeaf.parent || openPose.parent != doorLeaf.parent ||
                float.IsNaN(moveSeconds) || float.IsInfinity(moveSeconds) || moveSeconds < 0f)
            {
                Debug.LogError("SlidingExitDoor: assign a doorLeaf and two distinct stationary sibling pose " +
                    "markers (not children of the moving leaf), with a finite nonnegative moveSeconds.", this);
                return false;
            }
            return true;
        }

        private void OnEnable()
        {
            if (started && ValidateSetup())
                MoveToTarget();
        }

        private void OnDisable() => StopMovement();

        private void StopMovement()
        {
            if (moveRoutine == null)
                return;
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
    }
}
