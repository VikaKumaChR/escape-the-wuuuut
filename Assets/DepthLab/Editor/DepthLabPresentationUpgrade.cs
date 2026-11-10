using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabPresentationUpgrade
    {
        private const string Folder = "Assets/DepthLab/Generated/Presentation";
        private static readonly Color Ink = Hex("252B2C");
        private static readonly Color Paper = Hex("E6E2D8");
        private static readonly Color Muted = Hex("B5BEBB");
        private static readonly Color Cyan = Hex("58CFCC");
        private static readonly Color[] Identities = { Hex("5299D6"), Hex("AB8BCB"), Hex("57BDAA") };

        private sealed class Kit
        {
            public Mesh bevel, support;
            public Material floor, wall, metal, dark, cyan, white, seam, ready, waiting;
            public readonly Material[] identity = new Material[3];
            public readonly Material[] shape = new Material[3];
        }

        [MenuItem("DEPTHLAB/Upgrade Presentation In Place")]
        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before upgrading the authored scene.");
            Scene scene = SceneManager.GetActiveScene();
            DepthLabApp app = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (DepthLabApp candidate in root.GetComponentsInChildren<DepthLabApp>(true))
                {
                    if (app != null) throw new InvalidOperationException("Expected one DEPTHLAB app.");
                    app = candidate;
                }
            if (app == null || app.chapters == null || app.chapters.Length == 0 ||
                !(app.chapters[0] is DepthSortingChapter sorting) ||
                app.hud == null || app.hudText == null || app.hintText == null)
                throw new InvalidOperationException("Open the installed DEPTHLAB scene with its authored HUD.");
            if (sorting.pieces == null || sorting.pieces.Length != 3 ||
                sorting.slots == null || sorting.slots.Length != 3 ||
                sorting.slotIndicators == null || sorting.slotIndicators.Length != 3)
                throw new InvalidOperationException("The three authored sorting pieces and slots are required.");
            if (app.hud.GetComponent<Canvas>() == null ||
                app.hud.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length != 3)
                throw new InvalidOperationException("Expected the existing HUD Canvas and its three wired buttons.");
            int eventSystems = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                eventSystems += root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length;
            if (eventSystems != 1)
                throw new InvalidOperationException("Preserve exactly one existing EventSystem before applying.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade DEPTHLAB presentation in place");
            Undo.RegisterFullObjectHierarchyUndo(app.gameObject, "Preserve authored laboratory");
            Kit kit = CreateKit();
            UpgradeRoom(sorting, kit);
            UpgradeStations(sorting, app, kit);
            UpgradeInstructions(sorting, app, kit);
            foreach (DepthLabChapter chapter in app.chapters)
                if (chapter is LabChapter lab) UpgradeDoor(lab, app, kit);
            UpgradeHud(app);
            Physics.SyncTransforms();
            Undo.CollapseUndoOperations(undo);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("DEPTHLAB presentation upgraded in place: original piece/home/slot centers and HUD callbacks " +
                "preserved; warm laboratory, instrument bench, staggered bays, marked clear route, recessed doors " +
                "and desktop/XR HUD installed. Scene intentionally not saved; caller owns save/render validation.");
        }

        private static void UpgradeRoom(DepthSortingChapter chapter, Kit kit)
        {
            foreach (Transform child in chapter.transform)
            {
                Renderer renderer = child.GetComponent<Renderer>();
                Material material = null;
                switch (child.name)
                {
                    case "Lab floor": material = kit.floor; break;
                    case "Back wall":
                    case "Front wall":
                    case "Side wall": material = kit.wall; break;
                    case "Structural rib": material = kit.metal; MatchExistingBox(child); break;
                    case "Floor longitudinal seam":
                    case "Floor transverse seam": material = kit.seam; break;
                    case "Floor perimeter light":
                    case "Wall horizon": material = kit.seam; break;
                    case "Rib status": material = kit.cyan; break;
                }
                if (renderer != null && material != null) renderer.sharedMaterial = material;
                if (child.name == "Soft laboratory light")
                {
                    Light light = child.GetComponent<Light>();
                    if (light != null)
                    {
                        light.color = Hex("FFF1D8");
                        light.intensity = 2.4f;
                        light.range = 11f;
                    }
                    Transform housing = child.Find("Light housing");
                    if (housing != null) housing.GetComponent<Renderer>().sharedMaterial = kit.white;
                }
            }

            Transform root = Node(chapter.transform, "PresentationArchitecture");
            // The authored first home/slot occupies x=-.45. Keep its references and route
            // pedestrians to its left instead of putting invisible obstacles in a central aisle.
            for (int i = 0; i < 8; i++)
                Solid(root, $"AisleDash{i:00}", new Vector3(-1.65f, .006f, -2.7f + i * .95f),
                    new Vector3(.045f, .008f, .48f), kit.bevel, kit.cyan, false);
            Solid(root, "AisleExitTurn", new Vector3(-.84f, .006f, 4.65f),
                new Vector3(1.65f, .008f, .045f), kit.bevel, kit.cyan, false);
            for (int i = 0; i < 3; i++)
                Solid(root, $"ExitApproach{i}", new Vector3(0f, .006f, 4.8f + i * .36f),
                    new Vector3(.045f, .008f, .20f), kit.bevel, kit.cyan, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Solid(root, $"WallServiceRail{side}", new Vector3(side * 5.79f, .32f, 0f),
                    new Vector3(.12f, .25f, 13f), kit.bevel, kit.dark);
                for (int bay = 0; bay < 4; bay++)
                    Solid(root, $"WallInsert{side}_{bay}", new Vector3(side * 5.83f, 2.0f, -3.9f + bay * 2.7f),
                        new Vector3(.08f, 1.65f, 2.18f), kit.bevel, kit.metal);
            }
            TMP_Text wallTitle = WorldLabel(root, "LabIdentity", chapter.objectiveLabel.font,
                new Vector3(-3.55f, 2.45f, 6.85f), new Vector2(1100, 340), .0025f, null);
            wallTitle.text = "01 / 深度实验室\n观察距离，让形状归位";
            Style(wallTitle, 54, Ink);
        }

        private static void UpgradeStations(DepthSortingChapter chapter, DepthLabApp app, Kit kit)
        {
            Transform root = Node(chapter.transform, "PresentationStations");
            string[] names = { "01  立方体", "02  球体", "03  三棱柱" };
            string[] distances = { "近场", "中场", "远场" };
            for (int i = 0; i < 3; i++)
            {
                ChapterPiece piece = chapter.pieces[i];
                if (piece == null || piece.home == null || chapter.slots[i] == null ||
                    chapter.slotIndicators[i] == null)
                    throw new InvalidOperationException("Missing authored sorting reference.");
                Vector3 home = chapter.transform.InverseTransformPoint(piece.home.position);
                Vector3 slot = chapter.transform.InverseTransformPoint(chapter.slots[i].position);
                Transform launch = Closest(chapter.transform, "Launch pedestal", home);
                Transform rim = Closest(chapter.transform, "Launch rim", home);
                Transform target = Closest(chapter.transform, "Socket plinth", slot);
                if (launch == null || rim == null || target == null)
                    throw new InvalidOperationException("Expected the existing launch and target supports.");

                ReplaceSolid(launch, new Vector3(home.x, .43f, home.z),
                    new Vector3(.65f, .86f, .62f), kit.support, kit.metal);
                ReplaceSolid(rim, new Vector3(home.x, .90f, home.z),
                    new Vector3(.88f, .08f, .72f), kit.bevel, kit.dark);
                ReplaceSolid(target, new Vector3(slot.x, .43f, slot.z),
                    new Vector3(.58f, .86f, .64f), kit.support, kit.metal);
                ReplaceSolid(chapter.slotIndicators[i].transform, new Vector3(slot.x, .90f, slot.z),
                    new Vector3(.90f, .08f, .78f), kit.bevel, kit.identity[i]);
                Renderer shape = piece.GetComponent<Renderer>();
                if (shape != null)
                {
                    shape.sharedMaterial = kit.shape[i];
                    shape.shadowCastingMode = ShadowCastingMode.On;
                    shape.receiveShadows = true;
                }
                foreach (Renderer outline in chapter.slots[i].GetComponentsInChildren<Renderer>(true))
                    outline.sharedMaterial = kit.identity[i];
                Solid(root, $"LaunchIdentity{i}", new Vector3(home.x, .904f, home.z - .368f),
                    new Vector3(.54f, .035f, .008f), kit.bevel, kit.identity[i], false);
                Solid(root, $"TargetFoot{i}", new Vector3(slot.x, .045f, slot.z),
                    new Vector3(.74f, .09f, .79f), kit.bevel, kit.dark);
                Solid(root, $"TargetDeck{i}", new Vector3(slot.x, .927f, slot.z),
                    new Vector3(.81f, .026f, .69f), kit.bevel, kit.dark);
                Solid(root, $"TargetPlaqueBacking{i}", new Vector3(slot.x, .66f, slot.z - .43f),
                    new Vector3(.94f, .27f, .04f), kit.bevel, kit.dark);

                Transform plaque = Closest(chapter.transform, "Depth plaque", slot);
                if (plaque != null)
                {
                    RectTransform rect = (RectTransform)plaque;
                    rect.localPosition = new Vector3(slot.x, .66f, slot.z - .455f);
                    rect.localRotation = Quaternion.identity;
                    rect.localScale = Vector3.one * .00155f;
                    rect.sizeDelta = new Vector2(580, 160);
                    TMP_Text title = FindText(plaque, "Shape name");
                    TMP_Text depth = FindText(plaque, "Depth measurement");
                    if (title != null)
                    {
                        title.text = names[i];
                        Layout(title.rectTransform, 20, 12, 540, 58);
                        Style(title, 35, Paper);
                    }
                    if (depth != null)
                    {
                        depth.text = $"{distances[i]}  /  {slot.z - chapter.spawnPoint.localPosition.z:0.0} m";
                        Layout(depth.rectTransform, 20, 83, 540, 52);
                        Style(depth, 28, Muted);
                    }
                }
                TMP_Text homeLabel = WorldLabel(root, $"LaunchLabel{i}", app.hudText.font,
                    new Vector3(home.x, .73f, home.z - .372f), new Vector2(560, 92), .0014f, app.headCamera);
                homeLabel.text = names[i];
                Style(homeLabel, 34, Paper);
                Solid(root, $"LaunchLabelBacking{i}", new Vector3(home.x, .73f, home.z - .35f),
                    new Vector3(.82f, .16f, .035f), kit.bevel, kit.dark);
            }
            chapter.emptySlotMaterials = kit.identity;
            chapter.filledSlotMaterial = kit.ready;
            // A rear rail joins the original launch centers into one instrument bench.
            Vector3 first = chapter.transform.InverseTransformPoint(chapter.pieces[0].home.position);
            Vector3 last = chapter.transform.InverseTransformPoint(chapter.pieces[2].home.position);
            Solid(root, "WorkstationSpine", new Vector3((first.x + last.x) * .5f, .70f, first.z + .24f),
                new Vector3(last.x - first.x + .68f, .22f, .18f), kit.bevel, kit.metal);
            foreach (Transform child in chapter.transform)
                if (child.name == "Depth tick")
                {
                    Renderer renderer = child.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = kit.seam;
                }
            EditorUtility.SetDirty(chapter);
        }

        private static void UpgradeInstructions(DepthSortingChapter chapter, DepthLabApp app, Kit kit)
        {
            Transform console = chapter.transform.Find("Chapter instrument console");
            if (console == null) return;
            RectTransform rect = (RectTransform)console;
            rect.localPosition = new Vector3(-3.38f, 1.58f, -.75f);
            rect.localRotation = Quaternion.Euler(0, -22, 0);
            rect.localScale = Vector3.one * .0018f;
            rect.sizeDelta = new Vector2(960, 1020);
            UnityEngine.UI.Image background = console.GetComponent<UnityEngine.UI.Image>();
            if (background != null) { background.color = Ink; background.raycastTarget = false; }
            SetText(console, "Chapter eyebrow", "DEPTHLAB  /  01", 38, 28, 884, 48, 30, Cyan);
            SetText(console, "Chapter title", "看清远近\n让三件形状归位", 38, 96, 884, 150, 58, Paper);
            PlaceText(chapter.objectiveLabel, 38, 272, 884, 120, 33, Paper);
            PlaceText(chapter.progressLabel, 38, 418, 884, 64, 42, Cyan);
            PlaceText(chapter.hintLabel, 38, 694, 884, 285, 28, Muted);
            SetText(console, "Technique", "01  抓取实体    02  对准轮廓    03  松开\n\n完成后沿地面导向走入出口",
                38, 516, 884, 150, 30, Paper);
            Transform root = Node(chapter.transform, "PresentationArchitecture");
            Solid(root, "InstructionStand", new Vector3(-3.38f, .33f, -.65f),
                new Vector3(.76f, .66f, .64f), kit.support, kit.metal);
            Transform backing = Solid(root, "InstructionHousing", rect.localPosition + rect.forward * .04f,
                new Vector3(1.80f, 1.91f, .08f), kit.bevel, kit.dark);
            backing.localRotation = rect.localRotation;
        }

        private static void UpgradeDoor(LabChapter chapter, DepthLabApp app, Kit kit)
        {
            if (chapter.exitRenderer == null) return;
            Transform exit = chapter.exitRenderer.transform.parent;
            if (chapter.exitButton != null) chapter.exitButton.gameObject.SetActive(false);
            chapter.lockedExitMaterial = kit.waiting;
            chapter.openExitMaterial = kit.ready;
            chapter.exitRenderer.sharedMaterial = chapter.ExitReady ? kit.ready : kit.waiting;
            foreach (string name in new[] { "Sliding exit left panel", "Sliding exit right panel" })
            {
                Transform leaf = exit.Find(name);
                if (leaf == null) continue;
                Renderer renderer = leaf.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = kit.metal;
                MatchExistingBox(leaf);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Transform jamb = exit.Find(side < 0 ? "Exit left jamb" : "Exit right jamb");
                if (jamb != null)
                    ReplaceSolid(jamb, new Vector3(side * .97f, 1.36f, -.16f),
                        new Vector3(.18f, 2.72f, .28f), kit.bevel, kit.dark);
                Solid(exit, $"PresentationPocketFace{side}", new Vector3(side * 1.43f, 1.36f, -.16f),
                    new Vector3(.76f, 2.72f, .12f), kit.bevel, kit.wall);
                Solid(exit, $"PresentationPocketRear{side}", new Vector3(side * 1.43f, 1.36f, .40f),
                    new Vector3(.94f, 2.72f, .12f), kit.bevel, kit.dark);
                Solid(exit, $"PresentationPocketEnd{side}", new Vector3(side * 1.94f, 1.36f, .10f),
                    new Vector3(.12f, 2.72f, .72f), kit.bevel, kit.metal);
                Solid(exit, $"PresentationJambLight{side}", new Vector3(side * .865f, 1.36f, -.308f),
                    new Vector3(.025f, 2.34f, .008f), kit.bevel, kit.cyan, false);
            }
            Solid(exit, "PresentationHeader", new Vector3(0, 2.99f, .02f),
                new Vector3(4.0f, .51f, .76f), kit.bevel, kit.dark);
            Transform lintel = chapter.exitRenderer.transform;
            ReplaceSolid(lintel, new Vector3(0, 2.705f, -.32f),
                new Vector3(1.75f, .04f, .035f), kit.bevel, chapter.lockedExitMaterial, false);
            Transform threshold = exit.Find("Exit threshold");
            if (threshold != null)
                ReplaceSolid(threshold, new Vector3(0, .009f, .05f),
                    new Vector3(1.75f, .018f, .72f), kit.bevel, kit.metal);

            Transform ui = exit.Find("Exit use panel");
            if (ui == null) return;
            RectTransform rect = (RectTransform)ui;
            rect.localPosition = new Vector3(0, 2.99f, -.375f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * .002f;
            rect.sizeDelta = new Vector2(1500, 215);
            TMP_Text status = FindText(ui, "Exit label");
            TMP_Text instruction = FindText(ui, "Exit condition");
            if (status != null)
            {
                status.text = "出口  /  等待实验完成";
                PlaceText(status, 30, 18, 1440, 88, 52, Paper);
                status.alignment = TextAlignmentOptions.Center;
            }
            if (instruction != null)
            {
                instruction.text = "完成目标后，直接穿过门口";
                PlaceText(instruction, 30, 118, 1440, 66, 34, Muted);
                instruction.alignment = TextAlignmentOptions.Center;
            }
            if (chapter.exitDoor != null)
            {
                var door = new SerializedObject(chapter.exitDoor);
                bool statusWired = AssignText(door, "statusLabel", status);
                bool instructionWired = AssignText(door, "instructionLabel", instruction);
                door.ApplyModifiedProperties();
                if (!statusWired || !instructionWired)
                    Debug.LogWarning("Presentation: door statusLabel/instructionLabel not yet available on " +
                        chapter.name + ". Re-run Apply after the walkthrough door fields compile.");
            }
            EditorUtility.SetDirty(chapter);
        }

        private static void UpgradeHud(DepthLabApp app)
        {
            Transform hud = app.hud.transform;
            RectTransform panel = hud.Find("PresentationPanel") as RectTransform;
            if (panel == null)
            {
                var go = new GameObject("PresentationPanel", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Add HUD presentation surface");
                panel = (RectTransform)go.transform;
                panel.SetParent(hud, false);
            }
            var children = new List<Transform>();
            foreach (Transform child in hud) if (child != panel) children.Add(child);
            foreach (Transform child in children) Undo.SetTransformParent(child, panel, "Retain authored HUD controls");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(960, 500);
            panel.anchoredPosition3D = Vector3.zero;
            panel.localScale = Vector3.one;
            panel.localRotation = Quaternion.identity;
            UnityEngine.UI.Image glass = panel.Find("HUD glass")?.GetComponent<UnityEngine.UI.Image>();
            UnityEngine.UI.Image edge = panel.Find("HUD edge")?.GetComponent<UnityEngine.UI.Image>();
            if (glass != null)
            {
                Layout(glass.rectTransform, 0, 0, 960, 500);
                glass.color = Ink;
                glass.raycastTarget = true;
                glass.transform.SetAsFirstSibling();
            }
            if (edge != null)
            {
                Layout(edge.rectTransform, 0, 0, 5, 500);
                edge.color = Cyan;
                edge.raycastTarget = false;
            }
            PlaceText(app.hudText, 28, 65, 904, 115, 30, Paper);
            PlaceText(app.hintText, 28, 188, 904, 164, 26, Muted);
            TMP_Text step = TextNode(panel, "PresentationStep", app.hudText.font);
            step.text = "01  抓取   /   02  归位   /   03  通行";
            PlaceText(step, 28, 18, 904, 38, 26, Cyan);
            TMP_Text controls = TextNode(panel, "PresentationControls", app.hudText.font);
            controls.text = "左键抓取 · 滚轮远近 · 右键观察 · WASD 移动";
            PlaceText(controls, 28, 366, 904, 37, 24, Muted);
            UnityEngine.UI.Button[] buttons = panel.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                UnityEngine.UI.Button button = buttons[i];
                Layout((RectTransform)button.transform, 28 + i * 308, 420, 288, 60);
                foreach (UnityEngine.UI.Image image in button.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    bool halo = image.name == "Interaction halo";
                    Layout(image.rectTransform, halo ? -3 : 0, halo ? -3 : 0, halo ? 294 : 288, halo ? 66 : 60);
                    image.raycastTarget = image == button.targetGraphic;
                    if (!halo) image.color = Color.white;
                }
                UnityEngine.UI.ColorBlock colors = button.colors;
                colors.normalColor = Hex("394444");
                colors.highlightedColor = Hex("506867");
                colors.pressedColor = Hex("267B79");
                colors.selectedColor = Hex("506867");
                colors.disabledColor = Hex("333838");
                button.colors = colors;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    DepthLabCommand command = button.GetComponent<DepthLabCommand>();
                    if (command != null)
                        label.text = command.action == LabAction.Hint ? "提示" :
                            command.action == LabAction.Pause ? "暂停" : "设置";
                    PlaceText(label, 12, 0, 264, 60, 29, Paper);
                    label.alignment = TextAlignmentOptions.Center;
                }
            }
            GetOrAdd<UnityEngine.UI.GraphicRaycaster>(app.hud);
            GetOrAdd<TrackedDeviceGraphicRaycaster>(app.hud);
            GetOrAdd<UnityEngine.UI.CanvasScaler>(app.hud);
            var presentation = GetOrAdd<DepthLabHudPresentation>(app.hud);
            presentation.app = app;
            presentation.panel = panel;
            presentation.stepLabel = step;
            presentation.controlsLabel = controls;
            Canvas canvas = app.hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            RectTransform root = (RectTransform)hud;
            root.sizeDelta = panel.sizeDelta;
            root.localScale = Vector3.one * .0012f;
            Vector3 spawn = app.chapters[0].spawnPoint.position;
            hud.SetPositionAndRotation(spawn + new Vector3(-1.05f, 1.05f, 1.25f),
                Quaternion.Euler(18f, -40f, 0f));
            EditorUtility.SetDirty(presentation);
        }

        private static Kit CreateKit()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DepthLab/Generated"))
                AssetDatabase.CreateFolder("Assets/DepthLab", "Generated");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/DepthLab/Generated", "Presentation");
            var kit = new Kit
            {
                bevel = ProfileMesh("ChamferedCase", false),
                support = ProfileMesh("InstrumentSupport", true),
                floor = Material("WarmGraphiteFloor", Hex("454846"), .1f, .28f),
                wall = Material("WarmMineralWall", Hex("9C9C91"), .05f, .24f),
                metal = Material("OffwhitePowdercoat", Paper, .24f, .40f),
                dark = Material("CharcoalInstrument", Ink, .28f, .36f),
                cyan = Material("CyanNavigation", Cyan, .05f, .38f, .35f),
                white = Material("WarmLuminaire", Paper, 0, .3f, .5f),
                seam = Material("QuietFloorInlay", Hex("666B65"), .15f, .3f),
                ready = Material("ReadySignal", Hex("83CBA0"), 0, .3f, .4f),
                waiting = Material("WaitingSignal", Hex("D7AF68"), 0, .3f, .25f)
            };
            for (int i = 0; i < 3; i++)
            {
                kit.identity[i] = Material($"Identity{i}", Identities[i], .1f, .38f, .16f);
                kit.shape[i] = Material($"ShapeEnamel{i}", Identities[i], .20f, .48f);
            }
            return kit;
        }

        internal static Material Material(string name, Color color, float metallic, float smoothness, float emission = 0)
        {
            string path = $"{Folder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("A lit material shader is required.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (emission > 0) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Mesh ProfileMesh(string name, bool support)
        {
            string path = $"{Folder}/{name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] heights = support ? new[] { -.5f, -.43f, -.32f, .34f, .5f } : new[] { -.5f, -.43f, .43f, .5f };
            float[] widths = support ? new[] { .94f, 1f, .60f, .42f, .66f } : new[] { .88f, 1f, 1f, .88f };
            Vector2[] perimeter =
            {
                new Vector2(-.38f, -.5f), new Vector2(.38f, -.5f),
                new Vector2(.5f, -.38f), new Vector2(.5f, .38f),
                new Vector2(.38f, .5f), new Vector2(-.38f, .5f),
                new Vector2(-.5f, .38f), new Vector2(-.5f, -.38f)
            };
            Vector3 Point(int level, int corner) => new Vector3(
                perimeter[corner % 8].x * widths[level], heights[level], perimeter[corner % 8].y * widths[level]);
            for (int level = 0; level < heights.Length - 1; level++)
                for (int corner = 0; corner < 8; corner++)
                    Quad(vertices, triangles, Point(level, corner), Point(level + 1, corner),
                        Point(level + 1, corner + 1), Point(level, corner + 1));
            for (int corner = 0; corner < 8; corner++)
            {
                Triangle(vertices, triangles, new Vector3(0, heights[0], 0), Point(0, corner), Point(0, corner + 1));
                int top = heights.Length - 1;
                Triangle(vertices, triangles, new Vector3(0, heights[top], 0), Point(top, corner + 1), Point(top, corner));
            }
            mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void Quad(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(vertices, indices, a, b, c);
            Triangle(vertices, indices, a, c, d);
        }

        private static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(index); indices.Add(index + 1); indices.Add(index + 2);
        }

        private static Transform Solid(Transform parent, string name, Vector3 position, Vector3 scale,
            Mesh mesh, Material material, bool colliding = true)
        {
            Transform node = Node(parent, name);
            ReplaceSolid(node, position, scale, mesh, material, colliding);
            return node;
        }

        private static void ReplaceSolid(Transform node, Vector3 position, Vector3 scale,
            Mesh mesh, Material material, bool colliding = true)
        {
            node.localPosition = position;
            node.localRotation = Quaternion.identity;
            node.localScale = scale;
            GetOrAdd<MeshFilter>(node.gameObject).sharedMesh = mesh;
            MeshRenderer renderer = GetOrAdd<MeshRenderer>(node.gameObject);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = colliding ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = colliding;
            // Keep every original component reference alive; only the fitted collider is enabled.
            MeshCollider fitted = node.GetComponent<MeshCollider>();
            foreach (Collider old in node.GetComponents<Collider>()) old.enabled = false;
            if (colliding)
            {
                if (fitted == null) fitted = Undo.AddComponent<MeshCollider>(node.gameObject);
                fitted.sharedMesh = mesh;
                fitted.convex = false;
                fitted.isTrigger = false;
                fitted.enabled = true;
            }
        }

        private static void MatchExistingBox(Transform node)
        {
            if (node.GetComponent<Collider>() == null)
            {
                BoxCollider collider = Undo.AddComponent<BoxCollider>(node.gameObject);
                collider.center = Vector3.zero;
                collider.size = Vector3.one;
            }
        }

        private static Transform Node(Transform parent, string name)
        {
            Transform node = parent.Find(name);
            if (node != null) return node;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add laboratory presentation detail");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform Closest(Transform parent, string name, Vector3 position)
        {
            Transform best = null;
            float distance = float.PositiveInfinity;
            foreach (Transform child in parent)
            {
                if (child.name != name) continue;
                Vector3 delta = child.localPosition - position;
                float d = delta.x * delta.x + delta.z * delta.z;
                if (d >= distance) continue;
                distance = d;
                best = child;
            }
            return best;
        }

        private static TMP_Text WorldLabel(Transform parent, string name, TMP_FontAsset font, Vector3 position,
            Vector2 size, float scale, Camera camera)
        {
            Transform existing = parent.Find(name);
            RectTransform rect;
            if (existing == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
                Undo.RegisterCreatedObjectUndo(go, "Add mounted laboratory label");
                rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
            }
            else rect = (RectTransform)existing;
            rect.localPosition = position;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * scale;
            rect.sizeDelta = size;
            Canvas canvas = rect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            TMP_Text text = TextNode(rect, "Caption", font);
            Layout(text.rectTransform, 0, 0, size.x, size.y);
            return text;
        }

        private static TMP_Text TextNode(Transform parent, string name, TMP_FontAsset font)
        {
            TMP_Text text = FindText(parent, name);
            if (text == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                Undo.RegisterCreatedObjectUndo(go, "Add presentation text");
                go.transform.SetParent(parent, false);
                text = go.GetComponent<TextMeshProUGUI>();
            }
            text.font = font;
            return text;
        }

        private static TMP_Text FindText(Transform parent, string name) =>
            parent.Find(name)?.GetComponent<TMP_Text>();

        private static void SetText(Transform parent, string name, string value,
            float x, float y, float width, float height, float size, Color color)
        {
            TMP_Text text = FindText(parent, name);
            if (text == null) return;
            text.text = value;
            PlaceText(text, x, y, width, height, size, color);
        }

        private static void PlaceText(TMP_Text text, float x, float y, float width, float height, float size, Color color)
        {
            if (text == null) return;
            Layout(text.rectTransform, x, y, width, height);
            Style(text, size, color);
        }

        private static void Style(TMP_Text text, float size, Color color)
        {
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.margin = Vector4.zero;
        }

        private static void Layout(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(x, -y, 0);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static bool AssignText(SerializedObject target, string name, TMP_Text text)
        {
            SerializedProperty field = target.FindProperty(name);
            if (field == null || field.propertyType != SerializedPropertyType.ObjectReference || text == null) return false;
            field.objectReferenceValue = text;
            return true;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.GetComponent<T>() ?? Undo.AddComponent<T>(go);

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
