using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabUsabilityRepair
    {
        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before repairing scene interaction.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null || app.uiModule == null)
                throw new InvalidOperationException("The DEPTHLAB app and existing XR input module are required.");
            BindInput(app.uiModule);
            app.showChapterPreviews = false;
            foreach (Transform preview in app.previews)
                preview.gameObject.SetActive(false);
            foreach (Canvas canvas in app.GetComponentsInChildren<Canvas>(true))
            {
                canvas.worldCamera = app.headCamera;
                var mouse = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (mouse != null)
                {
                    mouse.enabled = true;
                    mouse.blockingObjects = UnityEngine.UI.GraphicRaycaster.BlockingObjects.None;
                }
                EditorUtility.SetDirty(canvas);
            }
            foreach (UnityEngine.UI.Graphic graphic in app.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (graphic is TMP_Text || graphic is TMP_SubMeshUI)
                    graphic.raycastTarget = false;
            RepairSliders(app);
            foreach (DepthLabChapter chapter in app.chapters)
            {
                var lab = chapter as LabChapter;
                if (lab == null || lab.exitDoor == null) continue;
                Transform panel = lab.exitDoor.transform.Find("Exit use panel");
                if (panel == null) throw new InvalidOperationException("Missing authored exit sign.");
                lab.exitDoor.statusLabel = panel.Find("Exit label").GetComponent<TMP_Text>();
                lab.exitDoor.conditionLabel = panel.Find("Exit condition").GetComponent<TMP_Text>();
                lab.exitButton.gameObject.SetActive(false);
                panel.localPosition = new Vector3(-1.65f, 1.65f, -.12f);
                panel.localScale = Vector3.one * .0014f;
                ((RectTransform)panel).sizeDelta = new Vector2(680, 330);
                Place(lab.exitDoor.statusLabel.rectTransform, 20, 25, 640, 80);
                Place(lab.exitDoor.conditionLabel.rectTransform, 20, 140, 640, 150);
                lab.exitDoor.statusLabel.fontSize = 40;
                lab.exitDoor.conditionLabel.fontSize = 31;
                lab.exitDoor.statusLabel.enableAutoSizing = lab.exitDoor.conditionLabel.enableAutoSizing = false;
                lab.exitDoor.statusLabel.overflowMode = lab.exitDoor.conditionLabel.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(lab.exitDoor);
            }
            RectTransform menu = (RectTransform)app.menuCanvas;
            if (app.menuCanvas.GetComponent<DepthLabCyberpunkView>() == null)
                menu.localScale = Vector3.one * .0012f;
            app.detailBody.enableAutoSizing = false;
            app.detailBody.fontSize = 25;
            app.detailBody.overflowMode = TextOverflowModes.Overflow;
            EditorUtility.SetDirty(app);
            EditorUtility.SetDirty(app.uiModule);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        private static void BindInput(XRUIInputModule module)
        {
            const string path = "Assets/DepthLab/Generated/DepthLabUI.inputactions";
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InputActionAsset>();
                var map = asset.AddActionMap("DepthLabUI");
                map.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", expectedControlLayout: "Vector2");
                map.AddAction("LeftClick", InputActionType.PassThrough, "<Mouse>/leftButton", expectedControlLayout: "Button");
                map.AddAction("RightClick", InputActionType.PassThrough, "<Mouse>/rightButton", expectedControlLayout: "Button");
                map.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton", expectedControlLayout: "Button");
                map.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");
                File.WriteAllText(path, asset.ToJson());
                UnityEngine.Object.DestroyImmediate(asset);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            }
            if (asset == null) throw new InvalidOperationException("UI action asset could not be imported.");
            module.pointAction = Reference(asset, "Point");
            module.leftClickAction = Reference(asset, "LeftClick");
            module.rightClickAction = Reference(asset, "RightClick");
            module.middleClickAction = Reference(asset, "MiddleClick");
            module.scrollWheelAction = Reference(asset, "Scroll");
            module.enableMouseInput = true;
            module.enableXRInput = true;
            module.enableBuiltinActionsAsFallback = true;
        }

        private static InputActionReference Reference(InputActionAsset asset, string action)
        {
            foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)))
                if (child is InputActionReference reference && reference.action == asset.FindAction(action, true))
                    return reference;
            throw new InvalidOperationException("Imported UI action reference missing: " + action);
        }

        public static void RepairSliders(DepthLabApp app)
        {
            var view = app.settingsView;
            if (view == null) throw new InvalidOperationException("Settings controls are missing.");
            UnityEngine.UI.Slider[] sliders = { view.master, view.music, view.sfx, view.speed, view.brightness };
            for (int i = 0; i < sliders.Length; i++)
            {
                var slider = sliders[i];
                Place((RectTransform)slider.transform, 60, 320 + i * 96, 650, 44);
                string titleName = slider.name.Replace(" slider", "");
                Transform title = app.settingsPage.transform.Cast<Transform>().FirstOrDefault(t => t.name == titleName);
                if (title == null) throw new InvalidOperationException("Settings slider title missing: " + slider.name);
                Place((RectTransform)title, 70, 278 + i * 96, 520, 36);
                RectTransform track = (RectTransform)slider.transform.Find("Track");
                RectTransform fill = slider.fillRect;
                RectTransform handle = slider.handleRect;
                track.anchorMin = new Vector2(0, .5f);
                track.anchorMax = new Vector2(1, .5f);
                track.pivot = new Vector2(.5f, .5f);
                track.anchoredPosition = Vector2.zero;
                track.sizeDelta = new Vector2(-22, 6);
                RectTransform fillArea = SliderArea(slider.transform, "FillArea");
                fillArea.anchorMin = new Vector2(0, .5f);
                fillArea.anchorMax = new Vector2(1, .5f);
                fillArea.offsetMin = new Vector2(11, -3);
                fillArea.offsetMax = new Vector2(-11, 3);
                fill.SetParent(fillArea, false);
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(slider.normalizedValue, 1);
                fill.pivot = new Vector2(.5f, .5f);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                RectTransform handleArea = SliderArea(slider.transform, "HandleArea");
                handleArea.anchorMin = Vector2.zero;
                handleArea.anchorMax = Vector2.one;
                handleArea.offsetMin = new Vector2(11, 0);
                handleArea.offsetMax = new Vector2(-11, 0);
                handle.SetParent(handleArea, false);
                handle.anchorMin = new Vector2(slider.normalizedValue, 0);
                handle.anchorMax = new Vector2(slider.normalizedValue, 1);
                handle.pivot = new Vector2(.5f, .5f);
                handle.offsetMin = new Vector2(-9, 0);
                handle.offsetMax = new Vector2(9, 0);
                slider.fillRect = null;
                slider.handleRect = null;
                slider.fillRect = fill;
                slider.handleRect = handle;
                track.GetComponent<UnityEngine.UI.Graphic>().raycastTarget = false;
                fill.GetComponent<UnityEngine.UI.Graphic>().raycastTarget = false;
                slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                var image = slider.GetComponent<UnityEngine.UI.Image>();
                if (image == null) image = slider.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.color = new Color(0, 0, 0, .01f);
                image.raycastTarget = true;
                slider.SetValueWithoutNotify(slider.value);
                EditorUtility.SetDirty(slider);
            }
            foreach (TMP_Text text in app.settingsPage.GetComponentsInChildren<TMP_Text>(true))
                if (text.text.StartsWith("桌面 WASD") || text.text.Contains("没有暗角"))
                    text.gameObject.SetActive(false);
        }

        private static RectTransform SliderArea(Transform parent, string name)
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
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
