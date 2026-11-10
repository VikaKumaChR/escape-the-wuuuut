using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Builds a simulator-ready XRI 3.x rig without legacy controller components.
    /// Import the official simulator sample first, then create the rig in a scene
    /// without another XR Origin, Camera or EventSystem. Scene changes support Undo.
    /// No XR hardware loader is required for keyboard/mouse simulation on Apple Silicon.
    /// </summary>
    public static class TwistedSpaceXRSetup
    {
        private const string PackageName = "com.unity.xr.interaction.toolkit";
        private const string PackageVersion = "3.3.2";
        private const string SimulatorSampleName = "XR Device Simulator";

        /// <summary>Imports the version-matched official simulator assets through Unity's sample API.</summary>
        [MenuItem("Twisted Space/1. Import XR Device Simulator")]
        public static void ImportSimulator()
        {
            Sample sample = GetSimulatorSample();
            if (!sample.isImported && !sample.Import(Sample.ImportOptions.None))
                throw new InvalidOperationException("Unity could not import the XR Device Simulator sample.");

            AssetDatabase.Refresh();
            Debug.Log("XR Device Simulator assets are ready. Create the rig after Unity finishes compiling.");
        }

        /// <summary>
        /// Adds the rig only when existing cameras/input modules cannot conflict with it.
        /// The simulator help overlay is deliberately disabled: all game UI must be World Space.
        /// </summary>
        [MenuItem("Twisted Space/2. Create Simulator XR Rig")]
        public static void CreateRig()
        {
            if (UnityEngine.Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include) != null ||
                UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null ||
                UnityEngine.Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include) != null)
            {
                Debug.LogError("Create the simulator rig in a scene without another Camera, EventSystem or XR Origin. No existing objects were modified.");
                return;
            }

            Sample sample = GetSimulatorSample();
            string simulatorPath = FileUtil.GetProjectRelativePath(sample.importPath + "/XR Device Simulator.prefab");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(simulatorPath);
            if (!sample.isImported || prefab == null)
            {
                Debug.LogError("Run Twisted Space > 1. Import XR Device Simulator and wait for compilation first.");
                return;
            }
            Material rayMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
            if (rayMaterial == null)
                throw new InvalidOperationException("Unity's Default-Line material could not be loaded.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Twisted Space XR rig");
            GameObject root = new GameObject("Twisted Space XR");
            Undo.RegisterCreatedObjectUndo(root, "Create Twisted Space XR rig");
            root.SetActive(false);
            XRInteractionManager manager = root.AddComponent<XRInteractionManager>();
            GameObject originObject = Child("XR Origin", root.transform);
            XROrigin origin = originObject.AddComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 1.6f;
            GameObject offset = Child("Camera Offset", originObject.transform);
            offset.transform.localPosition = Vector3.up * 1.6f;
            origin.CameraFloorOffsetObject = offset;

            GameObject head = Child("Main Camera", offset.transform);
            head.tag = "MainCamera";
            Camera camera = head.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            head.AddComponent<AudioListener>();
            ConfigureTracking(head.AddComponent<TrackedPoseDriver>(), "<XRHMD>", "centerEyePosition", "centerEyeRotation");
            origin.Camera = camera;

            CreateHand(offset.transform, manager, rayMaterial, "Left");
            CreateHand(offset.transform, manager, rayMaterial, "Right");
            GameObject eventSystem = Child("XR EventSystem", root.transform);
            eventSystem.AddComponent<EventSystem>();
            XRUIInputModule uiModule = eventSystem.AddComponent<XRUIInputModule>();
            uiModule.enableMouseInput = false;
            uiModule.enableTouchInput = false;
            uiModule.enableGamepadInput = false;
            uiModule.enableJoystickInput = false;

            GameObject simulator = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            XRDeviceSimulator deviceSimulator = simulator.GetComponent<XRDeviceSimulator>();
            deviceSimulator.cameraTransform = camera.transform;
            deviceSimulator.deviceSimulatorUI = null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(deviceSimulator);
            root.SetActive(true);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = root;
            Debug.Log("XRI rig ready: grip = direct grab; trigger = UI Press. Simulator help overlay is disabled. Save your scene.");
        }

        /// <summary>Resolves the installed package's sample; failures are explicit rather than creating a partial rig.</summary>
        private static Sample GetSimulatorSample()
        {
            Sample[] samples = Sample.FindByPackage(PackageName, PackageVersion)
                .Where(sample => sample.displayName == SimulatorSampleName).ToArray();
            if (samples.Length != 1)
                throw new InvalidOperationException("Install XR Interaction Toolkit 3.3.2 before configuring the simulator.");
            return samples[0];
        }

        /// <summary>
        /// Separates direct grab (grip) from the UI-only ray (trigger), so grabbing
        /// never competes with a ray selection of the same puzzle piece.
        /// Inline Input System actions are enabled/disabled by XRI itself.
        /// </summary>
        private static void CreateHand(Transform parent, XRInteractionManager manager, Material rayMaterial, string side)
        {
            GameObject hand = Child(side + " Controller", parent);
            string device = "<XRController>{" + side + "Hand}";
            ConfigureTracking(hand.AddComponent<TrackedPoseDriver>(), device, "devicePosition", "deviceRotation");

            GameObject directObject = Child("Direct Grab", hand.transform);
            SphereCollider sphere = directObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.09f;
            Rigidbody body = directObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            XRDirectInteractor direct = directObject.AddComponent<XRDirectInteractor>();
            direct.interactionManager = manager;
            direct.selectInput = ButtonInput("Grab", device + "/gripPressed", device + "/grip");

            GameObject rayObject = Child("UI Ray", hand.transform);
            XRRayInteractor ray = rayObject.AddComponent<XRRayInteractor>();
            ray.interactionManager = manager;
            ray.enableUIInteraction = true;
            ray.interactionLayers = 0;
            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            ray.uiPressInput = ButtonInput("UI Press", device + "/triggerPressed", device + "/trigger");
            ray.maxRaycastDistance = 10f;
            // AddComponent leaves the material null; explicitly assign Unity's stereo-capable line material.
            LineRenderer line = rayObject.AddComponent<LineRenderer>();
            line.sharedMaterial = rayMaterial;
            rayObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
        }

        /// <summary>Creates explicit performed/value readers using the supported XRI 3.x input-reader API.</summary>
        private static XRInputButtonReader ButtonInput(string name, string pressedBinding, string valueBinding)
        {
            return new XRInputButtonReader
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
                inputActionPerformed = new InputAction(name, InputActionType.Button, pressedBinding),
                inputActionValue = new InputAction(name + " Value", InputActionType.Value, valueBinding)
            };
        }

        /// <summary>Tracks simulated or real devices through the new Input System, never legacy XR controllers.</summary>
        private static void ConfigureTracking(TrackedPoseDriver driver, string device, string position, string rotation)
        {
            driver.positionInput = new InputActionProperty(
                new InputAction("Position", InputActionType.Value, device + "/" + position, expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction("Rotation", InputActionType.Value, device + "/" + rotation, expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(
                new InputAction("Tracking State", InputActionType.Value, device + "/trackingState", expectedControlType: "Integer"));
        }

        /// <summary>Keeps newly created hierarchy transforms at identity.</summary>
        private static GameObject Child(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
