using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DepthLab
{
    public sealed class ChapterPiece : MonoBehaviour
    {
        public DepthSortingChapter chapter;
        public int identity;
        public Transform home;
        public Rigidbody body;
        public XRGrabInteractable grab;
        public bool IsPlaced { get; private set; }
        public bool IsHeld => held;

        private bool held;
        private bool releasePending;
        private bool moved;
        private bool wasKinematic;
        private float settling;
        private Vector3 lastDrop;
        private Quaternion initialRotation;
        private CollisionDetectionMode? pendingCollisionMode;

        public void OnGrabbed(SelectEnterEventArgs args) => NotifyGrab();
        public void OnReleased(SelectExitEventArgs args) => NotifyRelease();

        public void NotifyGrab()
        {
            if (chapter == null || !chapter.InteractionAllowed || IsPlaced) return;
            held = true;
            moved = true;
            releasePending = false;
            settling = 0f;
            DepthLabApp.Instance.ReportTutorialAction("grab");
        }

        public void NotifyRelease()
        {
            held = false;
            if (chapter == null || !chapter.InteractionAllowed || IsPlaced) return;
            releasePending = true;
        }

        internal void RestoreCollisionModeWhenDynamic(CollisionDetectionMode mode)
        {
            pendingCollisionMode = mode;
        }

        public void ResetPiece()
        {
            IsPlaced = false;
            held = false;
            releasePending = false;
            moved = false;
            settling = 0f;
            if (home == null || body == null) return;
            initialRotation = home.rotation;
            CollisionDetectionMode collisionMode = pendingCollisionMode ?? body.collisionDetectionMode;
            pendingCollisionMode = null;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            transform.SetPositionAndRotation(home.position, initialRotation);
            body.position = home.position;
            body.rotation = initialRotation;
            body.isKinematic = false;
            body.collisionDetectionMode = collisionMode;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            lastDrop = home.position;
            wasKinematic = false;
            if (grab != null) grab.enabled = chapter != null && chapter.InteractionAllowed;
        }

        public void SnapTo(Transform target)
        {
            Vector3 releasePosition = transform.position;
            Quaternion releaseRotation = transform.rotation;
            IsPlaced = true;
            held = false;
            releasePending = false;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            pendingCollisionMode = pendingCollisionMode ?? body.collisionDetectionMode;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            transform.SetPositionAndRotation(target.position, target.rotation);
            body.position = target.position;
            body.rotation = target.rotation;
            if (grab != null) grab.enabled = false;
            if (TryGetComponent<InteractiveSocketVFX>(out var effects))
                effects.NotifySnapped(releasePosition, releaseRotation);
        }

        public void SuspendInteraction(bool interruptedGrab)
        {
            if (IsPlaced) return;
            if (interruptedGrab || held || releasePending || (wasKinematic && moved))
                ResetPiece();
            held = false;
            releasePending = false;
            settling = 0f;
        }

        private void Update()
        {
            if (body != null && !body.isKinematic && pendingCollisionMode.HasValue)
            {
                body.collisionDetectionMode = pendingCollisionMode.Value;
                pendingCollisionMode = null;
            }
            if (chapter == null || !chapter.InteractionAllowed || IsPlaced || body == null) return;
            if (grab != null && grab.isSelected)
            {
                held = true;
                moved = true;
                wasKinematic = body.isKinematic;
                return;
            }
            if (!releasePending && (held || body.isKinematic))
            {
                moved = true;
                wasKinematic = body.isKinematic;
                return;
            }
            if (chapter.IsOutOfBounds(transform.position))
            {
                chapter.ResetOutOfBounds(this);
                return;
            }
            if (releasePending)
            {
                releasePending = false;
                held = false;
                ValidateDrop();
                return;
            }

            // Desktop grabbers often hold a kinematic body without using XR selection.
            if (wasKinematic && !body.isKinematic && moved)
            {
                held = false;
                ValidateDrop();
                return;
            }
            wasKinematic = body.isKinematic;
        }

        private void ValidateDrop()
        {
            moved = false;
            settling = 0f;
            wasKinematic = body.isKinematic;
            chapter.PieceReleased(this);
            lastDrop = transform.position;
        }
    }
}
