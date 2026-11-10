using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TwistedSpace.Demo;
using TwistedSpace.Editor;
using TwistedSpace.Puzzles;
using TwistedSpace.UI;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace TwistedSpace.Tests
{
    /// <summary>
    /// Loads the shipped scene, uses confirmed pointer events, and completes a full round.
    /// Runs only in batch mode, so an interactive user's unsaved scene cannot be replaced.
    /// Set TWISTED_SPACE_CAPTURE_DIR with a graphics-capable editor to save real scene previews.
    /// </summary>
    public sealed class PlayableDemoSceneTests
    {
        private const string OwnsPlayMode = "TwistedSpace.Tests.PlayableDemoOwnsPlayMode";

        [UnityTest]
        public IEnumerator FullRoundSupportsResetWhileHeldSolveReplayAndMenu()
        {
            if (!Application.isBatchMode)
                Assert.Ignore("This saved-scene integration test is intended for an isolated batch editor.");
            Assert.That(File.Exists(ParallaxDemoBuilder.ScenePath), Is.True);
            EditorSceneManager.OpenScene(ParallaxDemoBuilder.ScenePath);
            SessionState.SetBool(OwnsPlayMode, true);
            yield return new EnterPlayMode();
            yield return new WaitForSecondsRealtime(0.65f);

            VerticalSliceFlow flow = Object.FindAnyObjectByType<VerticalSliceFlow>();
            ForcedPerspectivePuzzle puzzle = Object.FindAnyObjectByType<ForcedPerspectivePuzzle>();
            SlidingExitDoor door = Object.FindAnyObjectByType<SlidingExitDoor>();
            Camera head = Object.FindAnyObjectByType<XROrigin>().Camera;
            PerspectivePuzzleTarget[] targets = Get<PerspectivePuzzleTarget[]>(flow, "targets");
            VRWorldSpacePanel menu = Get<VRWorldSpacePanel>(flow, "mainMenuPanel");
            VRWorldSpacePanel hint = Get<VRWorldSpacePanel>(flow, "hintPanel");
            VRWorldSpacePanel reset = Get<VRWorldSpacePanel>(flow, "resetPanel");
            VRWorldSpacePanel result = Get<VRWorldSpacePanel>(flow, "resultPanel");
            Assert.That(flow.State, Is.EqualTo(DemoState.MainMenu));
            Assert.That(menu.IsInteractionReady, Is.True);
            Assert.That(puzzle.enabled, Is.False);
            Assert.That(targets.Length, Is.EqualTo(3));
            foreach (PerspectivePuzzleTarget target in targets)
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
                    Assert.That(renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.True,
                        "Emission must remain enabled on the saved material after URP import.");
            Assert.That(Vector3.Distance(head.transform.position, new Vector3(0, 1.6f, 0)), Is.LessThan(0.1f));
            foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Canvas canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
                    Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Font font = Get<Text>(flow, "hintText").font;
            foreach (char character in "逃离扭曲空间重置谜题开始")
                Assert.That(font.HasCharacter(character), Is.True, "Missing Chinese glyph: " + character);
            yield return Capture(head, "demo-main-menu.png");

            Assert.That(menu.FollowHead, Is.False);
            foreach (Renderer renderer in Get<Renderer[]>(Object.FindAnyObjectByType<OpeningMenuPresentation>(), "explorationRenderers"))
                Assert.That(renderer.forceRenderingOff, Is.True, "Room and glove meshes must not obscure the opening.");
            foreach (Text label in menu.GetComponentsInChildren<Text>())
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 2f),
                    "Opening text clips: " + label.text);
            yield return StartWithMouse(menu, head);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(flow.State, Is.EqualTo(DemoState.Playing));
            Assert.That(puzzle.IsSolved, Is.False);
            Assert.That(hint.IsInteractionReady, Is.True);
            foreach (Renderer renderer in Get<Renderer[]>(Object.FindAnyObjectByType<OpeningMenuPresentation>(), "explorationRenderers"))
                Assert.That(renderer.forceRenderingOff, Is.False, "Exploration meshes must return after the opening fades.");
            foreach (PerspectivePuzzleTarget target in targets)
                Assert.That(target.IsResetPending, Is.False);
            yield return Capture(head, "demo-room-playing.png");

            yield return Click(hint, "下一条提示");
            Assert.That(flow.HintsUsed, Is.EqualTo(1));
            foreach (Text label in hint.GetComponentsInChildren<Text>())
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 2f),
                    "Text clips vertically: " + label.text);
            Slider alignment = Get<Slider>(flow, "alignmentSlider");
            alignment.SetValueWithoutNotify(0.5f);
            Canvas.ForceUpdateCanvases();
            Assert.That(alignment.fillRect.rect.width,
                Is.EqualTo(((RectTransform)alignment.transform).rect.width * 0.5f).Within(0.1f));

            yield return Click(hint, "重置谜题");
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(flow.State, Is.EqualTo(DemoState.ResetConfirmation));
            Assert.That(puzzle.enabled, Is.False);
            yield return Capture(head, "demo-reset-confirmation.png");
            yield return Click(reset, "继续解谜");
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(flow.State, Is.EqualTo(DemoState.Playing));

            // Use XRI's supported manual interaction API so its real detach/reset lifecycle runs.
            XRDirectInteractor hand = Object.FindAnyObjectByType<XRDirectInteractor>();
            XRGrabInteractable grabbed = targets[0].GetComponent<XRGrabInteractable>();
            Vector3 initialPosition = targets[0].transform.position;
            hand.StartManualInteraction((IXRSelectInteractable)grabbed);
            yield return null;
            Assert.That(targets[0].IsGrabbed, Is.True);
            flow.RequestReset();
            LogAssert.Expect(LogType.Warning, "Puzzle reset deferred: release this target with every hand. " +
                "Its pose will be restored after XRI finishes detaching.");
            flow.ConfirmReset();
            yield return null;
            Assert.That(targets[0].IsResetPending, Is.True);
            hand.EndManualInteraction();
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(targets[0].IsResetPending, Is.False);
            Assert.That(Vector3.Distance(targets[0].transform.position, initialPosition), Is.LessThan(0.01f));

            foreach (PerspectivePuzzleTarget target in targets)
            {
                Rigidbody body = target.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Vector3 aligned = head.transform.position + Vector3.forward * target.transform.localScale.x;
                body.position = aligned;
                body.rotation = Quaternion.identity;
                target.transform.SetPositionAndRotation(aligned, Quaternion.identity);
            }
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.That(puzzle.IsSolved, Is.True, "The authored three-frame solution must satisfy the real overlap gate.");
            Assert.That(flow.State, Is.EqualTo(DemoState.Solved));
            Assert.That(door.IsOpen, Is.True);
            Assert.That(puzzle.enabled, Is.True, "Success emission must not be cancelled by disabling the solved puzzle.");
            Assert.That(result.IsInteractionReady, Is.True);
            Transform doorLeaf = Get<Transform>(door, "doorLeaf");
            Assert.That(Vector3.Distance(doorLeaf.position, Get<Transform>(door, "openPose").position),
                Is.LessThan(0.01f));
            yield return Capture(head, "demo-result.png");

            yield return Click(result, "重新挑战");
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(flow.State, Is.EqualTo(DemoState.Playing));
            Assert.That(puzzle.IsSolved, Is.False);
            Assert.That(flow.HintsUsed, Is.Zero);
            yield return Click(hint, "返回主菜单");
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(flow.State, Is.EqualTo(DemoState.MainMenu));
            Assert.That(menu.IsInteractionReady, Is.True);
            Assert.That(puzzle.enabled, Is.False);
            Assert.That(door.IsOpen, Is.False);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(Object.FindAnyObjectByType<SimulatorMenuInput>().IsDesktopMenuActive, Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator OpeningStillSupportsConfirmedXRRayWithoutDesktopMode()
        {
            if (!Application.isBatchMode)
                Assert.Ignore("The saved-scene integration test is isolated batch only.");
            EditorSceneManager.OpenScene(ParallaxDemoBuilder.ScenePath);
            SessionState.SetBool(OwnsPlayMode, true);
            yield return new EnterPlayMode();
            yield return new WaitForSecondsRealtime(0.65f);
            Object.FindAnyObjectByType<SimulatorMenuInput>().enabled = false;
            VerticalSliceFlow flow = Object.FindAnyObjectByType<VerticalSliceFlow>();
            yield return StartWithSimulator(Get<VRWorldSpacePanel>(flow, "mainMenuPanel"),
                Object.FindAnyObjectByType<XROrigin>().Camera);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(flow.State, Is.EqualTo(DemoState.Playing));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SimulatorKeyboardBlocksSolidsAndPassesOpenedDoor()
        {
            if (!Application.isBatchMode)
                Assert.Ignore("The collision integration test must not replace an interactive user's scene.");
            EditorSceneManager.OpenScene(ParallaxDemoBuilder.ScenePath);
            SessionState.SetBool(OwnsPlayMode, true);
            yield return new EnterPlayMode();
            yield return new WaitForSecondsRealtime(0.65f);

            SimulatorCollisionMotor motor = Object.FindAnyObjectByType<SimulatorCollisionMotor>();
            XRDeviceSimulator simulator = Object.FindAnyObjectByType<XRDeviceSimulator>();
            XROrigin origin = Object.FindAnyObjectByType<XROrigin>();
            Camera head = origin.Camera;
            SlidingExitDoor door = Object.FindAnyObjectByType<SlidingExitDoor>();
            Object.FindAnyObjectByType<VerticalSliceFlow>().StartGame();
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(motor, Is.Not.Null, "The delivered prefab must contain the wired motor.");
            Assert.That(Get<LayerMask>(motor, "environmentLayers").value,
                Is.EqualTo(1 << LayerMask.NameToLayer("TwistedSpaceSolid")));
            foreach (PerspectivePuzzleTarget target in Object.FindObjectsByType<PerspectivePuzzleTarget>(
                FindObjectsSortMode.None))
                foreach (Collider bar in target.GetComponentsInChildren<Collider>())
                    Assert.That(bar.gameObject.layer, Is.Not.EqualTo(LayerMask.NameToLayer("TwistedSpaceSolid")));

            InputSettings settings = InputSystem.settings;
            InputSettings.EditorInputBehaviorInPlayMode previousRouting = settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior previousBackground = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("CollisionTestKeyboard");
            try
            {
                // Accelerate this test's temporary simulator instance, not the saved prefab controls.
                simulator.keyboardXTranslateSpeed = 3f;
                simulator.keyboardYTranslateSpeed = 3f;
                simulator.keyboardZTranslateSpeed = 3f;
                simulator.keyboardBodyTranslateMultiplier = 1f;
                yield return null;
                yield return null;
                Assert.That(motor.IsSimulationActive, Is.True,
                    $"Motor inactive with simulated devices: {string.Join(", ", InputSystem.devices)}");
                yield return Drive(keyboard, 0.05f, Key.U);
                Assert.That(simulator.manipulatingFPS, Is.True);
                // XRI deliberately ignores FPS movement during its first second of game time.
                while (Time.time <= 1.1f)
                    yield return null;
                yield return Drive(keyboard, 4f, Key.W);
                Assert.That(head.transform.position.z, Is.InRange(4.3f, 4.65f), "Closed door must block the head.");
                door.Open();
                yield return new WaitForSecondsRealtime(1.3f);
                yield return Drive(keyboard, 1.5f, Key.W);
                Assert.That(head.transform.position.z, Is.InRange(5.65f, 5.85f),
                    "Open door must allow entry into the exit recess; far wall still blocks.");

                yield return Drive(keyboard, 5f, Key.S);
                Assert.That(head.transform.position.z, Is.InRange(-1.85f, -1.65f));
                yield return Drive(keyboard, 2.5f, Key.D);
                Assert.That(head.transform.position.x, Is.InRange(3.2f, 3.35f));
                float rear = head.transform.position.z;
                yield return Drive(keyboard, 0.3f, Key.D, Key.W);
                Assert.That(head.transform.position.x, Is.InRange(3.2f, 3.35f));
                Assert.That(head.transform.position.z, Is.GreaterThan(rear + 0.4f), "Diagonal pressure must slide.");
                float wall = head.transform.position.x;
                yield return Drive(keyboard, 0.1f, Key.A);
                Assert.That(head.transform.position.x, Is.LessThan(wall - 0.15f), "Reverse motion must respond immediately.");
                yield return Drive(keyboard, 0.6f, Key.Q);
                Assert.That(head.transform.position.y, Is.InRange(1.61f, 1.65f), "The head must not sink below the floor.");
                yield return Drive(keyboard, 0.8f, Key.E);
                Assert.That(head.transform.position.y, Is.InRange(2.9f, 3.1f), "The new ceiling must block upward flight.");

                // Hardware-like HMD ownership must stop correction, even with the simulator enabled.
                InputSystem.RegisterLayout<UnityEngine.InputSystem.XR.XRHMD>("SafetyTestHardwareHMD");
                var hardware = InputSystem.AddDevice("SafetyTestHardwareHMD");
                try
                {
                    Assert.That(hardware, Is.Not.InstanceOf<XRSimulatedHMD>());
                    LogAssert.Expect(LogType.Warning,
                        "SimulatorCollisionMotor is inactive: exactly one enabled simulated HMD, and no physical HMD, " +
                        "must be present. Physical HMD poses are never constrained by this motor.");
                    Vector3 before = origin.transform.position;
                    yield return new WaitForSecondsRealtime(0.1f);
                    Assert.That(motor.IsSimulationActive, Is.False);
                    Assert.That(origin.transform.position, Is.EqualTo(before));
                }
                finally
                {
                    InputSystem.RemoveDevice(hardware);
                    InputSystem.RemoveLayout("SafetyTestHardwareHMD");
                }
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(motor.IsSimulationActive, Is.True);
                VerticalSliceFlow flow = Object.FindAnyObjectByType<VerticalSliceFlow>();
                flow.ReturnToMenu();
                yield return new WaitForSecondsRealtime(0.6f);
                VRWorldSpacePanel reopened = Get<VRWorldSpacePanel>(flow, "mainMenuPanel");
                Assert.That(Vector3.Distance(reopened.transform.position, head.transform.position),
                    Is.EqualTo(1.5f).Within(0.02f), "Returning from elsewhere must open a reachable fixed menu.");
                Assert.That(simulator.keyboardZTranslateAction.action.enabled, Is.False);
                Assert.That(Object.FindAnyObjectByType<SimulatorMenuInput>().IsDesktopMenuActive, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = previousRouting;
                settings.backgroundBehavior = previousBackground;
            }
            yield return new ExitPlayMode();
        }

        private static IEnumerator StartWithMouse(VRWorldSpacePanel menu, Camera head)
        {
            InputSettings settings = InputSystem.settings;
            InputSettings.EditorInputBehaviorInPlayMode previousRouting = settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior previousBackground = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("OpeningTestKeyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("OpeningTestMouse");
            try
            {
                yield return new WaitForSecondsRealtime(0.15f);
                SimulatorMenuInput input = Object.FindAnyObjectByType<SimulatorMenuInput>();
                Assert.That(input.IsDesktopMenuActive, Is.True);
                ConfirmedXRButton start = FindButton(menu, "开始探索");
                Vector3 headPose = head.transform.position;
                Vector3 panelPose = menu.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.U));
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(70, 40) }
                    .WithButton(MouseButton.Right));
                yield return new WaitForSecondsRealtime(0.12f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(mouse, new MouseState());
                yield return new WaitForSecondsRealtime(0.05f);
                Assert.That(Vector3.Distance(headPose, head.transform.position), Is.LessThan(0.005f),
                    "Mouse movement and WASD must not drift the opening camera.");
                Assert.That(Vector3.Distance(panelPose, menu.transform.position), Is.LessThan(0.001f));

                RectTransform face = (RectTransform)start.targetGraphic.transform;
                Vector2 point = head.WorldToScreenPoint(face.TransformPoint(face.rect.center));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return new WaitForSecondsRealtime(0.08f);
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Assert.That(hits.Count, Is.GreaterThan(0), "The ordinary mouse must reach the World Space canvas.");
                Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject),
                    Is.EqualTo(start.gameObject));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(0.03f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return new WaitForSecondsRealtime(0.35f);
                Assert.That(Object.FindAnyObjectByType<VerticalSliceFlow>().State, Is.EqualTo(DemoState.Playing),
                    "An ordinary short mouse click must start exploration, without aiming a simulated hand.");
                Assert.That(input.IsDesktopMenuActive, Is.False);
                Assert.That(Object.FindAnyObjectByType<XRDeviceSimulator>().toggleManipulateRightAction.action.enabled, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                settings.editorInputBehaviorInPlayMode = previousRouting;
                settings.backgroundBehavior = previousBackground;
            }
        }

        private static IEnumerator Drive(Keyboard keyboard, float seconds, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            float endTime = Time.time + seconds;
            while (Time.time < endTime)
                yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ExitOwnedPlayMode()
        {
            if (!SessionState.GetBool(OwnsPlayMode, false))
                yield break;
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            SessionState.EraseBool(OwnsPlayMode);
        }

        private static IEnumerator Click(VRWorldSpacePanel panel, string name)
        {
            ConfirmedXRButton button = FindButton(panel, name);
            Assert.That(panel.IsInteractionReady, Is.True);
            TrackedDeviceEventData pointer = new TrackedDeviceEventData(EventSystem.current)
            {
                pointerId = -101,
                eligibleForClick = true,
                button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = new RaycastResult { gameObject = button.targetGraphic.gameObject }
            };
            button.OnPointerEnter(pointer);
            button.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(0.18f);
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            button.OnPointerExit(pointer);
        }

        private static ConfirmedXRButton FindButton(VRWorldSpacePanel panel, string name)
        {
            foreach (ConfirmedXRButton button in panel.GetComponentsInChildren<ConfirmedXRButton>())
                if (button.name == name) return button;
            Assert.Fail("Missing button: " + name);
            return null;
        }

        private static IEnumerator StartWithSimulator(VRWorldSpacePanel menu, Camera head)
        {
            InputSettings settings = InputSystem.settings;
            InputSettings.EditorInputBehaviorInPlayMode previousRouting = settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior previousBackground = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            // Virtual input devices avoid native macOS focus resets in a background batch editor.
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("DemoTestKeyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("DemoTestMouse");
            try
            {
            XRDeviceSimulator simulator = Object.FindAnyObjectByType<XRDeviceSimulator>();
            Assert.That(simulator.toggleManipulateRightAction, Is.Not.Null);
            Assert.That(simulator.toggleManipulateRightAction.action.enabled, Is.True,
                "The simulator toggle action must be enabled.");
            // New bindings resolve on the next Input System update before queued input is sent.
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Y));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(simulator.manipulatingRightController, Is.True,
                $"rightDevice={simulator.manipulatingRightDevice}, rightHand={simulator.manipulatingRightHand}, " +
                $"FPS={simulator.manipulatingFPS}, controls={simulator.toggleManipulateRightAction.action.controls.Count}");
            XRRayInteractor rightRay = null;
            foreach (XRRayInteractor ray in head.transform.root.GetComponentsInChildren<XRRayInteractor>())
                if (ray.transform.parent.name == "Right Controller") rightRay = ray;
            Assert.That(rightRay, Is.Not.Null);
            ConfirmedXRButton start = FindButton(menu, "开始探索");
            RectTransform face = (RectTransform)start.targetGraphic.transform;
            Vector3 center = face.TransformPoint(face.rect.center);
            GameObject aim = new GameObject("Test Only UI Aim");
            aim.transform.position = head.transform.position + new Vector3(0.1f, -0.05f, 0.3f);
            aim.transform.rotation = Quaternion.LookRotation(center - aim.transform.position);
            rightRay.rayOriginTransform = aim.transform;
            yield return null;
            yield return null;
            Assert.That(rightRay.TryGetCurrentUIRaycastResult(out RaycastResult hit), Is.True);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject), Is.EqualTo(start.gameObject));
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return new WaitForSecondsRealtime(0.2f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            yield return null;
            // The transient aim object is destroyed with the test Play Mode scene.
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                settings.editorInputBehaviorInPlayMode = previousRouting;
                settings.backgroundBehavior = previousBackground;
            }
        }

        private static T Get<T>(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(instance);
        }

        private static IEnumerator Capture(Camera camera, string name)
        {
            string directory = Environment.GetEnvironmentVariable("TWISTED_SPACE_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture texture = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = texture;
            yield return null;
            yield return null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(pixels);
        }
    }
}
