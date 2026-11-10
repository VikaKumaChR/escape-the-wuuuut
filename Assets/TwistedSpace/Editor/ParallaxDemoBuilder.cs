using System;
using System.IO;
using TwistedSpace.Demo;
using TwistedSpace.Puzzles;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Composes a saved, fully wired scene from ordinary editable objects.
    /// Existing scene/assets are never silently replaced; the current user's scene is not an output.
    /// </summary>
    public static class ParallaxDemoBuilder
    {
        public const string ScenePath = "Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity";
        public const string PrefabPath = "Assets/TwistedSpace/Demo/Prefabs/ParallaxChamber.prefab";

        /// <summary>
        /// Saves the entire wired room and rig as one prefab so references between the camera,
        /// UI, events and puzzle remain internal and survive drag-and-drop into an empty scene.
        /// </summary>
        [MenuItem("Twisted Space/4. Export Playable Demo Prefab")]
        public static void ExportPrefab()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Exit Play Mode before exporting the demo prefab.");
            if (File.Exists(PrefabPath))
                throw new InvalidOperationException("Existing demo prefab was left unchanged: " + PrefabPath);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject completeDemo = new GameObject("Escape The Distorted Space - Playable Demo");
            foreach (GameObject root in roots)
                root.transform.SetParent(completeDemo.transform, true);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                completeDemo, PrefabPath, InteractionMode.AutomatedAction);
            if (prefab == null || !EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save the complete demo prefab and its scene instance.");
            AssetDatabase.SaveAssets();
            Debug.Log("Complete editable demo prefab saved: " + PrefabPath);
        }

        [MenuItem("Twisted Space/3. Create Playable Demo Scene")]
        public static void CreateScene()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Exit Play Mode before generating the demo.");
            if (File.Exists(ScenePath))
            {
                if (!Application.isBatchMode)
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        EditorSceneManager.OpenScene(ScenePath);
                    return;
                }
                throw new InvalidOperationException("Demo scene already exists; it was not overwritten: " + ScenePath);
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TwistedSpaceXRSetup.CreateRig();
            XROrigin origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null)
                throw new InvalidOperationException("XR rig creation failed. Import the simulator sample first.");
            Camera head = origin.Camera;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color32(10, 17, 24, 255);
            head.fieldOfView = 65f;

            GameObject sceneRoot = new GameObject("Parallax Chamber");
            DemoEnvironmentBuilder.EnvironmentObjects environment = DemoEnvironmentBuilder.Build(sceneRoot.transform);
            Collider headCollider = CreateHeadProbe(head.transform);
            PuzzleObservationVolume volume = environment.ObservationVolumeObject.AddComponent<PuzzleObservationVolume>();
            DemoUIBuilder.Configure(volume, "playerHeadColliders", new UnityEngine.Object[] { headCollider });
            PerspectivePuzzleTarget[] targets = ConfigureTargets(environment.TargetRoots);

            ForcedPerspectivePuzzle puzzle = new GameObject("Forced Perspective Puzzle").AddComponent<ForcedPerspectivePuzzle>();
            puzzle.transform.SetParent(sceneRoot.transform, false);
            DemoUIBuilder.Configure(puzzle, "headCamera", head);
            DemoUIBuilder.Configure(puzzle, "requiredLookDirection", environment.RequiredLookDirection);
            DemoUIBuilder.Configure(puzzle, "observationVolumes", new UnityEngine.Object[] { volume });
            DemoUIBuilder.Configure(puzzle, "targets", targets);

            SlidingExitDoor door = new GameObject("Exit Door Controller").AddComponent<SlidingExitDoor>();
            door.transform.SetParent(sceneRoot.transform, false);
            DemoUIBuilder.Configure(door, "doorLeaf", environment.DoorLeaf);
            DemoUIBuilder.Configure(door, "closedPose", environment.DoorClosedPose);
            DemoUIBuilder.Configure(door, "openPose", environment.DoorOpenPose);
            environment.Guide.gameObject.AddComponent<FloatingGuide>();
            AddHandProxies(origin.transform.root);

            GameObject uiRoot = new GameObject("World Space UI");
            uiRoot.transform.SetParent(sceneRoot.transform, false);
            DemoUIBuilder.UIObjects ui = DemoUIBuilder.Build(uiRoot.transform, head);
            VerticalSliceFlow flow = new GameObject("Demo Flow").AddComponent<VerticalSliceFlow>();
            flow.transform.SetParent(sceneRoot.transform, false);
            DemoUIBuilder.Configure(flow, "puzzle", puzzle);
            DemoUIBuilder.Configure(flow, "targets", targets);
            DemoUIBuilder.Configure(flow, "observationVolume", volume);
            DemoUIBuilder.Configure(flow, "exitDoor", door);
            DemoUIBuilder.Configure(flow, "mainMenuPanel", ui.MainMenu);
            DemoUIBuilder.Configure(flow, "hintPanel", ui.Hint);
            DemoUIBuilder.Configure(flow, "resetPanel", ui.Reset);
            DemoUIBuilder.Configure(flow, "resultPanel", ui.Result);
            DemoUIBuilder.Configure(flow, "statusText", ui.StatusText);
            DemoUIBuilder.Configure(flow, "hintText", ui.HintText);
            DemoUIBuilder.Configure(flow, "resultText", ui.ResultText);
            DemoUIBuilder.Configure(flow, "alignmentSlider", ui.Alignment);
            DemoUIBuilder.Configure(flow, "stableHoldSlider", ui.StableHold);
            UnityEventTools.AddPersistentListener(ui.Start.onClick, flow.StartGame);
            UnityEventTools.AddPersistentListener(ui.NextHint.onClick, flow.ShowNextHint);
            UnityEventTools.AddPersistentListener(ui.CancelReset.onClick, flow.CancelReset);
            UnityEventTools.AddPersistentListener(ui.ConfirmReset.onClick, flow.ConfirmReset);
            UnityEventTools.AddPersistentListener(ui.Replay.onClick, flow.Replay);
            UnityEventTools.AddPersistentListener(ui.ResultMenu.onClick, flow.ReturnToMenu);
            // Exercise the existing panel-level Reset/Back event interfaces rather than duplicate listeners.
            DemoUIBuilder.Configure(ui.Hint, "resetPuzzleButton", ui.RequestReset);
            DemoUIBuilder.Configure(ui.Hint, "backButton", ui.Menu);
            UnityEventTools.AddPersistentListener(ui.Hint.OnResetPuzzle, flow.RequestReset);
            UnityEventTools.AddPersistentListener(ui.Hint.OnBack, flow.ReturnToMenu);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unity could not save the demo scene: " + ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = sceneRoot;
            Debug.Log("Playable demo saved: " + ScenePath + ". Open this scene and press Play.");
        }

        private static Collider CreateHeadProbe(Transform head)
        {
            GameObject probe = new GameObject("Head Observation Probe");
            probe.transform.SetParent(head, false);
            SphereCollider sphere = probe.AddComponent<SphereCollider>();
            sphere.radius = 0.07f;
            sphere.isTrigger = true;
            Rigidbody body = probe.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            return sphere;
        }

        private static PerspectivePuzzleTarget[] ConfigureTargets(GameObject[] roots)
        {
            PerspectivePuzzleTarget[] targets = new PerspectivePuzzleTarget[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                Rigidbody body = roots[i].AddComponent<Rigidbody>();
                body.useGravity = false;
                body.mass = 0.3f;
                body.linearDamping = 6f;
                body.angularDamping = 6f;
                XRGrabInteractable grab = roots[i].AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.throwOnDetach = false;
                // This introductory puzzle teaches translation/depth first, without accidental wrist tilts.
                grab.trackRotation = false;
                grab.useDynamicAttach = true;
                grab.matchAttachRotation = false;
                targets[i] = roots[i].AddComponent<PerspectivePuzzleTarget>();
                DemoUIBuilder.Configure(targets[i], "targetRenderers", roots[i].GetComponentsInChildren<Renderer>());
                DemoUIBuilder.Configure(targets[i], "successEmission", new Color(0.3f, 0.8f, 1f));
            }
            return targets;
        }

        private static void AddHandProxies(Transform rig)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                DemoEnvironmentBuilder.AssetDirectory + "/DemoStructure.mat");
            foreach (XRDirectInteractor interactor in rig.GetComponentsInChildren<XRDirectInteractor>(true))
            {
                GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hand.name = "Surveyor Glove Proxy";
                hand.transform.SetParent(interactor.transform.parent, false);
                hand.transform.localPosition = new Vector3(0f, -0.025f, -0.03f);
                hand.transform.localScale = new Vector3(0.075f, 0.045f, 0.12f);
                UnityEngine.Object.DestroyImmediate(hand.GetComponent<Collider>());
                hand.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
    }
}
