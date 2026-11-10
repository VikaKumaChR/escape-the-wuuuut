using System;
using System.IO;
using TwistedSpace.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Applies original authored PNG assets to the existing wired UI. No event or camera
    /// reference is replaced; buttons keep their hold confirmation and panel fades.
    /// </summary>
    public static class DemoPresentationUpgrade
    {
        private const string ArtDirectory = "Assets/TwistedSpace/Demo/Art/";
        private static readonly Color Cyan = new Color32(134, 217, 240, 255);
        private static readonly Color Amber = new Color32(243, 165, 93, 255);

        [MenuItem("Twisted Space/Upgrade Demo UI Art")]
        public static void ApplySavedPrefab()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Exit Play Mode before applying demo art.");
            GameObject contents = PrefabUtility.LoadPrefabContents(ParallaxDemoBuilder.PrefabPath);
            try
            {
                Apply(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, ParallaxDemoBuilder.PrefabPath) == null)
                    throw new IOException("Could not save the demo UI art.");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        public static void Apply(GameObject root)
        {
            PrepareTextures();
            Sprite panelSprite = Sprite("PanelGlass");
            Sprite buttonSprite = Sprite("ButtonFace");
            Sprite haloSprite = Sprite("ButtonHalo");
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                Transform background = canvas.transform.Find("Glass Surface");
                if (background != null)
                {
                    Image surface = background.GetComponent<Image>();
                    surface.sprite = panelSprite;
                    surface.type = Image.Type.Sliced;
                    surface.color = Color.white;
                }
                // The authored sprite supplies the border; remove only old decorative root strokes.
                for (int i = 0; i < canvas.transform.childCount; i++)
                {
                    Transform child = canvas.transform.GetChild(i);
                    if (child.name == "Edge")
                        child.gameObject.SetActive(false);
                }
                VRWorldSpacePanel panel = canvas.GetComponent<VRWorldSpacePanel>();
                if (panel == null)
                    continue;
                string icon = canvas.name.Contains("Reset") ? "IconReset" :
                    canvas.name.Contains("Complete") ? "IconComplete" :
                    canvas.name.Contains("Hint") ? "IconHint" : "IconObserve";
                AddIcon((RectTransform)canvas.transform, "Panel Identity", Sprite(icon),
                    ((RectTransform)canvas.transform).sizeDelta.x - 82, 27, 44,
                    icon == "IconReset" ? Amber : Cyan);
                AddRule((RectTransform)canvas.transform);
            }
            foreach (ConfirmedXRButton button in root.GetComponentsInChildren<ConfirmedXRButton>(true))
            {
                Image face = button.targetGraphic as Image;
                if (face == null)
                    throw new InvalidOperationException("Demo button requires a face Image: " + button.name);
                face.sprite = buttonSprite;
                face.color = new Color32(37, 61, 76, 255);
                Transform halo = button.transform.Find("Hover Halo");
                if (halo != null)
                {
                    Image haloImage = halo.GetComponent<Image>();
                    haloImage.sprite = haloSprite;
                    haloImage.type = Image.Type.Sliced;
                }
                bool danger = button.name.Contains("重置");
                string icon = danger ? "IconReset" : button.name.Contains("提示") ? "IconHint" :
                    button.name.Contains("菜单") ? "IconDoor" : "IconPlay";
                RectTransform rect = (RectTransform)button.transform;
                float size = rect.sizeDelta.y < 60 ? 25 : 31;
                AddIcon(rect, "Action Icon", Sprite(icon), 18, (rect.sizeDelta.y-size)*0.5f,
                    size, danger ? Amber : Cyan);
                foreach (Text label in button.GetComponentsInChildren<Text>(true))
                {
                    label.rectTransform.anchoredPosition = new Vector2(49, 0);
                    label.rectTransform.sizeDelta = new Vector2(rect.sizeDelta.x-65, rect.sizeDelta.y);
                    label.color = new Color32(234, 243, 250, 255);
                }
            }
        }

        /// <summary>Texture import stays small, tileable in 3D and uncompressed at UI edges.</summary>
        private static void PrepareTextures()
        {
            foreach (string path in Directory.GetFiles(ArtDirectory, "*.png"))
            {
                string normalized = path.Replace('\\', '/');
                TextureImporter importer = AssetImporter.GetAtPath(normalized) as TextureImporter;
                if (importer == null)
                    throw new InvalidOperationException("Import the original art textures first: " + normalized);
                string name = Path.GetFileNameWithoutExtension(normalized);
                if (name == "OpeningThreshold")
                    continue; // Its full-resolution import is owned by DemoOpeningMenuUpgrade.
                bool ui = name.StartsWith("Icon", StringComparison.Ordinal) ||
                    name == "PanelGlass" || name.StartsWith("Button", StringComparison.Ordinal);
                importer.textureType = ui ? TextureImporterType.Sprite : TextureImporterType.Default;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = !ui;
                importer.wrapMode = ui ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.alphaIsTransparency = ui;
                importer.textureCompression = ui ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
                if (ui)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spriteBorder = name == "PanelGlass" ? Vector4.one*64 :
                        name.StartsWith("Button", StringComparison.Ordinal) ? Vector4.one*32 : Vector4.zero;
                }
                importer.SaveAndReimport();
            }
        }

        private static Sprite Sprite(string name)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDirectory+name+".png");
            if (sprite == null)
                throw new InvalidOperationException("UI sprite unavailable: " + name);
            return sprite;
        }

        private static void AddIcon(RectTransform parent, string name, Sprite sprite,
            float x, float y, float size, Color color)
        {
            Transform existing = parent.Find(name);
            RectTransform rect = existing != null ? (RectTransform)existing :
                new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = Vector2.one*size;
            Image image = rect.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        private static void AddRule(RectTransform parent)
        {
            Transform existing = parent.Find("Header Rule");
            RectTransform rect = existing != null ? (RectTransform)existing :
                new GameObject("Header Rule", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(34, -85);
            rect.sizeDelta = new Vector2(parent.sizeDelta.x-68, 1.5f);
            Image image = rect.GetComponent<Image>();
            image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
            image.raycastTarget = false;
        }
    }
}
