using NUnit.Framework;
using UnityEngine;

namespace DepthLab.Tests
{
    public class DepthLabSettingsTests : DepthLabPlayerPrefsFixture
    {
        private GameObject root;
        private DepthLabSettings settings;
        private float originalVolume;
        private int originalQuality;

        [SetUp]
        public void CreateInactiveSettings()
        {
            originalVolume = AudioListener.volume;
            originalQuality = QualitySettings.GetQualityLevel();
            root = new GameObject("DepthLab settings test");
            root.SetActive(false);
            settings = root.AddComponent<DepthLabSettings>();
        }

        [TearDown]
        public void DestroySettingsAndRestoreGlobals()
        {
            try
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
            finally
            {
                AudioListener.volume = originalVolume;
                if (QualitySettings.names.Length > 0 && QualitySettings.GetQualityLevel() != originalQuality)
                    QualitySettings.SetQualityLevel(originalQuality);
            }
        }

        [Test]
        public void Load_MissingKeysAppliesDefaultsWithoutCreatingPreferences()
        {
            SetCustomValues();

            settings.Load();

            AssertDefaults();
            Assert.That(AudioListener.volume, Is.EqualTo(.8f).Within(.0001f));
            AssertNoSettingKeys();
        }

        [Test]
        public void Load_ReadsNativePreferenceTypesWithoutRewritingThem()
        {
            WriteCustomPreferences();

            settings.Load();

            AssertCustomValues(settings);
            AssertCustomPreferences();
            Assert.That(AudioListener.volume, Is.EqualTo(.25f).Within(.0001f));
        }

        [Test]
        public void Load_ClampsStoredValuesWithoutSavingCorrections()
        {
            PlayerPrefs.SetFloat(SettingsPrefix + "master", -2f);
            PlayerPrefs.SetFloat(SettingsPrefix + "music", 5f);
            PlayerPrefs.SetFloat(SettingsPrefix + "sfx", -4f);
            PlayerPrefs.SetInt(SettingsPrefix + "moveMode", -1);
            PlayerPrefs.SetInt(SettingsPrefix + "turnMode", 9);
            PlayerPrefs.SetFloat(SettingsPrefix + "snapAngle", 120f);
            PlayerPrefs.SetFloat(SettingsPrefix + "moveSpeed", 0f);
            PlayerPrefs.SetInt(SettingsPrefix + "quality", int.MaxValue);
            PlayerPrefs.SetFloat(SettingsPrefix + "brightness", 3f);

            settings.Load();

            Assert.That(settings.master, Is.Zero);
            Assert.That(settings.music, Is.EqualTo(1f));
            Assert.That(settings.sfx, Is.Zero);
            Assert.That(settings.moveMode, Is.Zero);
            Assert.That(settings.turnMode, Is.EqualTo(1));
            Assert.That(settings.snapAngle, Is.EqualTo(90f));
            Assert.That(settings.moveSpeed, Is.EqualTo(.1f));
            Assert.That(settings.quality, Is.EqualTo(MaximumQuality));
            Assert.That(settings.brightness, Is.EqualTo(2f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "master"), Is.EqualTo(-2f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "music"), Is.EqualTo(5f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "sfx"), Is.EqualTo(-4f));
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "moveMode"), Is.EqualTo(-1));
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "turnMode"), Is.EqualTo(9));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "snapAngle"), Is.EqualTo(120f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "moveSpeed"), Is.Zero);
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "quality"), Is.EqualTo(int.MaxValue));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "brightness"), Is.EqualTo(3f));
        }

        [Test]
        public void Save_PersistsAllFieldsForAFreshComponentWithoutTouchingProgress()
        {
            SetCustomValues();
            PlayerPrefs.SetString(ProgressPrefix + 0, "{\"completed\":true,\"stars\":2}");

            settings.Save();

            AssertCustomPreferences();
            DepthLabSettings fresh = root.AddComponent<DepthLabSettings>();
            fresh.Load();
            AssertCustomValues(fresh);
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 0),
                Is.EqualTo("{\"completed\":true,\"stars\":2}"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Save_ClampsEverySettingBeforePersisting(bool upper)
        {
            float value = upper ? 100f : -100f;
            settings.master = settings.music = settings.sfx = value;
            settings.snapAngle = settings.moveSpeed = settings.brightness = value;
            settings.moveMode = settings.turnMode = settings.quality = upper ? int.MaxValue : int.MinValue;

            settings.Save();

            AssertSavedFloat("master", settings.master, upper ? 1f : 0f);
            AssertSavedFloat("music", settings.music, upper ? 1f : 0f);
            AssertSavedFloat("sfx", settings.sfx, upper ? 1f : 0f);
            AssertSavedFloat("snapAngle", settings.snapAngle, upper ? 90f : 15f);
            AssertSavedFloat("moveSpeed", settings.moveSpeed, upper ? 10f : .1f);
            AssertSavedFloat("brightness", settings.brightness, upper ? 2f : 0f);
            AssertSavedInt("moveMode", settings.moveMode, upper ? 1 : 0);
            AssertSavedInt("turnMode", settings.turnMode, upper ? 1 : 0);
            AssertSavedInt("quality", settings.quality, upper ? MaximumQuality : 0);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Save_NonFiniteFloatsUseDefaultsRatherThanPersistingInvalidValues(float value)
        {
            settings.master = settings.music = settings.sfx = value;
            settings.snapAngle = settings.moveSpeed = settings.brightness = value;

            settings.Save();

            AssertSavedFloat("master", settings.master, .8f);
            AssertSavedFloat("music", settings.music, .3f);
            AssertSavedFloat("sfx", settings.sfx, .7f);
            AssertSavedFloat("snapAngle", settings.snapAngle, 30f);
            AssertSavedFloat("moveSpeed", settings.moveSpeed, 1.5f);
            AssertSavedFloat("brightness", settings.brightness, 1f);
        }

        [Test]
        public void Defaults_AppliesDefaultsButRetainsSavedUserChoices()
        {
            WriteCustomPreferences();
            settings.Load();

            settings.Defaults();

            AssertDefaults();
            Assert.That(AudioListener.volume, Is.EqualTo(.8f).Within(.0001f));
            AssertCustomPreferences();
            settings.Load();
            AssertCustomValues(settings);
        }

        [Test]
        public void Defaults_DoesNotCreatePreferences()
        {
            SetCustomValues();

            settings.Defaults();

            AssertDefaults();
            AssertNoSettingKeys();
        }

        [Test]
        public void Apply_UsesOriginalLightIntensityAcrossRepeatsReorderingAndReadding()
        {
            Light first = CreateLight("First light", 2f);
            Light second = CreateLight("Second light", 4f);
            settings.quality = originalQuality;
            settings.lights = new[] { first, null, second };
            settings.brightness = .5f;

            settings.Apply();
            settings.Apply();
            Assert.That(first.intensity, Is.EqualTo(1f).Within(.0001f));
            Assert.That(second.intensity, Is.EqualTo(2f).Within(.0001f));

            settings.lights = new[] { second, first };
            settings.brightness = 1.5f;
            settings.Apply();
            Assert.That(first.intensity, Is.EqualTo(3f).Within(.0001f));
            Assert.That(second.intensity, Is.EqualTo(6f).Within(.0001f));

            settings.lights = new[] { second };
            settings.brightness = .25f;
            settings.Apply();
            settings.lights = new[] { first, second };
            settings.brightness = 1f;
            settings.Apply();
            Assert.That(first.intensity, Is.EqualTo(2f).Within(.0001f));
            Assert.That(second.intensity, Is.EqualTo(4f).Within(.0001f));
            AssertNoSettingKeys();
        }

        private static int MaximumQuality => Mathf.Max(0, QualitySettings.names.Length - 1);

        private void SetCustomValues()
        {
            settings.master = .25f;
            settings.music = .5f;
            settings.sfx = .75f;
            settings.moveMode = 1;
            settings.turnMode = 1;
            settings.snapAngle = 60f;
            settings.moveSpeed = 2.5f;
            settings.quality = MaximumQuality;
            settings.brightness = 1.25f;
        }

        private static void WriteCustomPreferences()
        {
            PlayerPrefs.SetFloat(SettingsPrefix + "master", .25f);
            PlayerPrefs.SetFloat(SettingsPrefix + "music", .5f);
            PlayerPrefs.SetFloat(SettingsPrefix + "sfx", .75f);
            PlayerPrefs.SetInt(SettingsPrefix + "moveMode", 1);
            PlayerPrefs.SetInt(SettingsPrefix + "turnMode", 1);
            PlayerPrefs.SetFloat(SettingsPrefix + "snapAngle", 60f);
            PlayerPrefs.SetFloat(SettingsPrefix + "moveSpeed", 2.5f);
            PlayerPrefs.SetInt(SettingsPrefix + "quality", MaximumQuality);
            PlayerPrefs.SetFloat(SettingsPrefix + "brightness", 1.25f);
        }

        private static void AssertCustomPreferences()
        {
            foreach (string name in SettingNames)
                Assert.That(PlayerPrefs.HasKey(SettingsPrefix + name), Is.True, name);
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "master"), Is.EqualTo(.25f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "music"), Is.EqualTo(.5f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "sfx"), Is.EqualTo(.75f));
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "moveMode"), Is.EqualTo(1));
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "turnMode"), Is.EqualTo(1));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "snapAngle"), Is.EqualTo(60f));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "moveSpeed"), Is.EqualTo(2.5f));
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + "quality"), Is.EqualTo(MaximumQuality));
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + "brightness"), Is.EqualTo(1.25f));
        }

        private static void AssertCustomValues(DepthLabSettings actual)
        {
            Assert.That(actual.master, Is.EqualTo(.25f));
            Assert.That(actual.music, Is.EqualTo(.5f));
            Assert.That(actual.sfx, Is.EqualTo(.75f));
            Assert.That(actual.moveMode, Is.EqualTo(1));
            Assert.That(actual.turnMode, Is.EqualTo(1));
            Assert.That(actual.snapAngle, Is.EqualTo(60f));
            Assert.That(actual.moveSpeed, Is.EqualTo(2.5f));
            Assert.That(actual.quality, Is.EqualTo(MaximumQuality));
            Assert.That(actual.brightness, Is.EqualTo(1.25f));
        }

        private void AssertDefaults()
        {
            Assert.That(settings.master, Is.EqualTo(.8f));
            Assert.That(settings.music, Is.EqualTo(.3f));
            Assert.That(settings.sfx, Is.EqualTo(.7f));
            Assert.That(settings.moveMode, Is.Zero);
            Assert.That(settings.turnMode, Is.Zero);
            Assert.That(settings.snapAngle, Is.EqualTo(30f));
            Assert.That(settings.moveSpeed, Is.EqualTo(1.5f));
            Assert.That(settings.quality, Is.EqualTo(Mathf.Min(1, MaximumQuality)));
            Assert.That(settings.brightness, Is.EqualTo(1f));
        }

        private static void AssertSavedFloat(string name, float actual, float expected)
        {
            Assert.That(actual, Is.EqualTo(expected), name);
            Assert.That(PlayerPrefs.HasKey(SettingsPrefix + name), Is.True, name);
            Assert.That(PlayerPrefs.GetFloat(SettingsPrefix + name), Is.EqualTo(expected), name);
        }

        private static void AssertSavedInt(string name, int actual, int expected)
        {
            Assert.That(actual, Is.EqualTo(expected), name);
            Assert.That(PlayerPrefs.HasKey(SettingsPrefix + name), Is.True, name);
            Assert.That(PlayerPrefs.GetInt(SettingsPrefix + name), Is.EqualTo(expected), name);
        }

        private static void AssertNoSettingKeys()
        {
            foreach (string name in SettingNames)
                Assert.That(PlayerPrefs.HasKey(SettingsPrefix + name), Is.False, name);
        }

        private Light CreateLight(string name, float intensity)
        {
            GameObject child = new GameObject(name);
            child.SetActive(false);
            child.transform.SetParent(root.transform, false);
            Light light = child.AddComponent<Light>();
            light.intensity = intensity;
            return light;
        }
    }
}
