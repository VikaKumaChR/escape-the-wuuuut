using System;
using System.Collections.Generic;
using TwistedSpace.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace TwistedSpace.Demo
{
    /// <summary>
    /// Desktop-only main-menu input gate. Keep outside the menu hierarchy so hiding the
    /// panel does not disable this bridge. Assign the simulator's keyboard/mouse control
    /// assets, never the tracked camera/controller action asset. Scene setup owns poses
    /// and the main canvas's ordinary GraphicRaycaster.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-29992)] // Before XRI's simulator Update (-29991), after all startup OnEnables.
    public sealed class SimulatorMenuInput : MonoBehaviour
    {
        [SerializeField] private VerticalSliceFlow flow;
        [SerializeField] private XRDeviceSimulator deviceSimulator;
        [SerializeField] private XRUIInputModule uiInputModule;
        [SerializeField] private VRWorldSpacePanel mainMenuPanel;
        [SerializeField, Tooltip("All assigned simulator control assets (device, controller, hand). Never XRI tracking actions.")]
        private InputActionAsset[] simulatorActionAssets;

        private readonly List<InputAction> suspendedActions = new List<InputAction>();
        private readonly List<ConfirmedXRButton> menuButtons = new List<ConfirmedXRButton>();
        private XRUIInputModule capturedModule;
        private bool previousXRInput;
        private bool previousMouseInput;
        private bool previousTouchInput;
        private bool previousGamepadInput;
        private bool previousJoystickInput;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool started;

        public bool IsDesktopMenuActive { get; private set; }

        private void OnEnable()
        {
            InputSystem.onBeforeUpdate += RefreshInputMode;
            if (started)
                RefreshInputMode();
        }

        private void Start()
        {
            started = true;
            RefreshInputMode();
        }

        private void Update() => RefreshInputMode();

        // Flow callbacks can run in UI Update or a button's confirmation coroutine.
        private void LateUpdate() => RefreshInputMode();

        private void OnDisable()
        {
            InputSystem.onBeforeUpdate -= RefreshInputMode;
            ExitDesktopMenu();
        }

        private void RefreshInputMode()
        {
            if (!started || !isActiveAndEnabled)
                return;
            if (!flow || !deviceSimulator || !uiInputModule || !mainMenuPanel)
            {
                FailSetup("assign flow, deviceSimulator, uiInputModule and mainMenuPanel.");
                return;
            }

            bool desktopMenu = flow.isActiveAndEnabled && flow.State == DemoState.MainMenu &&
                mainMenuPanel.isActiveAndEnabled && deviceSimulator.isActiveAndEnabled &&
                !HasPhysicalHmd();
            if (!desktopMenu)
            {
                ExitDesktopMenu();
                return;
            }

            if (!IsDesktopMenuActive)
            {
                if (!ValidateDesktopSetup())
                    return;
                EnterDesktopMenu();
            }

            // Re-disabling our owned set also covers an InputActionManager re-enabling it.
            // The simulator remains alive and continues publishing its unchanged tracked poses.
            foreach (InputAction action in suspendedActions)
                if (action.enabled)
                    action.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool HasPhysicalHmd()
        {
            if (XRSettings.isDeviceActive)
                return true;
            foreach (InputDevice device in InputSystem.devices)
                if (device is XRHMD && !(device is XRSimulatedHMD))
                    return true; // Disabled physical HMDs also make desktop ownership unsafe.
            return false;
        }

        private bool ValidateDesktopSetup()
        {
            Canvas canvas = mainMenuPanel.GetComponent<Canvas>();
            GraphicRaycaster raycaster = mainMenuPanel.GetComponent<GraphicRaycaster>();
            if (!canvas || canvas.renderMode != RenderMode.WorldSpace || !canvas.worldCamera ||
                !raycaster || !raycaster.isActiveAndEnabled || !uiInputModule.isActiveAndEnabled)
                return FailSetup("desktop menu requires an enabled XRUIInputModule and a World Space " +
                    "main canvas with World Camera and an enabled ordinary GraphicRaycaster.");

            if (!ConfirmedXRButton.HasMouseOnlyPointerSource(uiInputModule))
                return FailSetup("XRUIInputModule needs its built-in New Input System mouse source " +
                    "(no action references), or enabled point/left-click actions bound exclusively to <Mouse>.");

            if (simulatorActionAssets == null || simulatorActionAssets.Length == 0 ||
                !ContainsAsset(deviceSimulator.deviceSimulatorActionAsset) ||
                !ContainsOptionalAsset(deviceSimulator.controllerActionAsset) ||
                !ContainsOptionalAsset(deviceSimulator.handActionAsset) ||
                deviceSimulator.mouseDeltaAction == null ||
                !ContainsAsset(deviceSimulator.mouseDeltaAction.asset))
                return FailSetup("supply all assigned XR Device Simulator control assets, including " +
                    "the mouse-delta asset. Do not supply tracked camera/controller actions.");

            foreach (InputActionAsset asset in simulatorActionAssets)
            {
                if (!asset || (asset != deviceSimulator.deviceSimulatorActionAsset &&
                    asset != deviceSimulator.controllerActionAsset && asset != deviceSimulator.handActionAsset))
                    return FailSetup("simulatorActionAssets may contain only the assigned simulator's " +
                        "device, controller and hand control assets.");

                foreach (InputAction action in asset)
                    foreach (InputBinding binding in action.bindings)
                    {
                        if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath))
                            continue;
                        if (!binding.effectivePath.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) &&
                            !binding.effectivePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
                            return FailSetup("simulator control assets must contain only keyboard/mouse " +
                                "bindings; tracked camera/controller actions must remain enabled.");
                    }
            }

            menuButtons.Clear();
            mainMenuPanel.GetComponentsInChildren(true, menuButtons);
            if (menuButtons.Count == 0)
                return FailSetup("mainMenuPanel must contain at least one ConfirmedXRButton.");
            return true;
        }

        private bool ContainsOptionalAsset(InputActionAsset asset) => !asset || ContainsAsset(asset);

        private bool ContainsAsset(InputActionAsset asset)
        {
            if (!asset)
                return false;
            foreach (InputActionAsset candidate in simulatorActionAssets)
                if (candidate == asset)
                    return true;
            return false;
        }

        private void EnterDesktopMenu()
        {
            capturedModule = uiInputModule;
            previousXRInput = capturedModule.enableXRInput;
            previousMouseInput = capturedModule.enableMouseInput;
            previousTouchInput = capturedModule.enableTouchInput;
            previousGamepadInput = capturedModule.enableGamepadInput;
            previousJoystickInput = capturedModule.enableJoystickInput;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            IsDesktopMenuActive = true;

            // Snapshot before disabling: cancellation callbacks must not alter what we restore.
            suspendedActions.Clear();
            foreach (InputActionAsset asset in simulatorActionAssets)
                foreach (InputAction action in asset)
                    if (action.enabled && !suspendedActions.Contains(action))
                        suspendedActions.Add(action);
            foreach (InputAction action in suspendedActions)
                action.Disable();

            capturedModule.enableXRInput = false;
            capturedModule.enableMouseInput = true;
            capturedModule.enableTouchInput = false;
            capturedModule.enableGamepadInput = false;
            capturedModule.enableJoystickInput = false;
            foreach (ConfirmedXRButton button in menuButtons)
                button.AllowDesktopMouseInput = true;
        }

        private void ExitDesktopMenu()
        {
            if (!IsDesktopMenuActive)
                return;
            IsDesktopMenuActive = false;
            foreach (ConfirmedXRButton button in menuButtons)
                if (button)
                    button.AllowDesktopMouseInput = false;
            menuButtons.Clear();

            if (capturedModule)
            {
                capturedModule.enableXRInput = previousXRInput;
                capturedModule.enableMouseInput = previousMouseInput;
                capturedModule.enableTouchInput = previousTouchInput;
                capturedModule.enableGamepadInput = previousGamepadInput;
                capturedModule.enableJoystickInput = previousJoystickInput;
            }
            capturedModule = null;
            foreach (InputAction action in suspendedActions)
                action.Enable();
            suspendedActions.Clear();
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private bool FailSetup(string reason)
        {
            ExitDesktopMenu();
            Debug.LogError($"{nameof(SimulatorMenuInput)}: {reason}", this);
            enabled = false;
            return false;
        }
    }
}
