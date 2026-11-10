using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DepthLab.Editor
{
    public static class DepthLabExitUpgrade
    {
        [MenuItem("DEPTHLAB/Connect Automatic Chapter Exits")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before wiring chapter exits.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null) throw new InvalidOperationException("Open the DEPTHLAB scene.");
            foreach (DepthLabChapter chapter in app.chapters)
            {
                var lab = chapter as LabChapter;
                if (lab == null || lab.exitDoor == null || lab.exitButton == null)
                    throw new InvalidOperationException("An authored chapter exit is missing.");
                var door = lab.exitDoor;
                Transform panel = door.transform.Find("Exit use panel");
                if (panel == null || panel.Find("Exit label") == null || panel.Find("Exit condition") == null)
                    throw new InvalidOperationException("The authored exit labels are missing.");
                Undo.RegisterFullObjectHierarchyUndo(door.gameObject, "Connect automatic chapter exit");
                door.chapter = lab;
                door.statusLabel = panel.Find("Exit label").GetComponent<TMP_Text>();
                door.conditionLabel = panel.Find("Exit condition").GetComponent<TMP_Text>();
                lab.exitButton.gameObject.SetActive(false);
                panel.localPosition = new Vector3(-1.65f, 1.65f, -.15f);
                panel.localScale = Vector3.one * .0014f;
                var rect = (RectTransform)panel;
                rect.sizeDelta = new Vector2(680, 330);
                rect.pivot = Vector2.one * .5f;
                Position(door.statusLabel, 24, 35, 632, 70, 38);
                Position(door.conditionLabel, 24, 135, 632, 150, 30);
                Transform background = panel.Find("ExitGlass");
                if (background == null)
                {
                    var go = new GameObject("ExitGlass", typeof(RectTransform), typeof(DepthLabHolographicGraphic));
                    go.transform.SetParent(panel, false);
                    background = go.transform;
                }
                var glassRect = (RectTransform)background;
                glassRect.anchorMin = Vector2.zero;
                glassRect.anchorMax = Vector2.one;
                glassRect.offsetMin = glassRect.offsetMax = Vector2.zero;
                background.SetAsFirstSibling();
                var glass = background.GetComponent<DepthLabHolographicGraphic>();
                glass.illustration = DepthLabHolographicGraphic.Illustration.Panel;
                glass.color = new Color(0, .898f, 1);
                glass.raycastTarget = false;
                panel.GetComponent<Canvas>().worldCamera = app.headCamera;
                door.CloseImmediate();
                EditorUtility.SetDirty(door);
            }
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
        }

        private static void Position(TMP_Text label, float x, float y, float width, float height, float fontSize)
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
        }
    }
}
