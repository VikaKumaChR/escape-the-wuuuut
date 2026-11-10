using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DistortedSpace.Localization
{
    /// <summary>
    /// String-table localization for the whole project (DepthLab and Twisted Space).
    /// The original Chinese text is the lookup key, so existing scenes, prefabs and editor builders keep working:
    /// <code>
    /// label.text = Loc.T("出口未解锁");                     // "Exit locked" / "출구 잠김" / "出口未解锁"
    /// label.text = Loc.F("进度  {0}/{1}", progress, goal);  // translate the format string, then fill it in
    /// </code>
    /// Translations live in Assets/Localization/Data/strings.csv (columns: zh, en, ko, context).
    /// A missing Korean cell falls back to English; a missing English cell falls back to the Chinese key.
    /// </summary>
    public static class Loc
    {
        public const string PreferenceKey = "DistortedSpace.Language";
        private const int LanguageCount = 3;

        private static readonly Dictionary<string, string[]> Rows =
            new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Reverse =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool loaded;
        private static bool languageResolved;
        private static GameLanguage current;

        /// <summary>Raised after the display language changes.</summary>
        public static event Action LanguageChanged;

        /// <summary>
        /// Display language. First run: Korean on a Korean OS, Chinese on a Chinese OS, otherwise English.
        /// After that, the last choice is remembered in PlayerPrefs.
        /// </summary>
        public static GameLanguage Current
        {
            get
            {
                ResolveLanguage();
                return current;
            }
        }

        /// <summary>Number of entries in the string table.</summary>
        public static int Count
        {
            get
            {
                EnsureLoaded();
                return Rows.Count;
            }
        }

        /// <summary>Translates a Chinese source string into the current language.</summary>
        public static string T(string key) => T(key, Current);

        /// <summary>Translates a Chinese source string into a specific language.</summary>
        public static string T(string key, GameLanguage language)
        {
            if (string.IsNullOrEmpty(key) || language == GameLanguage.Chinese)
                return key;
            EnsureLoaded();
            if (!Rows.TryGetValue(key, out string[] row))
                return key;
            string value = language == GameLanguage.Korean && !string.IsNullOrEmpty(row[1]) ? row[1] : row[0];
            return string.IsNullOrEmpty(value) ? key : value;
        }

        /// <summary>Translates a composite format string such as "进度  {0}/{1}", then fills in the arguments.</summary>
        public static string F(string key, params object[] args)
        {
            string format = T(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[Localization] Placeholder mismatch in the {Current} translation of \"{key}\". Showing the source text.");
                return string.Format(CultureInfo.InvariantCulture, key, args);
            }
        }

        /// <summary>True when the table contains this Chinese key.</summary>
        public static bool Has(string key)
        {
            EnsureLoaded();
            return key != null && Rows.ContainsKey(key);
        }

        /// <summary>
        /// Maps text that is currently displayed in any supported language back to its key and returns the
        /// current-language version. Used for labels that were baked into scenes and prefabs.
        /// </summary>
        public static bool TryRelocalize(string displayed, out string localized)
        {
            localized = displayed;
            if (string.IsNullOrEmpty(displayed))
                return false;
            EnsureLoaded();
            if (!Reverse.TryGetValue(displayed, out string key))
                return false;
            localized = T(key);
            return true;
        }

        /// <summary>Changes the display language. Tests pass persist: false to leave the player's choice untouched.</summary>
        public static void SetLanguage(GameLanguage language, bool persist = true)
        {
            ResolveLanguage();
            if (persist)
            {
                PlayerPrefs.SetInt(PreferenceKey, (int)language);
                PlayerPrefs.Save();
            }
            if (current == language)
                return;
            current = language;
            LanguageChanged?.Invoke();
        }

        /// <summary>English → 한국어 → 中文 → English.</summary>
        public static GameLanguage Cycle(bool persist = true)
        {
            var next = (GameLanguage)(((int)Current + 1) % LanguageCount);
            SetLanguage(next, persist);
            return next;
        }

        public static string DisplayName(GameLanguage language)
        {
            switch (language)
            {
                case GameLanguage.Korean: return "한국어";
                case GameLanguage.Chinese: return "中文";
                default: return "English";
            }
        }

        /// <summary>Reloads the CSV table, e.g. after editing it in the editor.</summary>
        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            LanguageChanged?.Invoke();
        }

        private static void ResolveLanguage()
        {
            if (languageResolved)
                return;
            languageResolved = true;
            if (PlayerPrefs.HasKey(PreferenceKey))
            {
                int stored = PlayerPrefs.GetInt(PreferenceKey, 0);
                current = stored >= 0 && stored < LanguageCount ? (GameLanguage)stored : GameLanguage.English;
                return;
            }
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Korean:
                    current = GameLanguage.Korean;
                    break;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    current = GameLanguage.Chinese;
                    break;
                default:
                    current = GameLanguage.English;
                    break;
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            Rows.Clear();
            Reverse.Clear();
            var settings = Resources.Load<LocalizationSettings>(LocalizationSettings.ResourcePath);
            if (settings == null || settings.table == null)
            {
                Debug.LogWarning($"[Localization] Resources/{LocalizationSettings.ResourcePath} or its string table is missing. Showing source text.");
                return;
            }
            Parse(settings.table.text);
        }

        private static void Parse(string csv)
        {
            List<string[]> records = CsvReader.Read(csv);
            for (int i = 1; i < records.Count; i++) // record 0 is the header row
            {
                string[] record = records[i];
                if (record.Length < 3)
                    continue;
                string key = Unescape(record[0]);
                if (string.IsNullOrEmpty(key) || Rows.ContainsKey(key))
                    continue;
                string english = Unescape(record[1]);
                string korean = Unescape(record[2]);
                Rows[key] = new[] { english, korean };
                AddReverse(key, key);
                AddReverse(english, key);
                AddReverse(korean, key);
            }
        }

        private static void AddReverse(string text, string key)
        {
            if (!string.IsNullOrEmpty(text) && !Reverse.ContainsKey(text))
                Reverse[text] = key;
        }

        // Cells store a line break as the two characters \n, so every entry stays on one CSV line.
        private static string Unescape(string cell)
        {
            if (string.IsNullOrEmpty(cell) || cell.IndexOf('\\') < 0)
                return cell;
            var builder = new StringBuilder(cell.Length);
            for (int i = 0; i < cell.Length; i++)
            {
                char c = cell[i];
                if (c == '\\' && i + 1 < cell.Length)
                {
                    char next = cell[i + 1];
                    if (next == 'n') { builder.Append('\n'); i++; continue; }
                    if (next == 't') { builder.Append('\t'); i++; continue; }
                    if (next == '\\') { builder.Append('\\'); i++; continue; }
                }
                builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
