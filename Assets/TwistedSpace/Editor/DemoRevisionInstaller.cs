using System;
using System.IO;
using TwistedSpace.Demo;
using TwistedSpace.Puzzles;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace TwistedSpace.Editor
{
    /// <summary>Updates the owned prefab, leaving the open scene and its instance overrides alone.</summary>
    public static class DemoRevisionInstaller
    {
        /// <summary>Installs the visual assets and simulator collision without reopening the user's scene.</summary>
        [MenuItem("Twisted Space/Apply Visual And Collision Upgrade")]
        public static void ApplyRevision() => Upgrade(true);

        [MenuItem("Twisted Space/Apply Visual Upgrade")]
        public static void ApplyVisuals() => Upgrade(false);

        private static void Upgrade(bool includeCollision)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Exit Play Mode before upgrading the demo prefab.");
            GameObject contents = PrefabUtility.LoadPrefabContents(ParallaxDemoBuilder.PrefabPath);
            try
            {
                DemoPresentationUpgrade.Apply(contents);
                DemoVisualUpgrade.Apply(contents);
                if (includeCollision)
                    ConfigureCollision(contents);
                DemoOpeningMenuUpgrade.Apply(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, ParallaxDemoBuilder.PrefabPath) == null)
                    throw new IOException("Could not save upgraded demo prefab.");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void ConfigureCollision(GameObject root)
        {
            int solidLayer = LayerMask.NameToLayer("TwistedSpaceSolid");
            if (solidLayer < 0)
                throw new InvalidOperationException("Create the TwistedSpaceSolid physics layer before upgrading.");
            Transform environment = root.transform.Find("Parallax Chamber/Demo Environment");
            XROrigin origin = root.GetComponentInChildren<XROrigin>(true);
            XRDeviceSimulator simulator = root.GetComponentInChildren<XRDeviceSimulator>(true);
            if (environment == null || origin == null || origin.Camera == null || simulator == null)
                throw new InvalidOperationException("The saved demo environment or simulator rig is incomplete.");

            // The new ceiling has visible plates but no mesh colliders; use one inexpensive envelope.
            Transform ceiling = environment.Find("Room Shell/Ceiling Collision");
            if (ceiling == null)
            {
                ceiling = new GameObject("Ceiling Collision").transform;
                ceiling.SetParent(environment.Find("Room Shell"), false);
            }
            BoxCollider roof = ceiling.GetComponent<BoxCollider>();
            if (roof == null)
                roof = ceiling.gameObject.AddComponent<BoxCollider>();
            roof.center = new Vector3(0f, 3.305f, 2f);
            roof.size = new Vector3(7f, 0.1f, 8f);
            foreach (Collider collider in environment.GetComponentsInChildren<Collider>(true))
            {
                // Frame bars remain grabbable objects, not obstacles for this simulator-only motor.
                if (!collider.isTrigger && collider.GetComponentInParent<PerspectivePuzzleTarget>() == null)
                    collider.gameObject.layer = solidLayer;
            }

            Transform holder = root.transform.Find("Simulator Collision");
            if (holder == null)
            {
                holder = new GameObject("Simulator Collision").transform;
                holder.SetParent(root.transform, false);
            }
            SimulatorCollisionMotor motor = holder.GetComponent<SimulatorCollisionMotor>();
            if (motor == null)
                motor = holder.gameObject.AddComponent<SimulatorCollisionMotor>();
            SerializedObject settings = new SerializedObject(motor);
            settings.FindProperty("xrOrigin").objectReferenceValue = origin;
            settings.FindProperty("headCamera").objectReferenceValue = origin.Camera;
            settings.FindProperty("deviceSimulator").objectReferenceValue = simulator;
            settings.FindProperty("environmentLayers").intValue = 1 << solidLayer;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
