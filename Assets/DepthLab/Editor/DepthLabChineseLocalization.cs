using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DepthLab.Editor
{
    public static class DepthLabChineseLocalization
    {
        private const string ScenePath = "Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity";
        private const string LatinPath = "Assets/DepthLab/Generated/DepthLab Latin SDF.asset";
        private const string CjkPath = "Assets/DepthLab/Generated/DepthLab CJK SDF.asset";
        private const string SourcePath = "Assets/TwistedSpace/Demo/Fonts/NotoSansSC-Medium.otf";
        private const string Controls =
            "桌面预览：左键点击／按住抓取，滚轮调远近；右键拖动视角；WASD 移动；Q/E 转向；Esc 暂停。VR 需另配 XR 加载器。";
        private const string ShapeTechnique =
            "01  抓取彩色实体形状\n02  对准同色同形的目标中心\n03  松开，匹配后自动吸附\n\n线框是目标全息影像，不是实体。\n彩色实体形状和底座应有碰撞。\n未匹配时，形状会落在原地，可再次抓取。\n\n桌面：左键按住抓取，滚轮调远近。\nVR（启用 XR 后）：握持键抓取，松开释放。";
        private static readonly Dictionary<string, string> Replacements = BuildReplacements();

        [MenuItem("DEPTHLAB/Localization/Apply Chinese In Place")]
        public static void ApplyToActiveScene()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Exit Play Mode before localizing.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || scene.path != ScenePath)
                throw new InvalidOperationException("Open the existing ParallaxChamber scene first.");
            DepthLabApp[] apps = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<DepthLabApp>(true)).ToArray();
            if (apps.Length != 1 || apps[0].chapters == null || apps[0].chapters.Length != 4)
                throw new InvalidOperationException("Expected one installed DEPTHLAB app with four chapters.");
            DepthLabApp app = apps[0];
            TMP_Text[] texts = app.GetComponentsInChildren<TMP_Text>(true);
            DepthLabButton[] buttons = app.GetComponentsInChildren<DepthLabButton>(true);
            TMP_FontAsset latin = RequireFont(LatinPath);
            TMP_FontAsset cjk = RequireFont(CjkPath);
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (source == null || !File.Exists("Assets/TwistedSpace/Demo/Fonts/OFL.txt"))
                throw new InvalidOperationException("Bundled licensed Noto Sans SC source is required.");

            var corpus = new StringBuilder(Controls).Append(ShapeTechnique);
            foreach (string value in Replacements.Values) corpus.Append(value);
            foreach (TMP_Text text in texts) corpus.Append(Translate(text.text));
            foreach (DepthLabButton button in buttons) corpus.Append(Translate(button.tooltip));
            foreach (LabChapter chapter in app.chapters.OfType<LabChapter>())
                corpus.Append(chapter.Objective).Append(chapter.Hint);
            // Read authored source rather than only the current scene: later tutorial/feedback
            // states and interpolated labels also need glyphs in a finite static atlas.
            foreach (string path in Directory.GetFiles("Assets/DepthLab", "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("/Tests/"))
                .OrderBy(path => path, StringComparer.Ordinal))
                corpus.Append(File.ReadAllText(path));
            uint[] required = CodePoints(corpus.ToString()).Where(IsPrintable).Distinct().OrderBy(c => c).ToArray();

            string backup = BackupSavedFiles();
            int before = cjk.characterTable.Count;
            int atlasBefore = cjk.atlasTextureCount;
            ExtendFont(latin, source, Enumerable.Range(32, 95).Select(c => (uint)c).ToArray());
            ExtendFont(cjk, source, required.Where(c => !latin.HasCharacter((int)c)).ToArray());
            if (latin.fallbackFontAssetTable == null)
                latin.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!latin.fallbackFontAssetTable.Contains(cjk))
                latin.fallbackFontAssetTable.Add(cjk);
            uint[] missing = required.Where(c => !latin.HasCharacter((int)c) && !cjk.HasCharacter((int)c)).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException("Static font coverage incomplete: " + Describe(missing));
            EditorUtility.SetDirty(latin);
            AssetDatabase.SaveAssetIfDirty(latin);
            AssetDatabase.SaveAssetIfDirty(cjk);

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Localize DEPTHLAB text in place");
            int changed = 0;
            foreach (TMP_Text text in texts)
            {
                string translated = Translate(text.text);
                Undo.RecordObject(text, "Chinese player-visible text");
                if (translated != text.text) { text.text = translated; changed++; }
                if (text.font != latin)
                {
                    text.font = latin;
                    text.fontSharedMaterial = latin.material;
                }
                EditorUtility.SetDirty(text);
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            foreach (DepthLabButton button in buttons)
            {
                string translated = Translate(button.tooltip);
                if (translated == button.tooltip) continue;
                Undo.RecordObject(button, "Chinese tooltip");
                button.tooltip = translated;
                EditorUtility.SetDirty(button);
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            }
            foreach (LabChapter chapter in app.chapters.OfType<LabChapter>())
            {
                SetText(chapter.objectiveLabel, chapter.Objective);
                SetText(chapter.hintLabel, chapter.Hint);
                // Long Chinese instructions must stay inside the existing console cells.
                Fit(chapter.hintLabel, 18, 22);
            }
            Fit(app.detailBody, 22, 28);
            Fit(app.tutorialText, 24, 34);
            Fit(app.hintText, 14, 18);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Chinese text applied but active scene could not be saved.");
            Debug.Log($"DEPTHLAB Chinese localization: {changed} labels replaced; static CJK glyphs " +
                $"{before} -> {cjk.characterTable.Count}, atlases {atlasBefore} -> {cjk.atlasTextureCount}; " +
                $"missing glyphs 0. Saved {scene.path}. Pre-patch saved-file backup: {backup}");
        }

        public static string Translate(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string normalized = value.Replace("\r\n", "\n")
                .Replace("未匹配时，形状会返回起始底座。", "未匹配时，形状会落在原地，可再次抓取。");
            if (Replacements.TryGetValue(normalized, out string translated)) return translated;
            Match depth = Regex.Match(normalized, @"^([0-9]+[.,][0-9]+) m FROM START$", RegexOptions.CultureInvariant);
            if (depth.Success) return $"距起点 {depth.Groups[1].Value} 米";
            Match landing = Regex.Match(normalized, @"^(0[1-4])  /  LAND HERE$", RegexOptions.CultureInvariant);
            if (landing.Success) return $"{landing.Groups[1].Value}  /  传送至此";
            return normalized;
        }

        private static Dictionary<string, string> BuildReplacements()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["章节完成"] = "出口未解锁",
                ["先完成实验\n再使用此出口"] = "请先完成本关任务\n完成后出口亮起",
                ["DEPTHLAB   /   PERCEPTION RESEARCH"] = "深度实验室   /   感知研究",
                ["DEPTHLAB   /   IMMERSIVE PERCEPTION RESEARCH"] = "深度实验室   /   沉浸式感知研究",
                ["DEPTHLAB   /   感知研究"] = "深度实验室   /   感知研究",
                ["DEPTHLAB   /   沉浸式感知研究"] = "深度实验室   /   沉浸式感知研究",
                ["SYSTEM ONLINE   •   01.0"] = "桌面预览   •   01.0",
                ["XR  RAY + TRIGGER  /  GRIP TO GRAB     •     DESKTOP  CLICK  /  RMB LOOK  /  WASD  /  Q,E TURN  /  ESC PAUSE"] = Controls,
                ["SEE BEYOND\nTHE OBVIOUS."] = "透过表象\n探索感知",
                ["DEPTH\nLAB"] = "深度\n实验室",
                ["VR"] = "预览",
                ["ESCAPE THE\nDISTORTED SPACE"] = "逃离\n扭曲空间",
                ["ESCAPE THE DISTORTED SPACE"] = "逃离扭曲空间",
                ["Four laboratories.\nOne unreliable reality.\n\nLearn how depth, perspective\nand light shape what you see."] = "四间实验室，\n探索不可靠的感知。\n\n了解深度、透视与光线\n如何影响眼前的世界。",
                ["RESEARCH BUILD   /   01.0\nSEATED OR STANDING"] = "研究预览版   /   01.0\nVR 尚需配置与硬件验证",
                ["START GAME"] = "开始游戏",
                ["CHAPTER SELECT"] = "章节选择",
                ["TUTORIAL"] = "操作教程",
                ["SETTINGS"] = "设置",
                ["CREDITS"] = "制作信息",
                ["EXIT"] = "退出",
                ["SELECT YOUR NEXT EXPERIMENT"] = "选择下一项实验",
                ["YOUR RESEARCH ARCHIVE"] = "研究档案",
                ["Continue at your next unlocked laboratory."] = "进入下一间已解锁的实验室。",
                ["Inspect all four chapters and your saved best runs."] = "查看四个章节与已保存的最佳成绩。",
                ["Seven action-gated calibration steps. Skip at any time."] = "七步操作练习，完成动作后继续，也可随时跳过。",
                ["Adjust audio, comfort, quality and brightness."] = "调整音量、舒适度、画质与亮度。",
                ["Project credits, perception concepts and build information."] = "查看制作信息、感知原理与版本说明。",
                ["Exit with confirmation. Completed results stay saved."] = "确认后退出，已完成的成绩会保留。",
                ["READY TO EXPLORE"] = "可以开始探索",
                ["LOCKED"] = "未解锁",
                ["CHOOSE YOUR LABORATORY"] = "选择实验室",
                ["Progress is saved on completion. Finish a laboratory to unlock the next."] = "完成章节后自动保存进度，并解锁下一间实验室。",
                ["UNLOCKED  /  NOT YET EXPLORED"] = "已解锁  /  尚未探索",
                ["LOCKED  /  COMPLETE PREVIOUS CHAPTER"] = "未解锁  /  请先完成上一章",
                ["BACK"] = "返回",
                ["RESEARCH SUMMARY"] = "研究总结",
                ["BACK TO CHAPTERS"] = "返回章节选择",
                ["PLAY LABORATORY"] = "进入实验室",
                ["Enter this unlocked chapter."] = "进入这个已解锁的章节。",
                ["CALIBRATE YOUR EXPERIENCE"] = "调整体验设置",
                ["Comfort first. Head tracking stays active while paused."] = "舒适优先。VR 仅在配置并启用 XR 后可用；暂停时仍保留头部追踪。",
                ["MASTER"] = "总音量",
                ["MUSIC"] = "音乐",
                ["SFX"] = "音效",
                ["MOVE SPEED   0.5 – 3 M/S"] = "移动速度   0.5 – 3 米/秒",
                ["LIGHT BRIGHTNESS   50 – 150%"] = "灯光亮度   50 – 150%",
                ["MOVEMENT   /   STEPPED"] = "VR 移动   /   步进",
                ["TURN   /   SNAP"] = "VR 转向   /   分段",
                ["SNAP ANGLE   /   30°"] = "VR 分段转向角度   /   30°",
                ["QUALITY   /   MEDIUM"] = "画质   /   默认",
                ["No artificial head rotation or time-scale pause.\nVignette is not implemented in this build."] = "桌面 WASD 连续移动，Q/E 连续转向。\nVR 模式设置不影响桌面。此版本没有暗角舒适遮罩。",
                ["DEFAULTS"] = "恢复默认",
                ["RESET PROGRESS"] = "重置进度",
                ["PERCEPTION IS AN EXPERIMENT."] = "感知，本身就是实验。",
                ["DEPTHLAB VR  /  ESCAPE THE DISTORTED SPACE\n\nBuilt in Unity 6000.6 with URP, uGUI / TextMeshPro and XR Interaction Toolkit.\nOriginal geometry, procedural sound and laboratory presentation.\nBundled Noto Sans font: see the existing font license in the project.\n\nFour perception studies:\nBinocular disparity • Motion parallax • Ponzo & Müller-Lyer • Reflection & materials\n\nDesktop research build. XR hardware validation remains required.\nNo OpenXR loader or XR Management is currently installed."] =
                    "DEPTHLAB  /  逃离扭曲空间\n\n使用 Unity 6000.6、URP、uGUI / TextMeshPro 与 XR Interaction Toolkit。\n原创几何场景、程序生成音效与实验室展示。\n内置 Noto Sans 字体遵循项目随附的 SIL 开源字体许可。\n\n四项感知研究：\n双眼视差 · 运动视差 · 庞佐与缪勒－莱尔错觉 · 反射与材质\n\n当前为桌面研究预览版，尚未安装 OpenXR 或 XR Management。\nVR 需另行配置 XR 加载器并进行真实设备验证。",
                ["BACK TO LAB"] = "返回实验室",
                ["EXPERIMENT PAUSED"] = "实验已暂停",
                ["Timer, puzzle interaction and locomotion are stopped.\nHead tracking and UI remain active."] = "计时、谜题交互与移动已暂停。\n界面仍可操作；启用 XR 后，头部追踪仍保持。",
                ["RESUME"] = "继续",
                ["RETRY CHAPTER"] = "重试本章",
                ["MAIN MENU"] = "主菜单",
                ["CHAPTER COMPLETE"] = "章节完成",
                ["REPLAY"] = "重新挑战",
                ["NEXT CHAPTER"] = "下一章",
                ["DEPTH EXPLORER"] = "深度探索者",
                ["PLAY AGAIN"] = "重新开始",
                ["PLEASE CONFIRM"] = "请确认",
                ["CANCEL"] = "取消",
                ["CONFIRM"] = "确认",
                ["This action cannot be undone."] = "此操作无法撤销。",
                ["LEARN TO TRUST YOUR ACTIONS"] = "通过行动，了解感知",
                ["CALIBRATION 01 / 07"] = "操作练习 01 / 07",
                ["PRACTICE OBJECT\nAT YOUR LEFT\n\nGrab the cyan object.\nRelease at the lit target.\n\nScroll while dragging\nto change depth."] = "练习物体在左侧\n\n抓取青色物体，\n在亮起的目标处松开。\n\n桌面：左键按住抓取，\n滚轮调整远近。\nVR：启用 XR 后用握持键。",
                ["SKIP TUTORIAL"] = "跳过教程",
                ["HINT"] = "提示",
                ["NEXT"] = "下一步",
                ["Perform the requested action first."] = "请先完成要求的动作。",
                ["LAB 01"] = "实验室 01",
                ["PAUSE"] = "暂停",
                ["01   /   BINOCULAR LAB"] = "01   /   双眼视差实验室",
                ["THREE SHAPES.\nTHREE DEPTHS."] = "三种形状\n三处深度",
                ["01  GRAB\n02  ALIGN IN THREE DIMENSIONS\n03  RELEASE\n\nMatch both shape and color.\nA near miss returns the piece safely.\n\nCUBE  /  SPHERE  /  TRIANGULAR PRISM"] = ShapeTechnique,
                ["BLUE CUBE"] = "蓝色立方体",
                ["VIOLET SPHERE"] = "紫色球体",
                ["CYAN TRIANGULAR PRISM"] = "青色三棱柱",
                ["02   /   MOTION PARALLAX"] = "02   /   运动视差",
                ["MOVE YOUR HEAD.\nREVEAL THE PATH."] = "移动视点\n发现路径",
                ["Inspect sideways: 0.00 / 0.22 m"] = "横向观察：0.00 / 0.22 米",
                ["Near foliage shifts more than distant foliage.\n\nVR: gently lean sideways.\nDesktop: inspect from both sides below.\n\nSelect cyan landing pads after discovery.\nMovement between pads is an instant teleport."] = "近处枝叶移动得比远处更明显。\n桌面：依次点击下方左右观察按钮，\n每次等待视角停稳。\nVR（启用 XR 后）：原地轻轻侧倾。\n发现路径后，依次选择青色平台传送。",
                ["LOOK LEFT"] = "向左观察",
                ["LOOK RIGHT"] = "向右观察",
                ["?  /  UNVERIFIED\nLAND HERE"] = "?  /  未验证\n传送至此",
                ["NEAR / 2.6 m"] = "近景 / 2.6 米",
                ["MID / 5.2 m"] = "中景 / 5.2 米",
                ["FAR / 7.9 m"] = "远景 / 7.9 米",
                ["03   /   PERCEPTUAL INFERENCE"] = "03   /   视觉感知推断",
                ["MEASURE REALITY.\nQUESTION CONTEXT."] = "测量真实\n质疑背景",
                ["Are the two cubes equal in actual size?"] = "两个立方体的实际大小相同，还是不同？",
                ["EQUAL SIZE"] = "大小相同",
                ["DIFFERENT"] = "不同",
                ["EQUAL LENGTH"] = "长度相同",
                ["CONTINUE  →"] = "继续  →",
                ["COMPARE THE OBJECTS — NOT THEIR SURROUNDINGS"] = "比较物体本身，而不是周围背景",
                ["04   /   OPTICAL MATERIALS"] = "04   /   光学材质",
                ["CONTROL LIGHT.\nCLOSE THE CIRCUIT."] = "控制光线\n点亮接收器",
                ["LIGHT 0°  /  MIRROR −15°"] = "光源 0°  /  镜面 −15°",
                ["DIRECTIONAL LIGHT / AZIMUTH"] = "定向光源 / 方位角",
                ["MIRROR −5°"] = "镜面 −5°",
                ["MIRROR +5°"] = "镜面 +5°",
                ["GRAB MIRROR OR USE FINE ROTATION"] = "桌面：按钮微调；VR 启用后可抓取转镜",
                ["RECEIVER"] = "接收器",
                ["POLISHED METAL\nM 1.00 / R 0.04"] = "抛光金属\n金属度 1.00 / 粗糙度 0.04",
                ["ROUGH METAL\nM 1.00 / R 0.84"] = "粗糙金属\n金属度 1.00 / 粗糙度 0.84",
                ["MATTE CERAMIC\nM 0.00 / R 0.76"] = "哑光陶瓷\n金属度 0.00 / 粗糙度 0.76",
                ["SOLVE THE EXPERIMENT\nTHEN USE THIS EXIT"] = "先完成实验\n再使用此出口",
                ["USE EXIT  →"] = "使用出口  →"
            };
            string[] oldTitles = { "Room of Disparity", "Parallax Forest", "Hallway of Illusions", "Labyrinth of Light" };
            string[] oldConcepts = { "BINOCULAR DISPARITY", "MOTION PARALLAX", "SIZE & LENGTH ILLUSIONS", "LIGHT & MATERIALS" };
            string[] oldTimes = { "3–5 MIN", "3–5 MIN", "2–4 MIN", "3–6 MIN" };
            string[] oldTranslations = { "시차의 방  /  立体视差之室", "패럴랙스 숲  /  运动视差森林",
                "착시의 회랑  /  视觉错觉回廊", "빛의 미로  /  光影迷宫" };
            string[] oldDescriptions = {
                "Two eyes. One depth map.\nGrab three shapes and match the slots at three different depths.",
                "Move your viewpoint, not your assumptions.\nReveal a hidden path through a layered alien forest.",
                "Context changes perception.\nCompare equal objects and lines, then explain the illusion.",
                "Follow the light.\nCompare metal and rough surfaces; steer a reflected beam into its receiver."
            };
            for (int i = 0; i < 4; i++)
            {
                string number = (i + 1).ToString("00", CultureInfo.InvariantCulture);
                map[oldTitles[i]] = DepthLabProgress.Titles[i];
                map[oldTranslations[i]] = DepthLabProgress.Translations[i];
                map[oldDescriptions[i]] = DepthLabProgress.Descriptions[i];
                map[$"{number}   {oldConcepts[i]}"] = $"{number}   {DepthLabProgress.Concepts[i]}";
                map[$"{number}  /  {oldConcepts[i]}   •   {oldTimes[i]}"] =
                    $"{number}  /  {DepthLabProgress.Concepts[i]}   •   {DepthLabProgress.Times[i]}";
                map[$"{number}  /  {oldTitles[i]}"] = $"{number}  /  {DepthLabProgress.Titles[i]}";
            }
            return map;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text == null) return;
            Undo.RecordObject(text, "Chinese chapter instructions");
            text.text = value;
            EditorUtility.SetDirty(text);
            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        }

        private static void Fit(TMP_Text text, float minimum, float maximum)
        {
            if (text == null) return;
            Undo.RecordObject(text, "Fit Chinese instructions in existing cell");
            text.enableAutoSizing = true;
            text.fontSize = text.fontSizeMax = maximum;
            text.fontSizeMin = minimum;
            EditorUtility.SetDirty(text);
            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        }

        private static TMP_FontAsset RequireFont(string path)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) throw new InvalidOperationException("Existing font asset missing: " + path);
            if (font.atlasPopulationMode != AtlasPopulationMode.Static)
                throw new InvalidOperationException("Expected the existing static font: " + path);
            return font;
        }

        private static void ExtendFont(TMP_FontAsset font, Font source, uint[] characters)
        {
            uint[] missing = characters.Where(c => !font.HasCharacter((int)c)).ToArray();
            if (missing.Length == 0) return;
            bool multiAtlas = font.isMultiAtlasTexturesEnabled;
            try
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                var serialized = new SerializedObject(font);
                SerializedProperty file = serialized.FindProperty("m_SourceFontFile");
                SerializedProperty guid = serialized.FindProperty("m_SourceFontFileGUID");
                if (file == null || guid == null)
                    throw new InvalidOperationException("TMP source-font serialization has changed.");
                file.objectReferenceValue = source;
                guid.stringValue = AssetDatabase.AssetPathToGUID(SourcePath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (font.sourceFontFile != source)
                    throw new InvalidOperationException("Could not bind bundled font source.");
                font.isMultiAtlasTexturesEnabled = true;
                if (!font.TryAddCharacters(missing, out uint[] unrendered) && unrendered != null && unrendered.Length != 0)
                    throw new InvalidOperationException("Noto SC cannot bake required glyphs: " + Describe(unrendered));
            }
            finally
            {
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                font.isMultiAtlasTexturesEnabled = multiAtlas;
                foreach (Texture2D atlas in font.atlasTextures)
                {
                    if (atlas == null) continue;
                    if (!EditorUtility.IsPersistent(atlas))
                    {
                        atlas.name = font.name + " Atlas " + Array.IndexOf(font.atlasTextures, atlas);
                        AssetDatabase.AddObjectToAsset(atlas, font);
                    }
                    EditorUtility.SetDirty(atlas);
                }
                EditorUtility.SetDirty(font.material);
                EditorUtility.SetDirty(font);
            }
        }

        private static IEnumerable<uint> CodePoints(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                uint point = (uint)char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i])) i++;
                yield return point;
            }
        }

        private static bool IsPrintable(uint point) =>
            point >= 32 && point != 127 && point != 0xFEFF && point != 0x200B;

        private static string Describe(IEnumerable<uint> points) =>
            string.Join(" ", points.Select(c => "U+" + c.ToString("X4", CultureInfo.InvariantCulture)));

        private static string BackupSavedFiles()
        {
            string folder = "Backups/DepthLabChineseLocalization/" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(folder);
            foreach (string path in new[] { ScenePath, LatinPath, CjkPath })
            {
                File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
                if (File.Exists(path + ".meta"))
                    File.Copy(path + ".meta", Path.Combine(folder, Path.GetFileName(path) + ".meta"));
            }
            return folder;
        }
    }
}
