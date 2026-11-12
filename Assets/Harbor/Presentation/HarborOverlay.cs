using UnityEngine;

namespace HarborLearning
{
    public sealed class HarborOverlay : MonoBehaviour
    {
        public static readonly string[] MissionNames = { "Buoy gate", "Cross-current", "Buoy slalom", "Precision docking" };
        private static readonly Color Panel = new Color(.028f, .070f, .080f, .93f);
        private static readonly Color Soft = new Color(.66f, .79f, .79f);
        private HarborDirector director;
        private GUIStyle title, heading, value, small, subtitle, button;

        private void OnEnable() => director = GetComponent<HarborDirector>();

        private void OnGUI()
        {
            BuildStyles();
            float scale = Mathf.Max(.2f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            var originalMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            DrawTitle();
            DrawStatus(width);
            if (director.ActiveShot == HarborDirector.Shot.Onboard)
            {
                Fill(new Rect(24, 142, 152, 27), Panel);
                Fill(new Rect(35, 152, 7, 7), HarborStage.Coral);
                Label(new Rect(49, 143, 121, 25), "ONBOARD CAM", small, Color.white);
            }
            DrawSubtitle(width, height);
            if (!director.FilmMode)
                DrawControls(width, height);
            GUI.matrix = originalMatrix;
        }

        private void DrawTitle()
        {
            Fill(new Rect(24, 23, 308, 111), Panel);
            Fill(new Rect(24, 23, 4, 111), HarborStage.Amber);
            Label(new Rect(42, 33, 260, 20), "BEACON 01   /   HARBOR TRIALS", small, HarborStage.Amber);
            Label(new Rect(40, 53, 286, 42), MissionNames[(int)director.Mission], title, Color.white);
            string source = director.Source switch
            {
                "ppo" => "PPO  /  Recorded test runs",
                "manual" => "Manual control  /  Simulation",
                _ => "Controller demo  /  Simulation"
            };
            Label(new Rect(42, 100, 274, 21), source, small, Soft);
        }

        private void DrawStatus(float width)
        {
            float x = width - 249;
            Fill(new Rect(x, 23, 225, 111), Panel);
            Label(new Rect(x + 15, 31, 190, 20), director.Manual ? "MANUAL CONTROL" : "ATTEMPT", small, Soft);
            string counter = director.Manual ? director.AttemptNumber.ToString("D2") :
                director.AttemptNumber.ToString("D2") + " / " + director.TotalAttempts.ToString("D2");
            Label(new Rect(x + 15, 51, 190, 35), counter, value, Color.white);
            string status = director.ReelComplete ? "Replay complete" : director.Paused ? "Paused" :
                director.Result switch
                {
                    Outcome.Passed => director.Mission == Mission.Dock ? "Docked" : "Clean pass",
                    Outcome.Collision => "Collision",
                    Outcome.MissedGate => "Missed gate",
                    Outcome.Timeout => "Time ran out",
                    _ => Mathf.Abs(director.Current) > .18f
                        ? (director.Current > 0 ? "Current →  " : "Current ←  ") + Mathf.Abs(director.Current).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m/s"
                        : director.Pose.speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m/s"
                };
            Label(new Rect(x + 15, 94, 205, 23), status, heading, HarborDirector.ResultColor(director.Result));
            Fill(new Rect(x, 131, 225, 3), new Color(.28f, .39f, .39f));
            Fill(new Rect(x, 131, 225 * director.Progress, 3), HarborDirector.ResultColor(director.Result));
            if (director.Source == "ppo" && director.Attempt != null)
            {
                Fill(new Rect(x, 141, 225, 28), Panel);
                Label(new Rect(x + 13, 142, 202, 26),
                    director.Attempt.trainingSteps.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US")) +
                    " training steps", small, Soft);
            }
        }

        private void DrawSubtitle(float width, float height)
        {
            if (string.IsNullOrEmpty(director.Caption))
                return;
            float subtitleWidth = Mathf.Min(width - 48, subtitle.CalcSize(new GUIContent(director.Caption)).x + 58);
            float y = height - (director.FilmMode ? 84 : 133);
            var rect = new Rect((width - subtitleWidth) / 2, y, subtitleWidth, 51);
            Fill(rect, new Color(.02f, .034f, .042f, .91f));
            Fill(new Rect(rect.x, rect.y, 4, rect.height), HarborDirector.ResultColor(director.Result));
            Label(rect, director.Caption, subtitle, Color.white);
        }

        private void DrawControls(float width, float height)
        {
            const float total = 1094;
            float x = (width - total) / 2;
            float y = height - 66;
            Fill(new Rect(x, y, total, 44), Panel);
            float left = x + 8;
            for (int i = 0; i < MissionNames.Length; i++)
            {
                if (Button(new Rect(left, y + 7, 130, 30), (i + 1) + "  " + MissionNames[i], (int)director.Mission == i))
                    director.LoadMission((Mission)i);
                left += 134;
            }
            if (Button(new Rect(left + 6, y + 7, 90, 30), director.Paused || director.ReelComplete ? "Play" : "Pause"))
                director.TogglePause();
            if (Button(new Rect(left + 102, y + 7, 72, 30), "Next"))
                director.Next();
            if (Button(new Rect(left + 180, y + 7, 58, 30), director.PlaybackRate.ToString("0") + "×"))
                director.CycleSpeed();
            if (Button(new Rect(left + 244, y + 7, 87, 30), "Routes", director.ShowRoutes))
                director.ToggleRoutes();
            if (Button(new Rect(left + 337, y + 7, 93, 30), director.Manual ? "Replays" : "Drive"))
                director.ToggleManual();
            if (Button(new Rect(left + 436, y + 7, 104, 30), "Film mode"))
                director.ToggleFilm();
            string hint = "TAB  Camera: " + director.CameraShot + "    H  Film    R  Restart     ·     " +
                director.Passed + " passed  /  " + director.Failed + " failed";
            Label(new Rect(x, y + 44, total, 20), hint, small, Soft);
        }

        private bool Button(Rect rect, string label, bool selected = false)
        {
            Fill(rect, selected ? new Color(.12f, .29f, .27f, 1) : new Color(.075f, .14f, .15f, 1));
            button.normal.textColor = selected ? HarborStage.Mint : Color.white;
            return GUI.Button(rect, label, button);
        }

        private void BuildStyles()
        {
            if (title != null)
                return;
            Font font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Arial" }, 32);
            title = Style(font, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
            heading = Style(font, 17, FontStyle.Bold, TextAnchor.MiddleLeft);
            value = Style(font, 32, FontStyle.Bold, TextAnchor.MiddleLeft);
            small = Style(font, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            subtitle = Style(font, 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            button = Style(font, 14, FontStyle.Bold, TextAnchor.MiddleCenter);
            button.hover.textColor = HarborStage.Amber;
            button.active.textColor = HarborStage.Cyan;
        }

        private static GUIStyle Style(Font font, int size, FontStyle weight, TextAnchor anchor) => new GUIStyle
        {
            font = font,
            fontSize = size,
            fontStyle = weight,
            alignment = anchor,
            clipping = TextClipping.Clip
        };

        private static void Label(Rect rect, string content, GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            GUI.Label(rect, content, style);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
