using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace HarborLearning
{
    public static class BeaconEpisodeRenderer
    {
        [Serializable]
        private sealed class Film
        {
            public int width, height, fps;
            public Shot[] shots;
        }

        [Serializable]
        private sealed class Shot
        {
            public string name, camera;
            public float length, offset, rate, angle, sweep;
            public bool track, compare, hideBoat;
            public HarborAttempt attempt, alternative;
            public HarborAttempt[] montage;
        }

        public static void Capture()
        {
            string directory = Environment.GetEnvironmentVariable("BEACON_FILM_DIR");
            string ffmpeg = Environment.GetEnvironmentVariable("BEACON_FFMPEG") ?? "ffmpeg";
            bool stills = Environment.GetEnvironmentVariable("BEACON_STILLS") == "1";
            Film film = JsonUtility.FromJson<Film>(File.ReadAllText(Path.Combine(directory, "shots.json")));
            EditorSceneManager.OpenScene("Assets/Harbor/Scenes/HarborPilot.unity");
            HarborDirector pilot = Object.FindFirstObjectByType<HarborDirector>();
            pilot.editorPreview.SetActive(false);
            Camera camera = pilot.shotCamera;
            camera.enabled = false;
            camera.allowHDR = false;
            QualitySettings.shadowDistance = 70;
            var stage = new HarborStage(null, new Course(Mission.Current));
            var target = new RenderTexture(film.width, film.height, 24) { antiAliasing = 2 };
            var image = new Texture2D(film.width, film.height, TextureFormat.RGB24, false);
            var pixels = new byte[film.width * film.height * 3];
            camera.targetTexture = target;
            camera.aspect = (float)film.width / film.height;
            RenderTexture previous = RenderTexture.active;
            using Process encoder = stills ? null : StartEncoder(ffmpeg, directory, film);
            try
            {
                float time = 0;
                foreach (Shot shot in film.shots)
                {
                    Debug.Log($"Beacon shot at {time:F1}s: {shot.name}");
                    Prepare(stage, shot);
                    int frameCount = Mathf.RoundToInt(shot.length * film.fps);
                    int montageIndex = 0;
                    float montageEnd = 0;
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        float local = (float)frame / film.fps;
                        if (shot.montage?.Length > 0)
                        {
                            while (montageIndex < shot.montage.Length)
                            {
                                HarborAttempt attempt = shot.montage[montageIndex];
                                float end = montageEnd + Duration(attempt);
                                if (local * 400 < end)
                                    break;
                                stage.DrawAttempt(attempt.frames, (Outcome)attempt.outcome);
                                montageEnd = end;
                                montageIndex++;
                            }
                        }
                        if (stills && frame != frameCount / 2)
                            continue;
                        SetFrame(stage, camera, shot, local, time + local);
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                        if (stills)
                            File.WriteAllBytes(Path.Combine(directory, shot.name + ".png"), image.EncodeToPNG());
                        else
                        {
                            image.GetRawTextureData<byte>().CopyTo(pixels);
                            encoder.StandardInput.BaseStream.Write(pixels, 0, pixels.Length);
                        }
                    }
                    time += (float)frameCount / film.fps;
                }
                if (encoder != null)
                {
                    encoder.StandardInput.Close();
                    encoder.WaitForExit();
                    if (encoder.ExitCode != 0)
                        throw new InvalidOperationException("FFmpeg exited with " + encoder.ExitCode);
                }
                Debug.Log($"Beacon capture complete ({time:F2}s).");
            }
            finally
            {
                if (encoder != null && !encoder.HasExited)
                    encoder.Kill();
                Shader.SetGlobalFloat("_HarborFilmTime", 0);
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
        }

        private static Process StartEncoder(string ffmpeg, string directory, Film film)
        {
            var info = new ProcessStartInfo(ffmpeg)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                Arguments = FormattableString.Invariant(
                    $"-hide_banner -loglevel warning -y -f rawvideo -pixel_format rgb24 -video_size {film.width}x{film.height} -framerate {film.fps} -i pipe:0 -an -vf vflip -c:v libx264 -preset ultrafast -crf 15 -pix_fmt yuv420p capture.mp4")
            };
            Process encoder = Process.Start(info);
            encoder.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                    Debug.LogWarning("FFmpeg: " + args.Data);
            };
            encoder.BeginErrorReadLine();
            return encoder;
        }

        private static void Prepare(HarborStage stage, Shot shot)
        {
            foreach (GameObject route in stage.Routes.Cast<Transform>().Select(route => route.gameObject).ToArray())
                Object.DestroyImmediate(route);
            stage.ClearWake();
            stage.Boat.gameObject.SetActive(!shot.hideBoat);
            stage.SetCurrent(shot.attempt.currentX);
            if (shot.compare)
                stage.DrawAttempt(shot.alternative.frames, (Outcome)shot.alternative.outcome);
            if (shot.track)
                stage.DrawAttempt(shot.attempt.frames, (Outcome)shot.attempt.outcome);
        }

        private static float Duration(HarborAttempt attempt) => attempt.frames[^1].tick * HarborEnvironment.Dt;

        private static HarborFrame Sample(HarborAttempt attempt, float time)
        {
            HarborFrame[] frames = attempt.frames;
            float tick = time / HarborEnvironment.Dt;
            int lower = 0;
            int upper = frames.Length - 1;
            while (upper - lower > 1)
            {
                int middle = (lower + upper) / 2;
                if (frames[middle].tick < tick)
                    lower = middle;
                else
                    upper = middle;
            }
            return HarborFrame.Between(frames[lower], frames[upper], tick);
        }

        private static void SetFrame(HarborStage stage, Camera camera, Shot shot, float local, float time)
        {
            float clock = shot.offset + local * shot.rate;
            HarborFrame frame = Sample(shot.attempt, clock);
            stage.SetPose(frame.x, frame.z, frame.yaw, frame.speed, time,
                shot.rate > 0 && clock <= Duration(shot.attempt) && !shot.hideBoat);
            Shader.SetGlobalFloat("_HarborFilmTime", time + 1);
            float fraction = local / shot.length;
            float angle = shot.angle + fraction * shot.sweep;
            Vector3 boat = new Vector3(frame.x, .30f, frame.z);
            Vector3 gate = new Vector3(0, .22f, 20);
            var rotation = Quaternion.Euler(0, angle, 0);
            camera.orthographic = false;
            switch (shot.camera)
            {
                case "onboard":
                    HarborDirector.SetOnboard(camera, stage.Boat);
                    break;
                case "plan":
                case "wide":
                    bool top = shot.camera == "plan";
                    camera.orthographic = true;
                    camera.orthographicSize = Mathf.Lerp(top ? 13.9f : 15.6f, top ? 13.4f : 14.3f, fraction);
                    var center = new Vector3(0, 0, 10.1f);
                    camera.transform.position = center + rotation * new Vector3(0, 44, top ? -3.6f : -23.6f);
                    camera.transform.LookAt(center);
                    break;
                case "chase":
                    camera.fieldOfView = 47;
                    Place(camera, boat + rotation * new Vector3(0, 3.7f, -8.7f),
                        boat + Vector3.Lerp(new Vector3(0, 0, 1.8f), gate - boat, .10f));
                    break;
                case "approach":
                    camera.fieldOfView = 42;
                    Place(camera, Vector3.Lerp(boat, gate, .42f) + rotation * new Vector3(0, 7.0f, -10.7f),
                        Vector3.Lerp(boat, gate, .43f));
                    break;
                case "gate":
                    camera.fieldOfView = 44;
                    Place(camera, gate + rotation * new Vector3(0, 4.0f, 10.0f),
                        Vector3.Lerp(gate, boat, .23f));
                    break;
                case "detail":
                    camera.fieldOfView = 40;
                    Place(camera, boat + rotation * new Vector3(0, 1.7f, -4.6f), boat + Vector3.up * .31f);
                    break;
                default:
                    throw new InvalidOperationException("Unknown camera " + shot.camera);
            }
        }

        private static void Place(Camera camera, Vector3 position, Vector3 aim)
        {
            camera.transform.position = position;
            camera.transform.LookAt(aim);
        }
    }
}
