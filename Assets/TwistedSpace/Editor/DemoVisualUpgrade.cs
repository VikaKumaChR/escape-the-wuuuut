#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Applies visual-only dressing to loaded demo prefab contents. The caller owns
    /// prefab/scene saving; this helper saves only its five Art*.mat assets.
    /// </summary>
    public static class DemoVisualUpgrade
    {
        private const string Materials = "Assets/TwistedSpace/Demo/Materials";
        private const string Art = "Assets/TwistedSpace/Demo/Art/";
        private const string UpgradeName = "Visual Upgrade";

        public static void Apply(GameObject prefabRoot)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Apply the visual upgrade in edit mode.");
            if (prefabRoot == null)
                throw new ArgumentNullException(nameof(prefabRoot));

            Transform environment = prefabRoot.transform.Find("Parallax Chamber/Demo Environment");
            if (environment == null)
                throw new InvalidOperationException("The generated Parallax Chamber/Demo Environment was not found.");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("The visual upgrade requires Universal Render Pipeline/Lit.");

            EnsureMaterialDirectory();
            Material wall = MaterialAsset("ArtWall", shader, new Color(0.18f, 0.23f, 0.29f),
                "Concrete.png", new Vector2(4f, 1.65f), 0.05f, 0.24f, 0.015f);
            Material floor = MaterialAsset("ArtFloor", shader, new Color(0.24f, 0.29f, 0.35f),
                "FloorPanels.png", new Vector2(3.5f, 4f), 0.18f, 0.32f, 0.012f);
            Material metal = MaterialAsset("ArtMetal", shader, new Color(0.34f, 0.41f, 0.48f),
                "BrushedMetal.png", Vector2.one, 0.35f, 0.42f, 0.018f);
            Material cyan = MaterialAsset("ArtCyan", shader, new Color(0.51f, 0.78f, 0.85f),
                null, Vector2.one, 0.12f, 0.4f, 0.65f);
            Material amber = MaterialAsset("ArtAmber", shader, new Color(0.85f, 0.43f, 0.17f),
                null, Vector2.one, 0.1f, 0.32f, 0.32f);

            DressExistingSurfaces(environment, wall, floor, metal, cyan, amber);
            Transform upgrade = Group(environment, UpgradeName);
            BuildWallPanels(upgrade, wall, metal);
            BuildCeiling(upgrade, wall, metal, cyan);
            BuildFloorMarkings(upgrade, metal, cyan);
            BuildHeader(upgrade, metal, cyan);
            DressProxies(environment, wall, metal, cyan, amber);
            FillLight(upgrade, "Observation Fill", new Vector3(0f, 2.65f, 0.2f),
                new Color(0.69f, 0.84f, 1f), 1.65f, 7f);
            FillLight(upgrade, "Portal Fill", new Vector3(0f, 2.45f, 4.1f),
                new Color(0.82f, 0.9f, 1f), 1.15f, 5f);
            ConfigureCamera(prefabRoot.transform);
        }

        private static void DressExistingSurfaces(Transform environment, Material wall,
            Material floor, Material metal, Material cyan, Material amber)
        {
            Assign(environment, "Room Shell/Floor", floor, "DemoFloor");
            foreach (string name in new[] { "Left Wall", "Right Wall", "Rear Wall", "Far Wall" })
                Assign(environment, "Room Shell/" + name, wall, "DemoStructure");
            for (int side = -1; side <= 1; side += 2)
            {
                Assign(environment, "Room Shell/Wall Base Rail " + side, wall, "DemoFloor");
                Assign(environment, "Room Shell/Perimeter Light " + side, cyan, "DemoCyanTrim");
                Assign(environment, "Room Shell/Exit Approach Line " + side, cyan, "DemoAmberTrim");
                Assign(environment, "Exit Portal/Portal Jamb " + side, metal, "DemoStructure");
                Assign(environment, "Exit Portal/Exit Side Panel " + side, wall, "DemoStructure");
                Assign(environment, "Exit Portal/Portal Light " + side, cyan, "DemoCyanTrim");
            }
            for (int i = 0; i < 3; i++)
            {
                string path = "Offset Structural Ribs/";
                Assign(environment, path + "Left Rib " + i, metal, "DemoStructure");
                Assign(environment, path + "Right Rib " + i, metal, "DemoStructure");
                Assign(environment, path + "Overhead Rib " + i, metal, "DemoStructure");
                Assign(environment, path + "Rib Light " + i, cyan, "DemoCyanTrim");
            }
            Assign(environment, "Exit Portal/Portal Lintel", metal, "DemoStructure");
            Assign(environment, "Exit Portal/Portal Header Light", cyan, "DemoCyanTrim");
            Assign(environment, "Exit Portal/Threshold Light", amber, "DemoAmberTrim");
            Assign(environment, "Sliding Door Leaf/Door Panel", wall, "DemoFloor");
            Assign(environment, "Sliding Door Leaf/Door Inset", metal, "DemoStructure");
            Assign(environment, "Sliding Door Leaf/Door Status Strip", amber, "DemoAmberTrim");
            foreach (string name in new[] { "Left", "Right", "Back", "Front" })
                Assign(environment, "Observation Ground Mark/" + name, cyan, "DemoCyanTrim");
            Assign(environment, "Observation Ground Mark/Forward Accent", amber, "DemoAmberTrim");
            // Target materials are deliberately untouched: runtime success highlighting owns them.
        }

        private static void BuildWallPanels(Transform root, Material wall, Material metal)
        {
            Transform panels = Group(root, "Recessed Wall Bays");
            for (int side = -1; side <= 1; side += 2)
            {
                for (int bay = 0; bay < 3; bay++)
                {
                    string id = side + "-" + bay;
                    float z = -0.75f + bay * 2.5f;
                    // The plate is recessed behind the shallow lips, not behind the shell.
                    Box(panels, "Inset " + id, new Vector3(side * 3.485f, 1.65f, z),
                        new Vector3(0.018f, 1.8f, 1.8f), metal);
                    Box(panels, "Upper Lip " + id, new Vector3(side * 3.46f, 2.58f, z),
                        new Vector3(0.06f, 0.06f, 1.92f), wall);
                    Box(panels, "Lower Lip " + id, new Vector3(side * 3.46f, 0.72f, z),
                        new Vector3(0.06f, 0.06f, 1.92f), wall);
                }
            }
        }

        private static void BuildCeiling(Transform root, Material wall, Material metal, Material cyan)
        {
            Transform ceiling = Group(root, "Slotted Ceiling");
            for (int row = 0; row < 3; row++)
            {
                float z = -0.65f + row * 2.65f;
                for (int column = 0; column < 3; column++)
                    Box(ceiling, "Cassette " + row + "-" + column,
                        new Vector3((column - 1) * 2.28f, 3.275f, z),
                        new Vector3(2.2f, 0.04f, 2.45f), metal);
                Box(ceiling, "Occlusion Band " + row, new Vector3(0f, 3.24f, z + 0.85f),
                    new Vector3(6.76f, 0.035f, 0.16f), wall);
                Box(ceiling, "Recessed Slot " + row, new Vector3(-0.2f + row * 0.2f, 3.217f, z + 0.85f),
                    new Vector3(2.2f - row * 0.25f, 0.008f, 0.022f), cyan);
            }
        }

        private static void BuildFloorMarkings(Transform root, Material metal, Material cyan)
        {
            Transform floor = Group(root, "Flush Floor Inlays");
            for (int side = -1; side <= 1; side += 2)
            {
                Box(floor, "Longitudinal Seam " + side, new Vector3(side * 2.65f, 0.003f, 1.8f),
                    new Vector3(0.014f, 0.002f, 7.3f), metal);
                for (int i = 0; i < 3; i++)
                    Box(floor, "Cross Seam " + side + "-" + i,
                        new Vector3(side * 2.15f, 0.003f, 0.45f + i * 1.65f),
                        new Vector3(2.3f, 0.002f, 0.012f), metal);
            }
            const int segments = 8;
            const float radius = 0.62f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                float b = (i + 1.5f) * Mathf.PI * 2f / segments;
                Vector3 start = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                Vector3 end = new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * radius;
                Transform strip = Box(floor, "Observation Ring " + i,
                    (start + end) * 0.5f + Vector3.up * 0.006f,
                    new Vector3(0.014f, 0.002f, Vector3.Distance(start, end)), cyan);
                strip.localRotation = Quaternion.LookRotation(end - start, Vector3.up);
            }
        }

        private static void BuildHeader(Transform root, Material metal, Material cyan)
        {
            Transform plaque = Group(root, "Architectural Header");
            Box(plaque, "Plaque Backplate", new Vector3(0f, 2.93f, 4.76f),
                new Vector3(1.9f, 0.25f, 0.035f), metal);
            Box(plaque, "Plaque Index", new Vector3(-0.88f, 2.93f, 4.736f),
                new Vector3(0.025f, 0.16f, 0.008f), cyan);
        }

        private static void DressProxies(Transform environment, Material wall,
            Material metal, Material cyan, Material amber)
        {
            Transform observer = environment.Find("Distant Observer");
            if (observer != null)
            {
                foreach (string name in new[] { "Body", "Head", "Leg -1", "Leg 1", "Arm -1", "Arm 1" })
                    Assign(observer, name, wall, "DemoObserver");
                Transform details = Group(observer, UpgradeName);
                Box(details, "Survey Vest", new Vector3(0f, 1.12f, -0.105f),
                    new Vector3(0.27f, 0.34f, 0.055f), metal);
                Box(details, "Helmet Visor", new Vector3(0f, 1.59f, -0.099f),
                    new Vector3(0.19f, 0.06f, 0.025f), metal);
                Box(details, "Survey Badge", new Vector3(0.065f, 1.19f, -0.136f),
                    new Vector3(0.035f, 0.06f, 0.006f), cyan);
            }
            Transform guide = environment.Find("Guide Proxy");
            if (guide != null)
            {
                for (int i = 0; i < 3; i++)
                    foreach (string bar in new[] { "Top Bar", "Bottom Bar", "Left Bar", "Right Bar" })
                        Assign(guide, "Guide Orbit " + i + "/" + bar, i == 1 ? cyan : metal, "DemoCyanTrim");
                Assign(guide, "Guide Core", amber, "DemoAmberTrim");
                Transform details = Group(guide, UpgradeName);
                Box(details, "Survey Sensor Housing", new Vector3(0f, -0.015f, 0.025f),
                    new Vector3(0.11f, 0.15f, 0.07f), metal);
                Box(details, "Instrument Index", new Vector3(0.04f, 0.035f, -0.014f),
                    new Vector3(0.012f, 0.05f, 0.006f), cyan);
            }
        }

        private static void ConfigureCamera(Transform root)
        {
            Transform head = root.Find("Twisted Space XR/XR Origin/Camera Offset/Main Camera");
            Camera camera = head != null ? head.GetComponent<Camera>() : null;
            if (camera == null)
                return;
            // Keep the head pose and projection/FOV: they are part of the alignment puzzle.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.052f, 0.075f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.allowMSAA = true;
        }

        private static void Assign(Transform root, string path, Material material, string original)
        {
            Transform child = root.Find(path);
            MeshRenderer renderer = child != null ? child.GetComponent<MeshRenderer>() : null;
            if (renderer == null || renderer.sharedMaterials.Length != 1)
                return;
            Material current = renderer.sharedMaterial;
            // Preserve a user's replacement material, even on an autogenerated object.
            if (current != material && AssetDatabase.GetAssetPath(current) != Materials + "/" + original + ".mat")
                return;
            renderer.sharedMaterial = material;
        }

        private static Transform Group(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;
            Transform result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }

        private static Transform Box(Transform parent, string name, Vector3 position,
            Vector3 size, Material material)
        {
            Transform result = parent.Find(name);
            bool isNew = result == null;
            if (result == null)
            {
                GameObject created = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.name = name;
                result = created.transform;
                result.SetParent(parent, false);
                UnityEngine.Object.DestroyImmediate(created.GetComponent<Collider>());
            }
            MeshRenderer renderer = result.GetComponent<MeshRenderer>();
            if (renderer == null || result.GetComponent<Collider>() != null)
                throw new InvalidOperationException("An incompatible object occupies the visual detail: " + name);
            result.localPosition = position;
            result.localRotation = Quaternion.identity;
            result.localScale = size;
            if (isNew || (renderer.sharedMaterials.Length == 1 && IsOwnedMaterial(renderer.sharedMaterial)))
                renderer.sharedMaterial = material;
            // No shadow maps or realtime probes for the decorative dressing. In
            // particular the ceiling must not block the original directional key.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return result;
        }

        private static bool IsOwnedMaterial(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            return path == Materials + "/ArtWall.mat" || path == Materials + "/ArtFloor.mat" ||
                path == Materials + "/ArtMetal.mat" || path == Materials + "/ArtCyan.mat" ||
                path == Materials + "/ArtAmber.mat";
        }

        private static void FillLight(Transform root, string name, Vector3 position,
            Color color, float intensity, float range)
        {
            Transform holder = Group(root, name);
            holder.localPosition = position;
            Light light = holder.GetComponent<Light>();
            if (light == null)
                light = holder.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.bounceIntensity = 0f;
        }

        private static Material MaterialAsset(string name, Shader shader, Color color,
            string textureName, Vector2 tiling, float metallic, float smoothness, float emission)
        {
            string path = Materials + "/" + name + ".mat";
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            Material material = existing as Material;
            if (existing != null && material == null)
                throw new InvalidOperationException("The owned material path contains a different asset: " + path);
            if (material == null)
            {
                if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta"))
                    throw new InvalidOperationException("The unimported material path is occupied: " + path);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            Texture2D texture = textureName == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Art + textureName);
            if (textureName != null && texture == null)
                Debug.LogWarning("Visual upgrade texture is not imported yet: " + Art + textureName +
                    ". Apply again after importing it; the untextured palette remains usable.");
            material.shader = shader;
            material.enableInstancing = true;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void EnsureMaterialDirectory()
        {
            string[] segments = Materials.Split('/');
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
    }
}
#endif
