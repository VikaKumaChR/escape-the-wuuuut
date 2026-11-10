using System.Collections.Generic;
using UnityEngine;

namespace DepthLab
{
    public class DepthLabSettings : MonoBehaviour
    {
        private const string PrefsPrefix = "DepthLab.Settings.";

        public float master = .8f;
        public float music = .3f;
        public float sfx = .7f;
        public int moveMode = 0; // 0 stepped, 1 smooth.
        public int turnMode = 0; // 0 snap, 1 smooth.
        public float snapAngle = 30f;
        public float moveSpeed = 1.5f;
        public int quality = 1;
        public float brightness = 1f;
        public Light[] lights;

        private readonly Dictionary<Light, float> initialIntensities =
            new Dictionary<Light, float>();

        // Initialization is explicit so the owner can assign scene references first.
        // Load and Defaults apply immediately, but never write preferences.
        public void Load()
        {
            master = PlayerPrefs.GetFloat(PrefsPrefix + nameof(master), .8f);
            music = PlayerPrefs.GetFloat(PrefsPrefix + nameof(music), .3f);
            sfx = PlayerPrefs.GetFloat(PrefsPrefix + nameof(sfx), .7f);
            moveMode = PlayerPrefs.GetInt(PrefsPrefix + nameof(moveMode), 0);
            turnMode = PlayerPrefs.GetInt(PrefsPrefix + nameof(turnMode), 0);
            snapAngle = PlayerPrefs.GetFloat(PrefsPrefix + nameof(snapAngle), 30f);
            moveSpeed = PlayerPrefs.GetFloat(PrefsPrefix + nameof(moveSpeed), 1.5f);
            quality = PlayerPrefs.GetInt(PrefsPrefix + nameof(quality), 1);
            brightness = PlayerPrefs.GetFloat(PrefsPrefix + nameof(brightness), 1f);
            Apply();
        }

        public void Save()
        {
            ClampSettings();
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(master), master);
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(music), music);
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(sfx), sfx);
            PlayerPrefs.SetInt(PrefsPrefix + nameof(moveMode), moveMode);
            PlayerPrefs.SetInt(PrefsPrefix + nameof(turnMode), turnMode);
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(snapAngle), snapAngle);
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(moveSpeed), moveSpeed);
            PlayerPrefs.SetInt(PrefsPrefix + nameof(quality), quality);
            PlayerPrefs.SetFloat(PrefsPrefix + nameof(brightness), brightness);
            PlayerPrefs.Save();
        }

        public void Defaults()
        {
            master = .8f;
            music = .3f;
            sfx = .7f;
            moveMode = 0;
            turnMode = 0;
            snapAngle = 30f;
            moveSpeed = 1.5f;
            quality = 1;
            brightness = 1f;
            Apply();
        }

        public void Apply()
        {
            int qualityCount = ClampSettings();
            AudioListener.volume = master;
            if (qualityCount > 0 && QualitySettings.GetQualityLevel() != quality)
            {
                QualitySettings.SetQualityLevel(quality);
            }

            // Music/sfx mixing and locomotion are handled by the owner.
            if (lights == null)
            {
                return;
            }

            foreach (Light light in lights)
            {
                if (light == null)
                {
                    continue;
                }

                float initialIntensity;
                if (!initialIntensities.TryGetValue(light, out initialIntensity))
                {
                    initialIntensity = light.intensity;
                    initialIntensities.Add(light, initialIntensity);
                }

                // Cache by light, not array index, so reordering or readding is safe.
                light.intensity = initialIntensity * brightness;
            }
        }

        private int ClampSettings()
        {
            master = ClampFinite(master, 0f, 1f, .8f);
            music = ClampFinite(music, 0f, 1f, .3f);
            sfx = ClampFinite(sfx, 0f, 1f, .7f);
            moveMode = Mathf.Clamp(moveMode, 0, 1);
            turnMode = Mathf.Clamp(turnMode, 0, 1);
            snapAngle = ClampFinite(snapAngle, 15f, 90f, 30f);
            moveSpeed = ClampFinite(moveSpeed, .1f, 10f, 1.5f);
            brightness = ClampFinite(brightness, 0f, 2f, 1f);

            int qualityCount = QualitySettings.names.Length;
            quality = qualityCount > 0 ? Mathf.Clamp(quality, 0, qualityCount - 1) : 0;
            return qualityCount;
        }

        private static float ClampFinite(float value, float min, float max, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, min, max);
        }
    }
}
