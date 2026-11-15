using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarborLearning
{
    public sealed class HarborStage
    {
        public static readonly Color Mint = new Color(.39f, 1f, .70f);
        public static readonly Color Coral = new Color(1f, .35f, .24f);
        public static readonly Color Amber = new Color(1f, .78f, .28f);
        public static readonly Color Cyan = new Color(.37f, .85f, 1f);

        public Transform Boat { get; }
        public Transform CurrentArrows { get; }
        public Transform Routes { get; }
        public GameObject Root { get; }

        private readonly Dictionary<string, Material> palette = new();
        private readonly Material ink;
        private readonly LineRenderer[] wakes = new LineRenderer[2];
        private readonly List<Vector3>[] wakePoints = { new(), new() };
        private readonly Transform contact;
        private readonly List<Transform> buoyFloats = new();

        public HarborStage(Transform parent, Course course)
        {
            Root = new GameObject("Training basin");
            Root.transform.SetParent(parent, false);
            ink = new Material(Resources.Load<Shader>("HarborInk")) { name = "Water marks and route ink" };
            var tank = Group(Root.transform, "Basin and docks");
            BuildTank(tank);
            BuildCourse(tank, course);
            CurrentArrows = Group(tank, "Current indicator");
            for (int z = 4; z < course.FinishZ; z += 4)
            {
                for (int x = -8; x <= 8; x += 8)
                {
                    var arrow = Group(CurrentArrows, "Current arrow");
                    arrow.localPosition = new Vector3(x, .029f, z);
                    Polyline(arrow, "Shaft", new[] { new Vector3(-.63f, 0, 0), new Vector3(.60f, 0, 0) },
                        .052f, new Color(.57f, .93f, .97f, .28f), false);
                    Polyline(arrow, "Head", new[] { new Vector3(.28f, 0, .23f), new Vector3(.60f, 0, 0),
                        new Vector3(.28f, 0, -.23f) }, .052f, new Color(.57f, .93f, .97f, .28f), false);
                }
            }
            CurrentArrows.gameObject.SetActive(false);
            Routes = Group(Root.transform, "Recorded trajectories");
            contact = Group(Root.transform, "Hull shadow");
            PaintShadow(contact);
            Boat = BuildBoat(Root.transform);
            for (int i = 0; i < wakes.Length; i++)
            {
                wakes[i] = Polyline(Root.transform, "Foam trail", System.Array.Empty<Vector3>(), .15f,
                    new Color(.76f, .95f, .94f, .48f));
            }
        }

        public void SetCurrent(float x)
        {
            CurrentArrows.gameObject.SetActive(Mathf.Abs(x) >= .18f);
            // Rotate arrows in place to keep their positions fixed in the basin.
            foreach (Transform arrow in CurrentArrows)
                arrow.localRotation = Quaternion.Euler(0, x < 0 ? 180 : 0, 0);
        }

        public void SetPose(float x, float z, float yaw, float speed, float time, bool foam)
        {
            float bob = Mathf.Sin(time * 3.2f + z * .35f) * .028f;
            Boat.position = new Vector3(x, .19f + bob, z);
            Boat.rotation = Quaternion.Euler(-speed * .45f + Mathf.Cos(time * 2.7f) * .85f,
                yaw * Mathf.Rad2Deg, Mathf.Sin(time * 2.2f + x) * 1.35f);
            contact.SetPositionAndRotation(new Vector3(x, .012f, z), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0));
            for (int i = 0; i < buoyFloats.Count; i++)
            {
                Transform buoy = buoyFloats[i];
                Vector3 position = buoy.localPosition;
                position.y = Mathf.Sin(time * 2.45f + i * .8f) * .045f;
                buoy.localPosition = position;
                buoy.localRotation = Quaternion.Euler(Mathf.Sin(time * 2.1f + i) * 1.8f, 0, 0);
            }
            if (foam && speed > .30f)
                UpdateWake();
        }

        public void ClearWake()
        {
            for (int i = 0; i < wakes.Length; i++)
            {
                wakePoints[i].Clear();
                wakes[i].positionCount = 0;
            }
        }

        public void DrawAttempt(HarborFrame[] frames, Outcome outcome)
        {
            bool passed = outcome == Outcome.Passed;
            Color color = HarborDirector.ResultColor(outcome);
            var route = new Vector3[frames.Length];
            for (int i = 0; i < route.Length; i++)
                route[i] = frames[i].RoutePoint;
            Mark(route[^1], color, passed);
            color.a = passed ? .87f : .43f;
            Polyline(Routes, "Attempt route", route, passed ? .139f : .076f, color);
        }

        private void Mark(Vector3 point, Color color, bool crossed)
        {
            point.y = .072f;
            const int sides = 28;
            Vector3[] ring = new Vector3[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                ring[i] = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .52f;
            }
            Polyline(Routes, "Outcome marker", ring, .067f, color);
            if (crossed)
            {
                Polyline(Routes, "Pass tick", new[] { point + new Vector3(-.26f, 0, .01f),
                    point + new Vector3(-.07f, 0, -.21f), point + new Vector3(.27f, 0, .22f) }, .079f, color);
                return;
            }
            Polyline(Routes, "Failure cross", new[] { point + new Vector3(-.20f, 0, -.20f),
                point + new Vector3(.20f, 0, .20f) }, .073f, color);
            Polyline(Routes, "Failure cross", new[] { point + new Vector3(-.20f, 0, .20f),
                point + new Vector3(.20f, 0, -.20f) }, .073f, color);
        }

        public void ClearRoutes()
        {
            foreach (Transform route in Routes)
            {
                route.gameObject.SetActive(false);
                Object.Destroy(route.gameObject);
            }
        }

        public void Dispose()
        {
            foreach (Material material in palette.Values)
                Object.Destroy(material);
            Object.Destroy(ink);
            foreach (MeshFilter filter in Root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh && filter.sharedMesh.name.StartsWith("Harbor procedural "))
                    Object.Destroy(filter.sharedMesh);
            }
            Root.SetActive(false);
            Object.Destroy(Root);
        }

        private void BuildTank(Transform tank)
        {
            var concrete = Material("Concrete", new Color(.57f, .61f, .57f), .06f, .26f);
            var border = Material("Dark cladding", new Color(.085f, .16f, .18f), .24f, .40f);
            var trim = Material("Rubber trim", new Color(.12f, .23f, .24f), 0, .3f);
            var stripe = Material("Safety amber", new Color(.92f, .63f, .16f), 0, .36f);
            var white = Material("Deck markings", new Color(.83f, .91f, .86f), 0, .21f);

            Box(tank, "Outer basin plinth", new Vector3(0, -1.02f, 18.5f), new Vector3(36.6f, 1.3f, 49.6f), border);
            Box(tank, "Perimeter foundation", new Vector3(0, -.47f, 18.5f), new Vector3(36.1f, .40f, 49.1f), trim);
            Surface(tank, "Water surface", new Vector3(0, 0, 18.5f), 32, 45.7f, true);
            Surface(tank, "Water outside basin", new Vector3(0, -1.38f, 18), 300, 300, false);

            foreach (float x in new[] { -16.66f, 16.66f })
            {
                Box(tank, "Concrete quay", new Vector3(x, .17f, 18.5f), new Vector3(2.72f, .63f, 48.3f), concrete);
                float edge = x - Mathf.Sign(x) * 1.34f;
                Box(tank, "Inner fender rail", new Vector3(edge, .13f, 18.5f), new Vector3(.17f, .26f, 46.0f), border);
                for (int z = -3; z <= 40; z += 3)
                {
                    Box(tank, "Quay expansion joint", new Vector3(x, .491f, z), new Vector3(2.55f, .014f, .037f), trim);
                    Box(tank, "Quay edge strip", new Vector3(edge + Mathf.Sign(x) * .33f, .504f, z + 1.0f),
                        new Vector3(.14f, .025f, 1.6f), z % 6 == 0 ? stripe : white);
                }
                for (int z = 1; z < 40; z += 8)
                    Bollard(tank, x - Mathf.Sign(x) * .6f, z, border, stripe);
            }
            foreach (float z in new[] { -4.95f, 41.75f })
            {
                Box(tank, "Service deck", new Vector3(0, .18f, z), new Vector3(36.0f, .64f, 2.0f), concrete);
                for (int x = -16; x <= 16; x += 2)
                    Box(tank, "Service deck dash", new Vector3(x, .511f, z), new Vector3(1.05f, .023f, .18f), stripe);
            }

            Box(tank, "Start deck stripe", new Vector3(0, .027f, -.85f), new Vector3(7.2f, .025f, .07f), white);
            for (int x = -5; x <= 5; x++)
                Box(tank, "Start marker", new Vector3(x * .65f, .024f, -.99f), new Vector3(.25f, .024f, .29f), stripe);

            var gray = Material("Control booth", new Color(.79f, .84f, .79f), .05f, .22f);
            var glass = Material("Control glass", new Color(.075f, .20f, .23f), .40f, .84f);
            Box(tank, "Control booth", new Vector3(-16.65f, 1.2f, 2.8f), new Vector3(1.72f, 1.4f, 4.0f), gray);
            Box(tank, "Control booth canopy", new Vector3(-16.65f, 1.98f, 2.8f), new Vector3(2.05f, .17f, 4.45f), border);
            Box(tank, "Booth windows", new Vector3(-15.77f, 1.43f, 2.8f), new Vector3(.06f, .59f, 3.42f), glass);
            for (int i = 0; i < 4; i++)
                Box(tank, "Booth window mullion", new Vector3(-15.73f, 1.43f, 1.4f + i * .94f),
                    new Vector3(.035f, .62f, .038f), gray);
            Cylinder(tank, "Radio mast", new Vector3(-16.7f, 2.85f, 2.0f), new Vector3(.06f, .85f, .06f), border);
            Cylinder(tank, "Radio aerial", new Vector3(-16.7f, 3.66f, 2.0f), new Vector3(.21f, .025f, .21f), stripe);
        }

        private void BuildCourse(Transform tank, Course course)
        {
            for (int i = 0; i < course.Gates.Length; i++)
            {
                Gate gate = course.Gates[i];
                for (float x = gate.X - gate.Width * .38f; x < gate.X + gate.Width * .42f; x += .55f)
                    Polyline(tank, "Gate crossing line", new[] { new Vector3(x, .037f, gate.Z),
                        new Vector3(x + .30f, .037f, gate.Z) }, .095f, new Color(.78f, .96f, .89f, .68f));
            }
            foreach (Buoy definition in course.Buoys)
                BuildBuoy(tank, definition);

            if (course.RequiresDocking)
            {
                var deck = Material("Berth decking", new Color(.54f, .46f, .30f), 0, .18f);
                var bumper = Material("Berth fender", new Color(.14f, .19f, .20f), 0, .28f);
                foreach (float x in new[] { -Course.DockX, Course.DockX })
                {
                    Box(tank, "Floating dock", new Vector3(x, .28f, Course.DockZ),
                        new Vector3(2 * Course.DockHalfWidth, .54f, 2 * Course.DockHalfLength), bumper);
                    for (int i = 0; i < 18; i++)
                        Box(tank, "Dock plank", new Vector3(x, .585f, 29 + i * .50f), new Vector3(1.32f, .09f, .45f), deck);
                }
                var berth = new[] { new Vector3(-1.35f, .040f, 32.45f), new Vector3(1.35f, .040f, 32.45f),
                    new Vector3(1.35f, .040f, 35.55f), new Vector3(-1.35f, .040f, 35.55f), new Vector3(-1.35f, .040f, 32.45f) };
                Polyline(tank, "Berthing box", berth, .085f, new Color(.55f, 1f, .68f, .80f));
            }
        }

        private void BuildBuoy(Transform tank, Buoy buoy)
        {
            Transform root = Group(tank, buoy.Port ? "Port buoy" : "Starboard buoy");
            root.localPosition = new Vector3(buoy.X, 0, buoy.Z);
            buoyFloats.Add(root);
            var paint = buoy.Port ? Material("Buoy vermilion", new Color(.83f, .17f, .105f), .16f, .47f) :
                Material("Buoy emerald", new Color(.05f, .59f, .39f), .17f, .48f);
            var rubber = Material("Buoy base", new Color(.055f, .11f, .13f), .1f, .25f);
            var cap = Material("Buoy stripe", new Color(.91f, .92f, .77f), .11f, .51f);
            Cylinder(root, "Buoy float", new Vector3(0, .09f, 0), new Vector3(buoy.Radius * 2, .13f, buoy.Radius * 2), rubber);
            Cylinder(root, "Buoy body", new Vector3(0, .39f, 0), new Vector3(.69f, .28f, .69f), paint);
            Cylinder(root, "Reflective belt", new Vector3(0, .54f, 0), new Vector3(.705f, .045f, .705f), cap);
            Cylinder(root, "Beacon stem", new Vector3(0, .79f, 0), new Vector3(.18f, .22f, .18f), paint);
            if (buoy.Port)
                Cylinder(root, "Port cap", new Vector3(0, 1.04f, 0), new Vector3(.36f, .06f, .36f), paint);
            else
                Box(root, "Starboard cap", new Vector3(0, 1.04f, 0), new Vector3(.28f, .13f, .28f), paint)
                    .localRotation = Quaternion.Euler(0, 45, 0);
            Cylinder(root, "Beacon lens", new Vector3(0, 1.14f, 0), new Vector3(.115f, .055f, .115f), cap);
        }

        private Transform BuildBoat(Transform parent)
        {
            var boat = Group(parent, "BEACON 01 — survey catamaran");
            var yellow = Material("Hull gold", new Color(1f, .61f, .095f), .22f, .54f);
            var blue = Material("Hull charcoal", new Color(.055f, .105f, .13f), .29f, .40f);
            var white = Material("Cabin shell", new Color(.91f, .93f, .81f), .05f, .45f);
            var glass = Material("Bridge glass", new Color(.047f, .19f, .24f), .44f, .85f);
            var steel = Material("Stainless rails", new Color(.61f, .76f, .76f), .51f, .45f);
            var solar = Material("Solar glass", new Color(.058f, .18f, .34f), .52f, .69f);
            foreach (float x in new[] { -.49f, .49f })
            {
                Hull(boat, new Vector3(x, -.14f, 0), .28f, .22f, blue);
                Hull(boat, new Vector3(x, .02f, 0), .30f, .15f, yellow);
                Box(boat, "Stern drive", new Vector3(x, -.02f, -1.08f), new Vector3(.23f, .32f, .31f), blue);
                Box(boat, "Stern drive trim", new Vector3(x, .145f, -1.10f), new Vector3(.21f, .035f, .27f), steel);
                Box(boat, "Rub rail", new Vector3(x * 1.55f, .048f, -.05f), new Vector3(.044f, .065f, 1.56f), blue);
                for (int i = 0; i < 3; i++)
                {
                    Box(boat, "Guard rail post", new Vector3(x * 1.43f, .263f, -.57f + i * .48f),
                        new Vector3(.027f, .20f, .027f), steel);
                }
                Box(boat, "Guard rail", new Vector3(x * 1.43f, .36f, -.10f), new Vector3(.027f, .026f, 1.08f), steel);
                var lamp = x < 0 ? Material("Red navigation lens", Coral, .1f, .65f) :
                    Material("Green navigation lens", Mint, .1f, .65f);
                Box(boat, "Navigation light", new Vector3(x * 1.18f, .46f, .22f), new Vector3(.075f, .063f, .115f), lamp);
            }
            Box(boat, "Cross deck", new Vector3(0, .112f, -.08f), new Vector3(1.48f, .15f, 1.65f), yellow);
            Box(boat, "Aft nonslip mat", new Vector3(0, .204f, -.69f), new Vector3(.92f, .035f, .42f), blue);
            Box(boat, "Wheelhouse", new Vector3(0, .36f, .13f), new Vector3(.83f, .37f, .84f), white);
            Box(boat, "Wheelhouse roof", new Vector3(0, .568f, .14f), new Vector3(.91f, .082f, .88f), yellow);
            Box(boat, "Front windscreen", new Vector3(0, .455f, .558f), new Vector3(.68f, .16f, .027f), glass);
            Box(boat, "Windscreen divider", new Vector3(0, .455f, .577f), new Vector3(.024f, .17f, .021f), white);
            for (int side = -1; side <= 1; side += 2)
                Box(boat, "Side windscreen", new Vector3(side * .422f, .455f, .17f), new Vector3(.022f, .155f, .62f), glass);
            Box(boat, "Roof solar array", new Vector3(0, .62f, .11f), new Vector3(.69f, .038f, .51f), solar);
            for (int i = -2; i <= 2; i++)
                Box(boat, "Solar cell seam", new Vector3(i * .13f, .642f, .11f), new Vector3(.012f, .008f, .48f), steel);
            Cylinder(boat, "Radar mast", new Vector3(0, .762f, -.24f), new Vector3(.063f, .16f, .063f), blue);
            Cylinder(boat, "Radar dome", new Vector3(0, .942f, -.24f), new Vector3(.28f, .085f, .28f), white);
            Cylinder(boat, "Antenna", new Vector3(.28f, .79f, -.27f), new Vector3(.028f, .235f, .028f), steel);
            Box(boat, "Forward camera", new Vector3(0, .28f, .91f), new Vector3(.22f, .19f, .23f), blue);
            Box(boat, "Camera lens", new Vector3(0, .28f, 1.032f), new Vector3(.122f, .102f, .024f), glass);
            PaintLabel(boat, "01", new Vector3(0, .379f, -.298f), Quaternion.identity, .032f, new Color(.10f, .19f, .20f));
            return boat;
        }

        private static void PaintLabel(Transform parent, string label, Vector3 position, Quaternion rotation,
            float size, Color color)
        {
            var root = Group(parent, "Painted identification");
            root.localPosition = position;
            root.localRotation = rotation;
            var text = root.gameObject.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontSize = 56;
            text.characterSize = size;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
        }

        private void Hull(Transform parent, Vector3 origin, float beam, float depth, Material material)
        {
            var hull = Group(parent, material.name);
            hull.localPosition = origin;
            var mesh = new Mesh { name = "Harbor procedural hull" };
            float[] z = { -1.09f, -.88f, .25f, .88f, 1.39f };
            float[] width = { .76f, 1, 1, .68f, .035f };
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i < z.Length; i++)
            {
                float half = beam * width[i];
                float lift = i == z.Length - 1 ? depth * .52f : 0;
                vertices.Add(new Vector3(-half, depth * .52f + lift, z[i]));
                vertices.Add(new Vector3(half, depth * .52f + lift, z[i]));
                vertices.Add(new Vector3(half * .63f, -depth * .55f + lift, z[i]));
                vertices.Add(new Vector3(-half * .63f, -depth * .55f + lift, z[i]));
            }
            for (int ring = 0; ring < z.Length - 1; ring++)
            {
                for (int side = 0; side < 4; side++)
                {
                    int a = ring * 4 + side;
                    int b = ring * 4 + (side + 1) % 4;
                    indices.AddRange(new[] { a, a + 4, b, b, a + 4, b + 4 });
                }
            }
            int end = vertices.Count - 4;
            indices.AddRange(new[] { 0, 1, 2, 0, 2, 3, end, end + 2, end + 1, end, end + 3, end + 2 });
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            hull.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            hull.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void PaintShadow(Transform root)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var vertices = new Vector3[33];
                var colors = new Color[vertices.Length];
                var triangles = new int[32 * 3];
                vertices[0] = new Vector3(side * .44f, 0, -.08f);
                colors[0] = new Color(.009f, .045f, .055f, .56f);
                for (int i = 0; i < 32; i++)
                {
                    float angle = i * Mathf.PI * 2 / 32;
                    vertices[i + 1] = vertices[0] + new Vector3(Mathf.Cos(angle) * .51f, 0, Mathf.Sin(angle) * 1.36f);
                    colors[i + 1] = new Color(.009f, .045f, .055f, 0);
                    int j = i * 3;
                    triangles[j] = 0;
                    triangles[j + 1] = i + 1;
                    triangles[j + 2] = (i + 1) % 32 + 1;
                }
                var mesh = new Mesh { name = "Harbor procedural contact" };
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.triangles = triangles;
                Transform shadow = Group(root, "Pontoon contact shadow");
                shadow.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                shadow.gameObject.AddComponent<MeshRenderer>().sharedMaterial = ink;
            }
        }

        private void UpdateWake()
        {
            for (int i = 0; i < wakes.Length; i++)
            {
                List<Vector3> points = wakePoints[i];
                Vector3 p = Boat.TransformPoint(new Vector3(i == 0 ? -.49f : .49f, 0, -1.12f));
                p.y = .046f;
                if (points.Count > 0 && Vector3.SqrMagnitude(points[^1] - p) < .037f)
                    continue;
                if (points.Count == 24)
                    points.RemoveAt(0);
                points.Add(p);
                wakes[i].positionCount = points.Count;
                wakes[i].SetPositions(points.ToArray());
                wakes[i].startColor = new Color(.74f, .94f, .92f, 0);
                wakes[i].endColor = new Color(.74f, .94f, .92f, .48f);
                wakes[i].startWidth = .32f;
                wakes[i].endWidth = .094f;
            }
        }

        private void Surface(Transform parent, string name, Vector3 location, float width, float length, bool basin)
        {
            string key = basin ? "Basin water" : "Outer water";
            var material = new Material(Resources.Load<Shader>("HarborWater")) { name = key };
            material.SetFloat("_Basin", basin ? 1 : 0);
            if (!basin)
            {
                material.SetColor("_Deep", new Color(.078f, .21f, .25f));
                material.SetColor("_Shallow", new Color(.12f, .29f, .34f));
            }
            palette.Add(key, material);
            var surface = Primitive(parent, PrimitiveType.Plane, name, location, new Vector3(width / 10, 1, length / 10), material);
            surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void Bollard(Transform parent, float x, float z, Material baseMat, Material cap)
        {
            Cylinder(parent, "Quay bollard", new Vector3(x, .64f, z), new Vector3(.26f, .18f, .26f), baseMat);
            Cylinder(parent, "Quay bollard cap", new Vector3(x, .84f, z), new Vector3(.32f, .043f, .32f), cap);
        }

        private Material Material(string name, Color color, float metallic, float gloss)
        {
            if (palette.TryGetValue(name, out Material result))
                return result;
            result = new Material(Shader.Find("Standard")) { name = name, color = color };
            result.SetFloat("_Metallic", metallic);
            result.SetFloat("_Glossiness", gloss);
            palette.Add(name, result);
            return result;
        }

        private LineRenderer Polyline(Transform parent, string name, Vector3[] points, float width, Color color, bool world = true)
        {
            var line = Group(parent, name).gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = ink;
            line.useWorldSpace = world;
            line.alignment = LineAlignment.TransformZ;
            line.transform.localRotation = Quaternion.Euler(90, 0, 0);
            if (!world)
            {
                // Local coordinates compensate for the horizontal renderer orientation.
                for (int i = 0; i < points.Length; i++)
                    points[i] = new Vector3(points[i].x, points[i].z, -points[i].y);
            }
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Transform Group(Transform parent, string name)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }

        private static Transform Box(Transform parent, string name, Vector3 location, Vector3 scale, Material material) =>
            Primitive(parent, PrimitiveType.Cube, name, location, scale, material);

        private static Transform Cylinder(Transform parent, string name, Vector3 location, Vector3 scale, Material material) =>
            Primitive(parent, PrimitiveType.Cylinder, name, location, scale, material);

        private static Transform Primitive(Transform parent, PrimitiveType type, string name, Vector3 location, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = location;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Collider>().enabled = false;
            return go.transform;
        }
    }
}
