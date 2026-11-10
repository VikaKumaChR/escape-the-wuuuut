using UnityEngine;

namespace DistortedSpace.Localization
{
    /// <summary>
    /// Project-wide localization data. The asset lives at Resources/DistortedSpaceLocalization.asset,
    /// so it loads in edit mode, in Play Mode and in player builds without any scene wiring.
    /// </summary>
    [CreateAssetMenu(menuName = "Distorted Space/Localization Settings", fileName = ResourcePath)]
    public sealed class LocalizationSettings : ScriptableObject
    {
        public const string ResourcePath = "DistortedSpaceLocalization";

        [Tooltip("CSV string table with the columns zh, en, ko, context. The Chinese column is the key used in code and scenes.")]
        public TextAsset table;

        [Tooltip("Font with Latin, Hangul and CJK glyphs. A dynamic TextMeshPro fallback is built from it at runtime, " +
                 "because the baked SDF atlases only contain the characters of the original Chinese UI.")]
        public Font fallbackFont;

        [Tooltip("Sampling point size of the runtime fallback atlas.")]
        [Range(32, 128)]
        public int fallbackSamplingPointSize = 72;
    }
}
