using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabFacilityBuilder
    {
        private static Mesh hull;
        private static Material navy, metal, dark, cyan, purple;
        private static TMP_FontAsset font;

        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before authoring the research hall.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null || app.menuSpace == null || app.chapters == null || app.chapters.Length != 4)
                throw new InvalidOperationException("The existing DEPTHLAB app and four chapters are required.");
            if (!AssetDatabase.IsValidFolder("Assets/DepthLab/Generated/Presentation"))
                AssetDatabase.CreateFolder("Assets/DepthLab/Generated", "Presentation");
            hull = DepthLabPresentationUpgrade.ProfileMesh("ChamferedCase", false);
            navy = DepthLabPresentationUpgrade.Material("FacilityNavy", Hex("081329"), .4f, .45f);
            dark = DepthLabPresentationUpgrade.Material("FacilityGraphite", Hex("050816"), .2f, .35f);
            metal = DepthLabPresentationUpgrade.Material("FacilityTitanium", Hex("496477"), .65f, .5f);
            cyan = DepthLabPresentationUpgrade.Material("FacilityCyan", Hex("00E5FF"), .3f, .55f, .65f);
            purple = DepthLabPresentationUpgrade.Material("FacilityPurple", Hex("A855F7"), .3f, .5f, .4f);
            font = app.hudText.font;
            Undo.RegisterFullObjectHierarchyUndo(app.menuSpace, "Author coherent research facility");
            Transform hall = app.menuSpace.transform;
            FitBox(hall.Find("Floor"), new Vector3(0, -.12f, 0), new Vector3(20, .24f, 20), navy);
            FitBox(hall.Find("Far wall"), new Vector3(0, 2.5f, 10), new Vector3(20, 5, .2f), dark);
            FitBox(hall.Find("Arrival front wall"), new Vector3(0, 2.5f, -10), new Vector3(20, 5, .2f), dark);
            FitBox(hall.Find("Arrival left wall"), new Vector3(-10, 2.5f, 0), new Vector3(.2f, 5, 20), dark);
            FitBox(hall.Find("Arrival right wall"), new Vector3(10, 2.5f, 0), new Vector3(.2f, 5, 20), dark);
            Transform platform = hall.Find("Central arrival platform");
            platform.localPosition = new Vector3(0, -.14f, 0);
            platform.GetComponent<Renderer>().sharedMaterial = metal;
            Transform structure = Node(hall, "ResearchFacility");
            Solid(structure, "Ceiling", new Vector3(0, 5.05f, 0), new Vector3(20, .25f, 20), dark, true);
            for (int i = -2; i <= 2; i++)
            {
                Solid(structure, "CeilingBeam" + i, new Vector3(i * 3.6f, 4.8f, 0),
                    new Vector3(.22f, .35f, 19.5f), metal, false);
                Solid(structure, "CeilingLuminaire" + i, new Vector3(i * 3.6f, 4.55f, 1),
                    new Vector3(.07f, .035f, 11), cyan, false);
            }
            for (int side = -1; side <= 1; side += 2)
                for (int bay = 0; bay < 5; bay++)
                {
                    Solid(structure, $"WallColumn{side}_{bay}", new Vector3(side * 9.75f, 2.4f, -8 + bay * 4),
                        new Vector3(.3f, 4.8f, .5f), metal, true);
                    Solid(structure, $"WallInset{side}_{bay}", new Vector3(side * 9.87f, 2.3f, -6 + bay * 3),
                        new Vector3(.08f, 2.5f, 2.6f), navy, false);
                }
            Vector3[] entries = { new Vector3(-6, 0, 6), new Vector3(6, 0, 6),
                new Vector3(-6, 0, -5), new Vector3(6, 0, -5) };
            for (int i = 0; i < 4; i++)
            {
                Transform bay = Node(structure, "ExperimentEntrance" + i);
                bay.localPosition = entries[i];
                bay.localRotation = i < 2 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                BuildEntrance(bay, app, i);
                for (int marker = 0; marker < 5; marker++)
                    Solid(structure, $"Route{i}_{marker}",
                        new Vector3(entries[i].x * (marker + 1) / 6f, .012f, entries[i].z * (marker + 1) / 6f),
                        new Vector3(.35f, .008f, .05f), i == 0 ? cyan : metal, false);
            }
            BuildResearchDesk(structure, app);
            Solid(structure, "RestBenchSeat", new Vector3(7.8f, .48f, 0),
                new Vector3(1.1f, .18f, 3.2f), metal, true);
            Solid(structure, "RestBenchBase", new Vector3(7.8f, .24f, 0),
                new Vector3(.65f, .48f, 2.5f), dark, true);
            Physics.SyncTransforms();
            EditorUtility.SetDirty(app.menuSpace);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        private static void BuildEntrance(Transform bay, DepthLabApp app, int index)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Solid(bay, "DoorJamb" + side, new Vector3(side, 1.4f, 0),
                    new Vector3(.22f, 2.9f, .65f), metal, true);
                Solid(bay, "DoorLight" + side, new Vector3(side * .86f, 1.4f, -.34f),
                    new Vector3(.035f, 2.4f, .018f), index == 0 ? cyan : purple, false);
                Solid(bay, "TransitionWall" + side, new Vector3(side * 1.25f, 1.5f, 1.4f),
                    new Vector3(.4f, 3f, 2.8f), navy, true);
            }
            Solid(bay, "DoorLintel", new Vector3(0, 2.94f, 0), new Vector3(2.25f, .22f, .65f), dark, true);
            Solid(bay, "TransitionCeiling", new Vector3(0, 3f, 1.4f), new Vector3(2.1f, .12f, 2.8f), dark, true);
            Solid(bay, "TransitionEnd", new Vector3(0, 1.5f, 2.8f), new Vector3(2.1f, 3f, .2f), dark, true);
            Transform gate = Solid(bay, "EntranceGate", new Vector3(0, 1.4f, .05f),
                new Vector3(1.65f, 2.65f, .1f), navy, true);
            Renderer signal = Solid(bay, "ReadySignal", new Vector3(0, 3.12f, -.3f),
                new Vector3(1.4f, .045f, .045f), cyan, false).GetComponent<Renderer>();
            var portal = bay.GetComponent<DepthLabHallPortal>();
            if (portal == null) portal = Undo.AddComponent<DepthLabHallPortal>(bay.gameObject);
            portal.app = app;
            portal.chapterIndex = index;
            portal.gate = gate;
            portal.indicator = signal;
            portal.readyMaterial = cyan;
            portal.lockedMaterial = purple;
            Transform sign = Canvas(bay, "EntranceSign", new Vector3(-1.8f, 1.7f, -.1f), new Vector2(560, 550), app);
            Label(sign, "Number", $"实验 {index + 1:00}", new Vector2(20, -20), new Vector2(520, 60), 35, Hex("00E5FF"));
            Label(sign, "Title", DepthLabProgress.Titles[index], new Vector2(20, -94), new Vector2(520, 95), 39, Hex("EAF6FF"));
            portal.status = Label(sign, "Status", "实验入口", new Vector2(20, -210), new Vector2(520, 110), 29, Hex("83A9C9"));
            Transform action = Node(sign, "EnterButton", true);
            RectTransform rect = (RectTransform)action;
            Place(rect, new Vector2(20, -360), new Vector2(520, 100));
            var graphic = action.GetComponent<UnityEngine.UI.Image>();
            if (graphic == null) graphic = Undo.AddComponent<UnityEngine.UI.Image>(action.gameObject);
            graphic.color = Hex("1677FF");
            var button = action.GetComponent<DepthLabButton>();
            if (button == null) button = Undo.AddComponent<DepthLabButton>(action.gameObject);
            button.targetGraphic = graphic;
            button.tooltip = "进入对应的视觉科学实验";
            if (button.onClick.GetPersistentEventCount() == 0)
                UnityEventTools.AddPersistentListener(button.onClick, portal.Enter);
            portal.enterButton = button;
            Label(action, "Caption", "进入实验", new Vector2(18, -14), new Vector2(484, 72), 33, Hex("EAF6FF"));
        }

        private static void BuildResearchDesk(Transform root, DepthLabApp app)
        {
            Transform desk = Node(root, "ResearchDesk");
            desk.localPosition = new Vector3(-7.8f, 0, 0);
            Solid(desk, "DeskBase", new Vector3(0, .45f, 0), new Vector3(1.2f, .9f, 2.5f), dark, true);
            Solid(desk, "DeskSurface", new Vector3(0, 1f, 0), new Vector3(1.8f, .16f, 2.8f), metal, true);
            Transform screen = Canvas(desk, "ResearchScreen", new Vector3(.3f, 1.9f, 0), new Vector2(750, 600), app);
            screen.localRotation = Quaternion.Euler(0, -90, 0);
            Label(screen, "ResearchHeader", "感知研究中心", new Vector2(25, -25), new Vector2(700, 65), 40, Hex("00E5FF"));
            Label(screen, "ResearchDescription", "观察 · 判断 · 验证\n四项实验，探索视觉的边界。\n\n沿青色地面标记进入第一实验室。\n也可以与诺娃交流或打开操作教程。",
                new Vector2(25, -130), new Vector2(700, 280), 33, Hex("EAF6FF"));
        }

        private static Transform Canvas(Transform parent, string name, Vector3 position, Vector2 size, DepthLabApp app)
        {
            Transform node = Node(parent, name, true);
            node.localPosition = position;
            node.localScale = Vector3.one * .0015f;
            ((RectTransform)node).sizeDelta = size;
            var canvas = node.GetComponent<UnityEngine.Canvas>();
            if (canvas == null) canvas = Undo.AddComponent<UnityEngine.Canvas>(node.gameObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            if (node.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                Undo.AddComponent<TrackedDeviceGraphicRaycaster>(node.gameObject);
            var backing = node.GetComponent<UnityEngine.UI.Image>();
            if (backing == null) backing = Undo.AddComponent<UnityEngine.UI.Image>(node.gameObject);
            backing.color = Hex("081329");
            backing.raycastTarget = false;
            return node;
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 position,
            Vector2 size, float fontSize, Color color)
        {
            Transform node = Node(parent, name, true);
            Place((RectTransform)node, position, size);
            var label = node.GetComponent<TextMeshProUGUI>();
            if (label == null) label = Undo.AddComponent<TextMeshProUGUI>(node.gameObject);
            label.font = font;
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.text = text;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Transform Solid(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, bool collides)
        {
            Transform node = Node(parent, name);
            node.localPosition = position;
            node.localScale = scale;
            var mesh = node.GetComponent<MeshFilter>();
            if (mesh == null) mesh = Undo.AddComponent<MeshFilter>(node.gameObject);
            mesh.sharedMesh = hull;
            var renderer = node.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(node.gameObject);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (collides)
            {
                var collider = node.GetComponent<MeshCollider>();
                if (collider == null) collider = Undo.AddComponent<MeshCollider>(node.gameObject);
                collider.sharedMesh = hull;
                collider.convex = true;
            }
            return node;
        }

        private static void FitBox(Transform node, Vector3 position, Vector3 scale, Material material)
        {
            if (node == null) throw new InvalidOperationException("The existing hall boundary is missing.");
            node.localPosition = position;
            node.localScale = scale;
            node.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Transform Node(Transform parent, string name, bool rect = false)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            GameObject node = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(node, "Author research facility");
            node.transform.SetParent(parent, false);
            return node.transform;
        }

        private static Color Hex(string hex)
        {
            if (!ColorUtility.TryParseHtmlString("#" + hex, out Color color))
                throw new ArgumentException("Invalid color.", nameof(hex));
            return color;
        }
    }
}
