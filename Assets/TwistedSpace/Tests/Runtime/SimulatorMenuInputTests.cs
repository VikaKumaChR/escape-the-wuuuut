using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TwistedSpace.Demo;
using TwistedSpace.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace TwistedSpace.Tests
{
    // Keep the real simulator's serialized properties without creating global simulated devices.
    public sealed class MenuInputTestSimulator : XRDeviceSimulator
    {
        protected override void Awake() { }
        protected override void OnEnable() { }
        protected override void OnDisable() { }
        protected override void Start() { }
        protected override void Update() { }
    }

    public sealed class SimulatorMenuInputTests
    {
        private const double MouseDelay = 0.15;
        private GameObject root;
        private EventSystem eventSystem;
        private EventSystem previousEventSystem;
        private VRWorldSpacePanel panel;
        private ConfirmedXRButton button;
        private float previousTimeScale;
        private int clickCount;
        private double invokedAt;
        private float invocationFrameDuration;
        private readonly List<InputActionAsset> actionAssets = new List<InputActionAsset>();
        private readonly List<InputActionReference> actionReferences = new List<InputActionReference>();
        private readonly List<UnityEngine.InputSystem.InputDevice> devices =
            new List<UnityEngine.InputSystem.InputDevice>();
        private SimulatorMenuInput bridge;
        private VerticalSliceFlow flow;
        private MenuInputTestSimulator simulator;
        private XRUIInputModule module;
        private InputActionAsset[] simulatorAssets;
        private InputAction trackingAction;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            previousEventSystem = EventSystem.current;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Time.timeScale = 1f;
            clickCount = 0;
            invokedAt = -1d;
            invocationFrameDuration = 0f;

            root = new GameObject("Simulator Menu Input Test");
            root.SetActive(false);
            GameObject cameraObject = new GameObject("Head", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            Camera head = cameraObject.GetComponent<Camera>();
            head.enabled = false;
            GameObject eventObject = new GameObject("Events", typeof(EventSystem));
            eventObject.transform.SetParent(root.transform, false);
            eventSystem = eventObject.GetComponent<EventSystem>();

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Canvas));
            panelObject.transform.SetParent(root.transform, false);
            Canvas canvas = panelObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            panel = panelObject.AddComponent<VRWorldSpacePanel>();
            SetField(panel, "fadeDuration", 0.05f);
            panel.FollowHead = false;

            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image));
            buttonObject.transform.SetParent(panelObject.transform, false);
            button = buttonObject.AddComponent<ConfirmedXRButton>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() =>
            {
                clickCount++;
                invokedAt = Time.unscaledTimeAsDouble;
                invocationFrameDuration = Time.unscaledDeltaTime;
            });
            root.SetActive(true);
            Assert.That(panel.IsInteractionReady, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            if (bridge != null)
                bridge.enabled = false;
            if (simulator != null)
                simulator.mouseDeltaAction = null;
            if (root != null)
                Object.DestroyImmediate(root);
            foreach (InputActionReference reference in actionReferences)
                Object.DestroyImmediate(reference);
            actionReferences.Clear();
            foreach (InputActionAsset asset in actionAssets)
                Object.DestroyImmediate(asset);
            actionAssets.Clear();
            foreach (UnityEngine.InputSystem.InputDevice device in devices)
                if (device.added)
                    InputSystem.RemoveDevice(device);
            devices.Clear();
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            EventSystem.current = previousEventSystem;
        }

        [UnityTest]
        public IEnumerator MouseIsRejectedByDefaultEvenAfterWaiting()
        {
            Assert.That(button.AllowDesktopMouseInput, Is.False);
            Click(Mouse());
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator OptInShortClickUsesFixedUnscaledDelayAndIgnoresPendingDuplicates()
        {
            button.AllowDesktopMouseInput = true;
            SetField(button, "confirmationHoldSeconds", 5f);
            Time.timeScale = 0f;
            PointerEventData pointer = Mouse();
            double clickedAt = Time.unscaledTimeAsDouble;
            Click(pointer);
            Assert.That(clickCount, Is.Zero, "A short mouse click must not invoke synchronously.");
            Assert.That(button.IsConfirming, Is.True);
            Assert.That(button.ConfirmationProgress, Is.EqualTo(0f).Within(0.001f));

            yield return AwaitMouseConfirmation(clickedAt, pointer);
            button.OnPointerClick(pointer);
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(clickCount, Is.EqualTo(1));
            AssertIdle();
        }

        [UnityTest]
        public IEnumerator MouseDelayStartsAtClickNotAtPointerDown()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(clickCount, Is.Zero, "Holding a mouse button must not invoke onClick.");
            button.OnPointerUp(pointer);
            Assert.That(clickCount, Is.Zero);
            double clickedAt = Time.unscaledTimeAsDouble;
            button.OnPointerClick(pointer);
            Assert.That(button.IsConfirming, Is.True);
            Assert.That(button.ConfirmationProgress, Is.EqualTo(0f).Within(0.001f));
            yield return AwaitMouseConfirmation(clickedAt);
        }

        [UnityTest]
        public IEnumerator MouseRequiresDownUpClickSequence()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            button.OnPointerClick(pointer);
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            yield return AssertNoDelayedClick();

            button.OnPointerDown(pointer);
            button.OnPointerClick(pointer);
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator MouseReleaseWithoutClickDoesNotConfirm()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            button.OnPointerDown(pointer);
            button.OnPointerUp(pointer);
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator PointerExitAfterMouseClickDoesNotCancelDelay()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            double clickedAt = Time.unscaledTimeAsDouble;
            Click(pointer);
            pointer.pointerCurrentRaycast = new RaycastResult();
            button.OnPointerExit(pointer);
            Assert.That(button.IsConfirming, Is.True);
            yield return AwaitMouseConfirmation(clickedAt);
        }

        [UnityTest]
        public IEnumerator PointerExitBeforeMouseReleaseCancelsEvenAfterReentry()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            button.OnPointerDown(pointer);
            button.OnPointerExit(pointer);
            button.OnPointerEnter(pointer);
            Release(pointer);
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator DefaultXrQuickReleaseAndSubmitAreRejected()
        {
            Click(Tracked());
            button.OnSubmit(new BaseEventData(eventSystem));
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator DefaultXrHoldUsesUnscaledTimeAndInvokesOnceOnlyOnRelease()
        {
            Time.timeScale = 0f;
            PointerEventData pointer = Tracked();
            button.OnPointerDown(pointer);
            Assert.That(button.IsConfirming, Is.True);
            yield return new WaitForSecondsRealtime(0.18f);
            Assert.That(button.ConfirmationProgress, Is.EqualTo(1f));
            Assert.That(clickCount, Is.Zero);
            Release(pointer);
            Assert.That(clickCount, Is.EqualTo(1), "XR confirmation must remain immediate on release-click.");
            button.OnPointerClick(pointer);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(clickCount, Is.EqualTo(1));
            AssertIdle();
        }

        [UnityTest]
        public IEnumerator DesktopModeRejectsSubmitTouchTrackedAndNonLeftMouse()
        {
            button.AllowDesktopMouseInput = true;
            Assert.That(eventSystem.currentInputModule, Is.Null);
            button.OnSubmit(new BaseEventData(eventSystem));
            Click(Mouse(0));
            Click(Mouse(1));
            Click(Mouse(10));
            Click(Mouse(-2, PointerEventData.InputButton.Right));
            Click(Mouse(-1, PointerEventData.InputButton.Right));
            Click(Mouse(-3, PointerEventData.InputButton.Middle));

            // Even a tracked pointer using the legacy mouse ID must not count as a mouse.
            PointerEventData tracked = Tracked(-1);
            button.OnPointerDown(tracked);
            AssertIdle();
            yield return new WaitForSecondsRealtime(0.18f);
            Release(tracked);
            tracked = Tracked();
            button.OnPointerDown(tracked);
            yield return new WaitForSecondsRealtime(0.18f);
            Release(tracked);
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator ChangingModeCancelsPendingMouseClickEvenWhenImmediatelyRestored()
        {
            yield return AssertMouseCancellation(() =>
            {
                button.AllowDesktopMouseInput = false;
                button.AllowDesktopMouseInput = true;
            });
        }

        [UnityTest]
        public IEnumerator ChangingModeCancelsMousePressAndUnconsumedRelease()
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            button.OnPointerDown(pointer);
            button.AllowDesktopMouseInput = false;
            button.AllowDesktopMouseInput = true;
            Release(pointer);
            yield return AssertNoDelayedClick();

            button.OnPointerDown(pointer);
            button.OnPointerUp(pointer);
            button.AllowDesktopMouseInput = false;
            button.AllowDesktopMouseInput = true;
            button.OnPointerClick(pointer);
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator ChangingModeCancelsXrHoldAndUnconsumedRelease()
        {
            PointerEventData pointer = Tracked();
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            button.AllowDesktopMouseInput = true;
            button.AllowDesktopMouseInput = false;
            Release(pointer);
            AssertIdle();
            Assert.That(clickCount, Is.Zero);

            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            button.OnPointerUp(pointer);
            button.AllowDesktopMouseInput = true;
            button.AllowDesktopMouseInput = false;
            button.OnPointerClick(pointer);
            AssertIdle();
            yield return AssertNoDelayedClick();
        }

        [UnityTest]
        public IEnumerator DisablingButtonCancelsPendingMouseClick()
        {
            yield return AssertMouseCancellation(() =>
            {
                button.enabled = false;
                button.enabled = true;
            });
        }

        [UnityTest]
        public IEnumerator DeactivatingButtonCancelsPendingMouseClick()
        {
            yield return AssertMouseCancellation(() =>
            {
                button.gameObject.SetActive(false);
                button.gameObject.SetActive(true);
            });
        }

        [UnityTest]
        public IEnumerator InteractableToggleCancelsPendingMouseClick()
        {
            yield return AssertMouseCancellation(() =>
            {
                button.interactable = false;
                button.interactable = true;
            });
        }

        [UnityTest]
        public IEnumerator FadeOutCancelsPendingMouseClickAndHiddenPanelRejectsInput()
        {
            Time.timeScale = 0f;
            yield return AssertMouseCancellation(() => panel.FadeOut());
            Assert.That(panel.IsInteractionReady, Is.False);
            Assert.That(panel.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Click(Mouse());
            yield return AssertNoDelayedClick();

            panel.FadeIn();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(panel.IsInteractionReady, Is.True);
            yield return AssertNoDelayedClick();
            double clickedAt = Time.unscaledTimeAsDouble;
            Click(Mouse());
            yield return AwaitMouseConfirmation(clickedAt);
        }

        [UnityTest]
        public IEnumerator DisablingPanelCancelsPendingMouseClick()
        {
            yield return AssertMouseCancellation(() =>
            {
                panel.enabled = false;
                panel.enabled = true;
                panel.FadeIn();
            });
            Assert.That(panel.IsInteractionReady, Is.True);
        }

        [UnityTest]
        public IEnumerator XriPointerZeroUsesBuiltInMouseSourceAndDelayedConfirmation()
        {
            CreateMouseModule();
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse(0);
            Assert.That(pointer.currentInputModule, Is.SameAs(module));
            double clickedAt = Time.unscaledTimeAsDouble;
            Click(pointer);
            Assert.That(clickCount, Is.Zero);
            Assert.That(button.IsConfirming, Is.True);
            yield return AwaitMouseConfirmation(clickedAt);
        }

        [TestCase(true, true, true, true)]
        [TestCase(false, false, true, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, true, true, false)]
        public void XriPointerZeroRejectsAmbiguousOrDisabledMouseSource(
            bool touch, bool mouse, bool fallback, bool desktop)
        {
            CreateMouseModule();
            module.enableTouchInput = touch;
            module.enableMouseInput = mouse;
            module.enableBuiltinActionsAsFallback = fallback;
            button.AllowDesktopMouseInput = desktop;
            Click(Mouse(0));
            AssertIdle();
            Assert.That(clickCount, Is.Zero);
        }

        [Test]
        public void XriPointerZeroRejectsActionReferencesThatDisableBuiltInFallback()
        {
            CreateMouseModule();
            InputActionAsset asset = CreateControlAsset("Navigation", "<Keyboard>/enter");
            module.submitAction = CreateReference(asset.FindAction("Enabled", true));
            button.AllowDesktopMouseInput = true;
            Click(Mouse(0));
            AssertIdle();
            Assert.That(clickCount, Is.Zero);
        }

        [TestCase(DemoState.Playing, false)]
        [TestCase(DemoState.ResetConfirmation, false)]
        [TestCase(DemoState.Solved, false)]
        [TestCase(DemoState.MainMenu, true)]
        public void BridgeRestoresExactActionSetUiFlagsAndCursorOnExit(DemoState nextState, bool disable)
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            // Read back effective cursor values: headless hosts may ignore lock/visibility requests.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            CursorLockMode desktopLock = Cursor.lockState;
            bool desktopVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            CursorLockMode savedLock = Cursor.lockState;
            bool savedVisible = Cursor.visible;

            RefreshBridge();
            AssertDesktopMenu();
            Assert.That(Cursor.lockState, Is.EqualTo(desktopLock));
            Assert.That(Cursor.visible, Is.EqualTo(desktopVisible));
            AssertSimulatorActions(false);
            Assert.That(trackingAction.enabled, Is.True, "Tracking actions are not simulator controls.");

            // Repeated refresh must not replace the entry snapshot with the now-disabled set.
            simulatorAssets[1].FindAction("Enabled", true).Enable();
            RefreshBridge();
            AssertSimulatorActions(false);
            Click(Mouse(0));
            Assert.That(button.IsConfirming, Is.True);

            SetField(flow, "<State>k__BackingField", nextState);
            if (disable)
                bridge.enabled = false;
            else
                RefreshBridge();

            Assert.That(bridge.IsDesktopMenuActive, Is.False);
            Assert.That(button.AllowDesktopMouseInput, Is.False);
            AssertIdle();
            Assert.That(clickCount, Is.Zero);
            AssertSimulatorActions(true);
            Assert.That(trackingAction.enabled, Is.True);
            Assert.That(simulator.isActiveAndEnabled, Is.True);
            Assert.That(ModuleFlags(), Is.EqualTo(flags));
            Assert.That(Cursor.lockState, Is.EqualTo(savedLock));
            Assert.That(Cursor.visible, Is.EqualTo(savedVisible));
            RefreshBridge();
            AssertSimulatorActions(true);
        }

        [Test]
        public void BridgeReentryTakesFreshSnapshotOfPreviouslyEnabledActions()
        {
            CreateBridge();
            RefreshBridge();
            bridge.enabled = false;
            foreach (InputActionAsset asset in simulatorAssets)
            {
                asset.FindAction("Enabled", true).Disable();
                asset.FindAction("Disabled", true).Enable();
            }

            bridge.enabled = true;
            AssertDesktopMenu();
            foreach (InputActionAsset asset in simulatorAssets)
                foreach (InputAction action in asset)
                    Assert.That(action.enabled, Is.False);
            bridge.enabled = false;
            foreach (InputActionAsset asset in simulatorAssets)
            {
                Assert.That(asset.FindAction("Enabled", true).enabled, Is.False);
                Assert.That(asset.FindAction("Disabled", true).enabled, Is.True);
            }
        }

        [TestCase("notStarted")]
        [TestCase("playing")]
        [TestCase("simulatorDisabled")]
        [TestCase("flowDisabled")]
        [TestCase("panelDisabled")]
        public void BridgeDoesNotTakeOwnershipWhenGuardFails(string guard)
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            switch (guard)
            {
                case "notStarted": SetField(bridge, "started", false); break;
                case "playing": SetField(flow, "<State>k__BackingField", DemoState.Playing); break;
                case "simulatorDisabled": simulator.enabled = false; break;
                case "flowDisabled": flow.enabled = false; break;
                case "panelDisabled": panel.enabled = false; break;
            }
            RefreshBridge();
            Assert.That(bridge.IsDesktopMenuActive, Is.False);
            Assert.That(bridge.enabled, Is.True, "An inactive mode is not a configuration error.");
            Assert.That(button.AllowDesktopMouseInput, Is.False);
            AssertSimulatorActions(true);
            Assert.That(ModuleFlags(), Is.EqualTo(flags));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PhysicalHmdIncludingDisabledDeviceReleasesDesktopOwnership(bool disabled)
        {
            CreateBridge();
            RefreshBridge();
            AssertDesktopMenu();
            XRHMD hmd = InputSystem.AddDevice<XRHMD>();
            devices.Add(hmd);
            if (disabled)
                InputSystem.DisableDevice(hmd);
            RefreshBridge();
            Assert.That(bridge.IsDesktopMenuActive, Is.False);
            Assert.That(button.AllowDesktopMouseInput, Is.False);
            AssertSimulatorActions(true);
            Assert.That(simulator.isActiveAndEnabled, Is.True);
            RefreshBridge();
            Assert.That(bridge.IsDesktopMenuActive, Is.False);
        }

        [Test]
        public void MissingAssignedSimulatorFailsLoudlyAndRestoresExistingOwnership()
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            RefreshBridge();
            AssertDesktopMenu();
            SetField(bridge, "deviceSimulator", null);
            ExpectSetupError("assign flow, deviceSimulator, uiInputModule and mainMenuPanel.");
            RefreshBridge();
            AssertFailedSetup(flags);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MissingAnyAssignedSimulatorAssetFailsLoudly(int missingIndex)
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            var supplied = new List<InputActionAsset>(simulatorAssets);
            supplied.RemoveAt(missingIndex);
            SetField(bridge, "simulatorActionAssets", supplied.ToArray());
            ExpectSetupError("supply all assigned XR Device Simulator control assets");
            RefreshBridge();
            AssertFailedSetup(flags);
        }

        [Test]
        public void MissingMouseDeltaReferenceFailsLoudly()
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            simulator.mouseDeltaAction = null;
            ExpectSetupError("supply all assigned XR Device Simulator control assets");
            RefreshBridge();
            AssertFailedSetup(flags);
        }

        [Test]
        public void NonKeyboardMouseBindingInAssignedAssetFailsLoudlyWithoutDisablingActions()
        {
            CreateBridge();
            bool[] flags = ModuleFlags();
            simulatorAssets[1].FindAction("Disabled", true).AddBinding("<XRController>/trigger");
            ExpectSetupError("simulator control assets must contain only keyboard/mouse bindings");
            RefreshBridge();
            AssertFailedSetup(flags);
        }

        private void CreateMouseModule()
        {
            module = eventSystem.gameObject.AddComponent<XRUIInputModule>();
            module.enableBuiltinActionsAsFallback = true;
            module.enableMouseInput = true;
            module.enableTouchInput = false;
            module.enableXRInput = false;
            module.enableGamepadInput = false;
            module.enableJoystickInput = false;
            Assert.That(new[] { module.pointAction, module.leftClickAction, module.rightClickAction,
                module.middleClickAction, module.scrollWheelAction, module.navigateAction,
                module.submitAction, module.cancelAction }, Is.All.Null);
            // Use XRI's actual module provenance; its shared mouse/touch PointerModel starts at ID 0.
            SetField(eventSystem, "m_CurrentInputModule", module);
        }

        private void CreateBridge()
        {
            if (XRSettings.isDeviceActive)
                Assert.Ignore("Desktop bridge requires no active physical XR backend.");
            foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
                if (device is XRHMD && !(device is XRSimulatedHMD))
                    Assert.Ignore("Desktop bridge requires no pre-existing physical HMD.");

            CreateMouseModule();
            module.enableXRInput = true;
            module.enableMouseInput = false;
            module.enableTouchInput = true;
            module.enableGamepadInput = true;
            module.enableJoystickInput = false;
            panel.gameObject.AddComponent<GraphicRaycaster>();
            flow = root.AddComponent<VerticalSliceFlow>();
            simulator = root.AddComponent<MenuInputTestSimulator>();
            simulatorAssets = new[]
            {
                CreateControlAsset("Device", "<Mouse>/delta"),
                CreateControlAsset("Controller", "<Keyboard>/space"),
                CreateControlAsset("Hand", "<Mouse>/leftButton")
            };
            simulator.deviceSimulatorActionAsset = simulatorAssets[0];
            simulator.controllerActionAsset = simulatorAssets[1];
            simulator.handActionAsset = simulatorAssets[2];
            simulator.mouseDeltaAction = CreateReference(simulatorAssets[0].FindAction("Enabled", true));
            trackingAction = CreateControlAsset("Tracking", "<XRHMD>/devicePosition")
                .FindAction("Enabled", true);
            bridge = root.AddComponent<SimulatorMenuInput>();
            SetField(bridge, "flow", flow);
            SetField(bridge, "deviceSimulator", simulator);
            SetField(bridge, "uiInputModule", module);
            SetField(bridge, "mainMenuPanel", panel);
            SetField(bridge, "simulatorActionAssets", simulatorAssets);
            // Bridge tests never yield: the real flow Start requires a complete puzzle scene.
            SetField(bridge, "started", true);
            SetField(flow, "<State>k__BackingField", DemoState.MainMenu);
        }

        private InputActionAsset CreateControlAsset(string name, string binding)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            actionAssets.Add(asset);
            asset.name = name;
            InputActionMap map = asset.AddActionMap(name);
            InputAction enabledAction = map.AddAction("Enabled", InputActionType.Value, binding);
            map.AddAction("Disabled", InputActionType.Button, "<Keyboard>/escape");
            enabledAction.Enable();
            return asset;
        }

        private InputActionReference CreateReference(InputAction action)
        {
            InputActionReference reference = InputActionReference.Create(action);
            actionReferences.Add(reference);
            return reference;
        }

        private void RefreshBridge()
        {
            MethodInfo refresh = typeof(SimulatorMenuInput).GetMethod("RefreshInputMode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            refresh.Invoke(bridge, null);
        }

        private bool[] ModuleFlags() => new[] { module.enableXRInput, module.enableMouseInput,
            module.enableTouchInput, module.enableGamepadInput, module.enableJoystickInput };

        private void AssertDesktopMenu()
        {
            Assert.That(bridge.IsDesktopMenuActive, Is.True);
            Assert.That(button.AllowDesktopMouseInput, Is.True);
            Assert.That(simulator.isActiveAndEnabled, Is.True, "Keep simulated pose publishing alive.");
            Assert.That(ModuleFlags(), Is.EqualTo(new[] { false, true, false, false, false }));
        }

        private void AssertSimulatorActions(bool restored)
        {
            foreach (InputActionAsset asset in simulatorAssets)
            {
                Assert.That(asset.FindAction("Enabled", true).enabled, Is.EqualTo(restored), asset.name);
                Assert.That(asset.FindAction("Disabled", true).enabled, Is.False,
                    asset.name + ": previously disabled actions must stay disabled.");
            }
        }

        private static void ExpectSetupError(string message)
        {
            LogAssert.Expect(LogType.Error, new Regex("^SimulatorMenuInput: " + Regex.Escape(message)));
        }

        private void AssertFailedSetup(bool[] flags)
        {
            Assert.That(bridge.enabled, Is.False);
            Assert.That(bridge.IsDesktopMenuActive, Is.False);
            Assert.That(button.AllowDesktopMouseInput, Is.False);
            Assert.That(simulator.isActiveAndEnabled, Is.True);
            AssertSimulatorActions(true);
            Assert.That(trackingAction.enabled, Is.True);
            Assert.That(ModuleFlags(), Is.EqualTo(flags));
        }

        private IEnumerator AssertMouseCancellation(Action cancel)
        {
            button.AllowDesktopMouseInput = true;
            PointerEventData pointer = Mouse();
            Click(pointer);
            Assert.That(button.IsConfirming, Is.True);
            cancel();
            AssertIdle();
            button.OnPointerClick(pointer);
            yield return AssertNoDelayedClick();
            AssertIdle();
        }

        private IEnumerator AssertNoDelayedClick()
        {
            Assert.That(clickCount, Is.Zero);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(clickCount, Is.Zero);
        }

        private IEnumerator AwaitMouseConfirmation(double clickedAt, PointerEventData duplicate = null)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 3d;
            while (clickCount == 0 && Time.realtimeSinceStartupAsDouble < deadline)
            {
                double elapsed = Time.unscaledTimeAsDouble - clickedAt;
                Assert.That(button.IsConfirming, Is.True);
                Assert.That(button.ConfirmationProgress,
                    Is.EqualTo(Mathf.Clamp01((float)(elapsed / MouseDelay))).Within(0.02f));
                if (duplicate != null)
                {
                    button.OnPointerClick(duplicate);
                    Click(duplicate);
                }
                yield return null;
                if (Time.unscaledTimeAsDouble - clickedAt < MouseDelay - 0.001d)
                    Assert.That(clickCount, Is.Zero, "Confirmation fired before the 0.15s click delay.");
            }

            Assert.That(clickCount, Is.EqualTo(1), "Mouse confirmation did not complete exactly once.");
            Assert.That(invokedAt - clickedAt, Is.GreaterThanOrEqualTo(MouseDelay - 0.001d));
            // Coroutines are frame-quantized; allow scheduling slack, not the configured XR hold time.
            double frameAllowance = Math.Max(0.05d, invocationFrameDuration * 2d);
            Assert.That(invokedAt - clickedAt, Is.LessThanOrEqualTo(MouseDelay + frameAllowance));
            AssertIdle();
        }

        private void AssertIdle()
        {
            Assert.That(button.IsConfirming, Is.False);
            Assert.That(button.ConfirmationProgress, Is.Zero);
        }

        private PointerEventData Mouse(int id = -1,
            PointerEventData.InputButton inputButton = PointerEventData.InputButton.Left)
        {
            return ConfigurePointer(new PointerEventData(eventSystem), id, inputButton);
        }

        private TrackedDeviceEventData Tracked(int id = 1)
        {
            TrackedDeviceEventData pointer = new TrackedDeviceEventData(eventSystem);
            ConfigurePointer(pointer, id, PointerEventData.InputButton.Left);
            return pointer;
        }

        private PointerEventData ConfigurePointer(PointerEventData pointer, int id,
            PointerEventData.InputButton inputButton)
        {
            pointer.pointerId = id;
            pointer.button = inputButton;
            pointer.eligibleForClick = true;
            pointer.pointerCurrentRaycast = new RaycastResult { gameObject = button.gameObject };
            return pointer;
        }

        private void Click(PointerEventData pointer)
        {
            button.OnPointerDown(pointer);
            Release(pointer);
        }

        private void Release(PointerEventData pointer)
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
