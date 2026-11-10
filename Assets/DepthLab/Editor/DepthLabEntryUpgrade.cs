using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace DepthLab.Editor
{
    public static class DepthLabEntryUpgrade
    {
        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before changing entry controls.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null) throw new InvalidOperationException("Open the DEPTHLAB scene.");
            DepthLabCommand start = app.homePage.GetComponentsInChildren<DepthLabCommand>(true)
                .First(c => c.action == LabAction.Start);
            var original = start.GetComponent<UnityEngine.UI.Button>();
            TMP_Text title = original.GetComponentInChildren<TMP_Text>(true);
            title.text = "从第一关开始";
            if (original is DepthLabButton originalButton)
                originalButton.tooltip = "从第一项实验开始，保留已保存的成绩。";
            Transform existing = app.homePage.transform.Find("ContinueSavedProgress");
            GameObject continuation = existing != null ? existing.gameObject :
                UnityEngine.Object.Instantiate(original.gameObject, original.transform.parent);
            continuation.name = "ContinueSavedProgress";
            var command = continuation.GetComponent<DepthLabCommand>();
            command.action = LabAction.Continue;
            var button = continuation.GetComponent<UnityEngine.UI.Button>();
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, command.Execute);
            button.GetComponentInChildren<TMP_Text>(true).text = "继续已保存的进度";
            if (button is DepthLabButton styled)
                styled.tooltip = "进入下一项尚未完成的实验，不会重置成绩。";
            RectTransform source = (RectTransform)original.transform;
            RectTransform rect = (RectTransform)continuation.transform;
            bool upgraded = app.menuCanvas.GetComponent<DepthLabCyberpunkView>() != null;
            if (upgraded)
            {
                source.sizeDelta = new Vector2(600, 85);
                rect.anchoredPosition = new Vector2(94, -638);
                rect.sizeDelta = new Vector2(600, 48);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(550, -890);
                rect.sizeDelta = new Vector2(460, 55);
            }
            foreach (DepthLabChapter chapter in app.chapters)
                if (chapter is ReflectionChapter reflection)
                {
                    reflection.objectiveLabel.text = reflection.Objective;
                    reflection.hintLabel.text = reflection.Hint;
                    Transform console = chapter.transform.Find("Chapter instrument console");
                    TMP_Text description = console.Find("Chapter title").GetComponent<TMP_Text>();
                    description.text = "把反射光\n照到接收球上";
                    TMP_Text guide = console.Find("Mirror control label").GetComponent<TMP_Text>();
                    guide.text = "先调光源，再用下方按钮转动镜面";
                }
            EditorUtility.SetDirty(app);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
        }
    }
}
