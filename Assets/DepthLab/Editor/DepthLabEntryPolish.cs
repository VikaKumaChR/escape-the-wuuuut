using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DepthLab.Editor
{
    public static class DepthLabEntryPolish
    {
        private static readonly Color Cyan = new Color(0, .898f, 1);
        private static readonly Color Text = new Color(.918f, .965f, 1);
        private static readonly Color[] Accents = {
            Cyan, new Color(.18f, .55f, 1), new Color(.659f, .333f, .969f), new Color(.9f, .45f, .83f)
        };

        [MenuItem("DEPTHLAB/Polish Entry Holographic Walls And Icons")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before applying entry visuals.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null || app.menuSpace == null || app.menuCanvas == null)
                throw new InvalidOperationException("Open the authored DEPTHLAB scene.");
            Shader shader = Shader.Find("DepthLab/Holographic Wall");
            if (shader == null) throw new InvalidOperationException("Holographic wall shader has not imported.");
            string[] names = { "Far wall", "Arrival left wall", "Arrival right wall", "Arrival front wall" };
            var walls = names.Select(n => app.menuSpace.transform.Find(n)).ToArray();
            if (walls.Any(w => w == null || w.GetComponent<MeshRenderer>() == null))
                throw new InvalidOperationException("The four original arrival walls are required.");
            var backing = app.menuCanvas.Find("Glass backing");
            if (backing == null)
                throw new InvalidOperationException("The existing menu glass backing is required.");
            Undo.RegisterFullObjectHierarchyUndo(app.gameObject, "Polish entry visuals");
            const string materialPath = "Assets/DepthLab/Generated/Entry Holographic Curtain.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", new Color(.0314f, .0745f, .1608f));
            material.SetColor("_CyanColor", Cyan);
            material.SetColor("_BlueColor", new Color(.0863f, .4667f, 1));
            material.SetColor("_AccentColor", Accents[2]);
            material.SetFloat("_Intensity", .85f);
            material.SetFloat("_FlowSpeed", .22f);
            foreach (Transform wall in walls)
            {
                wall.GetComponent<MeshRenderer>().sharedMaterial = material;
                EditorUtility.SetDirty(wall.GetComponent<MeshRenderer>());
            }
            EditorUtility.SetDirty(material);

            // Coplanar UI illustrations cannot drift away from text when viewed from the side.
            app.showChapterPreviews = false;
            foreach (Transform preview in app.previews)
                if (preview != null) preview.gameObject.SetActive(false);
            AddCardIcons(app.homePage, true);
            AddCardIcons(app.chapterPage, false);
            app.detailIllustration = Graphic(app.detailPage.transform, "ChapterIllustration",
                DepthLabHolographicGraphic.Illustration.Disparity, Cyan);
            Place(app.detailIllustration.rectTransform, 1280, 420, 200, 200);
            backing.GetComponent<UnityEngine.UI.Image>().enabled = false;
            var glass = Graphic(app.menuCanvas, "EntryHolographicGlass",
                DepthLabHolographicGraphic.Illustration.Panel, Cyan);
            Stretch(glass.rectTransform);
            glass.transform.SetAsFirstSibling();
            glass.glassColor = new Color(.018f, .046f, .09f, .96f);
            glass.SetVerticesDirty();
            foreach (var button in app.menuCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                StyleButton(button);
            foreach (var label in app.menuCanvas.GetComponentsInChildren<TMP_Text>(true))
                label.raycastTarget = false;
            foreach (var submesh in app.menuCanvas.GetComponentsInChildren<TMP_SubMeshUI>(true))
                submesh.raycastTarget = false;
            var continuation = app.homePage.transform.Find("ContinueSavedProgress");
            if (continuation != null)
            {
                Place((RectTransform)continuation, 550, 858, 460, 55);
                var label = continuation.GetComponentInChildren<TMP_Text>(true);
                Place(label.rectTransform, 26, 0, 408, 55);
                label.alignment = TextAlignmentOptions.MidlineLeft;
            }
            foreach (Canvas canvas in app.GetComponentsInChildren<Canvas>(true))
                canvas.worldCamera = app.headCamera;
            EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        private static void AddCardIcons(GameObject page, bool home)
        {
            foreach (var command in page.GetComponentsInChildren<DepthLabCommand>(true))
            {
                if (command.action != LabAction.Detail) continue;
                int index = command.argument;
                var card = (RectTransform)command.transform;
                var icon = Graphic(card, "ChapterIllustration",
                    (DepthLabHolographicGraphic.Illustration)(index + 1), Accents[index]);
                float size = home ? 72 : 116;
                Place(icon.rectTransform, home ? 16 : card.rect.width - size - 24,
                    home ? 37 : 55, size, size);
                if (home)
                {
                    foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>(true))
                    {
                        Vector2 pos = label.rectTransform.anchoredPosition;
                        Place(label.rectTransform, 104, -pos.y, card.rect.width - 120, label.rectTransform.sizeDelta.y);
                        label.alignment = TextAlignmentOptions.MidlineLeft;
                    }
                }
                else
                {
                    foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>(true))
                    {
                        float y = -label.rectTransform.anchoredPosition.y;
                        if (y < 50 || y >= 170) continue;
                        var r = label.rectTransform;
                        r.sizeDelta = new Vector2(card.rect.width - size - 72, r.sizeDelta.y);
                    }
                }
            }
        }

        private static void StyleButton(UnityEngine.UI.Button button)
        {
            var command = button.GetComponent<DepthLabCommand>();
            bool primary = command != null && (command.action == LabAction.Start ||
                command.action == LabAction.PlaySelected);
            Color normal = primary ? new Color(.035f, .30f, .46f, .96f) : new Color(.025f, .09f, .15f, .84f);
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(.045f, .38f, .49f, .98f);
            colors.selectedColor = new Color(.055f, .28f, .38f, .98f);
            colors.pressedColor = new Color(.20f, .12f, .36f, 1);
            colors.disabledColor = new Color(.04f, .065f, .10f, .7f);
            button.colors = colors;
            if (button.targetGraphic != null) button.targetGraphic.color = Color.white;
            if (command == null || command.action != LabAction.Detail)
                foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
                    label.color = Text;
            if (button is DepthLabButton styled) styled.accentColor = Cyan;
            EditorUtility.SetDirty(button);
        }

        private static DepthLabHolographicGraphic Graphic(Transform parent, string name,
            DepthLabHolographicGraphic.Illustration illustration, Color color)
        {
            Transform existing = parent.Find(name);
            var go = existing != null ? existing.gameObject :
                new GameObject(name, typeof(RectTransform), typeof(DepthLabHolographicGraphic));
            if (existing == null) go.transform.SetParent(parent, false);
            var graphic = go.GetComponent<DepthLabHolographicGraphic>();
            graphic.illustration = illustration;
            graphic.color = color;
            graphic.raycastTarget = false;
            graphic.SetVerticesDirty();
            return graphic;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * .5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }
    }
}
