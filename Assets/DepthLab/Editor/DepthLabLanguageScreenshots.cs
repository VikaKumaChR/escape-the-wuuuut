using System.IO;
using DistortedSpace.Localization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DepthLab.Editor
{
    /// <summary>
    /// Captures the main DepthLab screens in English, Korean and Chinese into docs/images/screens,
    /// so translations and text fitting can be reviewed in context without playing through the game.
    /// Menu: Tools ▸ Distorted Space ▸ Localization ▸ Capture UI Screenshots (EN · KO · ZH).
    /// Command line: Unity.exe -projectPath &lt;project&gt; -executeMethod DepthLab.Editor.DepthLabLanguageScreenshots.CaptureAndQuit
    /// </summary>
    [InitializeOnLoad]
    internal static class DepthLabLanguageScreenshots
    {
        private const string ScenePath = "Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity";
        private const string OutputFolder = "docs/images/screens";
        private const string ActiveKey = "DepthLab.Screenshots.Active";
        private const string DoneKey = "DepthLab.Screenshots.Done";
        private const string QuitKey = "DepthLab.Screenshots.Quit";
        private const double StartupDelay = 4.0;
        private const double ViewDelay = 1.5;
        private const double LanguageDelay = 1.0;
        private const double CaptureDelay = 0.5;

        private static readonly string[] ViewNames = { "menu", "chapters", "settings", "tutorial", "lab01" };
        private static readonly GameLanguage[] Languages = { GameLanguage.English, GameLanguage.Korean, GameLanguage.Chinese };
        private static readonly string[] LanguageCodes = { "en", "ko", "zh" };

        // Play Mode state; entering Play Mode reloads the domain, so the sequence restarts cleanly from here.
        private static int view = -1;
        private static int language = -1;
        private static bool captured;
        private static double nextTime;
        private static GameLanguage previousLanguage;

        static DepthLabLanguageScreenshots()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Distorted Space/Localization/Capture UI Screenshots (EN · KO · ZH)", priority = 40)]
        private static void CaptureFromMenu() => Begin(false);

        /// <summary>Entry point for -executeMethod: captures everything, then quits the editor.</summary>
        public static void CaptureAndQuit() => Begin(true);

        private static void Begin(bool quitWhenDone)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Screenshots] Exit Play Mode first.");
                return;
            }
            if (!quitWhenDone && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Directory.CreateDirectory(OutputFolder);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(DoneKey, false);
            SessionState.SetBool(QuitKey, quitWhenDone);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;

            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !SessionState.GetBool(DoneKey, false))
                    return;
                SessionState.EraseBool(ActiveKey);
                SessionState.EraseBool(DoneKey);
                Debug.Log($"[Screenshots] Saved {ViewNames.Length * Languages.Length} screenshots to {Path.GetFullPath(OutputFolder)}");
                if (SessionState.GetBool(QuitKey, false))
                    EditorApplication.Exit(0);
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (view < 0)
            {
                if (nextTime <= 0)
                {
                    nextTime = now + StartupDelay;
                    previousLanguage = Loc.Current;
                    return;
                }
                if (now < nextTime)
                    return;
                view = 0;
                language = -1;
                ShowView(view);
                nextTime = now + ViewDelay;
                return;
            }
            if (now < nextTime)
                return;

            if (language >= 0 && !captured)
            {
                string file = Path.GetFullPath(Path.Combine(OutputFolder, $"{ViewNames[view]}-{LanguageCodes[language]}.png"));
                ScreenCapture.CaptureScreenshot(file);
                captured = true;
                nextTime = now + CaptureDelay;
                return;
            }

            language++;
            captured = false;
            if (language < Languages.Length)
            {
                Loc.SetLanguage(Languages[language], persist: false);
                nextTime = now + LanguageDelay;
                return;
            }

            view++;
            language = -1;
            if (view < ViewNames.Length)
            {
                ShowView(view);
                nextTime = now + ViewDelay;
                return;
            }

            Loc.SetLanguage(previousLanguage, persist: false);
            SessionState.SetBool(DoneKey, true);
            view = -1;
            nextTime = 0;
            EditorApplication.ExitPlaymode();
        }

        private static void ShowView(int index)
        {
            DepthLabApp app = DepthLabApp.Instance;
            if (app == null)
            {
                Debug.LogWarning("[Screenshots] DepthLabApp was not found in the scene; capturing the current view.");
                return;
            }
            switch (index)
            {
                case 1: app.Execute(LabAction.Chapters); break;
                case 2: app.Execute(LabAction.Settings); break;
                case 3: app.Execute(LabAction.Tutorial); break;
                case 4: app.Execute(LabAction.Start); break;
            }
        }
    }
}
