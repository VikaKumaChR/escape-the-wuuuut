#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Creates the demo's visual environment only. The caller owns the XR rig,
    /// interaction/flow components, World Space UI, and saving the scene.
    /// </summary>
    public static class DemoEnvironmentBuilder
    {
        public const string AssetDirectory = "Assets/TwistedSpace/Demo/Materials";

        public sealed class EnvironmentObjects
        {
            public GameObject ObservationVolumeObject;
            public Transform RequiredLookDirection;
            public GameObject[] TargetRoots;
            public Transform DoorLeaf;
            public Transform DoorOpenPose;
            public Transform DoorClosedPose;
            public Transform Guide;
            public Transform Observer;
        }

        /// <summary>
        /// Build under an identity scene root in edit mode. All positions below
        /// are local to that root, hence also world positions under this contract.
        /// Target roots deliberately have neither Rigidbody nor XRGrabInteractable.
        /// </summary>
        public static EnvironmentObjects Build(Transform parent)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Build the demo environment in edit mode.");
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("The demo requires Universal Render Pipeline/Lit.");

            EnsureAssetDirectory();
            Color dark = Hex("#0A1118");
            Color panel = Hex("#142330");
            Color cyan = Hex("#86D9F0");
            Color amber = Hex("#F3A55D");
            Material floor = GetMaterial("DemoFloor", shader, dark, 0f, 0.18f);
            Material structure = GetMaterial("DemoStructure", shader, panel, 0f, 0.28f);
            Material trim = GetMaterial("DemoCyanTrim", shader, cyan, 0.18f, 0.3f);
            Material warmTrim = GetMaterial("DemoAmberTrim", shader, amber, 0.15f, 0.3f);
            Material target = GetMaterial("DemoTarget", shader, cyan, 0.08f, 0.35f);
            Material observerMaterial = GetMaterial(
                "DemoObserver", shader, Hex("#202C35"), 0f, 0.05f);

            Transform environment = Empty("Demo Environment", parent, Vector3.zero);
            BuildRoom(environment, floor, structure, trim, warmTrim);

            var result = new EnvironmentObjects();
            Transform observation = Empty("Observation Volume", environment, new Vector3(0f, 1.6f, 0f));
            BoxCollider volume = observation.gameObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(0.65f, 1f, 0.65f);
            result.ObservationVolumeObject = observation.gameObject;
            result.RequiredLookDirection = Empty(
                "Required Look Direction (+Z)", environment, new Vector3(0f, 1.6f, 0f));
            BuildObservationMark(environment, trim, warmTrim);

            result.TargetRoots = new[]
            {
                // Keep the resting geometry behind the 1.2m modal UI plane.
                BuildTarget("Alignment Frame 01", environment, new Vector3(-0.32f, 1.40f, 1.55f), target),
                BuildTarget("Alignment Frame 02", environment, new Vector3(0.30f, 1.78f, 1.90f), target),
                BuildTarget("Alignment Frame 03", environment, new Vector3(-0.28f, 1.55f, 2.25f), target)
            };

            BuildExit(environment, structure, floor, trim, warmTrim, result);
            result.Guide = BuildGuide(environment, trim, warmTrim);
            result.Observer = BuildObserver(environment, observerMaterial);
            BuildLighting(environment);
            return result;
        }

        private static void BuildRoom(
            Transform parent, Material floor, Material structure, Material trim, Material warmTrim)
        {
            Transform shell = Empty("Room Shell", parent, Vector3.zero);
            Box("Floor", shell, new Vector3(0f, -0.1f, 2f),
                new Vector3(7f, 0.2f, 8f), floor, true, true);
            Box("Left Wall", shell, new Vector3(-3.6f, 1.65f, 2f),
                new Vector3(0.2f, 3.3f, 8f), structure, true, true);
            Box("Right Wall", shell, new Vector3(3.6f, 1.65f, 2f),
                new Vector3(0.2f, 3.3f, 8f), structure, true, true);
            Box("Rear Wall", shell, new Vector3(0f, 1.65f, -2.1f),
                new Vector3(7.4f, 3.3f, 0.2f), structure, true, true);
            Box("Far Wall", shell, new Vector3(0f, 1.65f, 6.1f),
                new Vector3(7.4f, 3.3f, 0.2f), structure, true, true);

            // An open ceiling admits the single directional light. Offset ribs
            // suggest distorted perspective without crossing the alignment sightline.
            Transform ribs = Empty("Offset Structural Ribs", parent, Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float z = 0.5f + i * 1.7f;
                float shift = (i - 1) * 0.16f;
                float halfWidth = 3.1f - i * 0.12f;
                float height = 3.05f - i * 0.08f;
                Box("Left Rib " + i, ribs, new Vector3(-halfWidth + shift, height * 0.5f, z),
                    new Vector3(0.12f, height, 0.16f), structure, true, true);
                Box("Right Rib " + i, ribs, new Vector3(halfWidth + shift, height * 0.5f, z),
                    new Vector3(0.12f, height, 0.16f), structure, true, true);
                Box("Overhead Rib " + i, ribs, new Vector3(shift, height, z),
                    new Vector3(halfWidth * 2f + 0.12f, 0.12f, 0.16f), structure, true, true);
                Box("Rib Light " + i, ribs, new Vector3(-halfWidth + shift + 0.066f, 1.8f, z),
                    new Vector3(0.012f, 0.8f, 0.08f), trim, false, true);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Box("Wall Base Rail " + side, shell, new Vector3(side * 3.46f, 0.12f, 2f),
                    new Vector3(0.06f, 0.2f, 7.8f), floor, false, true);
                Box("Perimeter Light " + side, shell, new Vector3(side * 3.42f, 0.23f, 2f),
                    new Vector3(0.025f, 0.018f, 7.6f), trim, false, true);
                Box("Exit Approach Line " + side, shell, new Vector3(side * 0.9f, 0.01f, 3.45f),
                    new Vector3(0.025f, 0.008f, 2.4f), warmTrim, false, true);
            }
        }

        private static void BuildObservationMark(Transform parent, Material trim, Material warmTrim)
        {
            Transform mark = Empty("Observation Ground Mark", parent, new Vector3(0f, 0.01f, 0f));
            Box("Left", mark, new Vector3(-0.32f, 0f, 0f),
                new Vector3(0.018f, 0.008f, 0.65f), trim, false, true);
            Box("Right", mark, new Vector3(0.32f, 0f, 0f),
                new Vector3(0.018f, 0.008f, 0.65f), trim, false, true);
            Box("Back", mark, new Vector3(0f, 0f, -0.32f),
                new Vector3(0.65f, 0.008f, 0.018f), trim, false, true);
            Box("Front", mark, new Vector3(0f, 0f, 0.32f),
                new Vector3(0.65f, 0.008f, 0.018f), trim, false, true);
            Box("Forward Accent", mark, new Vector3(0f, 0f, 0.48f),
                new Vector3(0.045f, 0.008f, 0.18f), warmTrim, false, true);
        }

        private static GameObject BuildTarget(string name, Transform parent, Vector3 position, Material material)
        {
            Transform root = Empty(name, parent, position);
            // Scaling ALL dimensions by depth makes both front and back projected
            // extents identical from (0, 1.6, 0) when centers are aligned along +Z.
            root.localScale = Vector3.one * position.z;
            SquareFrame(root, 0.5f, 0.03f, 0.03f, material, true);
            return root.gameObject;
        }

        private static void SquareFrame(
            Transform root, float outerSize, float thickness, float depth, Material material, bool colliders)
        {
            float offset = (outerSize - thickness) * 0.5f;
            float innerSpan = outerSize - 2f * thickness;
            // Four independent bars form a compound collider once the caller adds
            // a Rigidbody at the root; nothing fills or blocks the frame's hole.
            Box("Top Bar", root, new Vector3(0f, offset, 0f),
                new Vector3(outerSize, thickness, depth), material, colliders);
            Box("Bottom Bar", root, new Vector3(0f, -offset, 0f),
                new Vector3(outerSize, thickness, depth), material, colliders);
            Box("Left Bar", root, new Vector3(-offset, 0f, 0f),
                new Vector3(thickness, innerSpan, depth), material, colliders);
            Box("Right Bar", root, new Vector3(offset, 0f, 0f),
                new Vector3(thickness, innerSpan, depth), material, colliders);
        }

        private static void BuildExit(
            Transform parent, Material structure, Material dark, Material trim,
            Material warmTrim, EnvironmentObjects result)
        {
            Transform portal = Empty("Exit Portal", parent, new Vector3(0f, 0f, 4.8f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Portal Jamb " + side, portal, new Vector3(side * 0.87f, 1.3f, 0f),
                    new Vector3(0.18f, 2.6f, 0.3f), structure, true, true);
                Box("Portal Light " + side, portal, new Vector3(side * 0.87f, 1.3f, -0.16f),
                    new Vector3(0.035f, 2.4f, 0.015f), trim, false, true);
                // Side panels sit behind the leaf's travel plane, leaving a clear
                // sliding pocket and a walkable central opening after it opens.
                Box("Exit Side Panel " + side, portal, new Vector3(side * 2.22f, 1.3f, 0.22f),
                    new Vector3(2.56f, 2.6f, 0.12f), structure, true, true);
            }
            Box("Portal Lintel", portal, new Vector3(0f, 2.65f, 0f),
                new Vector3(1.92f, 0.18f, 0.3f), structure, true, true);
            Box("Portal Header Light", portal, new Vector3(0f, 2.65f, -0.16f),
                new Vector3(1.72f, 0.035f, 0.015f), trim, false, true);
            Box("Threshold Light", portal, new Vector3(0f, 0.012f, -0.2f),
                new Vector3(1.55f, 0.008f, 0.035f), warmTrim, false, true);

            // Stable sibling poses must never be parented to the animated leaf.
            result.DoorClosedPose = Empty("Door Closed Pose", parent, new Vector3(0f, 1.25f, 4.8f));
            result.DoorOpenPose = Empty("Door Open Pose", parent, new Vector3(1.7f, 1.25f, 4.8f));
            // A unit-scale root keeps pose copying independent of mesh dimensions.
            result.DoorLeaf = Empty("Sliding Door Leaf", parent, result.DoorClosedPose.localPosition);
            result.DoorLeaf.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.56f, 2.5f, 0.14f);
            Box("Door Panel", result.DoorLeaf, Vector3.zero,
                new Vector3(1.56f, 2.5f, 0.14f), dark, false);
            Box("Door Inset", result.DoorLeaf, new Vector3(0f, 0f, -0.075f),
                new Vector3(1.30f, 2.20f, 0.012f), structure, false);
            Box("Door Status Strip", result.DoorLeaf, new Vector3(0f, 0f, -0.09f),
                new Vector3(0.025f, 1.9f, 0.012f), warmTrim, false);
        }

        private static Transform BuildGuide(Transform parent, Material trim, Material warmTrim)
        {
            Transform guide = Empty("Guide Proxy", parent, new Vector3(1.4f, 1.5f, 2.4f));
            for (int i = 0; i < 3; i++)
            {
                Transform frame = Empty("Guide Orbit " + i, guide, Vector3.zero);
                frame.localRotation = Quaternion.Euler(25f + i * 35f, i * 60f, 30f + i * 40f);
                SquareFrame(frame, 0.28f + i * 0.045f, 0.014f, 0.014f, trim, false);
            }
            Primitive("Guide Core", PrimitiveType.Sphere, guide, Vector3.zero,
                Vector3.one * 0.09f, warmTrim, false);
            return guide;
        }

        private static Transform BuildObserver(Transform parent, Material material)
        {
            Transform observer = Empty("Distant Observer", parent, new Vector3(-2f, 0f, 4.2f));
            Primitive("Body", PrimitiveType.Capsule, observer, new Vector3(0f, 0.98f, 0f),
                new Vector3(0.34f, 0.42f, 0.24f), material, false);
            Primitive("Head", PrimitiveType.Capsule, observer, new Vector3(0f, 1.57f, 0f),
                new Vector3(0.22f, 0.15f, 0.22f), material, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive("Leg " + side, PrimitiveType.Capsule, observer,
                    new Vector3(side * 0.1f, 0.32f, 0f),
                    new Vector3(0.13f, 0.32f, 0.15f), material, false);
                GameObject arm = Primitive("Arm " + side, PrimitiveType.Capsule, observer,
                    new Vector3(side * 0.25f, 0.96f, 0f),
                    new Vector3(0.1f, 0.32f, 0.1f), material, false);
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, side * 8f);
            }
            return observer;
        }

        private static void BuildLighting(Transform parent)
        {
            Light light = Empty("Room Directional Light", parent, new Vector3(0f, 3.5f, 0f))
                .gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.localRotation = Quaternion.Euler(55f, -25f, 0f);
            light.color = new Color(0.83f, 0.92f, 1f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.27f, 0.33f);
        }

        private static Transform Empty(string name, Transform parent, Vector3 position)
        {
            Transform result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.localPosition = position;
            return result;
        }

        private static GameObject Box(
            string name, Transform parent, Vector3 position, Vector3 size,
            Material material, bool collider, bool isStatic = false)
        {
            return Primitive(name, PrimitiveType.Cube, parent, position, size, material, collider, isStatic);
        }

        private static GameObject Primitive(
            string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale,
            Material material, bool collider, bool isStatic = false)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = position;
            result.transform.localScale = scale;
            result.isStatic = isStatic;
            MeshRenderer renderer = result.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = collider ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (!collider)
                UnityEngine.Object.DestroyImmediate(result.GetComponent<Collider>());
            return result;
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
                throw new ArgumentException("Invalid palette color.", nameof(value));
            return color;
        }

        private static void EnsureAssetDirectory()
        {
            string[] segments = AssetDirectory.Split('/');
            string folder = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = folder + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(folder, segments[i]);
                    if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(next))
                        throw new InvalidOperationException("Cannot create material directory: " + next);
                }
                folder = next;
            }
        }

        private static Material GetMaterial(
            string name, Shader shader, Color color, float emission, float smoothness)
        {
            string path = AssetDirectory + "/" + name + ".mat";
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                // Never repaint, reconfigure, or overwrite an existing user asset.
                // Reject incompatible assets instead of silently losing URP/emission.
                if (existing is Material material && material.shader == shader &&
                    (emission <= 0f || (material.IsKeywordEnabled("_EMISSION") &&
                    material.GetColor("_EmissionColor").maxColorComponent > 0f)))
                    return material;
                throw new InvalidOperationException(
                    "Existing asset is not a compatible demo material; left unchanged: " + path);
            }
            // Also protect files that exist on disk but have not yet been imported.
            if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path) ||
                System.IO.File.Exists(path + ".meta"))
                throw new InvalidOperationException("Unimported asset path is occupied: " + path);

            var created = new Material(shader) { name = name };
            created.SetColor("_BaseColor", color);
            created.SetFloat("_Metallic", 0f);
            created.SetFloat("_Smoothness", smoothness);
            if (emission > 0f)
            {
                // Persist the keyword and a low nonzero baseline on the asset so
                // URP retains the emissive variant for later puzzle success effects.
                created.EnableKeyword("_EMISSION");
                created.SetColor("_EmissionColor", color * emission);
                // URP's material importer derives _EMISSION from AnyEmissive. BakedEmissive
                // preserves the variant without enabling runtime Enlighten/GI updates.
                created.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssetIfDirty(created);
            return created;
        }
    }
}
#endif
