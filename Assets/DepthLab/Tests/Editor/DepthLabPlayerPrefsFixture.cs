using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab.Tests
{
    public abstract class DepthLabPlayerPrefsFixture
    {
        protected const string ProgressPrefix = "DepthLab.Progress.";
        protected const string SettingsPrefix = "DepthLab.Settings.";
        protected const string UnrelatedKey = "DepthLab.Tests.Unrelated";

        protected static readonly string[] SettingNames =
        {
            "master", "music", "sfx", "moveMode", "turnMode",
            "snapAngle", "moveSpeed", "quality", "brightness"
        };

        private readonly List<PreferenceSnapshot> snapshots = new List<PreferenceSnapshot>();
        private GameLanguage previousLanguage;

        [SetUp]
        public void PreservePreferences()
        {
            // Assertions use the original Chinese UI text; run in Chinese without touching the saved player choice.
            previousLanguage = Loc.Current;
            Loc.SetLanguage(GameLanguage.Chinese, persist: false);
            snapshots.Clear();
            for (int i = 0; i < 4; i++)
                snapshots.Add(new PreferenceSnapshot(ProgressPrefix + i));
            foreach (string name in SettingNames)
                snapshots.Add(new PreferenceSnapshot(SettingsPrefix + name));
            snapshots.Add(new PreferenceSnapshot(ProgressPrefix + 4));
            snapshots.Add(new PreferenceSnapshot(UnrelatedKey));

            // Capture everything before deleting anything, including keys used as reset sentinels.
            foreach (PreferenceSnapshot snapshot in snapshots)
                PlayerPrefs.DeleteKey(snapshot.Key);
        }

        [TearDown]
        public void RestorePreferences()
        {
            foreach (PreferenceSnapshot snapshot in snapshots)
                snapshot.Restore();
            PlayerPrefs.Save();
            snapshots.Clear();
            Loc.SetLanguage(previousLanguage, persist: false);
        }

        private sealed class PreferenceSnapshot
        {
            public readonly string Key;
            private readonly bool existed;
            private readonly string stringValue;
            private readonly int intValue;
            private readonly float floatValue;
            private readonly PreferenceType type;

            private enum PreferenceType { String, Int, Float }

            public PreferenceSnapshot(string key)
            {
                Key = key;
                existed = PlayerPrefs.HasKey(key);
                if (!existed)
                    return;

                // Two defaults distinguish an absent type from a stored sentinel value.
                // Preserve raw JSON strings, but also native numeric settings and unexpected types.
                stringValue = PlayerPrefs.GetString(key, "DepthLab.Tests.MissingStringA");
                if (stringValue == PlayerPrefs.GetString(key, "DepthLab.Tests.MissingStringB"))
                {
                    type = PreferenceType.String;
                    return;
                }

                intValue = PlayerPrefs.GetInt(key, int.MinValue);
                if (intValue == PlayerPrefs.GetInt(key, int.MaxValue))
                {
                    type = PreferenceType.Int;
                    return;
                }

                type = PreferenceType.Float;
                floatValue = PlayerPrefs.GetFloat(key);
            }

            public void Restore()
            {
                PlayerPrefs.DeleteKey(Key);
                if (!existed)
                    return;
                switch (type)
                {
                    case PreferenceType.String:
                        PlayerPrefs.SetString(Key, stringValue);
                        break;
                    case PreferenceType.Int:
                        PlayerPrefs.SetInt(Key, intValue);
                        break;
                    case PreferenceType.Float:
                        PlayerPrefs.SetFloat(Key, floatValue);
                        break;
                }
            }
        }
    }
}
