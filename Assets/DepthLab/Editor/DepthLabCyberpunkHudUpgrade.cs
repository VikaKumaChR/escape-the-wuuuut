using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabCyberpunkHudUpgrade
    {
        private static readonly Color Ink = Hex("050816");
        private static readonly Color Panel = Hex("081329");
        private static readonly Color Blue = Hex("1677FF");
        private static readonly Color Cyan = Hex("00E5FF");
        private static readonly Color Purple = Hex("A855F7");
        private static readonly Color Error = Hex("FF35B8");
        private static readonly Color Text = Hex("EAF6FF");
        private static readonly Color Muted = Hex("83A9C9");
        private static readonly Vector2 FrameSize = new Vector2(1600f, 650f);

        // Run after the overall presentation upgrade; the caller owns scene/prefab saving.
        [MenuItem("DEPTHLAB/Upgrade Cyberpunk Worldspace HUD")]
        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before authoring the HUD.");
            Scene scene = SceneManager.GetActiveScene();
            DepthLabApp app = FindApp(scene);
            if (app.hud == null || app.hudText == null || app.hintText == null ||
                app.headCamera == null || app.playerRoot == null ||
                !(app.hud.transform is RectTransform) || app.hud.GetComponent<Canvas>() == null ||
                app.hudText.font == null)
                throw new InvalidOperationException("Expected the installed HUD Canvas, TMP font and app references.");

            UnityEngine.UI.Button[] buttons = app.hud.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            bool hasHint = false, hasPause = false;
            foreach (UnityEngine.UI.Button button in buttons)
            {
                DepthLabCommand command = button.GetComponent<DepthLabCommand>();
                if (command == null) continue;
                hasHint |= command.action == LabAction.Hint;
                hasPause |= command.action == LabAction.Pause;
            }
            if (!hasHint || !hasPause || app.hud.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                throw new InvalidOperationException("Retain the installed Hint/Pause commands and tracked raycaster.");
            int eventSystems = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                eventSystems += root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length;
            if (eventSystems != 1)
                throw new InvalidOperationException("Expected exactly one existing EventSystem.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade DEPTHLAB worldspace HUD");
            Undo.RegisterFullObjectHierarchyUndo(app.hud, "Preserve authored HUD references and callbacks");

            RectTransform frame = Node(app.hud.transform, "CyberpunkHudFrame");
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition3D = Vector3.zero;
            frame.sizeDelta = FrameSize;
            frame.localScale = Vector3.one;
            frame.localRotation = Quaternion.identity;

            // Suppress old visuals, not the objects/labels referenced by the app or other upgrades.
            foreach (UnityEngine.UI.Image image in app.hud.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (!image.transform.IsChildOf(frame) && image.GetComponentInParent<UnityEngine.UI.Button>() == null)
                {
                    image.enabled = false;
                    image.raycastTarget = false;
                }
            foreach (TMP_Text label in app.hud.GetComponentsInChildren<TMP_Text>(true))
                if (!label.transform.IsChildOf(frame) && label != app.hintText &&
                    label.GetComponentInParent<UnityEngine.UI.Button>() == null)
                    label.enabled = false;

            DepthLabHudPresentation presentation = app.hud.GetComponent<DepthLabHudPresentation>();
            if (presentation == null) presentation = Undo.AddComponent<DepthLabHudPresentation>(app.hud);
            presentation.app = app;
            presentation.panel = frame;
            presentation.viewingDistance = 1.85f;
            presentation.repositionDistance = 1.25f;
            presentation.worldScale = .001f;
            presentation.correctColor = Cyan;
            presentation.errorColor = Error;
            presentation.hintColor = Purple;
            presentation.idleBorderColor = Alpha(Blue, .35f);

            // Only the peripheral readouts have backing; the gameplay center has no Graphic.
            Image(frame, "ObjectiveBacking", 0, 0, 790, 232, Alpha(Panel, .88f));
            Image(frame, "StatsBacking", 1150, 0, 450, 174, Alpha(Ink, .88f));
            Image(frame, "ControlsBacking", 0, 558, 1030, 92, Alpha(Panel, .82f));
            Image(frame, "ObjectiveAccent", 0, 18, 3, 196, Cyan);
            Image(frame, "StatsAccent", 1597, 18, 3, 138, Blue);
            presentation.hintBacking = Image(frame, "HintBacking", 0, 316, 1030, 182, Alpha(Ink, .85f));
            presentation.hintBacking.enabled = false;
            presentation.experimentLabel = Label(frame, "ExperimentLabel", app.hudText.font,
                24, 12, 742, 46, 32, Text, "实验 · 等待进入章节");
            presentation.objectiveLabel = Label(frame, "ObjectiveLabel", app.hudText.font,
                24, 64, 742, 158, 28, Text, "把三个实体放进同色同形线框");
            presentation.timerLabel = Label(frame, "TimerLabel", app.hudText.font,
                1174, 16, 402, 40, 30, Text, "用时 · 等待开始");
            presentation.mistakesLabel = Label(frame, "MistakesLabel", app.hudText.font,
                1174, 62, 190, 40, 28, Muted, "错误 · 待记录");
            presentation.progressLabel = Label(frame, "ProgressLabel", app.hudText.font,
                1174, 110, 402, 40, 28, Cyan, "进度 · 等待开始");
            UnityEngine.UI.Image track = Image(frame, "ProgressTrack", 1174, 158, 402, 3, Alpha(Blue, .25f));
            UnityEngine.UI.Image fill = Image(track.transform, "ProgressFill", 0, 0, 1, 3, Cyan);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = Vector2.zero;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            presentation.progressFill = fillRect;
            presentation.stepLabel = Label(frame, "StepLabel", app.hudText.font,
                24, 510, 1000, 38, 26, Cyan, "抓取实体 → 对齐线框 → 松开归位");
            presentation.feedbackLabel = Label(frame, "FeedbackLabel", app.hudText.font,
                1174, 190, 402, 70, 25, Alpha(Cyan, 0f), "");
            presentation.controlsLabel = Label(frame, "ControlsLabel", app.hudText.font,
                24, 570, 982, 68, 26, Muted,
                "左键按住抓取 / 松开释放 · 滚轮远近\n右键观察 · WASD 移动 · Esc 暂停");

            Undo.SetTransformParent(app.hintText.transform, frame, "Retain app hint output");
            Place(app.hintText.rectTransform, 24, 322, 982, 170);
            Style(app.hintText, app.hudText.font, 26, Purple);
            app.hintText.enabled = false;
            app.hudText.enabled = false;

            var borders = new List<UnityEngine.UI.Image>();
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? 0f : 1544f;
                borders.Add(Image(frame, $"TopCorner{side}", x, 0, 56, 2, presentation.idleBorderColor));
                borders.Add(Image(frame, $"BottomCorner{side}", x, 648, 56, 2, presentation.idleBorderColor));
                borders.Add(Image(frame, $"SideTop{side}", side == 0 ? 0f : 1598f,
                    0, 2, 36, presentation.idleBorderColor));
                borders.Add(Image(frame, $"SideBottom{side}", side == 0 ? 0f : 1598f,
                    614, 2, 36, presentation.idleBorderColor));
            }
            presentation.feedbackBorders = borders.ToArray();
            string[] detailedNames = {
                "ObjectiveBacking", "StatsBacking", "ObjectiveAccent", "StatsAccent",
                "ExperimentLabel", "ObjectiveLabel", "TimerLabel", "MistakesLabel",
                "ProgressLabel", "ProgressTrack", "FeedbackLabel"
            };
            presentation.detailedElements = new GameObject[detailedNames.Length];
            for (int i = 0; i < detailedNames.Length; i++)
                presentation.detailedElements[i] = frame.Find(detailedNames[i]).gameObject;
            presentation.compactGuidance = true;
            presentation.ApplyGuidanceLayout();

            int other = 0;
            foreach (UnityEngine.UI.Button button in buttons)
            {
                DepthLabCommand command = button.GetComponent<DepthLabCommand>();
                float x, y;
                string title;
                Color normal;
                if (command != null && command.action == LabAction.Hint)
                { x = 1060; y = 578; title = "提示"; normal = Purple; }
                else if (command != null && command.action == LabAction.Pause)
                { x = 1240; y = 578; title = "暂停"; normal = Blue; }
                else
                {
                    x = 1420;
                    y = 578 - other++ * 78;
                    title = command != null && command.action == LabAction.Settings ? "设置" : null;
                    normal = Panel;
                }
                StyleButton(button, frame, app.hudText.font, x, y, title, normal);
            }
            frame.SetAsLastSibling();
            Canvas canvas = app.hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.headCamera;
            canvas.sortingOrder = 5;
            UnityEngine.UI.GraphicRaycaster mouse = app.hud.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (mouse == null) mouse = Undo.AddComponent<UnityEngine.UI.GraphicRaycaster>(app.hud);
            mouse.enabled = true;
            app.hud.GetComponent<TrackedDeviceGraphicRaycaster>().enabled = true;
            UnityEngine.UI.CanvasScaler scaler = app.hud.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = Undo.AddComponent<UnityEngine.UI.CanvasScaler>(app.hud);
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 1f;
            RectTransform hud = (RectTransform)app.hud.transform;
            hud.sizeDelta = FrameSize;
            hud.localScale = Vector3.one * presentation.worldScale;
            Vector3 forward = Vector3.ProjectOnPlane(app.headCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 pose = app.headCamera.transform.position + forward * presentation.viewingDistance;
            pose.y = Mathf.Min(app.headCamera.transform.position.y - .16f, app.playerRoot.position.y + 1.6f);
            hud.SetPositionAndRotation(pose, Quaternion.LookRotation(forward, Vector3.up));
            EditorUtility.SetDirty(presentation);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            Debug.Log("DEPTHLAB worldspace HUD authored. App text and original button commands retained; " +
                "caller should save the scene after the overall upgrade and this Apply().");
        }

        private static DepthLabApp FindApp(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the installed DEPTHLAB scene.");
            DepthLabApp app = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (DepthLabApp candidate in root.GetComponentsInChildren<DepthLabApp>(true))
                {
                    if (app != null) throw new InvalidOperationException("Expected exactly one DEPTHLAB app.");
                    app = candidate;
                }
            if (app == null) throw new InvalidOperationException("Expected the installed DEPTHLAB app.");
            return app;
        }

        private static RectTransform Node(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (!(existing is RectTransform rect))
                    throw new InvalidOperationException("Expected RectTransform on " + name);
                return rect;
            }
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Add HUD element");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition3D = new Vector3(x, -y, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static UnityEngine.UI.Image Image(Transform parent, string name,
            float x, float y, float width, float height, Color color)
        {
            RectTransform rect = Node(parent, name);
            Place(rect, x, y, width, height);
            UnityEngine.UI.Image image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
            image.enabled = true;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font,
            float x, float y, float width, float height, float size, Color color, string value)
        {
            RectTransform rect = Node(parent, name);
            Place(rect, x, y, width, height);
            TMP_Text label = rect.GetComponent<TMP_Text>();
            if (label == null) label = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
            Style(label, font, size, color);
            label.text = value;
            return label;
        }

        private static void Style(TMP_Text label, TMP_FontAsset font, float size, Color color)
        {
            label.enabled = true;
            label.font = font;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.margin = Vector4.zero;
            label.raycastTarget = false;
        }

        private static void StyleButton(UnityEngine.UI.Button button, RectTransform frame,
            TMP_FontAsset font, float x, float y, string title, Color normal)
        {
            Undo.SetTransformParent(button.transform, frame, "Retain HUD button callback");
            Place((RectTransform)button.transform, x, y, 156, 60);
            UnityEngine.UI.Graphic target = button.targetGraphic;
            if (target == null)
            {
                target = Image(button.transform, "CyberpunkButtonFace", 0, 0, 156, 60, Color.white);
                button.targetGraphic = target;
            }
            foreach (UnityEngine.UI.Image image in button.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                bool halo = button is DepthLabButton labButton && labButton.halo == image;
                if (image.transform != button.transform)
                    Place(image.rectTransform, halo ? -3 : 0, halo ? -3 : 0, halo ? 162 : 156, halo ? 66 : 60);
                image.enabled = true;
                image.color = halo ? Color.clear : Color.white;
                image.raycastTarget = image == target;
            }
            target.enabled = true;
            target.color = Color.white;
            target.raycastTarget = true;
            UnityEngine.UI.ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Blue;
            colors.selectedColor = Blue;
            colors.pressedColor = Purple;
            colors.disabledColor = Alpha(Panel, .45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .18f;
            button.colors = colors;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                label = Label(button.transform, "CyberpunkButtonLabel", font, 6, 0, 144, 60, 28, Text, title ?? "操作");
            else
            {
                Place(label.rectTransform, 6, 0, 144, 60);
                Style(label, font, 28, Text);
                if (title != null) label.text = title;
            }
            label.alignment = TextAlignmentOptions.Center;
        }

        private static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
