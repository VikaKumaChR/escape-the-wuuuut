using System.Collections;
using NUnit.Framework;
using TwistedSpace.Editor;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.Tests
{
    /// <summary>Checks that the generated editor rig has usable current-API input wiring.</summary>
    public sealed class SimulatorRigTests
    {
        private const string OwnedRigKey = "TwistedSpace.Tests.OwnsSimulatorRig";

        [UnityTest]
        public IEnumerator GeneratedRigEnablesTrackedInputsInPlayMode()
        {
            PrepareEmptyScene();
            TwistedSpaceXRSetup.CreateRig();
            Assert.That(Object.FindAnyObjectByType<XROrigin>(), Is.Not.Null);
            SessionState.SetBool(OwnedRigKey, true);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;
            XROrigin origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(origin, Is.Not.Null);
            GameObject root = origin.transform.parent.gameObject;
            Assert.That(root.GetComponentsInChildren<Canvas>(true).Length, Is.Zero);
            foreach (TrackedPoseDriver driver in root.GetComponentsInChildren<TrackedPoseDriver>())
            {
                Assert.That(driver.positionInput.action.enabled, Is.True);
                Assert.That(driver.positionInput.action.controls.Count, Is.GreaterThan(0));
                Assert.That(driver.rotationInput.action.controls.Count, Is.GreaterThan(0));
            }
            foreach (XRRayInteractor ray in root.GetComponentsInChildren<XRRayInteractor>())
            {
                Assert.That(ray.uiPressInput.inputActionPerformed.enabled, Is.True);
                Assert.That(ray.uiPressInput.inputActionPerformed.controls.Count, Is.GreaterThan(0));
            }
            foreach (XRDirectInteractor direct in root.GetComponentsInChildren<XRDirectInteractor>())
                Assert.That(direct.selectInput.inputActionPerformed.controls.Count, Is.GreaterThan(0));
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator CleanUpPlayModeRig()
        {
            if (!SessionState.GetBool(OwnedRigKey, false))
                yield break;
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            GameObject root = GameObject.Find("Twisted Space XR");
            if (root != null)
                Object.DestroyImmediate(root);
            SessionState.EraseBool(OwnedRigKey);
        }

        [Test]
        public void GeneratedRigHasTrackedHandsConfirmedUiInputAndNoOverlay()
        {
            PrepareEmptyScene();
            TwistedSpaceXRSetup.CreateRig();
            XROrigin origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(origin, Is.Not.Null);
            GameObject root = origin.transform.parent.gameObject;
            try
            {
                Assert.That(origin.Camera, Is.Not.Null);
                Assert.That(root.GetComponentsInChildren<TrackedPoseDriver>().Length, Is.EqualTo(3));
                Assert.That(root.GetComponentsInChildren<XRDirectInteractor>().Length, Is.EqualTo(2));
                XRRayInteractor[] rays = root.GetComponentsInChildren<XRRayInteractor>();
                Assert.That(rays.Length, Is.EqualTo(2));
                foreach (XRRayInteractor ray in rays)
                {
                    Assert.That(ray.enableUIInteraction, Is.True);
                    Assert.That(ray.selectInput.inputSourceMode, Is.EqualTo(XRInputButtonReader.InputSourceMode.Unused));
                    Assert.That(ray.uiPressInput.inputActionPerformed.bindings[0].path, Does.EndWith("/triggerPressed"));
                    Assert.That(ray.GetComponent<LineRenderer>().sharedMaterial, Is.Not.Null);
                }
                foreach (XRDirectInteractor direct in root.GetComponentsInChildren<XRDirectInteractor>())
                    Assert.That(direct.selectInput.inputActionPerformed.bindings[0].path, Does.EndWith("/gripPressed"));
                Assert.That(root.GetComponentInChildren<XRUIInputModule>(), Is.Not.Null);
                Assert.That(root.GetComponentInChildren<XRDeviceSimulator>().deviceSimulatorUI, Is.Null);
                Assert.That(root.GetComponentsInChildren<Canvas>(true).Length, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void PrepareEmptyScene()
        {
            if (Application.isBatchMode)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Never remove a developer's existing scene objects just to make this fixture run.
            if (Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include) != null ||
                Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null ||
                Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include) != null)
                Assert.Ignore("Run this scene-generation test from an empty scene.");
        }
    }
}
