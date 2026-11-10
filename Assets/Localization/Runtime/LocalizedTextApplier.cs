using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace DistortedSpace.Localization
{
    /// <summary>
    /// Created automatically when Play Mode or a player build starts. It
    /// <list type="bullet">
    /// <item>re-localizes labels baked into scenes and prefabs (stored as Chinese source text),</item>
    /// <item>adds a dynamic Noto Sans fallback so Korean, and any glyph missing from the baked atlases, renders,</item>
    /// <item>lets the player press L to cycle English → 한국어 → 中文.</item>
    /// </list>
    /// Gameplay code that writes text at runtime calls <see cref="Loc.T"/> / <see cref="Loc.F"/> itself.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class LocalizedTextApplier : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private const float ToastSeconds = 2.5f;

        private static LocalizedTextApplier instance;

        private readonly HashSet<TMP_FontAsset> patchedFonts = new HashSet<TMP_FontAsset>();
        private TMP_FontAsset dynamicFallback;
        private bool fallbackUnavailable;
        private float nextScan;
        private float toastUntil;
        private GUIStyle toastStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;
            var host = new GameObject("Localization (runtime)");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<LocalizedTextApplier>();
        }

        private void OnEnable()
        {
            Loc.LanguageChanged += ApplyNow;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyNow();
        }

        private void OnDisable()
        {
            Loc.LanguageChanged -= ApplyNow;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            RemoveFallbacks();
            if (instance == this)
                instance = null;
        }

        // Fallback lists belong to font assets on disk; never leave a runtime-only font in them.
        private void OnApplicationQuit() => RemoveFallbacks();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyNow();

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
            {
                Loc.Cycle();
                toastUntil = Time.unscaledTime + ToastSeconds;
            }
            if (Time.unscaledTime >= nextScan)
                ApplyNow();
        }

        /// <summary>Re-localizes every scene label now. Runs on scene load, on language change and twice a second.</summary>
        public void ApplyNow()
        {
            nextScan = Time.unscaledTime + ScanInterval;

            TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                EnsureFallback(text.font);
                string current = text.text;
                if (Loc.TryRelocalize(current, out string localized) && !string.Equals(localized, current))
                    text.text = localized;
            }

            UnityEngine.UI.Text[] legacyTexts = FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include);
            for (int i = 0; i < legacyTexts.Length; i++)
            {
                UnityEngine.UI.Text text = legacyTexts[i];
                string current = text.text;
                if (Loc.TryRelocalize(current, out string localized) && !string.Equals(localized, current))
                    text.text = localized;
            }
        }

        private void EnsureFallback(TMP_FontAsset font)
        {
            if (font == null || fallbackUnavailable || patchedFonts.Contains(font))
                return;
            if (dynamicFallback == null && !CreateFallback())
                return;
            if (font == dynamicFallback)
                return;

            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(dynamicFallback))
                font.fallbackFontAssetTable.Add(dynamicFallback);
            patchedFonts.Add(font);
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, font);
        }

        private bool CreateFallback()
        {
            var settings = Resources.Load<LocalizationSettings>(LocalizationSettings.ResourcePath);
            if (settings == null || settings.fallbackFont == null)
            {
                fallbackUnavailable = true;
                Debug.LogWarning("[Localization] No fallback font is assigned; Korean text may render as boxes.");
                return false;
            }
            dynamicFallback = TMP_FontAsset.CreateFontAsset(settings.fallbackFont, settings.fallbackSamplingPointSize, 6,
                GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (dynamicFallback == null)
            {
                fallbackUnavailable = true;
                Debug.LogWarning("[Localization] Could not build the runtime fallback font; Korean text may render as boxes.");
                return false;
            }
            dynamicFallback.name = "Runtime Noto Sans Fallback";
            return true;
        }

        private void RemoveFallbacks()
        {
            if (dynamicFallback == null)
                return;
            foreach (TMP_FontAsset font in patchedFonts)
                if (font != null && font.fallbackFontAssetTable != null)
                    font.fallbackFontAssetTable.Remove(dynamicFallback);
            patchedFonts.Clear();
        }

        private void OnGUI()
        {
            if (Time.unscaledTime > toastUntil)
                return;
            if (toastStyle == null)
            {
                toastStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 22,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(18, 18, 10, 10)
                };
                toastStyle.normal.textColor = Color.white;
            }
            string label;
            switch (Loc.Current)
            {
                case GameLanguage.Korean: label = "언어: 한국어   [L]"; break;
                case GameLanguage.Chinese: label = "语言：中文   [L]"; break;
                default: label = "Language: English   [L]"; break;
            }
            Vector2 size = toastStyle.CalcSize(new GUIContent(label));
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, 24f, size.x, size.y), label, toastStyle);
        }
    }
}
