// GreyZone - zero-scene-configuration bootstrap: engine settings, camera, light, systems, GameDirector.
// The prototype must run from a completely empty scene (no hand placed objects).
using UnityEngine;
using UnityEngine.Rendering;
using GreyZone.Core;

namespace GreyZone.Game
{
    public static class GameBootstrap
    {
        public const string RootName = "GreyZone";

        private static GameObject _root;
        private static bool _booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_booted) return;
            if (Object.FindObjectOfType<GameDirector>() != null) { _booted = true; return; }
            _booted = true;

            GameSettings.Load();

            _root = new GameObject(RootName);
            Object.DontDestroyOnLoad(_root);
            GZ.Root = _root.transform;

            ProcAssets.EnsureInit();
            ApplyEngineSettings();
            SetupLight();
            SetupCamera();

            GameObject systems = new GameObject("Systems");
            systems.transform.SetParent(_root.transform, false);

            FxSystem fx = systems.AddComponent<FxSystem>();
            fx.Init(_root.transform);

            GzAudio audio = systems.AddComponent<GzAudio>();
            audio.Init(systems.transform);

            GameObject director = new GameObject("GameDirector");
            director.transform.SetParent(_root.transform, false);
            GameDirector gd = director.AddComponent<GameDirector>();
            gd.Init(_root.transform);

            Application.quitting += OnQuit;
            Debug.Log("[GreyZone] boot complete (Built-in RP, IMGUI, procedural assets).");
        }

        private static void OnQuit()
        {
            GameSettings.Save();
        }

        // ------------------------------------------------------------------ engine / rendering
        public static void ApplyEngineSettings()
        {
            Application.targetFrameRate = GameSettings.TargetFrameRate;
            Application.runInBackground = true;

            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.shadowCascades = 0;
            QualitySettings.pixelLightCount = 1;
            QualitySettings.antiAliasing = 0;
            QualitySettings.vSyncCount = 0;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.billboardsFaceCameraPosition = false;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = GameSettings.FogStart;
            RenderSettings.fogEndDistance = GameSettings.FogEnd;
            RenderSettings.fogColor = GZ.Col(0x6A, 0x6F, 0x73);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = GZ.Col(0x30, 0x31, 0x33);
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflection = null;

            // Query-only physics: no rigidbodies anywhere in this prototype.
#if UNITY_2022_2_OR_NEWER
            Physics.simulationMode = SimulationMode.Script;
#else
            Physics.autoSimulation = false;
#endif
            Physics.queriesHitTriggers = true;
            Physics.queriesHitBackfaces = false;
            // We move actors by transform every tick, so queries must always see the fresh pose
            // (there is no simulation step that would sync it for us).
            Physics.autoSyncTransforms = true;
        }

        public static void ApplyCameraQuality(Camera cam)
        {
            if (cam == null) return;
            cam.farClipPlane = GameSettings.FarClip;
            cam.fieldOfView = GameSettings.Fov;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = true;
            cam.renderingPath = RenderingPath.Forward;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GZ.Col(0x7E, 0x85, 0x8A);
            cam.nearClipPlane = 0.05f;
            cam.depthTextureMode = DepthTextureMode.None;
        }

        private static void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("GzCamera");
                go.transform.SetParent(_root.transform, false);
                cam = go.AddComponent<Camera>();
                go.tag = "MainCamera";
            }
            cam.transform.SetParent(_root.transform, true);
            ApplyCameraQuality(cam);
            cam.cullingMask = ~0;

            if (cam.GetComponent<AudioListener>() == null)
            {
                AudioListener listener = cam.gameObject.AddComponent<AudioListener>();
                listener.enabled = true;
            }
        }

        private static void SetupLight()
        {
            Light[] lights = Object.FindObjectsOfType<Light>();
            Light sun = null;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional) { sun = lights[i]; break; }
            }
            if (sun == null)
            {
                GameObject go = new GameObject("GzSun");
                go.transform.SetParent(_root.transform, false);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.shadows = LightShadows.None;
            sun.intensity = 0.92f;
            sun.color = GZ.Col(0xE8, 0xE6, 0xDE);
            sun.transform.rotation = Quaternion.Euler(48f, 38f, 0f);
            sun.renderMode = LightRenderMode.Auto;   // with pixelLightCount=1 it is the one pixel light
            sun.shadowStrength = 0f;
        }

        public static Transform RootTransform { get { return _root == null ? null : _root.transform; } }
    }
}
