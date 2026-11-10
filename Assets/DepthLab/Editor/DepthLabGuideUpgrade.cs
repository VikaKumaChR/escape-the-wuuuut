using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabGuideUpgrade
    {
        private const string RootName = "CH1GuideAssistant";
        private const string FontPath = "Assets/DepthLab/Generated/DepthLab Latin SDF.asset";
        public const string PrefabPath = "Assets/DepthLab/Nova/Prefabs/CH1GuideAssistant.prefab";
        private static readonly Color Cyan = new Color(.22f, .94f, 1f);
        private static readonly Color Paper = new Color(.92f, .98f, 1f);

        [MenuItem("DEPTHLAB/Upgrade CH1 Robot Guide")]
        public static void Apply()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Finish compilation and leave Play Mode before authoring the guide.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the installed DepthLab scene first.");
            GameObject[] roots = scene.GetRootGameObjects();
            DepthLabApp[] apps = roots.SelectMany(r => r.GetComponentsInChildren<DepthLabApp>(true)).ToArray();
            if (apps.Length != 1)
                throw new InvalidOperationException("Expected exactly one installed DepthLabApp.");
            DepthLabApp app = apps[0];
            if (app.chapters == null || app.chapters.Length == 0 ||
                !(app.chapters[0] is LabChapter chapter) || chapter.chapterIndex != 0 ||
                chapter.gameObject.scene != scene || chapter.spawnPoint == null ||
                app.headCamera == null || app.headCamera.gameObject.scene != scene || app.settings == null)
                throw new InvalidOperationException("Guide requires CH1, its spawn point, scene camera and settings.");
            var systems = roots.SelectMany(r =>
                r.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true)).ToArray();
            if (systems.Length != 1 || app.uiModule == null ||
                app.uiModule.GetComponent<UnityEngine.EventSystems.EventSystem>() != systems[0])
                throw new InvalidOperationException("Keep the existing single EventSystem and its XR UI input module.");
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null || font.material == null)
                throw new InvalidOperationException("Missing installed guide font: " + FontPath);

            var existing = roots.SelectMany(r => r.GetComponentsInChildren<VRGuideAssistant>(true))
                .Where(g => g.chapter == chapter || g.transform.parent == chapter.transform).ToArray();
            if (existing.Length > 1 || (existing.Length == 1 &&
                (existing[0].transform.parent != chapter.transform || existing[0].name != RootName)))
                throw new InvalidOperationException("Resolve duplicate or misplaced VRGuideAssistant instances before applying.");
            Transform root = chapter.transform.Find(RootName);
            if (root != null && root.GetComponent<VRGuideAssistant>() == null)
                throw new InvalidOperationException(RootName + " already exists but is not an owned guide.");
            if (root != null && PrefabUtility.IsPartOfPrefabInstance(root) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) != PrefabPath)
                throw new InvalidOperationException("The existing guide belongs to another prefab.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade CH1 robot guide");
            if (root != null) Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Update CH1 guide");
            root = Node(chapter.transform, RootName);
            root.localPosition = new Vector3(-2f, 1.3f, .3f);
            Vector3 toSpawn = chapter.spawnPoint.position - root.position;
            toSpawn.y = 0f;
            if (toSpawn.sqrMagnitude > .001f) root.rotation = Quaternion.LookRotation(-toSpawn);
            VRGuideAssistant guide = Component<VRGuideAssistant>(root);
            Transform model = Node(root, "Robot");
            DepthLabNova nova = DepthLabNovaBuilder.BuildGuideModel(model, font);
            model.localPosition = new Vector3(0, -.5f, 0);
            model.localScale = Vector3.one * .5f;
            guide.nova = nova;
            BuildSpeech(guide, root, font);
            nova.dialogueCanvas = guide.speechCanvas;
            nova.subtitle = guide.speechText;
            nova.subtitlePanel = guide.speechText.transform.parent.gameObject;
            nova.talkButton = (DepthLabButton)guide.talkButton;
            nova.hintButton = (DepthLabButton)guide.hintButton;
            nova.enabled = guide.enabled = true;
            ValidateOwned(guide, font);

            EnsureFolder("Assets/DepthLab/Nova/Prefabs");
            Vector3 position = root.localPosition;
            Quaternion rotation = root.localRotation;
            try
            {
                SetSceneReferences(guide, null, null);
                root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, PrefabPath,
                    InteractionMode.AutomatedAction, out bool success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Could not save CH1 guide prefab.");
                VRGuideAssistant saved = prefab.GetComponent<VRGuideAssistant>();
                ValidateOwned(saved, font);
                if (saved.app != null || saved.chapter != null || saved.nova.app != null ||
                    saved.nova.settings != null || saved.speechCanvas.worldCamera != null)
                    throw new InvalidOperationException("Guide prefab contains scene-only references.");
            }
            finally
            {
                root.SetLocalPositionAndRotation(position, rotation);
                guide = root.GetComponent<VRGuideAssistant>();
                SetSceneReferences(guide, app, chapter);
                // Persist all authored overrides when Apply is invoked on an existing instance.
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    EditorUtility.SetDirty(child.gameObject);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                    foreach (Component component in child.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        EditorUtility.SetDirty(component);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
            }
            Debug.Log("第一章诺娃已就位：左侧悬浮机器人、中文气泡、真实事件反馈与评分提示。请由主流程补全字体并保存场景。", guide);
        }

        private static void BuildSpeech(VRGuideAssistant guide, Transform root, TMP_FontAsset font)
        {
            RectTransform rect = Rect(root, "SpeechCanvas", Vector2.zero, new Vector2(620, 360));
            rect.localPosition = new Vector3(0, .86f, -.06f);
            rect.localScale = Vector3.one * .0016f;
            Canvas canvas = Component<Canvas>(rect);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = true;
            Component<UnityEngine.UI.CanvasScaler>(rect).dynamicPixelsPerUnit = 1f;
            Component<UnityEngine.UI.GraphicRaycaster>(rect).blockingObjects =
                UnityEngine.UI.GraphicRaycaster.BlockingObjects.ThreeD;
            var xr = Component<TrackedDeviceGraphicRaycaster>(rect);
            xr.checkFor3DOcclusion = true;
            xr.checkFor2DOcclusion = false;
            guide.speechCanvas = canvas;
            RectTransform panel = Rect(rect, "SpeechPanel", Vector2.zero, new Vector2(620, 360));
            var glass = Component<DepthLabHolographicGraphic>(panel);
            glass.illustration = DepthLabHolographicGraphic.Illustration.Panel;
            glass.color = Cyan;
            glass.glassColor = new Color(.012f, .035f, .065f, .97f);
            glass.raycastTarget = false;
            Text(panel, "Identity", "诺娃 · 研究助手", new Vector2(0, 145),
                new Vector2(564, 36), 25, Cyan, font);
            guide.speechText = Text(panel, "Speech", VRGuideAssistant.WelcomeText, new Vector2(0, 28),
                new Vector2(564, 188), 27, Paper, font);
            guide.speechText.alignment = TextAlignmentOptions.TopLeft;
            guide.speechText.overflowMode = TextOverflowModes.Page;
            guide.speechText.pageToDisplay = 1;
            Text(panel, "ReadingNote", "长提示自动翻页 · 提示计入评分", new Vector2(0, -89),
                new Vector2(564, 28), 20, new Color(.65f, .81f, .87f), font);
            guide.talkButton = Button(panel, "TalkButton", "交流", new Vector2(-146, -137),
                font, guide.Talk, Cyan);
            guide.hintButton = Button(panel, "HintButton", "提示（计入评分）", new Vector2(146, -137),
                font, guide.Hint, new Color(1f, .77f, .35f));
            var navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnRight = guide.hintButton
            };
            guide.talkButton.navigation = navigation;
            navigation.selectOnRight = null;
            navigation.selectOnLeft = guide.talkButton;
            guide.hintButton.navigation = navigation;
        }

        private static DepthLabButton Button(Transform parent, string name, string label,
            Vector2 position, TMP_FontAsset font, UnityAction callback, Color accent)
        {
            RectTransform rect = Rect(parent, name, position, new Vector2(274, 58));
            var face = Component<UnityEngine.UI.Image>(rect);
            face.color = new Color(.055f, .15f, .20f);
            face.raycastTarget = true;
            var halo = Component<UnityEngine.UI.Image>(
                Rect(rect, "Accent", new Vector2(-132, 0), new Vector2(4, 36)));
            halo.color = accent;
            halo.raycastTarget = false;
            Text(rect, "Label", label, Vector2.zero, new Vector2(252, 50), 24, Paper, font);
            DepthLabButton button = Component<DepthLabButton>(rect);
            button.targetGraphic = face;
            button.halo = halo;
            button.accentColor = accent;
            button.tooltip = label;
            button.interactable = true;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.6f, 1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.35f, .65f, .8f);
            colors.disabledColor = new Color(.4f, .4f, .4f, .7f);
            button.colors = colors;
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, callback);
            button.onClick.SetPersistentListenerState(0, UnityEventCallState.RuntimeOnly);
            return button;
        }

        private static TMP_Text Text(Transform parent, string name, string value,
            Vector2 position, Vector2 size, float fontSize, Color tint, TMP_FontAsset font)
        {
            var text = Component<TextMeshProUGUI>(Rect(parent, name, position, size));
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.text = value;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.color = tint;
            text.raycastTarget = false;
            return text;
        }

        private static void SetSceneReferences(VRGuideAssistant guide, DepthLabApp app, LabChapter chapter)
        {
            guide.app = app;
            guide.chapter = chapter;
            guide.nova.app = app;
            guide.nova.settings = app != null ? app.settings : null;
            guide.speechCanvas.worldCamera = app != null ? app.headCamera : null;
            EditorUtility.SetDirty(guide);
            EditorUtility.SetDirty(guide.nova);
            EditorUtility.SetDirty(guide.speechCanvas);
        }

        private static void ValidateOwned(VRGuideAssistant guide, TMP_FontAsset font)
        {
            if (guide == null || guide.nova == null || !guide.nova.externallyDriven ||
                guide.speechCanvas == null || guide.speechText == null ||
                guide.speechText.font != font || guide.nova.subtitle != guide.speechText ||
                guide.speechCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null ||
                guide.speechCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                throw new InvalidOperationException("Guide visual or speech references are incomplete.");
            if (!guide.nova.ValidateExternalConfiguration(out string error))
                throw new InvalidOperationException(error);
            ValidateButton(guide.talkButton, guide, nameof(VRGuideAssistant.Talk));
            ValidateButton(guide.hintButton, guide, nameof(VRGuideAssistant.Hint));
            foreach (MeshFilter mesh in guide.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh == null || !EditorUtility.IsPersistent(mesh.sharedMesh))
                    throw new InvalidOperationException("Guide mesh was not persisted: " + mesh.name);
            foreach (MeshRenderer renderer in guide.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial == null || !EditorUtility.IsPersistent(renderer.sharedMaterial))
                    throw new InvalidOperationException("Guide material was not persisted: " + renderer.name);
        }

        private static void ValidateButton(UnityEngine.UI.Button button, VRGuideAssistant guide, string method)
        {
            if (button == null || button.onClick.GetPersistentEventCount() != 1 ||
                button.onClick.GetPersistentTarget(0) != guide ||
                button.onClick.GetPersistentMethodName(0) != method ||
                button.targetGraphic == null || !button.targetGraphic.raycastTarget)
                throw new InvalidOperationException("Guide callback must be persistently wired exactly once: " + method);
        }

        private static Transform Node(Transform parent, string name)
        {
            Transform node = parent.Find(name);
            if (node == null)
            {
                node = new GameObject(name).transform;
                node.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(node.gameObject, "Create guide node");
            }
            node.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            node.localScale = Vector3.one;
            node.gameObject.SetActive(true);
            return node;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform found = parent.Find(name);
            if (found != null && !(found is RectTransform))
                throw new InvalidOperationException("Guide UI requires RectTransform: " + name);
            RectTransform rect = found as RectTransform;
            if (rect == null)
            {
                rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                rect.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create guide UI");
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition3D = new Vector3(position.x, position.y, 0);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.gameObject.SetActive(true);
            return rect;
        }

        private static T Component<T>(Transform transform) where T : Component
        {
            T component = transform.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(transform.gameObject);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
