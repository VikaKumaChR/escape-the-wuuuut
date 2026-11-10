using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabPlayabilityPolish
    {
        [MenuItem("DEPTHLAB/Apply Compact Gameplay Interface")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before applying scene UI.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null) throw new InvalidOperationException("Open the DEPTHLAB scene.");
            DepthLabUsabilityRepair.RepairSliders(app);
            DepthLabCyberpunkHudUpgrade.Apply();
            Transform console = app.chapters[0].transform.Find("Chapter instrument console");
            if (console == null) throw new InvalidOperationException("CH1 console is missing.");
            console.gameObject.SetActive(false);
            foreach (Transform child in app.chapters[0].transform)
                if (child.name == "Depth plaque") child.gameObject.SetActive(false);
            SetupForest(app);
            SetupRemainingChapters(app);
            SetupMusic(app);
            app.menuCanvas.localScale = Vector3.one * .0018f;
            var menuGlass = app.menuCanvas.GetComponentInChildren<DepthLabHolographicGraphic>(true);
            if (menuGlass != null && menuGlass.illustration == DepthLabHolographicGraphic.Illustration.Panel)
            {
                menuGlass.glassColor = new Color(.018f, .046f, .09f, .96f);
                menuGlass.SetVerticesDirty();
            }
            foreach (var graphic in app.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (graphic is TMP_Text || graphic is TMP_SubMeshUI) graphic.raycastTarget = false;
            SaveUiPrefabs(app);
            EditorUtility.SetDirty(app);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
        }

        private static void SetupForest(DepthLabApp app)
        {
            var forest = app.chapters[1] as ParallaxForestChapter;
            if (forest == null) throw new InvalidOperationException("CH2 forest is missing.");
            Transform console = forest.transform.Find("Chapter instrument console");
            Material wall = AssetDatabase.LoadAssetAtPath<Material>("Assets/DepthLab/Generated/CH01_EffectsV1_Wall.mat");
            ParticleSystem template = app.chapters[0].GetComponentsInChildren<ParticleSystem>(true)
                .First(p => p.name == "环境微尘");
            if (console == null || wall == null)
                throw new InvalidOperationException("Author the existing CH1 effects before forest presentation.");
            foreach (Transform child in forest.transform)
            {
                if (child.name == "Back wall" || child.name == "Side wall" || child.name == "Front wall")
                    child.GetComponent<Renderer>().sharedMaterial = wall;
                if (child.name == "Forest layer sign") child.gameObject.SetActive(false);
            }
            RectTransform panel = Rect(forest.transform, "ObservationControls");
            panel.sizeDelta = new Vector2(740, 220);
            panel.localPosition = new Vector3(0, 1.05f, -.6f);
            panel.localScale = Vector3.one * .0016f;
            var canvas = Get<Canvas>(panel);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            Get<UnityEngine.UI.GraphicRaycaster>(panel);
            Get<TrackedDeviceGraphicRaycaster>(panel).checkFor3DOcclusion = false;
            var glass = Get<DepthLabHolographicGraphic>(panel);
            glass.illustration = DepthLabHolographicGraphic.Illustration.Panel;
            glass.color = new Color(0, .898f, 1);
            glass.raycastTarget = false;
            string[] names = { "Desktop inspect left", "Desktop inspect right" };
            for (int i = 0; i < names.Length; i++)
            {
                Transform button = panel.Find(names[i]) ?? console.Find(names[i]);
                if (button == null) throw new InvalidOperationException("Observation button is missing.");
                button.SetParent(panel, false);
                Place((RectTransform)button, 24 + i * 356, 118, 336, 78);
                var label = button.GetComponentInChildren<TMP_Text>(true);
                Place(label.rectTransform, 12, 0, 312, 78);
                label.alignment = TextAlignmentOptions.Midline;
                label.enableAutoSizing = false;
                label.fontSize = 31;
                label.text = i == 0 ? "向左观察" : "向右观察";
            }
            forest.discoveryLabel.transform.SetParent(panel, false);
            Place(forest.discoveryLabel.rectTransform, 24, 20, 692, 76);
            forest.discoveryLabel.fontSize = 29;
            forest.discoveryLabel.enableAutoSizing = false;
            forest.discoveryLabel.text = "先向左、再向右观察，寻找隐藏平台";
            console.gameObject.SetActive(false);
            for (int i = 0; i < forest.safeButtons.Length; i++)
                forest.safeButtons[i].GetComponentInChildren<TMP_Text>(true).text = $"前往 {i + 1:00} 号平台";
            foreach (var button in forest.interactionButtons)
                if (button != null && button.name == "Unverified landing")
                    button.GetComponentInChildren<TMP_Text>(true).text = "断开的平台\n请找青色编号";
            Transform dustNode = forest.transform.Find("ForestDataDust");
            ParticleSystem dust;
            if (dustNode == null)
            {
                dust = UnityEngine.Object.Instantiate(template, forest.transform);
                dust.name = "ForestDataDust";
                dust.transform.localPosition = new Vector3(0, 2.6f, 1);
            }
            else dust = dustNode.GetComponent<ParticleSystem>();
            var atmosphere = Get<DepthLabRoomAtmosphere>(forest.transform);
            atmosphere.app = app;
            atmosphere.chapter = forest;
            atmosphere.forest = forest;
            atmosphere.dust = dust;
            atmosphere.observationControls = panel.gameObject;
            atmosphere.leaves = forest.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "Alien leaf").ToArray();
            SetupGuide(app, forest, new Vector3(-2, 1.3f, 1));
            AddExitLights(app, forest, atmosphere);
            EditorUtility.SetDirty(atmosphere);
        }

        private static void SetupGuide(DepthLabApp app, LabChapter chapter, Vector3 position)
        {
            string name = "CH" + (chapter.chapterIndex + 1) + "GuideAssistant";
            Transform guideRoot = chapter.transform.Find(name);
            if (guideRoot == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DepthLabGuideUpgrade.PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Author the CH1 guide prefab first.");
                guideRoot = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, chapter.transform)).transform;
                guideRoot.name = name;
            }
            guideRoot.localPosition = position;
            guideRoot.localRotation = Quaternion.Euler(0, -26, 0);
            var guide = guideRoot.GetComponent<VRGuideAssistant>();
            guide.app = app;
            guide.chapter = chapter;
            guide.nova.app = app;
            guide.nova.settings = app.settings;
            guide.speechCanvas.worldCamera = app.headCamera;
            guide.speechText.text = chapter.Objective;
            foreach (var component in guideRoot.GetComponentsInChildren<Component>(true))
            {
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        private static void SetupRemainingChapters(DepthLabApp app)
        {
            Material wall = AssetDatabase.LoadAssetAtPath<Material>("Assets/DepthLab/Generated/CH01_EffectsV1_Wall.mat");
            ParticleSystem template = app.chapters[0].GetComponentsInChildren<ParticleSystem>(true)
                .First(p => p.name == "环境微尘");
            for (int i = 2; i < app.chapters.Length; i++)
            {
                var chapter = (LabChapter)app.chapters[i];
                foreach (Transform child in chapter.transform)
                {
                    if (child.name == "Back wall" || child.name == "Side wall" || child.name == "Front wall")
                        child.GetComponent<Renderer>().sharedMaterial = wall;
                    if (child.name == "Gallery caption") child.gameObject.SetActive(false);
                }
                Transform node = chapter.transform.Find("AmbientDataDust");
                ParticleSystem dust;
                if (node == null)
                {
                    dust = UnityEngine.Object.Instantiate(template, chapter.transform);
                    dust.name = "AmbientDataDust";
                    dust.transform.localPosition = new Vector3(0, 2.7f, 1);
                }
                else dust = node.GetComponent<ParticleSystem>();
                var atmosphere = Get<DepthLabRoomAtmosphere>(chapter.transform);
                atmosphere.app = app;
                atmosphere.chapter = chapter;
                atmosphere.dust = dust;
                atmosphere.leaves = Array.Empty<Transform>();
                AddExitLights(app, chapter, atmosphere);
                SetupGuide(app, chapter, new Vector3(-2.2f, 1.3f, 1.6f));
                RectTransform console = (RectTransform)chapter.transform.Find("Chapter instrument console");
                console.localPosition = new Vector3(-1.35f, 1.15f, -.3f);
                console.localRotation = Quaternion.identity;
                console.localScale = Vector3.one * .0015f;
                console.sizeDelta = new Vector2(900, 550);
                console.GetComponent<UnityEngine.UI.Image>().enabled = false;
                foreach (TMP_Text text in console.GetComponentsInChildren<TMP_Text>(true))
                    if (text.GetComponentInParent<UnityEngine.UI.Button>() == null)
                        text.gameObject.SetActive(false);
                RectTransform glassRect = Rect(console, "CompactGlass");
                glassRect.anchorMin = Vector2.zero;
                glassRect.anchorMax = Vector2.one;
                glassRect.offsetMin = glassRect.offsetMax = Vector2.zero;
                glassRect.SetAsFirstSibling();
                var glass = Get<DepthLabHolographicGraphic>(glassRect);
                glass.illustration = DepthLabHolographicGraphic.Illustration.Panel;
                glass.color = new Color(0, .898f, 1);
                glass.raycastTarget = false;
                if (chapter is IllusionChapter illusion)
                {
                    CompactText(illusion.explanationLabel, 24, 25, 852, 210, 31);
                    for (int b = 0; b < 2; b++)
                    {
                        CompactButton(illusion.ponzoAnswerButtons[b], 24 + b * 438, 275, 414, 86);
                        CompactButton(illusion.mullerAnswerButtons[b], 24 + b * 438, 275, 414, 86);
                    }
                    CompactButton(illusion.continueButton, 24, 400, 852, 86);
                }
                else if (chapter is ReflectionChapter reflection)
                {
                    CompactText(reflection.instrumentLabel, 24, 25, 852, 160, 30);
                    for (int b = 0; b < 3; b++)
                        CompactButton(reflection.interactionButtons[b], 24 + b * 290, 240, 272, 86);
                    for (int b = 3; b < 5; b++)
                        CompactButton(reflection.interactionButtons[b], 24 + (b - 3) * 438, 385, 414, 86);
                }
                Get<DepthLabUiMotion>(console);
                EditorUtility.SetDirty(atmosphere);
            }
        }

        private static void AddExitLights(DepthLabApp app, LabChapter chapter, DepthLabRoomAtmosphere atmosphere)
        {
            Transform root = chapter.transform.Find("ExitEnergyFrame");
            if (root == null)
            {
                Transform source = app.chapters[0].GetComponentsInChildren<Transform>(true)
                    .First(t => t.name == "出口能量框");
                root = UnityEngine.Object.Instantiate(source, chapter.transform);
                root.name = "ExitEnergyFrame";
            }
            root.SetLocalPositionAndRotation(chapter.exitDoor.transform.localPosition,
                chapter.exitDoor.transform.localRotation);
            atmosphere.exitLights = root.GetComponentsInChildren<Renderer>(true);
        }

        private static void CompactText(TMP_Text text, float x, float y, float width, float height, float font)
        {
            text.gameObject.SetActive(true);
            Place(text.rectTransform, x, y, width, height);
            text.fontSize = font;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void CompactButton(UnityEngine.UI.Button button, float x, float y, float width, float height)
        {
            Place((RectTransform)button.transform, x, y, width, height);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            CompactText(label, 16, 0, width - 32, height, 31);
            label.alignment = TextAlignmentOptions.Midline;
            Get<DepthLabUiMotion>(button.transform);
        }

        private static void SetupMusic(DepthLabApp app)
        {
            if (app.musicAudio == null) throw new InvalidOperationException("Existing music source is missing.");
            var music = Get<DepthLabSoundscape>(app.transform);
            music.app = app;
            music.laboratoryMusic = Music("LaboratoryTheme", 0);
            music.forestMusic = Music("ForestTheme", 1);
            music.illusionMusic = Music("IllusionTheme", 2);
            music.opticsMusic = Music("OpticsTheme", 3);
            app.musicAudio.enabled = true;
            app.musicAudio.loop = true;
            app.musicAudio.spatialBlend = 0;
            app.musicAudio.clip = music.laboratoryMusic;
            EditorUtility.SetDirty(music);
            EditorUtility.SetDirty(app.musicAudio);
        }

        private static AudioClip Music(string name, int chapter)
        {
            string path = "Assets/DepthLab/Generated/" + name + ".wav";
            if (!File.Exists(path))
            {
                const int rate = 22050, seconds = 32, count = rate * seconds;
                int[] roots = chapter == 1 ? new[] { 50, 53, 48, 55 } :
                    chapter == 2 ? new[] { 48, 51, 46, 53 } :
                    chapter == 3 ? new[] { 52, 55, 50, 57 } : new[] { 45, 48, 43, 50 };
                int[] notes = { 0, 7, 12, 15, 19, 12, 7, 15 };
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    writer.Write(36 + count * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int i = 0; i < count; i++)
                    {
                        double t = (double)i / rate;
                        int bar = (int)(t / 8);
                        double local = t % 8;
                        double f = 440 * Math.Pow(2, (roots[bar] - 69) / 12d);
                        double pad = Math.Sin(2 * Math.PI * f * local) +
                            .45 * Math.Sin(2 * Math.PI * f * Math.Pow(2, 7d / 12) * local);
                        double padEnvelope = Math.Min(1, local / .7) * Math.Min(1, (8 - local) / .7);
                        double beat = t % 1;
                        double note = f * Math.Pow(2, notes[(int)t % notes.Length] / 12d);
                        double arp = Math.Sin(2 * Math.PI * note * beat) *
                            Math.Min(1, beat / .035) * Math.Exp(-beat * (chapter == 1 ? 4 : 6));
                        double edge = Math.Min(1, t) * Math.Min(1, seconds - t);
                        double sample = (.095 * pad * padEnvelope + .16 * arp) * edge;
                        writer.Write((short)(Math.Clamp(sample, -.9, .9) * short.MaxValue));
                    }
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void SaveUiPrefabs(DepthLabApp app)
        {
            const string folder = "Assets/DepthLab/UI/Prefabs";
            if (!AssetDatabase.IsValidFolder("Assets/DepthLab/UI"))
                AssetDatabase.CreateFolder("Assets/DepthLab", "UI");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/DepthLab/UI", "Prefabs");
            var hud = app.hud.GetComponent<DepthLabHudPresentation>();
            hud.app = null;
            try { PrefabUtility.SaveAsPrefabAsset(app.hud, folder + "/GameplayHUD.prefab"); }
            finally { hud.app = app; }
            var view = app.settingsView;
            var settings = view.settings;
            view.settings = null;
            try { PrefabUtility.SaveAsPrefabAsset(app.settingsPage, folder + "/SettingsPanel.prefab"); }
            finally { view.settings = settings; }
            ParticleSystem dust = app.chapters[1].transform.Find("ForestDataDust").GetComponent<ParticleSystem>();
            PrefabUtility.SaveAsPrefabAsset(dust.gameObject, folder + "/AmbientDataDust.prefab");
            PrefabUtility.SaveAsPrefabAsset(app.chapters[1].transform.Find("ExitEnergyFrame").gameObject,
                folder + "/ExitEnergyFrame.prefab");
        }

        private static T Get<T>(Transform parent) where T : Component
        {
            T value = parent.GetComponent<T>();
            return value != null ? value : parent.gameObject.AddComponent<T>();
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return (RectTransform)existing;
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(x, -y, 0);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }
    }
}
