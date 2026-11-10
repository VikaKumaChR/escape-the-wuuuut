using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DepthLab.Editor
{
    public static class DepthLabValidation
    {
        [MenuItem("DEPTHLAB/Validate Authored Scene")]
        public static string Audit()
        {
            DepthLabApp app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null) throw new InvalidOperationException("DEPTHLAB scene is not loaded.");
            var problems = new List<string>();
            foreach (Transform t in app.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    problems.Add("Missing script: " + t.name);
            foreach (var behaviour in app.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !behaviour.GetType().Namespace.StartsWith("DepthLab")) continue;
                var so = new SerializedObject(behaviour);
                var p = so.GetIterator();
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference &&
                        p.objectReferenceValue == null && p.objectReferenceEntityIdValue != default)
                        problems.Add("Broken reference: " + behaviour.name + "." + p.propertyPath);
            }
            if (UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length != 1)
                problems.Add("Expected one active EventSystem");
            if (UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>().Length != 1)
                problems.Add("Expected one active XRInteractionManager");
            foreach (var chapter in app.chapters)
                if (chapter == null || chapter.spawnPoint == null) problems.Add("Missing chapter or spawn point");
            foreach (var button in app.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.onClick.GetPersistentEventCount() == 0) problems.Add("Unwired button: " + button.name);
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentTarget(i) == null)
                        problems.Add("Missing callback target: " + button.name);
            }
            var missing = new HashSet<char>();
            foreach (TMP_Text label in app.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.font == null) { problems.Add("Missing font: " + label.name); continue; }
                foreach (char c in label.text)
                    if (!char.IsControl(c) && !label.font.HasCharacter(c, true, false)) missing.Add(c);
            }
            if (missing.Count > 0) problems.Add("Missing font characters: " + new string(missing.ToArray()));
            string result = $"Scene {app.gameObject.scene.path}\nObjects {app.GetComponentsInChildren<Transform>(true).Length}\nChapters {app.chapters.Length}\nButtons {app.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length}\n" +
                (problems.Count == 0 ? "PASS: no missing scripts, references, glyphs or callbacks; one XR manager and EventSystem." : string.Join("\n", problems));
            return result;
        }

        [MenuItem("DEPTHLAB/Refine Authored Typography")]
        public static void RefineTypography()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit mode only.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            foreach (TMP_Text t in app.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!(t is TextMeshProUGUI)) continue;
                Vector2 size = t.rectTransform.sizeDelta;
                size.y = Mathf.Max(size.y, t.fontSize * 1.5f);
                if (t.text == "DEPTH\nLAB") { t.fontSize = 85; size.y = 260; }
                if (t.text.StartsWith("Four laboratories.")) size.y = 180;
                t.rectTransform.sizeDelta = size;
                EditorUtility.SetDirty(t);
            }
            var primary = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/DepthLab/Generated/DepthLab Latin SDF.asset");
            primary.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            primary.TryAddCharacters("ü−", out string missing);
            primary.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(primary);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            EditorSceneManager.SaveScene(app.gameObject.scene);
        }

        [MenuItem("DEPTHLAB/Apply Interaction Refinements")]
        public static void RefineInteractions()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit mode only.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            app.input.controllerVisuals = app.playerRoot.GetComponentsInChildren<Renderer>(true);
            app.input.controllerLines = app.playerRoot.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>(true);
            app.menuCanvas.GetComponent<Canvas>().sortingOrder = 20;
            var backing = app.menuCanvas.Find("Glass backing").GetComponent<UnityEngine.UI.Image>();
            Color color = backing.color;
            color.a = 1;
            backing.color = color;
            app.hudText.fontSize = 23;
            app.hudText.rectTransform.sizeDelta = new Vector2(1150, 112);
            app.hudText.rectTransform.anchoredPosition = new Vector2(24, -12);
            app.hintText.rectTransform.sizeDelta = new Vector2(1150, 57);
            app.hintText.rectTransform.anchoredPosition = new Vector2(24, -140);
            foreach (var command in app.finalPage.GetComponentsInChildren<DepthLabCommand>(true))
                if (command.name == "PLAY AGAIN") command.action = LabAction.PlayAgain;
            foreach (LabChapter chapter in app.chapters) DepthLabChapterBuilder.EnsureExitDoor(chapter);
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            EditorSceneManager.SaveScene(app.gameObject.scene);
        }

        public static string Capture(string name)
        {
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            Camera camera = app.headCamera;
            RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
            var target = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            string path = "Backups/DepthLab-20261109/" + name + ".png";
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
            return path;
        }
    }
}
