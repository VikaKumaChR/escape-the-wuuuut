using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabInstaller
    {
        private const string Generated = "Assets/DepthLab/Generated";
        private static TMP_FontAsset font;
        private static DepthLabApp app;
        private static Camera camera;
        private static readonly Color Ink = Hex("050914"), Secondary = Hex("0A1424"), Panel = Hex("101E30");
        private static readonly Color Blue = Hex("00C8FF"), Cyan = Hex("00F0D0"), Violet = Hex("8C65FF");
        private static readonly Color Text = Hex("EAF6FF"), Muted = Hex("8CA7C4"), Warning = Hex("FFB547");

        [MenuItem("DEPTHLAB/Install Preserving Original Scene")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installing.");
            if (GameObject.Find("DEPTHLAB - Perception Research") != null)
                throw new InvalidOperationException("DEPTHLAB is already installed. Modify the authored hierarchy incrementally.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity")
                throw new InvalidOperationException("Expected the existing ParallaxChamber scene. No scene was replaced.");
            if (!File.Exists("Backups/DepthLab-20261109/ParallaxChamber.live-original.unity"))
                throw new InvalidOperationException("The original scene backup is required outside Assets.");
            XROrigin origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null) throw new InvalidOperationException("Existing XR origin missing.");
            camera = origin.Camera;
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            font = BuildFont();

            var root = new GameObject("DEPTHLAB - Perception Research");
            Undo.RegisterCreatedObjectUndo(root, "Add DEPTHLAB without deleting original objects");
            app = root.AddComponent<DepthLabApp>();
            app.headCamera = camera;
            app.playerRoot = origin.transform;
            app.uiModule = UnityEngine.Object.FindAnyObjectByType<XRUIInputModule>();
            app.uiModule.enableMouseInput = true;
            app.uiModule.enableXRInput = true;
            app.settings = root.AddComponent<DepthLabSettings>();
            app.input = root.AddComponent<DepthLabInput>();
            app.input.headCamera = camera;
            app.input.playerRoot = origin.transform;
            app.input.settings = app.settings;
            app.input.controllerVisuals = origin.GetComponentsInChildren<Renderer>(true);
            app.input.controllerLines = origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>(true);
            app.tutorial = root.AddComponent<DepthLabTutorial>();
            app.tutorial.head = camera.transform;
            app.tutorial.playerRoot = origin.transform;

            Transform space = Node("01 - Arrival Laboratory", root.transform);
            app.menuSpace = space.gameObject;
            BuildSpace(space);
            Transform canvas = Canvas("02 - Fixed World Menu", root.transform, 1600, 1040);
            canvas.position = new Vector3(0, 1.65f, 2.8f);
            canvas.localScale = Vector3.one * .0015f;
            app.menuCanvas = canvas;
            canvas.GetComponent<UnityEngine.Canvas>().sortingOrder = 20;
            BuildPages(canvas);
            BuildHud(root.transform);
            BuildTutorialProps(space);
            app.chapters = DepthLabChapterBuilder.Build(Node("03 - Chapter Laboratories", root.transform), font, camera);
            DepthLabPhysicsUpgrade.ApplyTo(app);
            BuildAudio(root.transform);
            app.settings.lights = root.GetComponentsInChildren<Light>(true);

            Transform originalChamber = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Parallax Chamber");
            if (originalChamber != null) originalChamber.gameObject.SetActive(false);
            foreach (XRDeviceSimulator simulator in UnityEngine.Object.FindObjectsByType<XRDeviceSimulator>())
                simulator.enabled = false;
            foreach (var legacy in UnityEngine.Object.FindObjectsByType<TwistedSpace.Demo.SimulatorCollisionMotor>())
                legacy.enabled = false;
            foreach (var legacy in UnityEngine.Object.FindObjectsByType<TwistedSpace.Demo.SimulatorMenuInput>())
                legacy.enabled = false;
            foreach (var legacy in UnityEngine.Object.FindObjectsByType<TwistedSpace.Demo.OpeningMenuPresentation>())
                legacy.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ink;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 150;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.13f, .18f, .25f);
            RenderSettings.fog = false;
            EditorUtility.SetDirty(app.uiModule);
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 1.6f, 2.5f), Quaternion.identity, 4f, false, true);
        }

        private static TMP_FontAsset BuildFont()
        {
            string path = Generated + "/DepthLab Latin SDF.asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TwistedSpace/Demo/Fonts/NotoSansSC-Medium.otf");
            if (source == null || Shader.Find("TextMeshPro/Distance Field") == null)
                throw new InvalidOperationException("Bundled font and noninteractive TMP essentials import required.");
            string latin = string.Concat(Enumerable.Range(32, 95).Select(i => (char)i)) + "★–—°×→•ü−";
            TMP_FontAsset primary = existing != null ? existing : BakeFont(source, "DepthLab Latin SDF", latin, 80, 8);
            string cjk = string.Concat(DepthLabProgress.Translations) + "逃离扭曲空间";
            TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Generated + "/DepthLab CJK SDF.asset");
            if (fallback == null) fallback = BakeFont(source, "DepthLab CJK SDF", cjk, 48, 5);
            if (primary.fallbackFontAssetTable == null) primary.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (!primary.fallbackFontAssetTable.Contains(fallback)) primary.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(primary);
            AssetDatabase.SaveAssets();
            return primary;
        }

        private static TMP_FontAsset BakeFont(Font source, string name, string characters, int size, int padding)
        {
            var asset = TMP_FontAsset.CreateFontAsset(source, size, padding, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = name;
            asset.TryAddCharacters(characters, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning("DEPTHLAB font missing characters: " + missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, Generated + "/" + name + ".asset");
            foreach (Texture2D atlas in asset.atlasTextures)
            {
                atlas.name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void BuildSpace(Transform parent)
        {
            Material dark = Mat("Arrival obsidian", Ink, false);
            Material steel = Mat("Arrival titanium", Panel, false);
            Material blue = Mat("Arrival blue", Blue, true);
            Material cyan = Mat("Arrival cyan", Cyan, true);
            Material violet = Mat("Arrival violet", Violet, true);
            Primitive("Central arrival platform", PrimitiveType.Cylinder, parent, new Vector3(0, -.18f, 0), new Vector3(6, .16f, 6), steel, true);
            Primitive("Floor", PrimitiveType.Cube, parent, new Vector3(0, -.4f, 2), new Vector3(13, .2f, 14), dark, true);
            Ring("Platform perimeter", parent, new Vector3(0, -.012f, 0), 2.9f, .016f, cyan, Quaternion.Euler(90, 0, 0));
            Ring("Platform inner", parent, new Vector3(0, -.008f, 0), 2.65f, .008f, blue, Quaternion.Euler(90, 0, 0));
            for (int i = -5; i <= 5; i++)
            {
                Primitive("Floor longitudinal guide", PrimitiveType.Cube, parent, new Vector3(i, -.285f, 2), new Vector3(.012f, .008f, 13), i % 2 == 0 ? blue : steel);
                Primitive("Floor depth guide", PrimitiveType.Cube, parent, new Vector3(0, -.28f, i + 2), new Vector3(12, .008f, .012f), steel);
            }
            Primitive("Far wall", PrimitiveType.Cube, parent, new Vector3(0, 3, 8), new Vector3(13, 7, .2f), dark);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int depth = 0; depth < 4; depth++)
                {
                    float z = 1.8f + depth * 1.5f;
                    Primitive("Depth rib", PrimitiveType.Cube, parent, new Vector3(side * (3.3f + depth * .3f), 2, z), new Vector3(.17f, 4.5f, .18f), steel);
                    Primitive("Rib edge", PrimitiveType.Cube, parent, new Vector3(side * (3.3f + depth * .3f), 2, z - .1f), new Vector3(.025f, 4, .012f), depth % 2 == 0 ? blue : violet);
                }
                Primitive("Menu plinth", PrimitiveType.Cylinder, parent, new Vector3(side * 1.75f, .15f, 2.8f), new Vector3(.8f, .15f, .8f), steel);
                Primitive("Light column", PrimitiveType.Cube, parent, new Vector3(side * 1.75f, 1.65f, 3), new Vector3(.025f, 2.7f, .025f), cyan);
                Ring("Column reticle", parent, new Vector3(side * 1.75f, .32f, 2.8f), .38f, .014f, blue, Quaternion.Euler(90, 0, 0));
            }
            var core = Node("Suspended perception core", parent);
            core.position = new Vector3(0, 2.9f, 5.4f);
            Primitive("Core shell", PrimitiveType.Sphere, core, Vector3.zero, Vector3.one * .72f, steel);
            var central = Primitive("Core emitter", PrimitiveType.Cube, core, Vector3.zero, Vector3.one * .52f, cyan);
            central.transform.rotation = Quaternion.Euler(35, 45, 25);
            var motion = core.gameObject.AddComponent<DepthLabAmbient>();
            motion.rotation = new Vector3(4, 12, 3);
            motion.bobAmount = .08f;
            Ring("Orbit alpha", core, Vector3.zero, 1.25f, .018f, blue, Quaternion.Euler(65, 20, 0));
            Ring("Orbit beta", core, Vector3.zero, 1.55f, .012f, violet, Quaternion.Euler(10, -20, 30));
            Ring("Orbit gamma", parent, new Vector3(0, 2.9f, 5.6f), 1.92f, .012f, blue, Quaternion.identity);
            for (int i = 0; i < 32; i++)
            {
                float angle = i * 2.39996f;
                Primitive("Suspended data mote", PrimitiveType.Cube, parent,
                    new Vector3(Mathf.Cos(angle) * (2.3f + i % 3 * .55f), .7f + (i % 9) * .46f, 4f + (i % 5) * .7f),
                    Vector3.one * .018f, i % 3 == 0 ? violet : blue);
            }
            WorldText("DEPTHLAB   /   PERCEPTION RESEARCH", parent, new Vector3(0, 5.4f, 7.7f), .34f, Muted);
            WorldText("ESCAPE THE DISTORTED SPACE", parent, new Vector3(0, 4.94f, 7.7f), .15f, Cyan);
            var key = Node("Laboratory key light", parent).gameObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(.72f, .85f, 1);
            key.intensity = .8f;
            key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(45, -25, 0);
            var fill = Node("Core blue fill", parent).gameObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 8;
            fill.intensity = 4;
            fill.color = Blue;
            fill.shadows = LightShadows.None;
            fill.transform.position = new Vector3(0, 3, 3.8f);
        }

        private static void BuildPages(Transform canvas)
        {
            Image(canvas, "Glass backing", 0, 0, 1600, 1040, Secondary);
            Image(canvas, "Top cyan edge", 0, 0, 1600, 3, Cyan);
            Image(canvas, "Left registration", 0, 0, 3, 1040, Blue);
            Label(canvas, "DEPTHLAB   /   IMMERSIVE PERCEPTION RESEARCH", 38, 27, 1050, 40, 22, Muted);
            Label(canvas, "SYSTEM ONLINE   •   01.0", 1200, 27, 360, 40, 18, Cyan);
            Image(canvas, "Header divider", 38, 90, 1524, 1, Panel);
            Image(canvas, "Footer divider", 38, 930, 1524, 1, Panel);
            Label(canvas, "XR  RAY + TRIGGER  /  GRIP TO GRAB     •     DESKTOP  CLICK  /  RMB LOOK  /  WASD  /  Q,E TURN  /  ESC PAUSE",
                38, 953, 1524, 35, 17, Muted);
            app.tooltipText = Label(canvas, "", 38, 994, 1524, 30, 18, Cyan);
            app.homePage = Page(canvas, "Home");
            app.chapterPage = Page(canvas, "Chapter Select");
            app.detailPage = Page(canvas, "Chapter Detail");
            app.settingsPage = Page(canvas, "Settings");
            app.creditsPage = Page(canvas, "Credits");
            app.pausePage = Page(canvas, "Pause");
            app.resultPage = Page(canvas, "Chapter Complete");
            app.finalPage = Page(canvas, "Final Summary");
            app.modalPage = Page(canvas, "Confirmation");
            app.tutorialPage = Page(canvas, "Tutorial");
            BuildHome(app.homePage.transform);
            BuildChapterSelect(app.chapterPage.transform);
            BuildDetails(app.detailPage.transform);
            BuildSettings(app.settingsPage.transform);
            BuildSecondaryPages();
            foreach (Transform page in canvas)
                if (page.name.StartsWith("Page - ")) page.gameObject.SetActive(page.gameObject == app.homePage);
        }

        private static void BuildHome(Transform page)
        {
            Label(page, "SEE BEYOND\nTHE OBVIOUS.", 50, 142, 480, 70, 22, Cyan);
            Label(page, "DEPTH\nLAB", 43, 230, 450, 260, 85, Text);
            Label(page, "VR", 334, 396, 120, 55, 37, Blue);
            Image(page, "Identity rule", 50, 500, 60, 4, Cyan);
            Label(page, "ESCAPE THE\nDISTORTED SPACE", 50, 540, 440, 100, 34, Text);
            Label(page, "Four laboratories.\nOne unreliable reality.\n\nLearn how depth, perspective\nand light shape what you see.", 50, 680, 440, 180, 23, Muted);
            Label(page, "RESEARCH BUILD   /   01.0\nSEATED OR STANDING", 50, 858, 440, 55, 18, Cyan);
            string[] labels = { "START GAME", "CHAPTER SELECT", "TUTORIAL", "SETTINGS", "CREDITS", "EXIT" };
            string[] tips = { "Continue at your next unlocked laboratory.", "Inspect all four chapters and your saved best runs.",
                "Seven action-gated calibration steps. Skip at any time.", "Adjust audio, comfort, quality and brightness.",
                "Project credits, perception concepts and build information.", "Exit with confirmation. Completed results stay saved." };
            LabAction[] actions = { LabAction.Start, LabAction.Chapters, LabAction.Tutorial, LabAction.Settings, LabAction.Credits, LabAction.Exit };
            Label(page, "SELECT YOUR NEXT EXPERIMENT", 550, 170, 470, 40, 20, Muted);
            for (int i = 0; i < labels.Length; i++)
                Button(page, labels[i], 550, 240 + i * 104, 460, 78, actions[i], 0, tips[i], i == 0);
            Label(page, "YOUR RESEARCH ARCHIVE", 1090, 150, 450, 40, 20, Muted);
            app.homeStatuses = new TMP_Text[4];
            app.previews = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                int y = 218 + i * 166;
                var button = Button(page, "", 1090, y, 458, 146, LabAction.Detail, i, DepthLabProgress.Descriptions[i]);
                Label(button.transform, $"0{i + 1}   {DepthLabProgress.Concepts[i]}", 100, 18, 344, 28, 15, Cyan);
                Label(button.transform, DepthLabProgress.Titles[i], 100, 53, 344, 38, 22, Text);
                app.homeStatuses[i] = Label(button.transform, i == 0 ? "READY TO EXPLORE" : "LOCKED", 100, 104, 344, 24, 15, Muted);
                app.previews[i] = BuildPreview(app.menuCanvas, i, 1140, y + 73);
            }
        }

        private static void BuildChapterSelect(Transform page)
        {
            Label(page, "CHOOSE YOUR LABORATORY", 50, 135, 1200, 60, 42, Text);
            Label(page, "Progress is saved on completion. Finish a laboratory to unlock the next.", 50, 204, 1480, 38, 23, Muted);
            app.chapterStatuses = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                float x = 50 + i % 2 * 770, y = 285 + i / 2 * 260;
                var card = Button(page, "", x, y, 715, 226, LabAction.Detail, i, DepthLabProgress.Descriptions[i]);
                Label(card.transform, $"0{i + 1}  /  {DepthLabProgress.Concepts[i]}   •   {DepthLabProgress.Times[i]}", 24, 18, 665, 32, 19, Cyan);
                Label(card.transform, DepthLabProgress.Titles[i], 24, 61, 555, 44, 32, Text);
                Label(card.transform, DepthLabProgress.Translations[i], 24, 112, 665, 35, 23, Muted);
                app.chapterStatuses[i] = Label(card.transform, i == 0 ? "UNLOCKED  /  NOT YET EXPLORED" : "LOCKED  /  COMPLETE PREVIOUS CHAPTER",
                    24, 174, 665, 30, 19, Warning);
            }
            Button(page, "BACK", 50, 835, 280, 65, LabAction.Menu);
            Button(page, "RESEARCH SUMMARY", 1100, 835, 448, 65, LabAction.Final);
        }

        private static void BuildDetails(Transform page)
        {
            app.detailTitle = Label(page, "01  /  Room of Disparity", 60, 140, 1440, 120, 47, Text);
            Image(page, "Detail rule", 60, 290, 1440, 2, Blue);
            app.detailBody = Label(page, "", 60, 335, 1160, 365, 28, Muted);
            app.detailStatus = Label(page, "", 60, 740, 1420, 45, 24, Cyan);
            Button(page, "BACK TO CHAPTERS", 60, 835, 500, 65, LabAction.Chapters);
            app.playSelectedButton = Button(page, "PLAY LABORATORY", 1010, 835, 490, 65, LabAction.PlaySelected, 0, "Enter this unlocked chapter.", true);
        }

        private static void BuildSettings(Transform page)
        {
            Label(page, "CALIBRATE YOUR EXPERIENCE", 50, 130, 1480, 60, 42, Text);
            Label(page, "Comfort first. Head tracking stays active while paused.", 50, 205, 1480, 40, 23, Muted);
            app.settingsView = page.gameObject.AddComponent<DepthLabSettingsView>();
            var view = app.settingsView;
            view.settings = app.settings;
            view.master = Slider(page, "MASTER", 60, 295, 0, 1, .8f, view.ChangeValues);
            view.music = Slider(page, "MUSIC", 60, 390, 0, 1, .3f, view.ChangeValues);
            view.sfx = Slider(page, "SFX", 60, 485, 0, 1, .7f, view.ChangeValues);
            view.speed = Slider(page, "MOVE SPEED   0.5 – 3 M/S", 60, 580, .5f, 3f, 1.5f, view.ChangeValues);
            view.brightness = Slider(page, "LIGHT BRIGHTNESS   50 – 150%", 60, 675, .5f, 1.5f, 1f, view.ChangeValues);
            view.movementLabel = SettingsButton(page, "MOVEMENT   /   STEPPED", 850, 300, view.Movement);
            view.turnLabel = SettingsButton(page, "TURN   /   SNAP", 850, 405, view.Turn);
            view.snapLabel = SettingsButton(page, "SNAP ANGLE   /   30°", 850, 510, view.Snap);
            view.qualityLabel = SettingsButton(page, "QUALITY   /   MEDIUM", 850, 615, view.Quality);
            Label(page, "No artificial head rotation or time-scale pause.\nVignette is not implemented in this build.", 850, 725, 650, 65, 20, Muted);
            Button(page, "BACK", 60, 835, 260, 65, LabAction.SettingsBack);
            Button(page, "DEFAULTS", 520, 835, 400, 65, LabAction.Defaults);
            Button(page, "RESET PROGRESS", 1100, 835, 448, 65, LabAction.ResetProgress);
        }

        private static TMP_Text SettingsButton(Transform parent, string title, float x, float y, UnityAction callback)
        {
            var b = Button(parent, title, x, y, 650, 70, null);
            UnityEventTools.AddPersistentListener(b.onClick, callback);
            return b.GetComponentInChildren<TMP_Text>();
        }

        private static void BuildSecondaryPages()
        {
            var p = app.creditsPage.transform;
            Label(p, "PERCEPTION IS AN EXPERIMENT.", 60, 145, 1450, 80, 46, Text);
            Label(p, "DEPTHLAB VR  /  ESCAPE THE DISTORTED SPACE\n\nBuilt in Unity 6000.6 with URP, uGUI / TextMeshPro and XR Interaction Toolkit.\nOriginal geometry, procedural sound and laboratory presentation.\nBundled Noto Sans font: see the existing font license in the project.\n\nFour perception studies:\nBinocular disparity • Motion parallax • Ponzo & Müller-Lyer • Reflection & materials\n\nDesktop research build. XR hardware validation remains required.\nNo OpenXR loader or XR Management is currently installed.",
                60, 275, 1450, 460, 27, Muted);
            Button(p, "BACK TO LAB", 60, 835, 500, 65, LabAction.Menu);
            p = app.pausePage.transform;
            Label(p, "EXPERIMENT PAUSED", 60, 180, 1450, 80, 56, Text);
            Label(p, "Timer, puzzle interaction and locomotion are stopped.\nHead tracking and UI remain active.", 60, 310, 1450, 100, 30, Muted);
            Button(p, "RESUME", 60, 495, 700, 85, LabAction.Resume, 0, "", true);
            Button(p, "SETTINGS", 830, 495, 700, 85, LabAction.Settings);
            Button(p, "RETRY CHAPTER", 60, 625, 700, 85, LabAction.Replay);
            Button(p, "MAIN MENU", 830, 625, 700, 85, LabAction.Menu);
            p = app.resultPage.transform;
            app.resultTitle = Label(p, "CHAPTER COMPLETE", 60, 150, 1450, 160, 60, Cyan);
            app.resultBody = Label(p, "", 60, 350, 1450, 370, 32, Text);
            Button(p, "REPLAY", 60, 820, 420, 75, LabAction.Replay);
            Button(p, "MAIN MENU", 565, 820, 420, 75, LabAction.Menu);
            app.nextChapterButton = Button(p, "NEXT CHAPTER", 1070, 820, 470, 75, LabAction.NextChapter, 0, "", true);
            p = app.finalPage.transform;
            app.finalTotals = Label(p, "DEPTH EXPLORER", 60, 135, 1450, 155, 44, Cyan);
            app.finalCards = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                Image(p, "Result card " + i, 60, 320 + i * 110, 1480, 90, Panel);
                app.finalCards[i] = Label(p, "", 85, 335 + i * 110, 1430, 70, 23, Text);
            }
            Button(p, "PLAY AGAIN", 60, 830, 420, 70, LabAction.PlayAgain);
            Button(p, "CHAPTER SELECT", 565, 830, 420, 70, LabAction.Chapters);
            Button(p, "MAIN MENU", 1070, 830, 470, 70, LabAction.Menu);
            p = app.modalPage.transform;
            Label(p, "PLEASE CONFIRM", 60, 160, 1450, 80, 52, Warning);
            app.modalText = Label(p, "", 60, 340, 1450, 300, 34, Text);
            Button(p, "CANCEL", 60, 770, 680, 95, LabAction.CancelModal);
            Button(p, "CONFIRM", 860, 770, 680, 95, LabAction.ConfirmExit, 0, "This action cannot be undone.", true);
            p = app.tutorialPage.transform;
            Label(p, "LEARN TO TRUST YOUR ACTIONS", 60, 135, 1450, 65, 42, Text);
            app.tutorialText = Label(p, "CALIBRATION 01 / 07", 560, 270, 940, 400, 34, Text);
            Label(p, "PRACTICE OBJECT\nAT YOUR LEFT\n\nGrab the cyan object.\nRelease at the lit target.\n\nScroll while dragging\nto change depth.", 60, 280, 440, 390, 28, Muted);
            Button(p, "SKIP TUTORIAL", 60, 820, 440, 75, LabAction.TutorialSkip);
            Button(p, "HINT", 575, 820, 440, 75, LabAction.Hint);
            app.tutorialNextButton = Button(p, "NEXT", 1090, 820, 440, 75, LabAction.TutorialNext, 0, "Perform the requested action first.", true);
            app.tutorialNextButton.interactable = false;
        }

        private static void BuildHud(Transform root)
        {
            Transform hud = Canvas("04 - Objective HUD", root, 1200, 290);
            hud.localScale = Vector3.one * .0013f;
            Image(hud, "HUD glass", 0, 0, 1200, 290, Secondary);
            Image(hud, "HUD edge", 0, 0, 1200, 3, Cyan);
            app.hudText = Label(hud, "LAB 01", 24, 12, 1150, 112, 23, Text);
            app.hintText = Label(hud, "", 24, 140, 1150, 57, 18, Muted);
            Button(hud, "HINT", 24, 205, 300, 60, LabAction.Hint);
            Button(hud, "PAUSE", 450, 205, 300, 60, LabAction.Pause);
            Button(hud, "SETTINGS", 875, 205, 300, 60, LabAction.Settings);
            app.hud = hud.gameObject;
            hud.gameObject.SetActive(false);
        }

        private static void BuildTutorialProps(Transform space)
        {
            Transform props = Node("Tutorial practice station", space);
            app.tutorialProps = props.gameObject;
            Material surface = Mat("Tutorial surface", Panel, false), glow = Mat("Tutorial target", Cyan, true);
            Primitive("Practice plinth", PrimitiveType.Cylinder, props, new Vector3(-.75f, .42f, .9f), new Vector3(.65f, .42f, .65f), surface, true);
            var piece = Primitive("Practice grab object", PrimitiveType.Cube, props, new Vector3(-.75f, 1.05f, .9f), Vector3.one * .2f, glow, true);
            piece.AddComponent<Rigidbody>().mass = .25f;
            piece.AddComponent<XRGrabInteractable>();
            var target = Primitive("Practice release target", PrimitiveType.Cylinder, props, new Vector3(-1.25f, .9f, 1.6f), new Vector3(.65f, .035f, .65f), surface, true);
            Ring("Release target halo", props, target.transform.localPosition + Vector3.up * .05f, .31f, .012f, glow, Quaternion.Euler(90, 0, 0));
            var targetPoint = Node("Release target center", props);
            targetPoint.position = target.transform.position + Vector3.up * .15f;
            app.tutorial.practiceObject = piece;
            app.tutorial.target = targetPoint.gameObject;
            props.gameObject.SetActive(false);
        }

        private static void BuildAudio(Transform root)
        {
            app.effectsAudio = Node("UI sound", root).gameObject.AddComponent<AudioSource>();
            app.effectsAudio.playOnAwake = false;
            app.effectsAudio.spatialBlend = 0;
            app.hoverSound = Tone("Hover", .065f, 620, false);
            app.confirmSound = Tone("Confirm", .16f, 880, false);
            app.musicAudio = Node("Laboratory ambience", root).gameObject.AddComponent<AudioSource>();
            app.musicAudio.clip = Tone("Laboratory ambient bed", 8f, 110, true);
            app.musicAudio.loop = true;
            app.musicAudio.volume = .3f;
            app.musicAudio.playOnAwake = true;
            app.musicAudio.spatialBlend = 0;
        }

        private static AudioClip Tone(string name, float duration, float frequency, bool ambient)
        {
            string path = Generated + "/" + name + ".wav";
            if (!File.Exists(path))
            {
                int count = (int)(duration * 22050);
                using (var stream = new BinaryWriter(File.Create(path)))
                {
                    stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    stream.Write(36 + count * 2);
                    stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    stream.Write(16); stream.Write((short)1); stream.Write((short)1);
                    stream.Write(22050); stream.Write(44100); stream.Write((short)2); stream.Write((short)16);
                    stream.Write(System.Text.Encoding.ASCII.GetBytes("data")); stream.Write(count * 2);
                    for (int i = 0; i < count; i++)
                    {
                        float t = i / 22050f;
                        float envelope = ambient ? .1f : .25f * Mathf.Sin(Mathf.PI * i / count);
                        float sample = Mathf.Sin(2 * Mathf.PI * frequency * t);
                        if (ambient) sample = (sample + .4f * Mathf.Sin(2 * Mathf.PI * 165 * t) + .25f * Mathf.Sin(2 * Mathf.PI * 220 * t)) / 1.65f;
                        stream.Write((short)(sample * envelope * short.MaxValue));
                    }
                }
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static Transform BuildPreview(Transform parent, int index, float x, float y)
        {
            Transform holder = Node("3D chapter preview " + (index + 1), parent);
            holder.localPosition = new Vector3(x - 800, 520 - y, -80);
            Material blue = Mat("Preview blue", Blue, true), cyan = Mat("Preview cyan", Cyan, true), violet = Mat("Preview violet", Violet, true);
            if (index == 0)
            {
                Primitive("Disparity cube", PrimitiveType.Cube, holder, new Vector3(-18, 0, 0), Vector3.one * 33, blue).transform.localRotation = Quaternion.Euler(25, 35, 10);
                Primitive("Disparity sphere", PrimitiveType.Sphere, holder, new Vector3(20, 18, 10), Vector3.one * 26, violet);
            }
            else if (index == 1)
            {
                for (int i = 0; i < 3; i++)
                {
                    Primitive("Tree", PrimitiveType.Cylinder, holder, new Vector3((i - 1) * 25, -5, i * 12), new Vector3(5, 28, 5), violet);
                    Primitive("Canopy", PrimitiveType.Sphere, holder, new Vector3((i - 1) * 25, 18, i * 12), new Vector3(32, 15, 25), cyan);
                }
            }
            else if (index == 2)
            {
                for (int i = 0; i < 3; i++)
                    Ring("Perspective portal", holder, new Vector3(0, 0, i * 23), 36 - i * 5, .003f, i == 1 ? violet : blue, Quaternion.identity);
            }
            else
            {
                Primitive("Reflector", PrimitiveType.Cube, holder, Vector3.zero, new Vector3(8, 58, 34), violet).transform.localRotation = Quaternion.Euler(0, 30, 20);
                Primitive("Light ray", PrimitiveType.Cube, holder, new Vector3(-18, 15, 0), new Vector3(40, 3, 3), cyan);
                Primitive("Receiver", PrimitiveType.Sphere, holder, new Vector3(25, -20, 0), Vector3.one * 18, blue);
            }
            return holder;
        }

        private static Transform Canvas(string name, Transform parent, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.Canvas), typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            var c = go.GetComponent<UnityEngine.Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = camera;
            go.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 1;
            go.GetComponent<TrackedDeviceGraphicRaycaster>().checkFor3DOcclusion = false;
            return rect;
        }
        private static GameObject Page(Transform parent, string name) => Rect(parent, "Page - " + name, 0, 0, 1600, 1040).gameObject;
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }
        private static UnityEngine.UI.Image Image(Transform p, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(p, name, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
        private static TMP_Text Label(Transform p, string value, float x, float y, float w, float h, float size, Color color)
        {
            var label = Rect(p, string.IsNullOrEmpty(value) ? "Readout" : value.Split('\n')[0], x, y, w, Mathf.Max(h, size * 1.5f)).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.text = value;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }
        private static DepthLabButton Button(Transform p, string title, float x, float y, float w, float h,
            LabAction? action, int argument = 0, string tooltip = "", bool primary = false)
        {
            var rect = Rect(p, title.Length > 0 ? title : "Chapter card " + argument, x, y, w, h);
            var halo = Image(rect, "Interaction halo", -4, -4, w + 8, h + 8, Color.clear);
            var face = Image(rect, "Button face", 0, 0, w, h, primary ? Blue : Panel);
            face.raycastTarget = true;
            var button = rect.gameObject.AddComponent<DepthLabButton>();
            button.targetGraphic = face;
            button.halo = halo;
            button.tooltip = tooltip;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = primary ? Blue : Panel;
            colors.highlightedColor = primary ? Cyan : new Color(.08f, .25f, .34f);
            colors.selectedColor = new Color(.1f, .35f, .45f);
            colors.pressedColor = Violet;
            colors.disabledColor = new Color(.07f, .1f, .14f, .5f);
            colors.fadeDuration = .12f;
            button.colors = colors;
            // Button colors multiply the target's color; keep the target neutral.
            face.color = Color.white;
            if (title.Length > 0)
            {
                var text = Label(rect, title, 26, 0, w - 52, h, 25, primary ? Ink : Text);
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (action.HasValue)
            {
                var command = rect.gameObject.AddComponent<DepthLabCommand>();
                command.action = action.Value;
                command.argument = argument;
                UnityEventTools.AddPersistentListener(button.onClick, command.Execute);
            }
            return button;
        }
        private static UnityEngine.UI.Slider Slider(Transform p, string title, float x, float y, float min, float max, float value, UnityAction<float> callback)
        {
            Label(p, title, x, y - 10, 680, 30, 21, Muted);
            var root = Rect(p, title + " slider", x, y + 35, 650, 32);
            Image(root, "Track", 0, 10, 650, 10, Panel);
            var fill = Image(root, "Fill", 0, 10, 650, 10, Blue);
            var handle = Image(root, "Handle", 0, 0, 24, 32, Cyan);
            handle.raycastTarget = true;
            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max;
            s.SetValueWithoutNotify(value);
            UnityEventTools.AddPersistentListener(s.onValueChanged, callback);
            return s;
        }
        private static Transform Node(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        private static Material Mat(string name, Color color, bool unlit)
        {
            string path = Generated + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            if (!unlit) { material.SetFloat("_Metallic", .65f); material.SetFloat("_Smoothness", .65f); }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static Transform Ring(string name, Transform parent, Vector3 position, float radius, float width, Material material, Quaternion rotation)
        {
            Transform t = Node(name, parent);
            t.localPosition = position;
            t.localRotation = rotation;
            var line = t.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 96;
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 96; i++)
            {
                float angle = i * Mathf.PI * 2 / 96;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
            }
            return t;
        }
        private static void WorldText(string value, Transform parent, Vector3 position, float size, Color color)
        {
            var go = Node(value, parent).gameObject;
            go.transform.localPosition = position;
            var label = go.AddComponent<TextMeshPro>();
            label.font = font;
            label.fontSize = size * 10;
            label.text = value;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(12, 1);
        }
        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color color); return color; }
    }
}
