using System.Collections.Generic;
using UnityEngine;

namespace HarborLearning
{
    [RequireComponent(typeof(HarborOverlay))]
    public sealed class HarborDirector : MonoBehaviour
    {
        public enum Shot { Auto, Overview, Follow, Onboard }
        public Camera shotCamera;
        public GameObject editorPreview;

        public Mission Mission { get; private set; } = Mission.Current;
        public Shot CameraShot { get; private set; }
        public Shot ActiveShot { get; private set; } = Shot.Overview;
        public bool Paused { get; private set; }
        public bool FilmMode { get; private set; }
        public bool ShowRoutes { get; private set; } = true;
        public bool Manual { get; private set; }
        public float PlaybackRate { get; private set; } = 2f;
        public int AttemptNumber => Manual ? manualNumber : clipIndex + 1;
        public int TotalAttempts => replay?.attempts?.Length ?? 0;
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        public HarborFrame Pose { get; private set; }
        public HarborAttempt Attempt => TotalAttempts > 0 ? replay.attempts[clipIndex] : null;
        public string Source => Manual ? "manual" : replay?.source ?? "";
        public string Caption { get; private set; }
        public bool ReelComplete { get; private set; }
        public float Current => Manual ? manualEnv.CurrentX : Attempt?.currentX ?? 0;
        public Outcome Result => Manual ? manualEnv.Result : outcomeVisible ? (Outcome)Attempt.outcome : Outcome.Running;
        public float Progress => Manual || Attempt == null ? 0 : Mathf.Clamp01(replayTime /
            Mathf.Max(.01f, Attempt.frames[^1].tick * HarborEnvironment.Dt));

        private HarborStage stage;
        private HarborReplay replay;
        private int clipIndex, frameIndex, manualSeed, manualNumber;
        private float replayTime, holdTime, frameAccumulator, clock;
        private bool outcomeVisible, forceCamera;
        private HarborEnvironment manualEnv;
        private readonly List<HarborFrame> manualFrames = new();

        private void OnEnable()
        {
            if (!Application.isPlaying || stage != null)
                return;
            foreach (Transform child in transform)
            {
                if (child.name != "Training basin")
                    continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            if (editorPreview)
            {
                editorPreview.SetActive(false);
                Destroy(editorPreview);
            }
            Application.targetFrameRate = 60;
            LoadMission(Mission);
            Debug.Log($"Beacon 01 running. {TotalAttempts} {Source} replays loaded. Onboard camera ready.");
        }

        public void LoadMission(Mission mission)
        {
            stage?.Dispose();
            Mission = mission;
            stage = new HarborStage(transform, new Course(mission));
            stage.Routes.gameObject.SetActive(ShowRoutes);
            replay = HarborReplay.Load(mission);
            Manual = false;
            manualNumber = 0;
            Restart();
        }

        public void Restart()
        {
            stage.ClearRoutes();
            stage.ClearWake();
            Passed = Failed = clipIndex = 0;
            ReelComplete = false;
            Paused = false;
            if (Manual || TotalAttempts == 0)
            {
                Manual = true;
                manualNumber = 0;
                manualSeed = 2100;
                manualEnv = new HarborEnvironment(Mission);
                StartManual();
                return;
            }
            BeginClip();
        }

        public void TogglePause()
        {
            if (ReelComplete)
                Restart();
            else
                Paused = !Paused;
        }

        public void Next()
        {
            if (Manual)
            {
                StartManual();
                return;
            }
            if (ReelComplete)
            {
                Restart();
                return;
            }
            if (clipIndex + 1 == TotalAttempts)
            {
                ReelComplete = true;
                Caption = "Same course. A very different route.";
                return;
            }
            clipIndex++;
            BeginClip();
        }

        public void CycleCamera()
        {
            CameraShot = (Shot)(((int)CameraShot + 1) % 4);
            forceCamera = true;
        }

        public void CycleSpeed() => PlaybackRate = PlaybackRate == 1 ? 2 : PlaybackRate == 2 ? 4 : 1;
        public void ToggleFilm() => FilmMode = !FilmMode;

        public void ToggleRoutes()
        {
            ShowRoutes = !ShowRoutes;
            stage.Routes.gameObject.SetActive(ShowRoutes);
        }

        public void ToggleManual()
        {
            Manual = !Manual;
            Restart();
        }

        private void Update()
        {
            ReadKeys();
            float dt = Time.unscaledDeltaTime;
            if (!Paused)
            {
                clock += dt;
                if (Manual)
                    UpdateManual(dt);
                else if (!ReelComplete)
                    UpdateReplay(dt);
            }
            stage.SetPose(Pose.x, Pose.z, Pose.yaw, Pose.speed, clock, !Paused && Result == Outcome.Running);
            UpdateCamera(dt);
        }

        private void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) LoadMission(Mission.Gate);
            if (Input.GetKeyDown(KeyCode.Alpha2)) LoadMission(Mission.Current);
            if (Input.GetKeyDown(KeyCode.Alpha3)) LoadMission(Mission.Slalom);
            if (Input.GetKeyDown(KeyCode.Alpha4)) LoadMission(Mission.Dock);
            if (Input.GetKeyDown(KeyCode.Alpha5)) LoadMission(Mission.Harbor);
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.Tab)) CycleCamera();
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (Input.GetKeyDown(KeyCode.N)) Next();
            if (Input.GetKeyDown(KeyCode.T)) ToggleRoutes();
            if (Input.GetKeyDown(KeyCode.H)) ToggleFilm();
            if (Input.GetKeyDown(KeyCode.M)) ToggleManual();
            if (Input.GetKeyDown(KeyCode.F)) CycleSpeed();
        }

        private void BeginClip()
        {
            frameIndex = 0;
            replayTime = holdTime = 0;
            outcomeVisible = false;
            forceCamera = true;
            Pose = Attempt.frames[0];
            stage.SetCurrent(Attempt.currentX);
            stage.ClearWake();
            Caption = null;
        }

        private void UpdateReplay(float dt)
        {
            HarborAttempt attempt = Attempt;
            HarborFrame[] frames = attempt.frames;
            if (outcomeVisible)
            {
                holdTime += dt;
                if (holdTime >= 2.05f)
                    Next();
                return;
            }
            // The decisive approach plays at 1×, even when the montage is accelerated.
            float endTime = frames[^1].tick * HarborEnvironment.Dt;
            float remaining = endTime - replayTime;
            float rate = remaining < 1.6f || IsOnboardWindow(remaining) ? 1 : PlaybackRate;
            if (remaining >= 1.6f && attempt.phase == "early")
                rate *= 2;
            replayTime += dt * rate;
            float tick = replayTime / HarborEnvironment.Dt;
            while (frameIndex + 1 < frames.Length && frames[frameIndex + 1].tick <= tick)
                frameIndex++;
            Pose = frameIndex + 1 < frames.Length ? HarborFrame.Between(frames[frameIndex], frames[frameIndex + 1], tick) :
                frames[^1];

            if (replayTime < 1.75f && clipIndex == 0)
                Caption = Mission == Mission.Current ? "Two buoys. One sideways current." : "All right, Beacon. Your turn.";
            else if (clipIndex % 3 == 1 && endTime - replayTime < 1.65f && (Outcome)attempt.outcome == Outcome.MissedGate)
                Caption = "Beacon… the gate is over there.";
            else
                Caption = null;
            if (frameIndex + 1 < frames.Length)
                return;
            Outcome result = (Outcome)attempt.outcome;
            RecordRoute(frames, result);
            outcomeVisible = true;
            Caption = Reaction(result);
        }

        private void StartManual()
        {
            manualEnv.Reset(manualSeed++);
            manualFrames.Clear();
            Pose = HarborFrame.From(manualEnv);
            manualFrames.Add(Pose);
            manualNumber++;
            frameAccumulator = 0;
            forceCamera = true;
            stage.ClearWake();
            stage.SetCurrent(manualEnv.CurrentX);
            Caption = "Use WASD or the arrow keys to steer.";
        }

        private void UpdateManual(float dt)
        {
            if (manualEnv.Result != Outcome.Running)
                return;
            float throttle = (Held(KeyCode.W, KeyCode.UpArrow) ? 1 : 0) - (Held(KeyCode.S, KeyCode.DownArrow) ? 1 : 0);
            float rudder = (Held(KeyCode.D, KeyCode.RightArrow) ? 1 : 0) - (Held(KeyCode.A, KeyCode.LeftArrow) ? 1 : 0);
            if (throttle != 0 || rudder != 0)
                Caption = null;
            frameAccumulator += Mathf.Min(dt, .25f);
            while (frameAccumulator >= HarborEnvironment.Dt && manualEnv.Result == Outcome.Running)
            {
                frameAccumulator -= HarborEnvironment.Dt;
                manualEnv.Step(throttle, rudder);
                Pose = HarborFrame.From(manualEnv);
                if (manualEnv.Tick % HarborEnvironment.ActionRepeat == 0 || manualEnv.Result != Outcome.Running)
                    manualFrames.Add(Pose);
            }
            if (manualEnv.Result != Outcome.Running)
            {
                RecordRoute(manualFrames.ToArray(), manualEnv.Result);
                Caption = Reaction(manualEnv.Result) + "  [N] Try again";
            }
        }

        private void RecordRoute(HarborFrame[] frames, Outcome outcome)
        {
            bool passed = outcome == Outcome.Passed;
            if (passed) Passed++; else Failed++;
            stage.DrawAttempt(frames, outcome);
        }

        private void UpdateCamera(float dt)
        {
            Shot requested = CameraShot;
            if (requested == Shot.Auto)
            {
                requested = Shot.Overview;
                if (!Manual && !ReelComplete && !outcomeVisible)
                {
                    float remaining = Attempt.frames[^1].tick * HarborEnvironment.Dt - replayTime;
                    if (IsOnboardWindow(remaining))
                        requested = Shot.Onboard;
                    else if (clipIndex % 3 == 1)
                        requested = Shot.Follow;
                }
            }
            if (requested != ActiveShot)
                forceCamera = true;
            ActiveShot = requested;
            if (requested == Shot.Overview)
            {
                SetOverview(shotCamera, Mission);
                return;
            }
            if (requested == Shot.Onboard)
            {
                SetOnboard(shotCamera, stage.Boat);
                return;
            }
            shotCamera.orthographic = false;
            shotCamera.fieldOfView = 37;
            Vector3 boat = new Vector3(Pose.x, .22f, Pose.z);
            Vector3 desiredPosition = boat + new Vector3(5.4f, 2.7f, -6.7f);
            Vector3 target = boat + new Vector3(0, .18f, 2.0f);
            float weight = forceCamera ? 1 : 1 - Mathf.Exp(-dt * 5.0f);
            shotCamera.transform.position = Vector3.Lerp(shotCamera.transform.position, desiredPosition, weight);
            shotCamera.transform.rotation = Quaternion.Slerp(shotCamera.transform.rotation,
                Quaternion.LookRotation(target - shotCamera.transform.position), weight);
            forceCamera = false;
        }

        private bool IsOnboardWindow(float remaining) => CameraShot == Shot.Auto &&
            clipIndex % 4 == 2 && Attempt.phase != "early" && remaining is >= 1.15f and <= 3.25f;

        public static void SetOnboard(Camera camera, Transform boat)
        {
            camera.orthographic = false;
            camera.fieldOfView = 66;
            Vector3 position = boat.TransformPoint(new Vector3(0, .31f, 1.12f));
            camera.transform.SetPositionAndRotation(position, boat.rotation * Quaternion.Euler(3, 0, 0));
        }

        public static void SetOverview(Camera camera, Mission mission)
        {
            bool full = mission is Mission.Slalom or Mission.Dock or Mission.Harbor;
            float center = full ? 17.6f : mission == Mission.Current ? 10.4f : 8.5f;
            camera.orthographic = true;
            camera.orthographicSize = full ? 22f : 14.1f;
            camera.transform.position = new Vector3(7.0f, 41, center - 20f);
            camera.transform.LookAt(new Vector3(0, 0, center));
        }

        public static Color ResultColor(Outcome result) => result switch
        {
            Outcome.Passed => HarborStage.Mint,
            Outcome.Collision => HarborStage.Coral,
            Outcome.Timeout => HarborStage.Amber,
            Outcome.MissedGate => new Color(.99f, .54f, .25f),
            _ => HarborStage.Cyan
        };

        private string Reaction(Outcome result) => result switch
        {
            Outcome.Passed => Mission is Mission.Dock or Mission.Harbor ? "Easy… and we're docked." : "There it is. Clean pass!",
            Outcome.Collision => "Whoa! A little too close.",
            Outcome.MissedGate => "Come on, Beacon. You missed the gate.",
            Outcome.Timeout => "Any time now, Beacon.",
            _ => null
        };

        private static bool Held(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b);
    }
}
