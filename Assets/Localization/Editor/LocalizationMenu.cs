using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DistortedSpace.Localization.Editor
{
    /// <summary>Tools ▸ Distorted Space menu: pick the play-mode language and find untranslated scene text.</summary>
    internal static class LocalizationMenu
    {
        private const string LanguageRoot = "Tools/Distorted Space/Language/";
        private const string EnglishItem = LanguageRoot + "English";
        private const string KoreanItem = LanguageRoot + "한국어 (Korean)";
        private const string ChineseItem = LanguageRoot + "中文 (Chinese)";

        [MenuItem(EnglishItem, priority = 0)]
        private static void English() => Select(GameLanguage.English);

        [MenuItem(KoreanItem, priority = 1)]
        private static void Korean() => Select(GameLanguage.Korean);

        [MenuItem(ChineseItem, priority = 2)]
        private static void Chinese() => Select(GameLanguage.Chinese);

        [MenuItem(EnglishItem, true)]
        private static bool EnglishCheck() => Check(EnglishItem, GameLanguage.English);

        [MenuItem(KoreanItem, true)]
        private static bool KoreanCheck() => Check(KoreanItem, GameLanguage.Korean);

        [MenuItem(ChineseItem, true)]
        private static bool ChineseCheck() => Check(ChineseItem, GameLanguage.Chinese);

        private static bool Check(string item, GameLanguage language)
        {
            Menu.SetChecked(item, Loc.Current == language);
            return true;
        }

        private static void Select(GameLanguage language)
        {
            Loc.SetLanguage(language);
            Debug.Log($"[Localization] Language set to {Loc.DisplayName(language)}. " +
                      "It applies immediately in Play Mode; players can also press L in-game.");
        }

        [MenuItem("Tools/Distorted Space/Localization/Reload String Table", priority = 20)]
        private static void ReloadTable()
        {
            Loc.Reload();
            Debug.Log($"[Localization] Loaded {Loc.Count} entries from Assets/Localization/Data/strings.csv.");
        }

        [MenuItem("Tools/Distorted Space/Localization/Report Untranslated Scene Text", priority = 21)]
        private static void ReportUntranslated()
        {
            var missing = new SortedSet<string>();
            foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
                Collect(text.text, missing);
            foreach (UnityEngine.UI.Text text in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include))
                Collect(text.text, missing);

            if (missing.Count == 0)
            {
                Debug.Log("[Localization] Every Chinese label in the open scenes has a translation.");
                return;
            }
            Debug.LogWarning($"[Localization] {missing.Count} Chinese label(s) have no row in Assets/Localization/Data/strings.csv:\n" +
                             string.Join("\n", missing.Select(s => s.Replace("\n", "\\n"))));
        }

        private static void Collect(string text, ISet<string> missing)
        {
            if (ContainsHan(text) && !Loc.Has(text))
                missing.Add(text);
        }

        private static bool ContainsHan(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (char c in text)
                if (c >= '一' && c <= '鿿')
                    return true;
            return false;
        }
    }
}
