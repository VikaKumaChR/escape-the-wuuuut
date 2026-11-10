using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TwistedSpace.Editor
{
    /// <summary>
    /// Explicit opening entry point for the shipped demo. A local, one-use Library request
    /// can also open it after script compilation without simulating keyboard shortcuts.
    /// Never replaces an unsaved scene without Unity's normal save/cancel dialog.
    /// </summary>
    public static class OpenPlayableDemo
    {
        private const string RequestPath = "Library/TwistedSpace.OpenDemo.request";

        [MenuItem("Twisted Space/Open Playable Demo", false, 0)]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before opening the demo scene.");
                return;
            }
            AssetDatabase.ImportAsset(ParallaxDemoBuilder.PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ParallaxDemoBuilder.ScenePath, ImportAssetOptions.ForceSynchronousImport);
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ParallaxDemoBuilder.ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError("Playable scene could not be imported: " + ParallaxDemoBuilder.ScenePath);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(ParallaxDemoBuilder.ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length == 0)
            {
                Debug.LogError("The demo opened without any root objects. Check its prefab dependency.");
                return;
            }
            Selection.activeGameObject = roots[0];
            EditorGUIUtility.PingObject(sceneAsset);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0f, 1.5f, 2f),
                    Quaternion.Euler(18f, 0f, 0f), 5f);
            Debug.Log("TWISTED_SPACE_DEMO_OPENED: " + scene.path + " | root=" + roots[0].name +
                " | transforms=" + roots[0].GetComponentsInChildren<Transform>(true).Length);
        }

        [InitializeOnLoadMethod]
        private static void ScheduleRequestedOpen()
        {
            if (!Application.isBatchMode && File.Exists(RequestPath))
                EditorApplication.delayCall += OpenRequestedScene;
        }

        private static void OpenRequestedScene()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += OpenRequestedScene;
                return;
            }
            if (!File.Exists(RequestPath))
                return;
            File.Delete(RequestPath);
            Open();
        }
    }
}
