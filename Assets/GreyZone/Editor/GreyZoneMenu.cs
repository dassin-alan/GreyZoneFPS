// GreyZone - editor conveniences. This is the only place that may use UnityEditor.
// Tools: create the playable scene (and register it for builds) and apply the mobile profile.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GreyZone.EditorTools
{
    [InitializeOnLoad]
    public static class GreyZoneMenu
    {
        private const string SceneFolder = "Assets/GreyZone/Scenes";
        private const string ScenePath = "Assets/GreyZone/Scenes/GreyZone.unity";

        static GreyZoneMenu()
        {
            // First launch in a fresh project: make sure a scene exists in the build list.
            EditorApplication.delayCall += AutoCreateScene;
        }

        [MenuItem("GreyZone/创建场景并加入构建列表", false, 10)]
        public static void CreateSceneMenu()
        {
            CreateSceneIfNeeded(true);
        }

        [MenuItem("GreyZone/应用移动端设置", false, 20)]
        public static void ApplyMobileSettings()
        {
            try
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode);
                PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);

                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new GraphicsDeviceType[]
                {
                    GraphicsDeviceType.Vulkan,
                    GraphicsDeviceType.OpenGLES3
                });

                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;

                PlayerSettings.productName = "GreyZone FPS";
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.greyzone.fps");

                AssetDatabase.SaveAssets();
                Debug.Log("[GreyZone] Android profile applied (IL2CPP / ARM64 / minSdk 24 / Vulkan+GLES3 / landscape).");
            }
            catch (Exception e)
            {
                Debug.LogError("[GreyZone] failed to apply mobile settings: " + e);
            }
        }

        private static void AutoCreateScene()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath)) CreateSceneIfNeeded(false);
            }
            catch (Exception e)
            {
                Debug.LogError("[GreyZone] auto scene creation failed: " + e);
            }
        }

        /// <summary>
        /// Creates an empty scene asset at Assets/GreyZone/Scenes/GreyZone.unity and registers it in
        /// the build settings. The scene is created additively so whatever the user has open is never
        /// modified; it is closed again right after saving.
        /// </summary>
        public static void CreateSceneIfNeeded(bool verbose)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(SceneFolder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/GreyZone")) AssetDatabase.CreateFolder("Assets", "GreyZone");
                    AssetDatabase.CreateFolder("Assets/GreyZone", "Scenes");
                }

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
                if (!saved) Debug.LogError("[GreyZone] could not save " + ScenePath);

                // register (keep any other scenes the user already had, avoid duplicates)
                EditorBuildSettingsScene[] old = EditorBuildSettings.scenes;
                int extra = 0;
                for (int i = 0; i < old.Length; i++) if (old[i].path != ScenePath) extra++;
                EditorBuildSettingsScene[] next = new EditorBuildSettingsScene[extra + 1];
                next[0] = new EditorBuildSettingsScene(ScenePath, true);
                int k = 1;
                for (int i = 0; i < old.Length; i++) if (old[i].path != ScenePath) next[k++] = old[i];
                EditorBuildSettings.scenes = next;

                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.Refresh();

                if (verbose) Debug.Log("[GreyZone] scene ready: " + ScenePath + " (added to build settings).");
            }
            catch (Exception e)
            {
                Debug.LogError("[GreyZone] scene creation failed: " + e);
            }
        }
    }
}
