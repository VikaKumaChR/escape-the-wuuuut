using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabCyberpunkUpgrade
    {
        private const string Folder = "Assets/DepthLab/UI";
        private const string Prefabs = Folder + "/Prefabs";
        private const float Width = 2260, Height = 1330;
        private static readonly Color Background = Hex("050816"), Navy = Hex("081329");
        private static readonly Color Blue = Hex("1677FF"), Cyan = Hex("00E5FF");
        private static readonly Color Purple = Hex("A855F7"), Magenta = Hex("FF35B8");
        private static readonly Color Text = Hex("EAF6FF"), Secondary = Hex("83A9C9");
        private static readonly Color[] ChapterColors = { Cyan, Blue, Purple, Magenta };
        private static readonly string[] Descriptions = {
            "用双眼视差判断距离，让实体与目标归位。",
            "移动观察位置，发现被遮挡的安全路径。",
            "比较大小与长度，拆解透视制造的错觉。",
            "调整光源与镜面，追踪材质和反射的线索。"
        };
        private static TMP_FontAsset font;
        private static Sprite panelSprite, strokeSprite, gridSprite;
        private static Sprite[] icons;

        [MenuItem("DEPTHLAB/Upgrade Cyberpunk Interface In Place")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before applying the cyberpunk migration.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || scene.path != "Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity")
                throw new InvalidOperationException("Open the installed ParallaxChamber scene first.");
            DepthLabApp[] apps = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<DepthLabApp>(true)).ToArray();
            if (apps.Length != 1) throw new InvalidOperationException("Expected exactly one installed DEPTHLAB app.");
            DepthLabApp app = apps[0];
            GameObject[] pages = Pages(app);
            if (app.menuCanvas == null || app.menuCanvas.GetComponent<Canvas>() == null ||
                pages.Any(p => p == null) || app.chapters == null || app.chapters.Length != 4 ||
                app.previews == null || app.previews.Length != 4 || app.previews.Any(p => p == null) ||
                app.homeStatuses == null || app.homeStatuses.Length != 4 ||
                app.chapterStatuses == null || app.chapterStatuses.Length != 4 ||
                app.finalCards == null || app.finalCards.Length != 4 ||
                app.homeStatuses.Any(t => t == null) || app.chapterStatuses.Any(t => t == null) ||
                app.finalCards.Any(t => t == null) || app.tooltipText == null)
                throw new InvalidOperationException("The installed ten menu pages, four previews and saved-progress readouts are required.");
            int eventSystems = scene.GetRootGameObjects()
                .Sum(r => r.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length);
            if (eventSystems != 1 || app.uiModule == null ||
                app.menuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                throw new InvalidOperationException("Preserve the existing single XR EventSystem, XRUIInputModule and tracked raycaster.");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/DepthLab/Generated/DepthLab Latin SDF.asset");
            if (font == null) throw new InvalidOperationException("The installed TMP font is required.");
            // Validate before touching the hierarchy. Locked cards must keep their original detail commands.
            RequireButton(app.homePage.transform, LabAction.Start);
            RequireButton(app.homePage.transform, LabAction.Chapters);
            RequireButton(app.homePage.transform, LabAction.Tutorial);
            RequireButton(app.homePage.transform, LabAction.Settings);
            RequireButton(app.homePage.transform, LabAction.Exit);
            for (int i = 0; i < 4; i++)
            {
                RequireButton(app.homePage.transform, LabAction.Detail, i);
                RequireButton(app.chapterPage.transform, LabAction.Detail, i);
            }
            var callbacks = app.menuCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                .ToDictionary(b => b, CallbackSignature);
            Undo.RegisterFullObjectHierarchyUndo(app.gameObject, "Cyberpunk UI in place");
            BuildAtlas();
            Canvas canvas = app.menuCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            RectTransform menu = (RectTransform)app.menuCanvas;
            menu.pivot = new Vector2(.5f, .5f);
            menu.sizeDelta = new Vector2(Width, Height);
            menu.localScale = Vector3.one * .0015f;
            foreach (GameObject page in pages) Layout((RectTransform)page.transform, 0, 0, Width, Height);
            UpgradeChrome(app);
            foreach (GameObject page in pages)
            {
                if (page == app.homePage || page == app.chapterPage || page == app.finalPage) continue;
                UpgradeStandardPage(page.transform);
            }
            UpgradeHome(app);
            UpgradeChapters(app);
            UpgradeRecord(app);
            UpgradePageCopy(app);
            DepthLabCyberpunkView view = app.menuCanvas.GetComponent<DepthLabCyberpunkView>();
            if (view == null) view = app.menuCanvas.gameObject.AddComponent<DepthLabCyberpunkView>();
            view.app = app;
            view.researcherLabel = Label(app.menuCanvas, "CyberpunkResearcher", "", 55, 1200, 830, 52, 31, Text);
            view.progressLabel = Label(app.menuCanvas, "CyberpunkProgress", "", 1400, 1200, 805, 52, 31, Cyan);
            var track = Image(app.menuCanvas, "CyberpunkExperienceTrack", 920, 1216, 420, 10, Navy, panelSprite);
            var fill = Image(track.transform, "CyberpunkExperienceFill", 0, 0, 420, 10, Cyan, panelSprite);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            view.experienceFill = fill;
            Label(app.menuCanvas, "CyberpunkExperienceNote", "每完成一章，获得 100 经验", 920, 1236, 460, 32, 23, Secondary);
            view.archiveDetails = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
                view.archiveDetails[i] = app.finalPage.transform.Find("CyberpunkRecordCard" + i)
                    .Find("CyberpunkRunDetails").GetComponent<TMP_Text>();
            PopulateSavedProgress(app, view);
            view.RefreshPreviewLayout();
            CreatePrefabs();
            foreach (var pair in callbacks)
                if (pair.Key == null || CallbackSignature(pair.Key) != pair.Value)
                    throw new InvalidOperationException("An authored button callback changed during migration.");
            ValidateLayout(app);
            foreach (TMP_Text label in app.menuCanvas.GetComponentsInChildren<TMP_Text>(true))
            {
                label.font = font;
                label.fontSharedMaterial = font.material;
                label.raycastTarget = false;
                EditorUtility.SetDirty(label);
            }
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(app);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Cyberpunk UI authored in place: 3.39 x 1.995 m at the existing 2.8 m distance; " +
                "original pages/buttons/callbacks and XR EventSystem retained. Shared angular atlas, animated neural " +
                "hologram, real progress and Panel/Button/ChapterCard/HUD prefabs generated. Caller owns scene save and visual validation.");
        }

        private static GameObject[] Pages(DepthLabApp app) => new[] {
            app.homePage, app.chapterPage, app.detailPage, app.settingsPage, app.creditsPage,
            app.pausePage, app.resultPage, app.finalPage, app.modalPage, app.tutorialPage
        };

        private static string CallbackSignature(UnityEngine.UI.Button button)
        {
            return string.Join("|", Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                .Select(i => button.onClick.GetPersistentTarget(i)?.GetEntityId() + ":" +
                    button.onClick.GetPersistentMethodName(i) + ":" + button.onClick.GetPersistentListenerState(i)));
        }

        private static void UpgradeChrome(DepthLabApp app)
        {
            foreach (Transform child in app.menuCanvas)
            {
                if (child == app.tooltipText.transform) continue;
                TMP_Text old = child.GetComponent<TMP_Text>();
                if (old != null && !child.name.StartsWith("Cyberpunk", StringComparison.Ordinal))
                    old.gameObject.SetActive(false);
                if (new[] { "Glass backing", "Top cyan edge", "Left registration", "Header divider", "Footer divider" }.Contains(child.name))
                    child.gameObject.SetActive(false);
            }
            Transform chrome = Rect(app.menuCanvas, "CyberpunkChrome", 0, 0, Width, Height);
            chrome.SetAsFirstSibling();
            Image(chrome, "ObsidianGlass", 0, 0, Width, Height, Background, panelSprite);
            Image(chrome, "Perimeter", 0, 0, Width, Height, Alpha(Cyan, .7f), strokeSprite);
            Image(chrome, "SparseGrid", 20, 90, Width - 40, Height - 170, Alpha(Blue, .14f), gridSprite,
                UnityEngine.UI.Image.Type.Tiled);
            Image(chrome, "TopRule", 55, 99, 2150, 2, Alpha(Cyan, .5f), panelSprite);
            Image(chrome, "BottomRule", 55, 1180, 2150, 2, Alpha(Cyan, .5f), panelSprite);
            Label(chrome, "Identity", "深度实验室  /  感知研究", 55, 30, 1250, 50, 30, Text);
            Label(chrome, "Status", "感知实验  /  自由观察 · 逐章探索", 1450, 33, 750, 45, 26, Secondary);
            Layout(app.tooltipText.rectTransform, 55, 1268, 2150, 40);
            StyleText(app.tooltipText, 24, Cyan);
            app.tooltipText.gameObject.SetActive(true);
        }

        private static void UpgradeHome(DepthLabApp app)
        {
            Transform page = app.homePage.transform;
            HideOldDirectLabels(page);
            Panel(page, "CyberpunkHomeNavigation", 55, 140, 680, 995, Cyan);
            Panel(page, "CyberpunkHomeNeural", 770, 140, 810, 995, Purple);
            Panel(page, "CyberpunkHomeArchive", 1620, 140, 585, 995, Blue);
            Label(page, "CyberpunkHomeLogo", "深度实验室", 92, 194, 600, 100, 74, Text);
            Label(page, "CyberpunkHomeVR", "VR", 94, 305, 180, 58, 43, Cyan);
            Label(page, "CyberpunkHomeSubtitle", "虚拟现实深度感知体验", 94, 380, 600, 58, 34, Secondary);
            Label(page, "CyberpunkHomeTagline", "深入感知，重新认识现实。", 94, 457, 600, 48, 28, Text);
            PlaceButton(RequireButton(page, LabAction.Start), 94, 542, 600, 120, "进入实验室", Cyan, true, 0);
            PlaceButton(RequireButton(page, LabAction.Chapters), 94, 704, 282, 92, "实验选择", Blue, false, 1);
            PlaceButton(RequireButton(page, LabAction.Tutorial), 410, 704, 284, 92, "操作教程", Cyan, false, 2);
            PlaceButton(RequireButton(page, LabAction.Settings), 94, 830, 282, 92, "设置", Purple, false, 3);
            UnityEngine.UI.Button record = FindButton(page, LabAction.Final);
            if (record == null) record = NewButton(page, "CyberpunkResearchRecord", LabAction.Final);
            PlaceButton(record, 410, 830, 284, 92, "研究记录", Magenta, false, 4);
            PlaceButton(RequireButton(page, LabAction.Exit), 94, 958, 600, 88, "退出", Secondary, false, 5);
            UnityEngine.UI.Button credits = FindButton(page, LabAction.Credits);
            if (credits != null) PlaceButton(credits, 1940, 1076, 230, 48, "制作信息", Secondary, false, -1);
            Label(page, "CyberpunkNeuralTitle", "神经感知网络", 810, 180, 710, 58, 34, Text);
            Label(page, "CyberpunkNeuralSubtitle", "观察 · 空间 · 深度", 810, 245, 710, 40, 27, Secondary);
            RectTransform holo = Rect(page, "CyberpunkNeuralHologram", 850, 306, 650, 740);
            if (holo.GetComponent<DepthLabCyberpunkHologram>() == null)
                holo.gameObject.AddComponent<DepthLabCyberpunkHologram>();
            Label(page, "CyberpunkNeuralCaption", "双眼视差  /  运动视差  /  视觉错觉  /  光影", 810, 1073, 710, 45, 26, Cyan);
            Label(page, "CyberpunkAboutTitle", "关于游戏", 1655, 180, 510, 58, 37, Text);
            Label(page, "CyberpunkAboutBody", "穿过四间感知实验室，探索距离、\n透视与光线如何塑造眼前的世界。", 1655, 258, 510, 105, 28, Secondary);
            for (int i = 0; i < 4; i++)
            {
                UnityEngine.UI.Button card = RequireButton(page, LabAction.Detail, i);
                Layout((RectTransform)card.transform, 1620, 385 + i * 160, 570, 144);
                StyleButton(card, ChapterColors[i], false);
                TMP_Text[] old = card.GetComponentsInChildren<TMP_Text>(true).Where(t => t != app.homeStatuses[i]).ToArray();
                foreach (TMP_Text t in old) t.gameObject.SetActive(false);
                Label(card.transform, "CyberpunkMiniTitle", DepthLabProgress.Titles[i], 130, 15, 420, 44, 30, Text);
                Label(card.transform, "CyberpunkMiniConcept", $"0{i + 1}  /  {DepthLabProgress.Concepts[i]}", 130, 63, 420, 34, 24, ChapterColors[i]);
                app.homeStatuses[i].transform.SetParent(card.transform, false);
                app.homeStatuses[i].gameObject.SetActive(true);
                Layout(app.homeStatuses[i].rectTransform, 130, 103, 420, 32);
                StyleText(app.homeStatuses[i], 24, Secondary);
            }
            Label(page, "CyberpunkArchiveNote", "完成章节后自动保存研究进度", 1655, 1040, 510, 34, 24, Secondary);
        }

        private static void UpgradeChapters(DepthLabApp app)
        {
            Transform page = app.chapterPage.transform;
            HideOldDirectLabels(page);
            Label(page, "CyberpunkChapterHeading", "选择你的感知实验", 55, 145, 1700, 70, 48, Text);
            Label(page, "CyberpunkChapterSubheading", "完成前一章以解锁下一章；未解锁的实验仍可查看详情。", 55, 222, 2100, 42, 28, Secondary);
            for (int i = 0; i < 4; i++)
            {
                UnityEngine.UI.Button card = RequireButton(page, LabAction.Detail, i);
                Layout((RectTransform)card.transform, 55 + i % 2 * 1090, 280 + i / 2 * 340, 1060, 310);
                StyleButton(card, ChapterColors[i], false);
                foreach (TMP_Text old in card.GetComponentsInChildren<TMP_Text>(true))
                    if (old != app.chapterStatuses[i] && !old.name.StartsWith("Cyberpunk", StringComparison.Ordinal))
                        old.gameObject.SetActive(false);
                Label(card.transform, "CyberpunkChapterIndex", $"0{i + 1}  /  {DepthLabProgress.Concepts[i]}  /  {DepthLabProgress.Times[i]}",
                    30, 26, 750, 42, 27, ChapterColors[i]);
                Label(card.transform, "CyberpunkChapterTitle", DepthLabProgress.Titles[i], 30, 82, 740, 64, 44, Text);
                Label(card.transform, "CyberpunkChapterDescription", Descriptions[i], 30, 157, 760, 66, 28, Secondary);
                app.chapterStatuses[i].transform.SetParent(card.transform, false);
                app.chapterStatuses[i].gameObject.SetActive(true);
                Layout(app.chapterStatuses[i].rectTransform, 30, 243, 985, 45);
                StyleText(app.chapterStatuses[i], 27, ChapterColors[i]);
                Image(card.transform, "CyberpunkPreviewDivider", 798, 33, 2, 174, Alpha(ChapterColors[i], .35f), panelSprite);
            }
            PlaceButton(RequireButton(page, LabAction.Menu), 55, 1020, 430, 86, "返回首页", Cyan, false, -1);
            PlaceButton(RequireButton(page, LabAction.Final), 1720, 1020, 485, 86, "研究记录", Purple, false, 4);
        }

        private static void UpgradeStandardPage(Transform page)
        {
            bool migrated = page.Find("CyberpunkLayoutStamp") != null;
            if (!migrated)
            {
                foreach (Transform child in page)
                {
                    if (!(child is RectTransform rect)) continue;
                    Vector2 pos = rect.anchoredPosition;
                    Vector2 size = rect.sizeDelta;
                    Layout(rect, pos.x * 1.38f, -pos.y * 1.22f, size.x * 1.38f, size.y * 1.22f);
                }
                Rect(page, "CyberpunkLayoutStamp", 0, 0, 1, 1);
            }
            Panel(page, "CyberpunkPageGlass", 55, 132, 2150, 1000, Cyan);
            foreach (TMP_Text label in page.GetComponentsInChildren<TMP_Text>(true))
            {
                string translated = DepthLabChineseLocalization.Translate(label.text);
                label.text = translated;
                StyleText(label, Mathf.Clamp(label.fontSize, 30, 64), Text);
            }
            foreach (UnityEngine.UI.Image image in page.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.name.StartsWith("Cyberpunk", StringComparison.Ordinal)) continue;
                image.sprite = panelSprite;
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.color = image.name == "Fill" ? Blue : image.name == "Handle" ? Cyan : Navy;
                image.raycastTarget = image.name == "Handle" || image.name == "Button face";
            }
            foreach (UnityEngine.UI.Button button in page.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                DepthLabCommand command = button.GetComponent<DepthLabCommand>();
                bool primary = command != null && new[] { LabAction.Resume, LabAction.PlaySelected,
                    LabAction.NextChapter, LabAction.TutorialNext, LabAction.ConfirmExit, LabAction.ConfirmReset }.Contains(command.action);
                StyleButton(button, primary ? Cyan : Blue, primary);
                TMP_Text caption = button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                if (caption != null)
                {
                    Vector2 size = ((RectTransform)button.transform).sizeDelta;
                    Layout(caption.rectTransform, 30, 0, size.x - 60, size.y);
                    StyleText(caption, 32, Text);
                    caption.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
            foreach (UnityEngine.UI.Slider slider in page.GetComponentsInChildren<UnityEngine.UI.Slider>(true))
            {
                ColorBlockSlider(slider);
                // Slider-driven anchors must not be replaced by the panel layout.
                if (slider.fillRect != null)
                {
                    slider.fillRect.anchorMin = new Vector2(0, 0);
                    slider.fillRect.anchorMax = new Vector2(1, 1);
                    slider.fillRect.offsetMin = new Vector2(0, 10);
                    slider.fillRect.offsetMax = new Vector2(0, -10);
                }
                if (slider.handleRect != null)
                {
                    slider.handleRect.sizeDelta = new Vector2(30, 44);
                    slider.handleRect.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
                }
            }
        }

        private static void ColorBlockSlider(UnityEngine.UI.Slider slider)
        {
            var colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Cyan;
            colors.pressedColor = Purple;
            colors.selectedColor = Cyan;
            colors.disabledColor = Alpha(Secondary, .4f);
            slider.colors = colors;
        }

        private static void UpgradeRecord(DepthLabApp app)
        {
            Transform page = app.finalPage.transform;
            HideOldDirectLabels(page, app.finalTotals);
            foreach (Transform old in page)
                if (old.name.StartsWith("Result card ", StringComparison.Ordinal)) old.gameObject.SetActive(false);
            Label(page, "CyberpunkRecordHeading", "研究记录", 55, 142, 2100, 64, 48, Text);
            Layout(app.finalTotals.rectTransform, 55, 230, 2130, 145);
            StyleText(app.finalTotals, 35, Cyan);
            for (int i = 0; i < 4; i++)
            {
                Transform card = Panel(page, "CyberpunkRecordCard" + i, 55 + i % 2 * 1090, 400 + i / 2 * 285,
                    1060, 255, ChapterColors[i]);
                app.finalCards[i].transform.SetParent(card, false);
                app.finalCards[i].gameObject.SetActive(true);
                Layout(app.finalCards[i].rectTransform, 30, 22, 990, 100);
                StyleText(app.finalCards[i], 30, Text);
                Label(card, "CyberpunkRunDetails", "", 30, 142, 990, 90, 28, Secondary);
            }
            PlaceButton(RequireButton(page, LabAction.PlayAgain), 55, 1020, 640, 86, "重新探索", Cyan, false, 0);
            PlaceButton(RequireButton(page, LabAction.Chapters), 810, 1020, 640, 86, "实验选择", Blue, false, 1);
            PlaceButton(RequireButton(page, LabAction.Menu), 1565, 1020, 640, 86, "返回首页", Purple, false, -1);
        }

        private static void UpgradePageCopy(DepthLabApp app)
        {
            SetDirectCopy(app.detailPage.transform, new[] { app.detailTitle, app.detailBody, app.detailStatus }, null);
            Layout(app.detailTitle.rectTransform, 85, 165, 2020, 110);
            Layout(app.detailBody.rectTransform, 85, 335, 1450, 550);
            StyleText(app.detailBody, 34, Secondary);
            Layout(app.detailStatus.rectTransform, 85, 925, 1960, 56);
            StyleText(app.detailStatus, 32, Cyan);
            Label(app.detailPage.transform, "CyberpunkPreviewCaption", "实验全息预览", 1610, 390, 490, 52, 32, Purple);
            PlaceButton(RequireButton(app.detailPage.transform, LabAction.Chapters), 85, 1030, 680, 88, "返回实验选择", Blue, false, -1);
            PlaceButton(app.playSelectedButton, 1505, 1030, 600, 88, "进入实验室", Cyan, true, 0);
            SetDirectCopy(app.pausePage.transform, null, new[] {
                "实验已暂停", "计时、实验交互与移动已暂停。\n头部追踪与界面操作保持可用。"
            });
            SetDirectCopy(app.modalPage.transform, new[] { app.modalText }, new[] { "确认操作" });
            StyleText(app.modalText, 40, Text);
            SetDirectCopy(app.tutorialPage.transform, new[] { app.tutorialText }, new[] {
                "操作教程", "左侧操作练习\n\n抓取青色物体。\n在亮起的目标处松开。\n\n桌面抓取时，滚轮调整远近。"
            });
            Layout(app.tutorialText.rectTransform, 800, 330, 1270, 535);
            StyleText(app.tutorialText, 36, Text);
            SetDirectCopy(app.settingsPage.transform, null, new[] {
                "体验设置", "按你的习惯调整声音、移动方式与画面。",
                "总音量", "音乐", "音效", "移动速度  /  米每秒", "灯光亮度",
                "桌面：WASD 移动，Q/E 转向。\n暂停时仍可调整设置。"
            });
            SetDirectCopy(app.creditsPage.transform, null, new[] {
                "制作信息",
                "深度实验室 VR\n\n基于 Unity、uGUI、TextMeshPro 与 XR 交互工具包构建。\n原创建模、程序化声音、神经网络全息界面与线性图标。\n字体：Noto Sans SC，授权文件见项目内 OFL.txt。\n\n四项感知研究：双眼视差、运动视差、视觉错觉、光线与材质。\n\n开发与设备说明：桌面研究预览；VR 加载器配置与头显验证仍需完成。"
            });
            foreach (GameObject page in Pages(app))
                foreach (DepthLabCommand command in page.GetComponentsInChildren<DepthLabCommand>(true))
                {
                    UnityEngine.UI.Button button = command.GetComponent<UnityEngine.UI.Button>();
                    if (button == null || command.action == LabAction.Detail) continue;
                    TMP_Text caption = button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.activeSelf);
                    if (caption != null) caption.text = ActionLabel(command.action);
                    if (button is DepthLabButton labButton) labButton.tooltip = ActionTooltip(command.action);
                }
        }

        private static void SetDirectCopy(Transform page, TMP_Text[] preserve, string[] copy)
        {
            int index = 0;
            foreach (Transform child in page)
            {
                TMP_Text label = child.GetComponent<TMP_Text>();
                if (label == null || child.name.StartsWith("Cyberpunk", StringComparison.Ordinal) ||
                    (preserve != null && preserve.Contains(label))) continue;
                if (copy != null && index < copy.Length) label.text = copy[index++];
            }
        }

        private static string ActionLabel(LabAction action)
        {
            switch (action)
            {
                case LabAction.Start: case LabAction.PlaySelected: return "进入实验室";
                case LabAction.Chapters: return "实验选择";
                case LabAction.Tutorial: return "操作教程";
                case LabAction.Settings: return "设置";
                case LabAction.Credits: return "制作信息";
                case LabAction.Exit: return "退出";
                case LabAction.Final: return "研究记录";
                case LabAction.Menu: return "返回首页";
                case LabAction.SettingsBack: return "返回";
                case LabAction.Resume: return "继续实验";
                case LabAction.Replay: return "重试实验";
                case LabAction.PlayAgain: return "重新探索";
                case LabAction.NextChapter: return "下一章";
                case LabAction.ResetProgress: return "重置进度";
                case LabAction.Defaults: return "恢复默认";
                case LabAction.CancelModal: return "取消";
                case LabAction.ConfirmExit: case LabAction.ConfirmReset: return "确认";
                case LabAction.TutorialSkip: return "跳过教程";
                case LabAction.Hint: return "提示";
                case LabAction.TutorialNext: return "下一步";
                default: return "查看详情";
            }
        }

        private static string ActionTooltip(LabAction action)
        {
            switch (action)
            {
                case LabAction.Start: return "继续下一间已解锁的感知实验室。";
                case LabAction.Final: return "查看已保存的成绩、星级与研究进度。";
                case LabAction.ResetProgress: return "确认后仅清除章节成绩与解锁记录，保留设置。";
                case LabAction.TutorialNext: return "完成本步操作后继续。";
                case LabAction.Hint: return "查看提示；实验中的提示会计入评分。";
                case LabAction.Exit: return "确认后退出，已完成的研究记录会保留。";
                default: return ActionLabel(action);
            }
        }

        private static void PopulateSavedProgress(DepthLabApp app, DepthLabCyberpunkView view)
        {
            int completed = 0, stars = 0;
            float seconds = 0;
            for (int i = 0; i < 4; i++)
            {
                ChapterRecord record = DepthLabProgress.Read(i);
                if (record.completed) { completed++; stars += record.stars; seconds += record.seconds; }
                app.chapterStatuses[i].text = DepthLabProgress.Summary(i);
                app.homeStatuses[i].text = record.completed ? "已完成  " + new string('★', record.stars) :
                    DepthLabProgress.Unlocked(i) ? "可以探索" : "未解锁";
                app.finalCards[i].text = $"0{i + 1}  {DepthLabProgress.Titles[i]}\n{DepthLabProgress.Summary(i)}";
                view.archiveDetails[i].text = record.completed
                    ? $"用时 {DepthLabProgress.TimeLabel(record.seconds)}  /  错误 {record.mistakes}  /  提示 {record.hints}\n重试 {record.retries}  /  尝试 {record.attempts}"
                    : $"未完成  /  尝试 {record.attempts}";
            }
            app.finalTotals.text = $"{(completed == 4 ? "深度探索者" : "探索进行中")}\n实验室 {completed}/4  /  星级 {stars}/12  /  最佳成绩总用时 {DepthLabProgress.TimeLabel(seconds)}";
            view.researcherLabel.text = $"研究员  /  等级 {completed + 1}  /  经验 {completed * 100}";
            view.progressLabel.text = $"已完成 {completed}/4  /  星星 {stars}/12";
            view.experienceFill.fillAmount = completed / 4f;
        }

        private static void HideOldDirectLabels(Transform page, TMP_Text preserve = null)
        {
            foreach (Transform child in page)
                if (!child.name.StartsWith("Cyberpunk", StringComparison.Ordinal) &&
                    child.TryGetComponent<TMP_Text>(out var label) && label != preserve)
                    child.gameObject.SetActive(false);
        }

        private static UnityEngine.UI.Button FindButton(Transform parent, LabAction action, int argument = 0)
        {
            return parent.GetComponentsInChildren<DepthLabCommand>(true)
                .Where(c => c.action == action && (action != LabAction.Detail || c.argument == argument))
                .Select(c => c.GetComponent<UnityEngine.UI.Button>()).FirstOrDefault(b => b != null);
        }

        private static UnityEngine.UI.Button RequireButton(Transform parent, LabAction action, int argument = 0)
        {
            var button = FindButton(parent, action, argument);
            if (button == null) throw new InvalidOperationException($"Missing authored {action} button on {parent.name}.");
            return button;
        }

        private static UnityEngine.UI.Button NewButton(Transform parent, string name, LabAction? action)
        {
            RectTransform root = Rect(parent, name, 0, 0, 600, 90);
            var button = root.GetComponent<DepthLabButton>();
            if (button == null) button = root.gameObject.AddComponent<DepthLabButton>();
            if (action.HasValue)
            {
                var command = root.GetComponent<DepthLabCommand>();
                if (command == null)
                {
                    command = root.gameObject.AddComponent<DepthLabCommand>();
                    command.action = action.Value;
                    UnityEventTools.AddPersistentListener(button.onClick, command.Execute);
                }
            }
            return button;
        }

        private static void PlaceButton(UnityEngine.UI.Button button, float x, float y, float w, float h,
            string caption, Color accent, bool primary, int icon)
        {
            Layout((RectTransform)button.transform, x, y, w, h);
            StyleButton(button, accent, primary);
            TMP_Text label = button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (label == null) label = Label(button.transform, "CyberpunkCaption", caption, 30, 0, w - 60, h, 32, Text);
            label.gameObject.SetActive(true);
            label.text = caption;
            Layout(label.rectTransform, icon < 0 ? 30 : 86, 0, w - (icon < 0 ? 60 : 112), h);
            StyleText(label, primary ? 46 : h < 60 ? 24 : 32, Text);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            if (icon >= 0) Image(button.transform, "CyberpunkIcon", 26, (h - 40) * .5f, 40, 40, primary ? Text : accent, icons[icon]);
        }

        private static void StyleButton(UnityEngine.UI.Button button, Color accent, bool primary)
        {
            RectTransform rect = (RectTransform)button.transform;
            Vector2 size = rect.sizeDelta;
            UnityEngine.UI.Image face = button.targetGraphic as UnityEngine.UI.Image;
            if (face == null) face = Image(rect, "CyberpunkButtonFace", 0, 0, size.x, size.y, Color.white, panelSprite);
            Layout(face.rectTransform, 0, 0, size.x, size.y);
            face.sprite = panelSprite;
            face.type = UnityEngine.UI.Image.Type.Sliced;
            face.raycastTarget = true;
            face.color = Color.white;
            face.transform.SetAsFirstSibling();
            button.targetGraphic = face;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = primary ? Blue : Navy;
            colors.highlightedColor = primary ? Hex("1659B8") : Hex("10334D");
            colors.selectedColor = Hex("10334D");
            colors.pressedColor = Purple;
            colors.disabledColor = Alpha(Navy, .65f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .13f;
            button.colors = colors;
            face.canvasRenderer.SetColor(colors.normalColor);
            Image(rect, "CyberpunkStroke", 0, 0, size.x, size.y, accent, strokeSprite);
            Image(rect, "CyberpunkAccent", 0, 18, 4, Mathf.Max(10, size.y - 36), accent, panelSprite);
            if (button is DepthLabButton labButton)
            {
                if (labButton.halo != null)
                {
                    Layout(labButton.halo.rectTransform, -3, -3, size.x + 6, size.y + 6);
                    labButton.halo.sprite = strokeSprite;
                    labButton.halo.type = UnityEngine.UI.Image.Type.Sliced;
                    labButton.halo.raycastTarget = false;
                    labButton.halo.color = Color.clear;
                }
            }
        }

        private static Transform Panel(Transform parent, string name, float x, float y, float w, float h, Color accent)
        {
            RectTransform panel = Rect(parent, name, x, y, w, h);
            panel.SetAsFirstSibling();
            Image(panel, "CyberpunkGlass", 0, 0, w, h, Alpha(Navy, .95f), panelSprite);
            Image(panel, "CyberpunkBorder", 0, 0, w, h, Alpha(accent, .65f), strokeSprite);
            Image(panel, "CyberpunkRegistration", 24, 0, Mathf.Min(150, w / 3), 4, accent, panelSprite);
            return panel;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            Transform existing = parent.Find(name);
            RectTransform rect;
            if (existing == null)
            {
                rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                rect.SetParent(parent, false);
            }
            else
            {
                rect = existing as RectTransform;
                if (rect == null) throw new InvalidOperationException("Expected UI RectTransform: " + name);
            }
            Layout(rect, x, y, w, h);
            return rect;
        }

        private static void Layout(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition3D = new Vector3(x, -y, 0);
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static UnityEngine.UI.Image Image(Transform parent, string name, float x, float y, float w, float h,
            Color color, Sprite sprite, UnityEngine.UI.Image.Type type = UnityEngine.UI.Image.Type.Sliced)
        {
            var rect = Rect(parent, name, x, y, w, h);
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string copy, float x, float y, float w, float h,
            float size, Color color)
        {
            RectTransform rect = Rect(parent, name, x, y, w, h);
            TMP_Text label = rect.GetComponent<TMP_Text>();
            if (label == null) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = copy;
            StyleText(label, size, color);
            return label;
        }

        private static void StyleText(TMP_Text label, float size, Color color)
        {
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void BuildAtlas()
        {
            Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh();
            string path = Folder + "/CyberpunkLineAtlas.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(512, 256, TextureFormat.RGBA32, false);
                texture.name = "CyberpunkLineAtlas";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                AssetDatabase.CreateAsset(texture, path);
            }
            Color32[] pixels = new Color32[512 * 256];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    bool inside = x + y >= 18 && x + y <= 236 && x - y <= 109 && y - x <= 109;
                    if (inside) pixels[y * 512 + x] = Color.white;
                    bool inset = x >= 2 && x <= 125 && y >= 2 && y <= 125 &&
                        x + y >= 21 && x + y <= 233 && x - y <= 106 && y - x <= 106;
                    if (inside && !inset) pixels[y * 512 + 128 + x] = Color.white;
                    if (x % 64 == 0 || y % 64 == 0)
                        pixels[y * 512 + 256 + x] = new Color32(255, 255, 255, 100);
                    else if (y % 16 == 0)
                        pixels[y * 512 + 256 + x] = new Color32(255, 255, 255, 30);
                }
            // Original line icons: enter, chapters, tutorial, settings, archive, exit, eye, light.
            DrawIcon(pixels, 0, new[] { V(14, 14), V(14, 50), V(34, 50), V(34, 40) });
            DrawIcon(pixels, 0, new[] { V(14, 14), V(34, 14), V(34, 24) });
            DrawIcon(pixels, 0, new[] { V(22, 32), V(52, 32), V(42, 22) });
            DrawIcon(pixels, 0, new[] { V(52, 32), V(42, 42) });
            for (int i = 0; i < 4; i++)
            {
                float x = 12 + i % 2 * 24, y = 12 + i / 2 * 24;
                DrawIcon(pixels, 1, new[] { V(x + 4, y), V(x + 17, y), V(x + 20, y + 4), V(x + 20, y + 18),
                    V(x, y + 18), V(x, y + 4), V(x + 4, y) });
            }
            DrawIcon(pixels, 2, new[] { V(12, 48), V(30, 43), V(30, 14), V(12, 19), V(12, 48), V(30, 43),
                V(50, 48), V(50, 19), V(30, 14) });
            DrawIcon(pixels, 3, new[] { V(12, 18), V(52, 18) });
            DrawIcon(pixels, 3, new[] { V(12, 32), V(52, 32) });
            DrawIcon(pixels, 3, new[] { V(12, 46), V(52, 46) });
            DrawIcon(pixels, 3, new[] { V(24, 13), V(24, 23) });
            DrawIcon(pixels, 3, new[] { V(42, 27), V(42, 37) });
            DrawIcon(pixels, 3, new[] { V(22, 41), V(22, 51) });
            DrawIcon(pixels, 4, new[] { V(15, 12), V(49, 12), V(49, 44), V(39, 54), V(15, 54), V(15, 12) });
            DrawIcon(pixels, 4, new[] { V(23, 24), V(41, 24) });
            DrawIcon(pixels, 4, new[] { V(23, 34), V(41, 34) });
            DrawIcon(pixels, 4, new[] { V(23, 44), V(32, 44) });
            DrawIcon(pixels, 5, new[] { V(26, 12), V(13, 12), V(13, 52), V(26, 52) });
            DrawIcon(pixels, 5, new[] { V(24, 32), V(52, 32), V(42, 22) });
            DrawIcon(pixels, 5, new[] { V(52, 32), V(42, 42) });
            DrawIcon(pixels, 6, new[] { V(8, 32), V(20, 20), V(44, 20), V(56, 32), V(44, 44), V(20, 44), V(8, 32) });
            DrawIcon(pixels, 6, new[] { V(26, 26), V(38, 26), V(38, 38), V(26, 38), V(26, 26) });
            DrawIcon(pixels, 7, new[] { V(36, 10), V(16, 34), V(31, 34), V(27, 54), V(49, 28), V(34, 28), V(36, 10) });
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            panelSprite = AtlasSprite(path, texture, "ChamferFill", new Rect(0, 0, 128, 128), new Vector4(22, 22, 22, 22));
            strokeSprite = AtlasSprite(path, texture, "ChamferStroke", new Rect(128, 0, 128, 128), new Vector4(22, 22, 22, 22));
            gridSprite = AtlasSprite(path, texture, "SparseGrid", new Rect(256, 0, 128, 128), Vector4.zero);
            icons = Enumerable.Range(0, 8).Select(i => AtlasSprite(path, texture, "LineIcon" + i,
                new Rect(i * 64, 128, 64, 64), Vector4.zero)).ToArray();
        }

        private static Sprite AtlasSprite(string path, Texture2D texture, string name, Rect rect, Vector4 border)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(s => s.name == name);
            if (sprite != null) return sprite;
            sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            AssetDatabase.AddObjectToAsset(sprite, path);
            return sprite;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static void DrawIcon(Color32[] pixels, int icon, Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = points[i - 1], b = points[i];
                for (int y = 4; y < 60; y++)
                    for (int x = 4; x < 60; x++)
                    {
                        Vector2 point = new Vector2(x, y);
                        float t = Mathf.Clamp01(Vector2.Dot(point - a, b - a) / (b - a).sqrMagnitude);
                        float distance = Vector2.Distance(point, Vector2.Lerp(a, b, t));
                        byte alpha = (byte)(255 * Mathf.Clamp01(2.0f - distance));
                        int index = (128 + y) * 512 + icon * 64 + x;
                        if (alpha > pixels[index].a) pixels[index] = new Color32(255, 255, 255, alpha);
                    }
            }
        }

        private static void CreatePrefabs()
        {
            RectTransform root = null;
            try
            {
                root = (RectTransform)new GameObject("CyberpunkPanel", typeof(RectTransform)).transform;
                Layout(root, 0, 0, 640, 320);
                Panel(root, "Panel", 0, 0, 640, 320, Cyan);
                SavePrefab(root, "Panel");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                root = (RectTransform)new GameObject("CyberpunkButton", typeof(RectTransform)).transform;
                Layout(root, 0, 0, 600, 96);
                UnityEngine.UI.Button button = NewButton(root, "Button", null);
                PlaceButton(button, 0, 0, 600, 96, "进入实验室", Cyan, true, 0);
                SavePrefab(root, "Button");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                root = (RectTransform)new GameObject("CyberpunkChapterCard", typeof(RectTransform)).transform;
                Layout(root, 0, 0, 1060, 310);
                button = NewButton(root, "ChapterCard", null);
                Layout((RectTransform)button.transform, 0, 0, 1060, 310);
                StyleButton(button, Purple, false);
                Label(button.transform, "Concept", "01  /  双眼视差", 30, 28, 990, 44, 28, Purple);
                Label(button.transform, "Title", "立体视差之室", 30, 85, 990, 62, 44, Text);
                Label(button.transform, "Description", Descriptions[0], 30, 163, 990, 64, 28, Secondary);
                Label(button.transform, "Status", "尚未探索", 30, 245, 990, 44, 28, Cyan);
                SavePrefab(root, "ChapterCard");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                root = (RectTransform)new GameObject("CyberpunkHUD", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(TrackedDeviceGraphicRaycaster)).transform;
                Layout(root, 0, 0, 1200, 320);
                root.pivot = new Vector2(.5f, .5f);
                root.localScale = Vector3.one * .0013f;
                root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                root.GetComponent<TrackedDeviceGraphicRaycaster>().checkFor3DOcclusion = false;
                Panel(root, "Panel", 0, 0, 1200, 320, Cyan);
                Label(root, "Objective", "感知实验  /  当前目标", 28, 22, 1144, 75, 32, Text);
                Label(root, "Hint", "观察环境，寻找深度线索。", 28, 120, 1144, 54, 28, Secondary);
                for (int i = 0; i < 3; i++)
                {
                    LabAction action = new[] { LabAction.Hint, LabAction.Pause, LabAction.Settings }[i];
                    button = NewButton(root, "Action" + i, action);
                    PlaceButton(button, 28 + i * 398, 221, 348, 74, ActionLabel(action), i == 2 ? Purple : Cyan, false, -1);
                }
                SavePrefab(root, "HUD");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        private static void SavePrefab(RectTransform root, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Prefabs + "/" + name + ".prefab", out bool saved);
            if (!saved) throw new IOException("Could not persist cyberpunk " + name + " prefab.");
        }

        private static void ValidateLayout(DepthLabApp app)
        {
            foreach (GameObject page in Pages(app))
            {
                RectTransform[] buttons = page.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                    .Where(b => b.gameObject.activeSelf).Select(b => (RectTransform)b.transform).ToArray();
                foreach (RectTransform button in buttons)
                {
                    Vector3[] corners = new Vector3[4];
                    button.GetWorldCorners(corners);
                    Vector3 a = page.transform.InverseTransformPoint(corners[1]);
                    Vector3 b = page.transform.InverseTransformPoint(corners[3]);
                    if (a.x < -1 || a.y > 1 || b.x > Width + 1 || b.y < -Height - 1)
                        throw new InvalidOperationException("Button exceeds its page: " + button.name);
                }
            }
            if (app.playSelectedButton == null || app.tutorialNextButton == null || app.nextChapterButton == null)
                throw new InvalidOperationException("The existing gameplay-gated buttons must remain referenced.");
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }

        private static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
    }
}
