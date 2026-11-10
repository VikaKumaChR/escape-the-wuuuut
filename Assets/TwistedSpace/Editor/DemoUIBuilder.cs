using System;
using System.IO;
using TwistedSpace.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Creates editable World Space UGUI assets, not a screen overlay or runtime-generated UI.
    /// The glass is a small reusable noise sprite, not a camera blur or post-processing effect.
    /// </summary>
    public static class DemoUIBuilder
    {
        private const string GeneratedPath = "Assets/TwistedSpace/Demo/UI";
        private static readonly Color TextColor = new Color32(234, 243, 250, 255);
        private static readonly Color SecondaryColor = new Color32(176, 192, 206, 255);
        private static readonly Color Cyan = new Color32(134, 217, 240, 255);
        private static readonly Color Orange = new Color32(243, 165, 93, 255);

        /// <summary>References used by the scene composer to persist all event wiring.</summary>
        public sealed class UIObjects
        {
            public VRWorldSpacePanel MainMenu, Hint, Reset, Result;
            public Text StatusText, HintText, ResultText;
            public Slider Alignment, StableHold;
            public ConfirmedXRButton Start, NextHint, RequestReset, Menu, CancelReset, ConfirmReset, Replay, ResultMenu;
        }

        public static UIObjects Build(Transform parent, Camera head)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TwistedSpace/Demo/Fonts/NotoSansSC-Medium.otf");
            if (font == null)
                throw new InvalidOperationException("The bundled NotoSansSC-Medium.otf font must be imported before creating the demo.");
            Sprite glass = CreateTextureAsset("GlassSurface", false);
            Sprite halo = CreateTextureAsset("SoftHalo", true);
            UIObjects ui = new UIObjects();
            ui.MainMenu = Panel("Main Menu", parent, head, new Vector2(820, 610), glass, true, true);
            RectTransform main = (RectTransform)ui.MainMenu.transform;
            Label(main, font, "观测终端  /  01", 26, 42, 30, 736, 40, Cyan);
            Label(main, font, "逃离扭曲空间", 54, 42, 98, 736, 80, TextColor, FontStyle.Bold);
            Label(main, font, "你的视角，是这个房间唯一可靠的坐标。", 28, 42, 196, 736, 70, SecondaryColor);
            ui.Start = Button(main, font, glass, halo, "开始探索", 42, 306, 736, 84);
            Label(main, font, "先按 Y 选择右手，用鼠标瞄准按钮。\n左键按住 0.15 秒后松开，确认选择。", 25, 42, 423, 736, 80, SecondaryColor);
            Label(main, font, "试玩关卡：视差观测室", 25, 42, 532, 736, 40, Cyan);

            ui.Hint = Panel("Hint and Progress", parent, head, new Vector2(680, 650), glass, false, false);
            ui.Hint.transform.SetPositionAndRotation(new Vector3(1.05f, 1.45f, 1.65f),
                Quaternion.Euler(0, 32, 0));
            ui.Hint.transform.localScale = Vector3.one * 0.001f;
            RectTransform hint = (RectTransform)ui.Hint.transform;
            Label(hint, font, "观察记录", 34, 34, 27, 612, 52, TextColor, FontStyle.Bold);
            ui.StatusText = Label(hint, font, "请进入观测区", 27, 34, 93, 612, 86, Cyan);
            ui.HintText = Label(hint, font, "握住框体并移动；回到地面标记处观察。", 28, 34, 196, 612, 95, SecondaryColor);
            Label(hint, font, "对齐进度", 24, 34, 310, 612, 36, SecondaryColor);
            ui.Alignment = ProgressBar(hint, "Alignment", 34, 356, 612, 16, Cyan);
            Label(hint, font, "稳定确认", 24, 34, 394, 612, 36, SecondaryColor);
            ui.StableHold = ProgressBar(hint, "Stable Hold", 34, 438, 612, 10, Cyan);
            ui.NextHint = Button(hint, font, glass, halo, "下一条提示", 34, 486, 294, 64);
            ui.RequestReset = Button(hint, font, glass, halo, "重置谜题", 352, 486, 294, 64, true);
            ui.Menu = Button(hint, font, glass, halo, "返回主菜单", 34, 574, 612, 48);

            ui.Reset = Panel("Reset Confirmation", parent, head, new Vector2(820, 460), glass, false, true);
            RectTransform reset = (RectTransform)ui.Reset.transform;
            Label(reset, font, "空间回退", 26, 42, 30, 736, 40, Orange);
            Label(reset, font, "重置当前谜题？", 44, 42, 103, 736, 68, TextColor, FontStyle.Bold);
            Label(reset, font, "框体将恢复初始位置。\n如果仍握着物体，请松手后等待复位。", 29, 42, 199, 736, 100, SecondaryColor);
            ui.CancelReset = Button(reset, font, glass, halo, "继续解谜", 42, 336, 352, 80);
            ui.ConfirmReset = Button(reset, font, glass, halo, "确认重置", 426, 336, 352, 80, true);

            ui.Result = Panel("Chamber Complete", parent, head, new Vector2(820, 540), glass, false, true);
            RectTransform result = (RectTransform)ui.Result.transform;
            Label(result, font, "观测完成  /  01", 26, 42, 30, 736, 40, Cyan);
            Label(result, font, "空间已稳定", 50, 42, 107, 736, 78, TextColor, FontStyle.Bold);
            ui.ResultText = Label(result, font, "出口已开启。", 30, 42, 216, 736, 130, SecondaryColor);
            ui.Replay = Button(result, font, glass, halo, "重新挑战", 42, 400, 352, 84);
            ui.ResultMenu = Button(result, font, glass, halo, "返回主菜单", 426, 400, 352, 84);

            // A fixed diegetic instruction plaque stays readable without adding another input module.
            GameObject plaqueObject = new GameObject("Simulator Quick Reference", typeof(RectTransform), typeof(Canvas));
            plaqueObject.transform.SetParent(parent, false);
            Canvas plaqueCanvas = plaqueObject.GetComponent<Canvas>();
            plaqueCanvas.renderMode = RenderMode.WorldSpace;
            plaqueCanvas.worldCamera = head;
            RectTransform plaque = (RectTransform)plaqueObject.transform;
            plaque.sizeDelta = new Vector2(690, 540);
            plaque.SetPositionAndRotation(new Vector3(-1.25f, 1.5f, 1.8f), Quaternion.Euler(0, -35, 0));
            plaque.localScale = Vector3.one * 0.001f;
            Surface(plaque, glass, new Color32(20, 35, 48, 251));
            Label(plaque, font, "模拟器操作", 34, 32, 30, 626, 55, TextColor, FontStyle.Bold);
            Label(plaque, font, "Tab  切换控制的设备\n按住右键  调整头部视角\nR  切换鼠标平移 / 旋转\nW A S D  平移所选设备\nQ / E  向下 / 向上\nG  握柄抓取 · 左键  扳机确认", 27,
                32, 117, 626, 290, SecondaryColor);
            Label(plaque, font, "先点击 Game 窗口，再开始操作。\n详细绑定见 DEMO_GUIDE.txt。", 24, 32, 436, 626, 74, Cyan);
            return ui;
        }

        private static VRWorldSpacePanel Panel(string name, Transform parent, Camera head,
            Vector2 size, Sprite glass, bool visible, bool follow)
        {
            GameObject node = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            node.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)node.transform;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * 0.0012f;
            rect.position = new Vector3(0, 1.6f, 1.2f);
            Canvas canvas = node.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            VRWorldSpacePanel panel = node.AddComponent<VRWorldSpacePanel>();
            Configure(panel, "headCamera", head);
            Configure(panel, "followHead", follow);
            Configure(panel, "startVisible", visible);
            Configure(panel, "distance", 1.2f);
            Configure(panel, "verticalOffset", 0f);
            Surface(rect, glass, new Color32(20, 35, 48, 251));
            return panel;
        }

        private static void Surface(RectTransform parent, Sprite glass, Color color)
        {
            Image image = Rect("Glass Surface", parent, 0, 0, parent.sizeDelta.x, parent.sizeDelta.y).gameObject.AddComponent<Image>();
            image.sprite = glass;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            float w = parent.sizeDelta.x;
            float h = parent.sizeDelta.y;
            Stroke(parent, 0, 0, w, 2, new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f));
            Stroke(parent, 0, h-2, w, 2, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            Stroke(parent, 0, 0, 2, h, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            Stroke(parent, w-2, 0, 2, h, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            Stroke(parent, 0, 0, 80, 4, Cyan);
            Stroke(parent, 0, 0, 4, 24, Cyan);
            Stroke(parent, w-80, h-4, 80, 4, Cyan);
        }

        private static ConfirmedXRButton Button(RectTransform parent, Font font, Sprite glass,
            Sprite halo, string title, float x, float y, float width, float height, bool danger = false)
        {
            RectTransform root = Rect(title, parent, x, y, width, height);
            Image glow = Rect("Hover Halo", root, -12, -12, width+24, height+24).gameObject.AddComponent<Image>();
            glow.sprite = halo;
            glow.type = Image.Type.Sliced;
            glow.color = Color.clear;
            glow.raycastTarget = false;
            Image face = Rect("Face", root, 0, 0, width, height).gameObject.AddComponent<Image>();
            face.sprite = glass;
            face.type = Image.Type.Sliced;
            face.color = new Color32(29, 50, 65, 255);
            Color accent = danger ? Orange : Cyan;
            Stroke(root, 0, 0, width, 2, accent);
            Stroke(root, 0, height-2, width, 2, new Color(accent.r, accent.g, accent.b, 0.5f));
            Text label = Label(root, font, title, 28, 12, 0, width-24, height, TextColor);
            label.alignment = TextAnchor.MiddleCenter;
            ConfirmedXRButton button = root.gameObject.AddComponent<ConfirmedXRButton>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Configure(button, "faceImage", face);
            Configure(button, "haloImage", glow);
            Configure(button, "hoverHaloColor", new Color(accent.r, accent.g, accent.b, 0.42f));
            return button;
        }

        private static Slider ProgressBar(RectTransform parent, string name, float x, float y,
            float width, float height, Color color)
        {
            RectTransform root = Rect(name, parent, x, y, width, height);
            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color32(48, 69, 84, 255);
            background.raycastTarget = false;
            RectTransform fill = Rect("Fill", root, 0, 0, width, height);
            // Slider drives anchors; zero offsets prevent a full-width fill at value zero.
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0.5f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = Vector2.zero;
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = color;
            fillImage.raycastTarget = false;
            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        private static Text Label(RectTransform parent, Font font, string value, int size,
            float x, float y, float width, float height, Color color, FontStyle style = FontStyle.Normal)
        {
            Text label = Rect("Label", parent, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Rect(string name, RectTransform parent, float x, float y, float w, float h)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static void Stroke(RectTransform parent, float x, float y, float w, float h, Color color)
        {
            Image image = Rect("Edge", parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>Fail explicitly if a refactor changes a field; never silently create an unwired scene.</summary>
        public static void Configure(UnityEngine.Object target, string field, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new InvalidOperationException(target.GetType().Name + " has no serialized field " + field);
            if (value is UnityEngine.Object reference) property.objectReferenceValue = reference;
            else if (value is bool boolean) property.boolValue = boolean;
            else if (value is float number) property.floatValue = number;
            else if (value is Color color) property.colorValue = color;
            else if (value is UnityEngine.Object[] references)
            {
                property.arraySize = references.Length;
                for (int i = 0; i < references.Length; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = references[i];
            }
            else throw new ArgumentException("Unsupported serialized value for " + field);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Small original textures keep the glass and hover soft without any screen sampling.</summary>
        private static Sprite CreateTextureAsset(string name, bool halo)
        {
            string path = GeneratedPath + "/" + name + ".png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(GeneratedPath);
                const int size = 128;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                System.Random random = new System.Random(1842);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, size-1-x), Mathf.Min(y, size-1-y));
                    float alpha = halo ? Mathf.SmoothStep(0f, 0.75f, Mathf.Clamp01(edge/24f)) : 1f;
                    float grain = halo ? 1f : 0.96f + (float)random.NextDouble()*0.04f;
                    texture.SetPixel(x, y, new Color(grain, grain, grain, alpha));
                }
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteBorder = Vector4.one * (halo ? 32f : 8f);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new InvalidOperationException("Could not import UI sprite: " + path);
            return sprite;
        }
    }
}
