using TMPro;
using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab
{
    public class DepthLabSettingsView : MonoBehaviour
    {
        public DepthLabSettings settings;
        public UnityEngine.UI.Slider master, music, sfx, speed, brightness;
        public TMP_Text movementLabel, turnLabel, snapLabel, qualityLabel;
        private bool refreshing;
        private void OnEnable()
        {
            if (settings == null)
            {
                var app = GetComponentInParent<DepthLabApp>();
                if (app == null) app = DepthLabApp.Instance;
                if (app != null) settings = app.settings;
            }
            Refresh();
        }
        public void Refresh()
        {
            if (settings == null || master == null) return;
            refreshing = true;
            master.SetValueWithoutNotify(settings.master);
            music.SetValueWithoutNotify(settings.music);
            sfx.SetValueWithoutNotify(settings.sfx);
            speed.SetValueWithoutNotify(settings.moveSpeed);
            brightness.SetValueWithoutNotify(settings.brightness);
            movementLabel.text = Loc.F("VR 移动   /   {0}", Loc.T(settings.moveMode == 0 ? "步进" : "连续"));
            turnLabel.text = Loc.F("VR 转向   /   {0}", Loc.T(settings.turnMode == 0 ? "分段" : "连续"));
            snapLabel.text = Loc.F("VR 分段转向角度   /   {0:0}°", settings.snapAngle);
            qualityLabel.text = Loc.F("画质   /   {0}", QualityLabel(settings.quality));
            refreshing = false;
        }
        private static string QualityLabel(int index)
        {
            switch (QualitySettings.names[index])
            {
                case "Mobile": return Loc.T("移动设备");
                case "PC": return Loc.T("电脑");
                default: return Loc.F("档位 {0}", index + 1);
            }
        }
        public void ChangeValues(float unused)
        {
            if (refreshing) return;
            settings.master = master.value;
            settings.music = music.value;
            settings.sfx = sfx.value;
            settings.moveSpeed = speed.value;
            settings.brightness = brightness.value;
            Apply();
        }
        public void Movement() { settings.moveMode = 1 - settings.moveMode; Apply(); }
        public void Turn() { settings.turnMode = 1 - settings.turnMode; Apply(); }
        public void Snap() { settings.snapAngle = settings.snapAngle >= 60 ? 15 : settings.snapAngle + 15; Apply(); }
        public void Quality() { settings.quality = (settings.quality + 1) % QualitySettings.names.Length; Apply(); }
        public void Defaults() { settings.Defaults(); Apply(); }
        private void Apply()
        {
            settings.Apply();
            settings.Save();
            if (DepthLabApp.Instance != null) DepthLabApp.Instance.ApplyAudio();
            Refresh();
        }
    }
}
