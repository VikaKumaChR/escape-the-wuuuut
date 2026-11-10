using System;
using System.Collections.Generic;
using TwistedSpace.Demo;
using TwistedSpace.UI;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace TwistedSpace.Editor
{
    /// <summary>Rebuilds only the opening's authored presentation, preserving the existing Start event.</summary>
    public static class DemoOpeningMenuUpgrade
    {
        private const string Art = "Assets/TwistedSpace/Demo/Art/OpeningThreshold.png";
        private static readonly Color Ink = new Color32(9, 20, 29, 255);
        private static readonly Color Paper = new Color32(232, 241, 244, 255);
        private static readonly Color Quiet = new Color32(147, 174, 189, 255);
        private static readonly Color Accent = new Color32(165, 216, 226, 255);

        public static void Apply(GameObject root)
        {
            Transform ui = root.transform.Find("Parallax Chamber/World Space UI");
            if (ui == null)
                throw new InvalidOperationException("The saved demo UI is missing.");
            VRWorldSpacePanel main = ui.Find("Main Menu").GetComponent<VRWorldSpacePanel>();
            ConfirmedXRButton start = main.GetComponentInChildren<ConfirmedXRButton>(true);
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TwistedSpace/Demo/Fonts/NotoSansSC-Medium.otf");
            if (!start || !font)
                throw new InvalidOperationException("The opening needs its existing Start button and bundled Chinese font.");

            RectTransform canvasRect = (RectTransform)main.transform;
            canvasRect.sizeDelta = new Vector2(1600, 960);
            canvasRect.localScale = Vector3.one * 0.00125f;
            canvasRect.localPosition = new Vector3(0, 1.6f, 1.5f);
            canvasRect.localRotation = Quaternion.identity;
            DemoUIBuilder.Configure(main, "followHead", false);
            DemoUIBuilder.Configure(main, "distance", 1.5f);
            foreach (Transform child in new List<Transform>(Children(canvasRect)))
            {
                if (child == start.transform)
                    continue;
                if (child.name == "Label" || child.name == "Glass Surface" || child.name == "Edge" ||
                    child.name == "Panel Identity" || child.name == "Header Rule" || child.name == "Opening Layout")
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            RectTransform layout = Rect(canvasRect, "Opening Layout", 0, 0, 1600, 960);
            layout.SetAsFirstSibling();
            Image artwork = Rect(layout, "Architectural Key Art", 0, 0, 1600, 960).gameObject.AddComponent<Image>();
            artwork.sprite = ImportArt();
            artwork.raycastTarget = false;
            Label(layout, font, "T W I S T E D   S P A C E", 23, 96, 76, 660, 44, Quiet);
            Rule(layout, "Chapter Mark", 96, 136, 42, 3, new Color32(232, 165, 108, 255));
            Label(layout, font, "逃离\n扭曲空间", 102, 88, 168, 720, 310, Paper);
            Label(layout, font, "相信你的视角，\n不必相信眼前的空间。", 32, 96, 498, 660, 108, Quiet);
            Rule(layout, "Footer Rule", 96, 840, 1408, 1, new Color32(51, 74, 88, 255));
            Label(layout, font, "第一章   /   视差观测室", 25, 96, 873, 720, 44, Quiet);
            Text number = Label(layout, font, "01  /  PERCEPTION", 22, 1070, 873, 434, 44, Quiet);
            number.alignment = TextAnchor.UpperRight;
            Text inputHint = Label(layout, font, "鼠标点击进入 · 探索时启用 XR 模拟器", 24,
                96, 766, 710, 48, Quiet);
            inputHint.name = "Input Hint";

            RectTransform buttonRect = (RectTransform)start.transform;
            Position(buttonRect, 96, 630, 620, 104);
            foreach (Transform child in Children(buttonRect))
                if (child.name == "Edge" || child.name == "Action Icon")
                    child.gameObject.SetActive(false);
            Image face = (Image)start.targetGraphic;
            Position(face.rectTransform, 0, 0, 620, 104);
            face.sprite = null;
            face.type = Image.Type.Simple;
            face.color = Accent;
            Image halo = buttonRect.Find("Hover Halo").GetComponent<Image>();
            Position(halo.rectTransform, -9, -9, 638, 122);
            DemoUIBuilder.Configure(start, "hoverHaloColor", new Color(0.65f, 0.85f, 0.91f, 0.18f));
            foreach (Text label in start.GetComponentsInChildren<Text>(true))
            {
                Position(label.rectTransform, 32, 0, 510, 104);
                label.fontSize = 37;
                label.color = Ink;
                label.alignment = TextAnchor.MiddleLeft;
            }
            Transform oldArrow = buttonRect.Find("Opening Arrow");
            if (oldArrow)
                UnityEngine.Object.DestroyImmediate(oldArrow.gameObject);
            Text arrow = Label(buttonRect, font, "→", 44, 535, 0, 60, 104, Ink);
            arrow.name = "Opening Arrow";
            arrow.alignment = TextAnchor.MiddleCenter;

            // Mouse and tracked rays share one XR UI Input Module, not competing EventSystems.
            GraphicRaycaster mouseRaycaster = main.GetComponent<GraphicRaycaster>();
            if (!mouseRaycaster)
                mouseRaycaster = main.gameObject.AddComponent<GraphicRaycaster>();
            mouseRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            mouseRaycaster.ignoreReversedGraphics = true;
            BindPresentation(root, ui, main, inputHint);
        }

        private static void BindPresentation(GameObject root, Transform ui, VRWorldSpacePanel main, Text hint)
        {
            VerticalSliceFlow flow = root.GetComponentInChildren<VerticalSliceFlow>(true);
            XROrigin origin = root.GetComponentInChildren<XROrigin>(true);
            XRDeviceSimulator simulator = root.GetComponentInChildren<XRDeviceSimulator>(true);
            XRUIInputModule module = root.GetComponentInChildren<XRUIInputModule>(true);
            SimulatorMenuInput input = flow.GetComponent<SimulatorMenuInput>();
            if (!input)
                input = flow.gameObject.AddComponent<SimulatorMenuInput>();
            DemoUIBuilder.Configure(input, "flow", flow);
            DemoUIBuilder.Configure(input, "deviceSimulator", simulator);
            DemoUIBuilder.Configure(input, "uiInputModule", module);
            DemoUIBuilder.Configure(input, "mainMenuPanel", main);
            List<InputActionAsset> assets = new List<InputActionAsset>();
            foreach (InputActionAsset asset in new[] { simulator.deviceSimulatorActionAsset,
                simulator.controllerActionAsset, simulator.handActionAsset })
                if (asset && !assets.Contains(asset))
                    assets.Add(asset);
            DemoUIBuilder.Configure(input, "simulatorActionAssets", assets.ToArray());

            OpeningMenuPresentation presentation = flow.GetComponent<OpeningMenuPresentation>();
            if (!presentation)
                presentation = flow.gameObject.AddComponent<OpeningMenuPresentation>();
            DemoUIBuilder.Configure(presentation, "flow", flow);
            DemoUIBuilder.Configure(presentation, "headCamera", origin.Camera);
            DemoUIBuilder.Configure(presentation, "mainMenuPanel", main);
            DemoUIBuilder.Configure(presentation, "resetPanel", ui.Find("Reset Confirmation").GetComponent<VRWorldSpacePanel>());
            DemoUIBuilder.Configure(presentation, "resultPanel", ui.Find("Chamber Complete").GetComponent<VRWorldSpacePanel>());
            DemoUIBuilder.Configure(presentation, "menuInput", input);
            DemoUIBuilder.Configure(presentation, "inputHint", hint);
            List<Renderer> hidden = new List<Renderer>();
            hidden.AddRange(root.transform.Find("Parallax Chamber/Demo Environment").GetComponentsInChildren<Renderer>(true));
            foreach (Renderer item in origin.GetComponentsInChildren<Renderer>(true))
                if (!(item is LineRenderer))
                    hidden.Add(item);
            DemoUIBuilder.Configure(presentation, "explorationRenderers", hidden.ToArray());
            DemoUIBuilder.Configure(presentation, "controllerRays", origin.GetComponentsInChildren<LineRenderer>(true));
            DemoUIBuilder.Configure(presentation, "explorationCanvases",
                new UnityEngine.Object[] { ui.Find("Simulator Quick Reference").GetComponent<Canvas>() });
            foreach (VRWorldSpacePanel panel in ui.GetComponentsInChildren<VRWorldSpacePanel>(true))
                DemoUIBuilder.Configure(panel, "followHead", false);
        }

        private static Sprite ImportArt()
        {
            TextureImporter importer = AssetImporter.GetAtPath(Art) as TextureImporter;
            if (!importer)
                throw new InvalidOperationException("Import the authored opening artwork before applying the menu.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = Vector4.zero;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art);
        }

        private static IEnumerable<Transform> Children(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
                yield return parent.GetChild(i);
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Position(rect, x, y, width, height);
            return rect;
        }

        private static void Position(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Text Label(Transform parent, Font font, string text, int size,
            float x, float y, float width, float height, Color color)
        {
            Text label = Rect(parent, "Label", x, y, width, height).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static void Rule(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            Image line = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            line.color = color;
            line.raycastTarget = false;
        }
    }
}
