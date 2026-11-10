using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DepthLab
{
    public abstract class LabChapter : DepthLabChapter
    {
        public TMP_Text objectiveLabel;
        public TMP_Text hintLabel;
        public TMP_Text progressLabel;
        public Button exitButton;
        public Renderer exitRenderer;
        public Material lockedExitMaterial;
        public Material openExitMaterial;
        public Button[] interactionButtons;
        public XRGrabInteractable[] interactables;
        public DepthLabExitDoor exitDoor;

        protected abstract bool PuzzleSolved { get; }
        public bool ExitReady => PuzzleSolved;
        public bool InteractionAllowed => CanInteract && !requestedPause && isActiveAndEnabled;

        private bool exited;
        private bool requestedPause;
        private bool frozen;
        private BodyState[] bodyStates;

        private struct BodyState
        {
            public Rigidbody body;
            public bool kinematic;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        protected virtual void Update()
        {
            ApplyPause(!InteractionAllowed);
            RefreshStatus();
        }

        protected virtual void LateUpdate()
        {
            if (frozen) FreezeBodies();
        }

        public override void SetPaused(bool paused)
        {
            requestedPause = paused;
            if (!InteractionAllowed && exitDoor != null) exitDoor.ResetCrossing();
            ApplyPause(!InteractionAllowed);
            RefreshStatus();
        }

        protected void ResetExit()
        {
            exited = false;
            if (exitDoor != null) exitDoor.CloseImmediate();
            SetPaused(false);
        }

        public void UseExit()
        {
            if (!InteractionAllowed || !PuzzleSolved || exited)
                return;
            exited = true;
            DepthLabApp.Instance.ReportTutorialAction("exit");
            Complete();
        }

        public void WalkThroughExit()
        {
            if (!InteractionAllowed || !PuzzleSolved || exited || exitDoor == null || !exitDoor.IsOpen)
                return;
            exited = true;
            DepthLabApp.Instance.ReportTutorialAction("exit");
            DepthLabApp.Instance.CompleteChapterFromExit(this);
        }

        protected void RefreshStatus()
        {
            if (objectiveLabel != null) objectiveLabel.text = Objective;
            if (hintLabel != null) hintLabel.text = Hint;
            if (progressLabel != null) progressLabel.text = $"{Progress:00} / {Goal:00}";
            if (exitButton != null)
                exitButton.interactable = InteractionAllowed && PuzzleSolved && !exited;
            if (exitRenderer != null)
                exitRenderer.sharedMaterial = PuzzleSolved ? openExitMaterial : lockedExitMaterial;
        }

        protected virtual bool IsGrabAvailable(XRGrabInteractable grab)
        {
            ChapterPiece piece = grab.GetComponent<ChapterPiece>();
            return piece == null || !piece.IsPlaced;
        }

        private void ApplyPause(bool pause)
        {
            if (interactionButtons != null)
                foreach (Button button in interactionButtons)
                    if (button != null) button.interactable = !pause;

            if (pause != frozen)
            {
                frozen = pause;
                if (pause)
                {
                    int count = interactables == null ? 0 : interactables.Length;
                    bodyStates = new BodyState[count];
                    for (int i = 0; i < count; i++)
                    {
                        XRGrabInteractable grab = interactables[i];
                        if (grab == null) continue;
                        Rigidbody body = grab.GetComponent<Rigidbody>();
                        ChapterPiece piece = grab.GetComponent<ChapterPiece>();
                        bool interruptedGrab = grab.isSelected || (body != null && body.isKinematic && piece != null && !piece.IsPlaced);
                        grab.enabled = false;
                        if (piece != null) piece.SuspendInteraction(interruptedGrab);
                        if (body != null)
                        {
                            bodyStates[i] = new BodyState
                            {
                                body = body, kinematic = body.isKinematic,
                                position = body.position, rotation = body.rotation,
                                velocity = body.linearVelocity, angularVelocity = body.angularVelocity
                            };
                        }
                        if (body != null) body.isKinematic = true;
                    }
                }
                else if (bodyStates != null)
                {
                    foreach (BodyState state in bodyStates)
                    {
                        if (state.body == null) continue;
                        ChapterPiece piece = state.body.GetComponent<ChapterPiece>();
                        state.body.isKinematic = piece != null ? piece.IsPlaced : state.kinematic;
                        if (!state.body.isKinematic)
                        {
                            state.body.linearVelocity = state.velocity;
                            state.body.angularVelocity = state.angularVelocity;
                        }
                    }
                    bodyStates = null;
                }
            }

            if (pause) FreezeBodies();

            if (interactables != null)
                foreach (XRGrabInteractable grab in interactables)
                {
                    if (grab == null) continue;
                    grab.enabled = !pause && IsGrabAvailable(grab);
                }
        }

        private void FreezeBodies()
        {
            if (bodyStates == null) return;
            foreach (BodyState state in bodyStates)
                if (state.body != null)
                {
                    state.body.isKinematic = true;
                    state.body.position = state.position;
                    state.body.rotation = state.rotation;
                }
        }
    }
}
