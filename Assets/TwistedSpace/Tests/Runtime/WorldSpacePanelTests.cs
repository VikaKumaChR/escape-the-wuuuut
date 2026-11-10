using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TwistedSpace.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.Tests
{
    /// <summary>Exercises XRI's Down -> Up -> Click sequence, cancellation and unscaled fades.</summary>
    public sealed class WorldSpacePanelTests
    {
        private GameObject root;
        private Camera head;
        private Canvas canvas;
        private VRWorldSpacePanel panel;
        private ConfirmedXRButton button;
        private EventSystem eventSystem;
        private int clickCount;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Panel Test");
            root.SetActive(false);
            GameObject cameraObject = new GameObject("Head", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            head = cameraObject.GetComponent<Camera>();
            head.enabled = false;
            GameObject eventObject = new GameObject("Events", typeof(EventSystem));
            eventObject.transform.SetParent(root.transform, false);
            eventSystem = eventObject.GetComponent<EventSystem>();
            GameObject canvasObject = new GameObject("Panel", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(root.transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            panel = canvasObject.AddComponent<VRWorldSpacePanel>();
            SetField(panel, "fadeDuration", 0.05f);
            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image));
            buttonObject.transform.SetParent(canvas.transform, false);
            button = buttonObject.AddComponent<ConfirmedXRButton>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() => clickCount++);
            clickCount = 0;
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(root);
        }

        [Test]
        public void QuickReleaseAndSubmitCannotBypassConfirmation()
        {
            TrackedDeviceEventData pointer = Pointer(1);
            button.OnPointerDown(pointer);
            Release(pointer);
            button.OnSubmit(new BaseEventData(eventSystem));
            button.OnPointerClick(pointer);
            Assert.That(clickCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HeldTriggerInvokesOnlyOnReleaseAndOnlyOnce()
        {
            TrackedDeviceEventData pointer = Pointer(1);
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            Assert.That(clickCount, Is.Zero);
            Assert.That(button.ConfirmationProgress, Is.EqualTo(1f));
            Release(pointer);
            button.OnPointerClick(pointer);
            Assert.That(clickCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PointerExitAndOtherHandCannotCompleteTheHold()
        {
            TrackedDeviceEventData owner = Pointer(1);
            button.OnPointerDown(owner);
            yield return new WaitForSecondsRealtime(0.18f);
            Release(Pointer(2));
            Assert.That(button.IsConfirming, Is.True);
            button.OnPointerExit(owner);
            button.OnPointerEnter(owner);
            Release(owner);
            Assert.That(clickCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InteractionDisableCancelsEvenWhenReenabledInTheSameFrame()
        {
            TrackedDeviceEventData pointer = Pointer(1);
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            button.interactable = false;
            button.interactable = true;
            Release(pointer);
            Assert.That(clickCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FadeCancelsHoldAndCanBeReversedWhileTimeIsPaused()
        {
            TrackedDeviceEventData pointer = Pointer(1);
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            Time.timeScale = 0f;
            panel.FadeOut();
            Assert.That(panel.IsInteractionReady, Is.False);
            Release(pointer);
            Assert.That(clickCount, Is.Zero);
            yield return null;
            panel.FadeIn();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(panel.IsInteractionReady, Is.True);
            Assert.That(canvas.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator DisableHidesPanelAndExplicitFadeRestoresInteraction()
        {
            panel.enabled = false;
            Assert.That(canvas.GetComponent<CanvasGroup>().alpha, Is.Zero);
            panel.enabled = true;
            Assert.That(panel.IsInteractionReady, Is.False);
            panel.FadeIn();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(panel.IsInteractionReady, Is.True);
        }

        [UnityTest]
        public IEnumerator FollowStartsInFrontThenDampsMotionWithoutRollOrPitchFlip()
        {
            yield return null;
            yield return null;
            Vector3 startingPosition = canvas.transform.position;
            Assert.That(startingPosition.z, Is.EqualTo(1.5f).Within(0.01f));
            head.transform.position = Vector3.right * 2f;
            head.transform.rotation = Quaternion.Euler(85f, 90f, 40f);
            yield return null;
            Assert.That(Vector3.Distance(canvas.transform.position, startingPosition), Is.LessThan(1f));
            float pitch = Mathf.DeltaAngle(0f, canvas.transform.eulerAngles.x);
            float roll = Mathf.DeltaAngle(0f, canvas.transform.eulerAngles.z);
            Assert.That(Mathf.Abs(pitch), Is.LessThanOrEqualTo(25.01f));
            Assert.That(Mathf.Abs(roll), Is.LessThan(0.01f));
        }

        private TrackedDeviceEventData Pointer(int id)
        {
            return new TrackedDeviceEventData(eventSystem)
            {
                pointerId = id,
                button = PointerEventData.InputButton.Left,
                eligibleForClick = true,
                pointerCurrentRaycast = new RaycastResult { gameObject = button.gameObject }
            };
        }

        private void Release(TrackedDeviceEventData pointer)
        {
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing serialized field: " + name);
            field.SetValue(target, value);
        }
    }
}
