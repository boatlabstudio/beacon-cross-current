using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarborLearning
{
    public static class HarborSceneAuthoring
    {
        private const string SceneFile = "Assets/Harbor/Scenes/HarborPilot.unity";
        private const string PreviewFile = "Assets/Harbor/Scenes/HarborPreview.asset";

        [MenuItem("Harbor Learning/Create or rebuild pilot scene")]
        public static void CreatePilot()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            BuildPilot();
        }

        private static HarborStage BuildPilot()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SceneFile)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Pilot camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(.13f, .27f, .32f);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 180;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            HarborDirector.SetOverview(camera, Mission.Current);

            var sun = new GameObject("Afternoon sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .91f, .75f);
            sun.intensity = 1.16f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .70f;
            sun.transform.rotation = Quaternion.Euler(52, -32, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .81f, .87f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .61f, .63f);
            RenderSettings.ambientGroundColor = new Color(.18f, .31f, .34f);
            RenderSettings.reflectionIntensity = .37f;
            RenderSettings.fog = false;
            RenderSettings.skybox = new Material(Resources.Load<Shader>("HarborSky")) { name = "Coastal haze" };

            var director = new GameObject("Harbor pilot").AddComponent<HarborDirector>();
            director.shotCamera = camera;
            var preview = new HarborStage(null, new Course(Mission.Current));
            preview.Root.name = "Basin preview (play for the simulator)";
            preview.SetPose(-.5f, 11.4f, .22f, 0, 0, false);
            preview.SetCurrent(.65f);
            director.editorPreview = preview.Root;
            SavePreviewMaterials(preview.Root);

            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 66;
            PlayerSettings.productName = "Beacon 01 — Harbor Learning";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            EditorSceneManager.SaveScene(scene, SceneFile);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SceneFile, true) };
            AssetDatabase.SaveAssets();
            if (!Application.isBatchMode && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 0, 12), camera.transform.rotation, 23, true);
            return preview;
        }

        [MenuItem("Harbor Learning/Open pilot")]
        public static void OpenPilot()
        {
            EditorSceneManager.OpenScene(SceneFile);
            EditorApplication.delayCall += () =>
            {
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (viewType != null)
                    EditorWindow.GetWindow(viewType).Focus();
                EditorApplication.isPlaying = true;
            };
        }

        public static void VerifyAndCapture()
        {
            HarborStage preview = BuildPilot();
            foreach (Mission mission in System.Enum.GetValues(typeof(Mission)))
            {
                HarborReplay reel = HarborReplay.Load(mission);
                if (reel?.attempts == null || reel.attempts.Length == 0)
                    throw new System.InvalidOperationException("Replay missing for " + mission);
                foreach (HarborAttempt attempt in reel.attempts)
                {
                    if (attempt.frames == null || attempt.frames.Length < 2 || attempt.outcome < 1 || attempt.outcome > 4)
                        throw new System.InvalidOperationException("Malformed attempt in " + mission);
                }
                Debug.Log($"{mission}: verified {reel.attempts.Length} {reel.source} trajectories.");
            }
            var pilot = Object.FindFirstObjectByType<HarborDirector>();
            Camera camera = pilot.shotCamera;
            Directory.CreateDirectory("Preview");
            HarborReplay current = HarborReplay.Load(Mission.Current);
            HarborAttempt final = current.attempts[^1];
            preview.SetCurrent(final.currentX);
            foreach (HarborAttempt attempt in current.attempts)
            {
                if (attempt.outcome != (int)Outcome.Passed)
                    preview.DrawAttempt(attempt.frames, (Outcome)attempt.outcome);
            }
            preview.DrawAttempt(final.frames, (Outcome)final.outcome);
            HarborFrame crossing = final.frames[^1];
            preview.SetPose(crossing.x, crossing.z, crossing.yaw, crossing.speed, 3, false);
            Capture(camera, "Preview/harbor-overview.png");
            preview.Routes.gameObject.SetActive(false);
            for (int i = 0; i < final.frames.Length; i++)
            {
                HarborFrame frame = final.frames[i];
                if (frame.z > 13.0f)
                    break;
                preview.SetPose(frame.x, frame.z, frame.yaw, frame.speed, frame.tick * HarborEnvironment.Dt, true);
            }
            camera.orthographic = false;
            camera.fieldOfView = 41;
            camera.transform.position = preview.Boat.position + new Vector3(4.0f, 3.05f, -6.65f);
            camera.transform.LookAt(preview.Boat.position + new Vector3(0, 0, 1.5f));
            Capture(camera, "Preview/harbor-catamaran.png");
            HarborDirector.SetOnboard(camera, preview.Boat);
            Capture(camera, "Preview/harbor-onboard.png");
            Debug.Log("Harbor pilot scene and preview captures verified.");
        }

        private static void Capture(Camera camera, string file)
        {
            var target = new RenderTexture(1440, 810, 24) { antiAliasing = 4 };
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture old = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = old;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }

        private static void SavePreviewMaterials(GameObject root)
        {
            AssetDatabase.DeleteAsset(PreviewFile);
            var assets = new HashSet<Object>();
            assets.Add(RenderSettings.skybox);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                assets.Add(renderer.sharedMaterial);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                assets.Add(filter.sharedMesh);
            bool mainAsset = true;
            foreach (Object asset in assets)
            {
                if (!asset || EditorUtility.IsPersistent(asset))
                    continue;
                if (mainAsset)
                    AssetDatabase.CreateAsset(asset, PreviewFile);
                else
                    AssetDatabase.AddObjectToAsset(asset, PreviewFile);
                mainAsset = false;
            }
        }
    }
}
