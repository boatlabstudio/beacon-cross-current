using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HarborLearning
{
    // Presentation for the harbor replay. All navigational poses and commands come from its recorded run.
    internal sealed class HarborFilmSet
    {
        [Serializable]
        public sealed class Decision
        {
            public int tick;
            public float throttle, rudder;
            public Contribution[] paths;
        }

        [Serializable]
        public sealed class Contribution
        {
            public int input, unit, output;
            public float value;
        }

        private static readonly Color Ink = new(.047f, .090f, .118f);
        private static readonly Color PanelInk = new(.071f, .14f, .17f);
        private static readonly Color Linen = new(.96f, .93f, .85f);
        private static readonly Color Muted = new(.58f, .70f, .71f);
        private static readonly Color Mint = new(.39f, 1f, .77f);
        private static readonly Color Amber = new(1f, .69f, .27f);
        private static readonly Color Red = new(1f, .37f, .28f);
        private static readonly Color Haze = new(.59f, .70f, .75f);
        private static readonly Vector3 Sun = new Vector3(-.60f, .63f, .48f).normalized;
        private static readonly Vector3 ControlWall = new(8.2f, 3.45f, 42.45f);
        private static readonly Vector3 Scoreboard = new(-7.5f, 3.25f, 42.45f);

        private readonly HarborStage stage;
        private readonly Course course = new(Mission.Harbor);
        private readonly Transform[] eyes = new Transform[2];
        private readonly Transform[] brows = new Transform[2];
        private readonly Transform[] pupils = new Transform[2];
        private readonly TextMesh countLabel, countTitle, resultLabel, mapCount, mapTitle;
        private readonly TextMesh throttle, rudder, signalStatus;
        private readonly System.Collections.Generic.List<TextMesh> lettering = new();
        private readonly TextMesh[] gateNumbers = new TextMesh[3];
        private readonly LineRenderer berth;
        private readonly Vector3[] inputs = new Vector3[13];
        private readonly Vector3[] hidden = new Vector3[48];
        private readonly Vector3[] outputs = new Vector3[2];
        private readonly LineRenderer[] lines = new LineRenderer[12];
        private readonly Material lineInk = new(Resources.Load<Shader>("HarborInk"));
        private Decision displayed;
        private Transform coverFleet;

        public HarborFilmSet(HarborStage stage)
        {
            this.stage = stage;
            Light sun = RenderSettings.sun;
            sun.transform.rotation = Quaternion.LookRotation(-Sun);
            sun.color = new Color(1f, .90f, .76f);
            sun.intensity = 1.08f;
            sun.shadowStrength = .75f;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 86;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.74f, .83f, .91f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .57f, .63f);
            RenderSettings.ambientGroundColor = new Color(.26f, .34f, .44f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Haze;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .010f;
            RenderSettings.skybox = new Material(Resources.Load<Shader>("HarborFilmSky"));
            RenderSettings.skybox.SetVector("_SunDirection", Sun);

            var water = new Material(Resources.Load<Shader>("HarborFilmWater"));
            water.SetVector("_SunDirection", Sun);
            water.SetColor("_Haze", Haze);
            foreach (Renderer renderer in stage.Root.GetComponentsInChildren<Renderer>())
            {
                Material material = renderer.sharedMaterial;
                if (material.shader.name == "Harbor/Water") renderer.sharedMaterial = water;
                else if (renderer.name == "Berthing box" && renderer is LineRenderer marker)
                    berth = marker;
                else if (material.name == "Berth decking") material.color = new Color(.46f, .36f, .28f);
                else if (material.name == "Concrete") material.color = new Color(.64f, .67f, .65f);
            }

            Transform world = stage.Root.transform;
            BuildDock(world);
            BuildFace();
            BuildCoast(world, 117, 11, .75f, new Color(.56f, .65f, .68f));
            BuildCoast(world, 88, 6, 2.2f, new Color(.41f, .52f, .57f));
            Transform score = Board(world, "Main attempt scoreboard", Scoreboard, 7.7f, 3.55f);
            Label(score, "BOAT LAB   /   HARBOR CONTROL", new Vector3(0, 1.40f, -.09f), .065f, Muted);
            countTitle = Label(score, "ATTEMPT", new Vector3(0, .82f, -.09f), .092f, Mint);
            countLabel = Label(score, "0001", new Vector3(0, -.12f, -.09f), .325f, Linen);
            resultLabel = Label(score, "GATES   0 / 3", new Vector3(0, -1.24f, -.09f), .115f, Linen);
            Mount(world, Scoreboard, 3.15f);
            Transform mapBoard = Board(world, "Basin-side attempt board", new Vector3(-18.15f, 1.03f, 1.8f), 6.0f, 3.15f);
            mapBoard.localRotation = Quaternion.Euler(73, 0, 0);
            Solid(world, "Basin board pedestal", PrimitiveType.Cube, new Vector3(-17.48f, -.02f, 1.8f), new Vector3(2.4f, 1.4f, 2.6f), Paint(Ink, .31f));
            mapTitle = Label(mapBoard, "ATTEMPT", new Vector3(0, .82f, -.105f), .104f, Mint);
            mapCount = Label(mapBoard, "0001", new Vector3(0, -.27f, -.105f), .29f, Linen);
            (throttle, rudder, signalStatus) = BuildMonitor(world);

            for (int i = 0; i < course.Gates.Length; i++)
            {
                Gate gate = course.Gates[i];
                float x = gate.X + gate.Width * .5f + 1.14f;
                Transform sign = Board(world, "Gate number", new Vector3(x, .37f, gate.Z), 1.12f, .92f);
                sign.localRotation = Quaternion.Euler(61, 0, 0);
                gateNumbers[i] = Label(sign, $"{i + 1:00}", new Vector3(0, 0, -.088f), .119f, Linen);
            }
            lettering.AddRange(world.GetComponentsInChildren<TextMesh>());
        }

        public void Draw(Camera camera, string shot, string display, HarborAttempt attempt, HarborFrame frame,
            float clock, float time, float fraction, float angle, float sweep, Decision[] decisions, int routes, bool hideBoat)
        {
            Decision decision = Sample(decisions, Mathf.FloorToInt(clock / HarborEnvironment.Dt + .001f));
            bool done = clock >= attempt.frames[^1].tick * HarborEnvironment.Dt;
            Outcome outcome = done ? (Outcome)attempt.outcome : Outcome.Running;
            bool test = attempt.phase != "training";
            string title = display == "montage" ? "ROUTES LOGGED" : test ? "FIXED TEST" : "ATTEMPT";
            string count = (display == "montage" ? routes : test ? (attempt.id - 1) % 6 + 1 : attempt.id).ToString("0000");
            countTitle.text = mapTitle.text = title;
            countLabel.text = mapCount.text = count;
            resultLabel.text = display == "montage" ? "TRAINING  /  400×" : outcome switch
            {
                Outcome.Passed => "DOCKED",
                Outcome.Collision => "CONTACT",
                Outcome.MissedGate => "GATE MISSED",
                Outcome.Timeout => "TIME EXPIRED",
                _ => frame.gateIndex >= 3 ? "BERTH APPROACH" : $"GATES   {frame.gateIndex} / 3"
            };
            resultLabel.color = outcome switch { Outcome.Passed => Mint, Outcome.Running => Linen, _ => Red };
            countLabel.color = mapCount.color = outcome == Outcome.Passed ? Mint : Linen;
            for (int i = 0; i < gateNumbers.Length; i++)
                gateNumbers[i].color = i < frame.gateIndex && display != "montage" ? Mint : Linen;
            bool fleetCover = shot == "cover-fleet";
            bool cover = shot is "cover-dock" or "cover-map" || fleetCover;
            berth.startColor = berth.endColor = outcome == Outcome.Passed || cover ? Mint : new Color(.77f, .90f, .86f, .77f);
            berth.startWidth = berth.endWidth = cover ? .13f : .086f;

            ShowDecision(shot == "monitor" ? decision : null);
            float look = decision != null ? decision.rudder * .022f : Mathf.Sin(time * 1.15f) * .010f;
            float blinkTime = Mathf.Abs(Mathf.Repeat(time + .87f, 5.35f) - 2.675f);
            float blink = Mathf.SmoothStep(.17f, 1, Mathf.Clamp01(blinkTime / .084f));
            for (int i = 0; i < pupils.Length; i++)
            {
                eyes[i].localScale = new Vector3(1, shot == "cover" || cover ? 1 : blink, 1);
                Vector3 p = pupils[i].localPosition;
                p.x = look;
                pupils[i].localPosition = p;
                float expression = outcome == Outcome.Passed ? 6f : shot == "cover" ? -10f : -4f;
                brows[i].localRotation = Quaternion.Euler(0, 0, i == 0 ? expression - 2 : -expression + 1);
            }
            bool map = shot is "plan" or "map";
            stage.CurrentArrows.gameObject.SetActive(map && Mathf.Abs(attempt.currentX) >= .09f && display != "montage");
            Shader.SetGlobalFloat("_TrialMap", map ? .88f : 0);
            Shader.SetGlobalVector("_DockBoat", new Vector4(frame.x, frame.z, frame.yaw, hideBoat || done ? 0 : frame.speed));
            SetCamera(camera, shot, frame, fraction, angle + fraction * sweep);
            if (fleetCover)
            {
                coverFleet ??= BuildCoverFleet(frame.z);
                coverFleet.gameObject.SetActive(true);
            }
            else if (coverFleet != null) coverFleet.gameObject.SetActive(false);
            Vector3 viewpoint = camera.transform.position;
            foreach (TextMesh label in lettering)
                label.GetComponent<MeshRenderer>().enabled = Vector3.Dot(viewpoint - label.transform.position, label.transform.forward) < 0;
        }

        private void SetCamera(Camera camera, string shot, HarborFrame frame, float fraction, float angle)
        {
            camera.orthographic = false;
            var position = new Vector3(frame.x, .39f, frame.z);
            Quaternion orbit = Quaternion.Euler(0, angle, 0);
            Quaternion heading = Quaternion.Euler(0, frame.yaw * Mathf.Rad2Deg, 0);
            switch (shot)
            {
                case "onboard":
                    HarborDirector.SetOnboard(camera, stage.Boat);
                    camera.fieldOfView = 70;
                    camera.transform.position = stage.Boat.TransformPoint(new Vector3(0, .51f, 1.16f));
                    break;
                case "face":
                    Aim(camera, position + heading * orbit * new Vector3(-1.5f, 1.14f, 3.9f), position + new Vector3(0, .25f, 0), 34);
                    break;
                case "cover":
                    Aim(camera, position + heading * new Vector3(1.46f, 1.08f, 3.7f),
                        position + heading * new Vector3(1.02f, .22f, .22f), 39);
                    break;
                case "cover-dock":
                    Aim(camera, position + new Vector3(-2.3f, 3.35f, 5.0f),
                        position + new Vector3(.94f, -.06f, 2.14f), 44);
                    break;
                case "cover-map":
                    Aim(camera, position + new Vector3(5.4f, 5.85f, 6.5f),
                        position + new Vector3(-.45f, .12f, 2.06f), 38);
                    break;
                case "cover-fleet":
                    Aim(camera, position + orbit * new Vector3(-2.75f, 5.35f, 5.78f),
                        position + new Vector3(.55f, 0, .55f), 45);
                    break;
                case "waterline":
                    Aim(camera, position + orbit * new Vector3(-4.5f, .78f, -3.9f), position + new Vector3(.05f, .19f, 1.1f), 43);
                    break;
                case "gate":
                    Gate gate = course.Gates[Mathf.Clamp(Mathf.RoundToInt(angle), 0, 2)];
                    var center = new Vector3(gate.X, .18f, gate.Z);
                    Aim(camera, center + new Vector3(5.45f - fraction, 3.4f, 5.9f), Vector3.Lerp(center, position, .26f), 49);
                    break;
                case "follow":
                    Aim(camera, position + orbit * new Vector3(3.2f, 4.4f, -7.65f), position + new Vector3(0, 0, 2.7f), 46);
                    break;
                case "berth":
                    Aim(camera, position + orbit * new Vector3(5.05f, 4.5f, -5.65f), position + new Vector3(-.45f, 0, 2.30f), 43);
                    break;
                case "plan":
                case "map":
                    camera.orthographic = true;
                    camera.orthographicSize = 22.6f;
                    Vector3 mapCenter = new(-2.6f, 0, 18.45f);
                    Aim(camera, mapCenter + orbit * new Vector3(0, 51, shot == "plan" ? -1.5f : -15.6f), mapCenter, 43);
                    break;
                case "establish":
                    Aim(camera, new Vector3(-12.3f + fraction * 3.4f, 22, -12.5f + fraction * 4),
                        new Vector3(-.8f, .28f, 21), 53);
                    break;
                case "score":
                    Aim(camera, Scoreboard + new Vector3(-2.9f + fraction * 1.65f, -1.65f, -9.65f), Scoreboard, 34);
                    break;
                case "monitor":
                    Aim(camera, ControlWall + new Vector3(-1.8f + fraction * .73f, -.55f, -10.45f), ControlWall, 35);
                    break;
                default:
                    throw new InvalidOperationException("Unknown harbor film camera " + shot);
            }
        }

        private static void Aim(Camera camera, Vector3 from, Vector3 to, float fieldOfView)
        {
            camera.fieldOfView = fieldOfView;
            camera.transform.position = from;
            camera.transform.LookAt(to);
        }

        private static Decision Sample(Decision[] decisions, int tick)
        {
            if (decisions == null || decisions.Length == 0) return null;
            int lo = 0, hi = decisions.Length - 1;
            while (lo < hi)
            {
                int middle = (lo + hi + 1) / 2;
                if (decisions[middle].tick <= tick) lo = middle;
                else hi = middle - 1;
            }
            return decisions[lo];
        }

        private void ShowDecision(Decision decision)
        {
            if (ReferenceEquals(displayed, decision)) return;
            displayed = decision;
            signalStatus.text = decision == null ? "REPLAY FEED OFFLINE" : "RECORDED COMMANDS";
            throttle.text = decision == null ? "—" : decision.throttle.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);
            rudder.text = decision == null ? "—" : decision.rudder.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);
            int count = decision?.paths.Length ?? 0;
            for (int i = 0; i < lines.Length / 2; i++)
            {
                bool visible = i < count;
                lines[i * 2].enabled = lines[i * 2 + 1].enabled = visible;
                if (!visible) continue;
                Contribution path = decision.paths[i];
                Color color = path.value >= 0 ? Mint : Amber;
                color.a = .55f + Mathf.Clamp01(Mathf.Abs(path.value) * 3) * .42f;
                HeatLine(lines[i * 2], inputs[path.input], hidden[path.unit], color);
                HeatLine(lines[i * 2 + 1], hidden[path.unit], outputs[path.output], color);
            }
        }

        private static void HeatLine(LineRenderer line, Vector3 a, Vector3 b, Color color)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.startColor = line.endColor = color;
        }

        private (TextMesh, TextMesh, TextMesh) BuildMonitor(Transform world)
        {
            var walls = Paint(new Color(.24f, .32f, .34f), .30f);
            Solid(world, "Control room", PrimitiveType.Cube, ControlWall + new Vector3(0, -.75f, 1.05f), new Vector3(11.7f, 5.47f, 2.0f), walls);
            Solid(world, "Control roof", PrimitiveType.Cube, ControlWall + new Vector3(0, 2.74f, .64f), new Vector3(12.4f, .20f, 3.0f), Paint(Ink, .31f));
            Transform panel = Board(world, "Framed actor screen", ControlWall, 10.65f, 5.05f);
            Label(panel, "BEACON / POLICY MONITOR", new Vector3(-4.8f, 2.07f, -.09f), .065f, Linen, TextAnchor.MiddleLeft);
            var status = Label(panel, "REPLAY FEED OFFLINE", new Vector3(4.8f, 2.07f, -.09f), .038f, Mint, TextAnchor.MiddleRight);
            Label(panel, "13 OBSERVATIONS", new Vector3(-4.8f, 1.58f, -.09f), .046f, Muted, TextAnchor.MiddleLeft);
            Label(panel, "48 HIDDEN UNITS", new Vector3(-.13f, 1.58f, -.09f), .049f, Muted);
            Label(panel, "2 COMMANDS", new Vector3(4.8f, 1.58f, -.09f), .047f, Muted, TextAnchor.MiddleRight);
            string[] labels = { "Offset X", "Offset Z", "Target right", "Target ahead", "Heading sin", "Heading cos", "Water speed", "Yaw rate", "Cross-current", "Along-current", "Target width", "Basin position", "Dock mode" };
            var pins = Flat(new Color(.39f, .54f, .57f));
            for (int i = 0; i < inputs.Length; i++)
            {
                float y = 1.2f - i * .249f;
                Label(panel, labels[i], new Vector3(-4.75f, y, -.094f), .039f, Linen, TextAnchor.MiddleLeft);
                inputs[i] = new Vector3(-2.94f, y, -.143f);
                Solid(panel, "Observation node", PrimitiveType.Sphere, inputs[i], new Vector3(.093f, .093f, .016f), pins);
            }
            for (int i = 0; i < hidden.Length; i++)
            {
                hidden[i] = new Vector3(-1.36f + i % 8 * .345f, 1.05f - i / 8 * .49f, -.143f);
                Solid(panel, "Hidden node", PrimitiveType.Sphere, hidden[i], new Vector3(.079f, .079f, .016f), pins);
            }
            outputs[0] = new Vector3(2.75f, .7f, -.143f);
            outputs[1] = new Vector3(2.75f, -.83f, -.143f);
            foreach (Vector3 output in outputs)
                Solid(panel, "Command node", PrimitiveType.Sphere, output, new Vector3(.135f, .135f, .018f), pins);
            Label(panel, "THROTTLE", new Vector3(3.07f, .88f, -.095f), .056f, Linen, TextAnchor.MiddleLeft);
            Label(panel, "RUDDER", new Vector3(3.07f, -.63f, -.095f), .056f, Linen, TextAnchor.MiddleLeft);
            var throttleValue = Label(panel, "—", new Vector3(3.08f, .35f, -.095f), .109f, Mint, TextAnchor.MiddleLeft);
            var rudderValue = Label(panel, "—", new Vector3(3.08f, -1.16f, -.095f), .109f, Mint, TextAnchor.MiddleLeft);
            Label(panel, "TEAL  +", new Vector3(-4.73f, -2.17f, -.09f), .062f, Mint, TextAnchor.MiddleLeft);
            Label(panel, "AMBER  −", new Vector3(-2.87f, -2.17f, -.09f), .062f, Amber, TextAnchor.MiddleLeft);
            Label(panel, "STRONGEST CONTRIBUTIONS", new Vector3(4.73f, -2.17f, -.09f), .060f, Muted, TextAnchor.MiddleRight);
            for (int i = 0; i < lines.Length; i++)
            {
                var link = new GameObject("Recorded contribution").AddComponent<LineRenderer>();
                link.transform.SetParent(panel, false);
                link.useWorldSpace = false;
                link.sharedMaterial = lineInk;
                link.positionCount = 2;
                link.startWidth = link.endWidth = .031f;
                link.shadowCastingMode = ShadowCastingMode.Off;
                link.enabled = false;
                lines[i] = link;
            }
            return (throttleValue, rudderValue, status);
        }

        private void BuildFace()
        {
            var ivory = Paint(new Color(.93f, .918f, .86f), .43f);
            var iris = Paint(new Color(.079f, .14f, .15f), .55f);
            var blueBlack = Paint(new Color(.019f, .029f, .034f), .48f);
            var brow = Paint(new Color(.132f, .107f, .086f), .31f);
            var glint = Flat(new Color(.90f, .95f, .95f));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -.166f : .166f;
                eyes[i] = Group(stage.Boat, "Eye decal", new Vector3(x, .489f, .581f));
                Solid(eyes[i], "Eye white", PrimitiveType.Sphere, Vector3.zero, new Vector3(.234f, .159f, .031f), ivory);
                pupils[i] = Group(eyes[i], "Eye focus", new Vector3(0, -.005f, .015f));
                Solid(pupils[i], "Iris", PrimitiveType.Sphere, Vector3.zero, new Vector3(.105f, .111f, .014f), iris);
                Solid(pupils[i], "Pupil", PrimitiveType.Sphere, new Vector3(0, 0, .006f), new Vector3(.065f, .071f, .010f), blueBlack);
                Solid(pupils[i], "Catchlight", PrimitiveType.Sphere, new Vector3(-.018f, .021f, .011f), new Vector3(.016f, .016f, .005f), glint);
                brows[i] = Solid(stage.Boat, "Brow", PrimitiveType.Sphere, new Vector3(x, .589f + (i == 0 ? .007f : 0), .600f),
                    new Vector3(.204f, .035f, .034f), brow);
            }
        }

        private Transform BuildCoverFleet(float approachZ)
        {
            // The cover arranges independent training copies together to depict parallel practice.
            Transform fleet = Group(stage.Root.transform, "Parallel training boats (cover only)", Vector3.zero);
            var ochre = Paint(new Color(.88f, .54f, .18f), .38f);
            var light = Paint(new Color(.92f, .72f, .32f), .36f);
            float[][] rows =
            {
                new[] { -1.53f, .0f, 1.49f },
                new[] { -4.1f, -2.65f, -1.05f, .41f, 1.93f, 3.40f, 4.91f },
                new[] { -4.60f, -3.06f, -1.59f, -.10f, 1.43f, 2.96f, 4.48f },
                new[] { -4.10f, -2.49f, -.98f, .54f, 2.01f, 3.55f, 5.11f }
            };
            for (int row = 0; row < rows.Length; row++)
            for (int i = 0; i < rows[row].Length; i++)
            {
                float variation = Mathf.Sin(i * 3.6f + row * 2.5f);
                Transform boat = Object.Instantiate(stage.Boat, fleet);
                boat.name = "Parallel practice boat";
                boat.localPosition = new Vector3(rows[row][i] + .12f * variation, .17f,
                    approachZ - 2.75f - row * 1.68f - .28f * variation);
                boat.localRotation = Quaternion.Euler(0, 7 + 18 * variation, 0);
                boat.localScale = Vector3.one * (.66f + .035f * variation);
                foreach (Renderer renderer in boat.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.GetComponent<TextMesh>()) renderer.enabled = false;
                    else if (renderer.sharedMaterial.name == "Hull gold")
                        renderer.sharedMaterial = (i + row) % 5 == 0 ? light : ochre;
                }
            }
            return fleet;
        }

        private static void BuildDock(Transform world)
        {
            var pilings = Paint(new Color(.26f, .34f, .37f), .45f);
            var bumper = Paint(new Color(.067f, .10f, .12f), .27f);
            var amber = Paint(new Color(.95f, .63f, .19f), .34f);
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < 4; i++)
                {
                    float x = side * 4.19f;
                    float z = 29.71f + i * 2.48f;
                    Solid(world, "Mooring pile", PrimitiveType.Cylinder, new Vector3(x, .55f, z), new Vector3(.25f, .78f, .25f), pilings);
                    Solid(world, "Piling cap", PrimitiveType.Cylinder, new Vector3(x, 1.345f, z), new Vector3(.30f, .046f, .30f), amber);
                    Solid(world, "Fender block", PrimitiveType.Cube, new Vector3(side * 3.11f, .22f, z + .47f), new Vector3(.21f, .45f, .61f), bumper);
                }
                Transform board = Board(world, side < 0 ? "Boat Lab sign" : "Sponsorship sign", new Vector3(side * 3.86f, 1.03f, 28.91f), 1.51f, .62f);
                Solid(board, "Sign stand", PrimitiveType.Cube, new Vector3(0, -.38f, 0), new Vector3(.14f, .25f, .1f), pilings);
                if (side < 0)
                {
                    var logo = new Material(Shader.Find("Unlit/Texture")) { mainTexture = Resources.Load<Texture2D>("HarborFilmBrand") };
                    logo.mainTextureScale = new Vector2(.70f, .70f);
                    logo.mainTextureOffset = new Vector2(.15f, .15f);
                    Solid(board, "Boat Lab logo", PrimitiveType.Quad, new Vector3(-.45f, 0, -.094f), new Vector3(.47f, .47f, 1), logo);
                    Label(board, "CHART\nWHAT’S NEXT", new Vector3(.22f, 0, -.099f), .026f, Linen);
                }
                else Label(board, "YOUR BRAND\nHERE", new Vector3(0, 0, -.099f), .035f, Linen);
            }
        }

        private static void BuildCoast(Transform parent, float z, float elevation, float offset, Color paint)
        {
            const int points = 66;
            var vertices = new Vector3[points * 2];
            var triangles = new int[(points - 1) * 6];
            for (int i = 0; i < points; i++)
            {
                float x = -164 + 5 * i;
                float ridge = Mathf.Pow(.5f + .5f * Mathf.Sin(x * .04f + offset), 1.45f);
                float y = elevation * (.20f + .63f * ridge + .14f * Mathf.Sin(x * .11f + offset));
                vertices[2 * i] = new Vector3(x, -2.2f, z);
                vertices[2 * i + 1] = new Vector3(x, y, z + 2f * Mathf.Sin(x * .048f));
                if (i == points - 1) continue;
                int a = 2 * i, start = 6 * i;
                triangles[start] = a;
                triangles[start + 1] = a + 1;
                triangles[start + 2] = a + 2;
                triangles[start + 3] = a + 2;
                triangles[start + 4] = a + 1;
                triangles[start + 5] = a + 3;
            }
            Transform coast = Group(parent, "Distant coastline", Vector3.zero);
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            coast.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = coast.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Flat(paint);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Transform Board(Transform parent, string name, Vector3 position, float width, float height)
        {
            Transform panel = Group(parent, name, position);
            Solid(panel, "Metal frame", PrimitiveType.Cube, Vector3.zero, new Vector3(width, height, .13f), Paint(Ink, .31f));
            float frame = Mathf.Min(.14f, height * .11f);
            Solid(panel, "Display face", PrimitiveType.Cube, new Vector3(0, 0, -.070f),
                new Vector3(width - frame, height - frame, .018f), Flat(PanelInk));
            return panel;
        }

        private static void Mount(Transform parent, Vector3 panel, float offset)
        {
            var paint = Paint(Ink, .28f);
            for (int side = -1; side <= 1; side += 2)
                Solid(parent, "Scoreboard mount", PrimitiveType.Cube, new Vector3(panel.x + side * offset, 1.39f, panel.z + .18f), new Vector3(.24f, 2.30f, .33f), paint);
        }

        private static TextMesh Label(Transform parent, string value, Vector3 position, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var label = Group(parent, "Lettering", position).gameObject.AddComponent<TextMesh>();
            label.fontSize = 58;
            label.characterSize = size;
            label.fontStyle = FontStyle.Bold;
            label.anchor = anchor;
            label.alignment = anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Center;
            label.color = color;
            label.text = value;
            return label;
        }

        private static Material Paint(Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static Material Flat(Color color) => new(Shader.Find("Unlit/Color")) { color = color };

        private static Transform Solid(Transform parent, string name, PrimitiveType kind, Vector3 position, Vector3 size, Material material)
        {
            GameObject solid = GameObject.CreatePrimitive(kind);
            solid.name = name;
            solid.transform.SetParent(parent, false);
            solid.transform.localPosition = position;
            solid.transform.localScale = size;
            solid.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(solid.GetComponent<Collider>());
            return solid.transform;
        }

        private static Transform Group(Transform parent, string name, Vector3 position)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = position;
            return group;
        }
    }
}
