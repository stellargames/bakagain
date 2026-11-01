namespace BakAgain.World.Encounters {
    using System;
    using System.Collections.Generic;
    using BakAgain.World.Converters;
    using BakAgain.World.Hotspots;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// The enhanced combat board's marks (TASK-972): the reach fill, the thrust and swing marks, the
    /// forecast path and a letter plate on every living foe. Added to the arena root only when the
    /// Enhanced combat interface is on, so Classic never builds it.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is kept on the arena's GameObjects.</b> The arena is rebuilt on every combat redraw, so
    /// this component rides on the root, checks a hash of the model's inputs every frame, and asks for the
    /// shapes and plates (and rebuilds its meshes) only when that hash changed. Plates face the camera, which the player can orbit to any angle.
    /// </remarks>
    public sealed class ArenaBoardMarks : MonoBehaviour {
        private const float LiftStep = 0.015f;
        private const float WorldUnitsPerCellDefault = 3f;

        private static readonly Color Dark = new Color(0.08f, 0.07f, 0.06f, 0.88f);
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.25f);

        private Func<List<HotspotService.BoardShape>> _shapes;
        private Func<List<HotspotService.FoePlate>> _plates;
        private EnhancedCombatLayout _layout;
        private float _unitsPerCell = WorldUnitsPerCellDefault;
        private Func<int> _inputs;
        private Material _material, _lighten;
        private readonly List<GameObject> _meshes = new();
        private readonly Dictionary<int, GameObject> _plateObjects = new();
        private readonly List<ArenaCombatant> _markers = new();
        private bool _drawn;
        private int _drawnInputs;
        private int _drawnMarkers;

        /// <summary>How many times the marks were rebuilt; for tests.</summary>
        internal int Rebuilds { get; private set; }

        /// <summary>The plate of the foe at this roster index, or null.</summary>
        public GameObject PlateOf(int index) => _plateObjects.TryGetValue(index, out GameObject p) ? p : null;

        /// <summary>The drawn board shapes' kinds, for tests.</summary>
        public IEnumerable<string> DrawnKinds() {
            foreach (GameObject g in _meshes) {
                yield return g.name;
            }
        }

        /// <param name="inputs">A hash of everything the shapes and plates read; they are asked for, and
        /// rebuilt, only when it changes (<see cref="HotspotService.EnhancedBoardInputs"/>).</param>
        public void Bind(Func<List<HotspotService.BoardShape>> shapes,
            Func<List<HotspotService.FoePlate>> plates, Func<int> inputs, EnhancedCombatLayout layout,
            float unitsPerCell, Func<Camera> camera = null) {
            _camera = camera;
            _inputs = inputs;
            _drawn = false;
            _shapes = shapes;
            _plates = plates;
            _layout = layout;
            _unitsPerCell = unitsPerCell > 0f ? unitsPerCell : WorldUnitsPerCellDefault;
            if (_material != null) {
                Destroy(_material);
            }
            if (_lighten != null) {
                Destroy(_lighten);
            }
            // The reach squares lighten the ground under them (Resources/Shaders, so a build keeps it).
            _lighten = new Material(Shader.Find("BakAgain/GroundLighten")) { renderQueue = 3000 };
            _material = LitMark(_layout);
        }

        /// <summary>The marks' material: vertex colour, dimmed most of the way with the world's light
        /// (Resources/Shaders/LitMark, so a build keeps it).</summary>
        internal static Material LitMark(EnhancedCombatLayout layout) {
            var m = new Material(Shader.Find("BakAgain/LitMark"));
            m.SetFloat("_LightFollow", Mathf.Clamp01(layout?.MarkLightFollow ?? 1f));
            return m;
        }

        private void Update() {
            if (_shapes == null || _layout == null) {
                return;
            }
            // Markers appear a redraw after the model, so the plates also wait on them.
            GetComponentsInChildren(_markers);
            int inputs = _inputs?.Invoke() ?? 0;
            if (_drawn && inputs == _drawnInputs && _markers.Count == _drawnMarkers) {
                return;
            }
            _drawn = true;
            _drawnInputs = inputs;
            _drawnMarkers = _markers.Count;
            Rebuilds++;
            RefreshShapes();
            RefreshPlates();
        }

        private static void DestroyWithMesh(IEnumerable<GameObject> objects) {
            foreach (GameObject g in objects) {
                if (g == null) {
                    continue;
                }
                foreach (MeshFilter f in g.GetComponentsInChildren<MeshFilter>()) {
                    Destroy(f.sharedMesh);
                }
                Destroy(g);
            }
        }

        private void OnDestroy() {
            DestroyWithMesh(_meshes);
            DestroyWithMesh(_plateObjects.Values);
            if (_material != null) {
                Destroy(_material);
            }
            if (_lighten != null) {
                Destroy(_lighten);
            }
        }

        // The world camera: the game owns a dedicated one and Camera.main is null in a fight, which left
        // every plate unrotated and read mirrored from the opening pose (seen live, task 11).
        private Func<Camera> _camera;

        private void LateUpdate() {
            Camera cam = _camera?.Invoke() ?? Camera.main;
            if (cam == null) {
                return;
            }
            foreach (GameObject plate in _plateObjects.Values) {
                if (plate != null) {
                    plate.transform.rotation = cam.transform.rotation;
                }
            }
        }

        // The layout's RGBA for each kind; the path's dots are white. A reach square's colour is half its
        // lightening factor (GroundLighten doubles it), the factor taken to linear when the project is.
        private Color ColourOf(ArenaOverlayKind kind) {
            if (kind == ArenaOverlayKind.ReachFill) {
                float k = Mathf.Clamp(_layout.MarkReachLighten, 1f, 2f);
                k = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.Min(Mathf.Pow(k, 2.2f), 2f) : k;
                return new Color(k / 2f, k / 2f, k / 2f, 1f);
            }
            float[] c = kind switch {
                ArenaOverlayKind.Actor => _layout.MarkActorRgba,
                ArenaOverlayKind.ThrustMark => _layout.MarkThrustRgba,
                ArenaOverlayKind.SwingMark => _layout.MarkSwingRgba,
                ArenaOverlayKind.Target => _layout.MarkTargetRgba,
                ArenaOverlayKind.WalkEnd => _layout.MarkWalkEndRgba,
                _ => null,
            };
            return c == null || c.Length < 4 ? Color.white : new Color(c[0], c[1], c[2], c[3]);
        }

        private void RefreshShapes() {
            List<HotspotService.BoardShape> shapes = _shapes() ?? new List<HotspotService.BoardShape>();
            DestroyWithMesh(_meshes);
            _meshes.Clear();

            // One mesh per kind, drawn in the kind's own layer of height so the marks never z-fight.
            float frame = _layout.MarkFrameCells * _unitsPerCell;
            foreach (ArenaOverlayKind kind in new[] { ArenaOverlayKind.ReachFill, ArenaOverlayKind.Actor,
                ArenaOverlayKind.ThrustMark, ArenaOverlayKind.SwingMark, ArenaOverlayKind.Target,
                ArenaOverlayKind.WalkEnd, ArenaOverlayKind.Path }) {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                float lift = _layout.MarkLiftCells * _unitsPerCell + ((int)kind - (int)ArenaOverlayKind.ReachFill) * LiftStep;
                foreach (HotspotService.BoardShape s in shapes) {
                    if (s.Kind != kind) {
                        continue;
                    }
                    if (kind == ArenaOverlayKind.Path) {
                        AddDots(s.Points, lift, verts, tris);
                    } else {
                        AddFrame(s.Points, lift, frame, verts, tris);
                    }
                }
                if (verts.Count == 0) {
                    continue;
                }
                var mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                var colours = new Color[verts.Count];
                Array.Fill(colours, ColourOf(kind));
                mesh.SetColors(colours);
                var go = new GameObject(kind.ToString());
                go.transform.SetParent(transform, worldPositionStays: false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = kind == ArenaOverlayKind.ReachFill ? _lighten : _material;
                _meshes.Add(go);
            }
        }

        /// <summary>
        /// A square cell mark as a frame <paramref name="width"/> wide with an open middle: four quads between the
        /// shape's corners and the same corners drawn in toward its centre.
        /// </summary>
        private static void AddFrame((long X, long Y)[] points, float lift, float width, List<Vector3> verts, List<int> tris) {
            var outer = new Vector3[points.Length];
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < points.Length; i++) {
                outer[i] = BakCoordinateConverter.ConvertPosition((int)points[i].X, (int)points[i].Y, 0) + (Vector3.up * lift);
                centre += outer[i];
            }
            centre /= points.Length;
            // A square's half side is its corner distance over root 2; the frame never closes the middle.
            float half = Vector3.Distance(outer[0], centre) / Mathf.Sqrt(2f);
            float t = half > 0f ? Mathf.Clamp(width / half, 0f, 0.9f) : 0f;
            int at = verts.Count;
            foreach (Vector3 o in outer) {
                verts.Add(o);
            }
            foreach (Vector3 o in outer) {
                verts.Add(Vector3.Lerp(o, centre, t));
            }
            int n = outer.Length;
            for (int i = 0; i < n; i++) {
                int j = (i + 1) % n;
                tris.AddRange(new[] { at + i, at + j, at + n + j, at + i, at + n + j, at + n + i });
            }
        }

        /// <summary>Small discs along the walk, every <see cref="EnhancedCombatLayout.PathDotSpacingCells"/>, and a
        /// bigger one where it ends (discs, not squares: owner, 2026-11-01).</summary>
        private void AddDots((long X, long Y)[] points, float lift, List<Vector3> verts, List<int> tris) {
            float spacing = _layout.PathDotSpacingCells * _unitsPerCell;
            float size = _layout.PathDotSizeCells * _unitsPerCell;
            Vector3 Ground((long X, long Y) p) =>
                BakCoordinateConverter.ConvertPosition((int)p.X, (int)p.Y, 0) + (Vector3.up * lift);
            for (int i = 0; i + 1 < points.Length; i++) {
                Vector3 a = Ground(points[i]);
                Vector3 b = Ground(points[i + 1]);
                int dots = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / spacing));
                for (int d = 0; d < dots; d++) {
                    AddDisc(Vector3.Lerp(a, b, d / (float)dots), size, verts, tris);
                }
            }
            AddDisc(Ground(points[points.Length - 1]), size * 2f, verts, tris);
        }

        private const int DiscSegments = 16;

        // A flat disc on the ground: a fan round its centre, wound to face up as the squares were.
        private static void AddDisc(Vector3 centre, float radius, List<Vector3> verts, List<int> tris) {
            int at = verts.Count;
            verts.Add(centre);
            for (int k = 0; k < DiscSegments; k++) {
                float angle = -2f * Mathf.PI * k / DiscSegments;
                verts.Add(centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            for (int k = 0; k < DiscSegments; k++) {
                tris.AddRange(new[] { at, at + 1 + k, at + 1 + (k + 1) % DiscSegments });
            }
        }

        /// <summary>
        /// A dark plate with the foe's gold letter above its drawn top. Thrust and swing reach are the board's
        /// cell marks, not the plate's; the damage seen is the HUD's alone (owner, 2026-11-01).
        /// </summary>
        private void RefreshPlates() {
            List<HotspotService.FoePlate> plates = _plates?.Invoke() ?? new List<HotspotService.FoePlate>();
            DestroyWithMesh(_plateObjects.Values);
            _plateObjects.Clear();
            foreach (HotspotService.FoePlate p in plates) {
                foreach (ArenaCombatant m in _markers) {
                    if (!m.PartyMember && m.RosterSlot == p.Index) {
                        _plateObjects[p.Index] = BuildPlate(p, BillboardSprite.DrawnTopCentre(m.transform));
                        break;
                    }
                }
            }
        }

        private GameObject BuildPlate(HotspotService.FoePlate p, Vector3 top) {
            float w = _layout.PlateWidthCells * _unitsPerCell;
            float h = _layout.PlateHeightCells * _unitsPerCell;
            var plate = new GameObject("FoePlate " + p.Letter);
            plate.transform.SetParent(transform, worldPositionStays: true);
            plate.transform.position = top + (Vector3.up * (_layout.PlateLiftCells * _unitsPerCell + (h * 0.5f)));

            var quad = new GameObject("Back");
            quad.transform.SetParent(plate.transform, false);
            var mesh = new Mesh();
            mesh.SetVertices(new[] { new Vector3(-w / 2, -h / 2), new Vector3(-w / 2, h / 2),
                new Vector3(w / 2, h / 2), new Vector3(w / 2, -h / 2) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.SetColors(new[] { Dark, Dark, Dark, Dark });
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            quad.AddComponent<MeshRenderer>().sharedMaterial = _material;

            // The letter alone: the reach swords were more clutter than use (owner, 2026-10-29).
            Text(plate.transform, "Letter", p.Letter.ToString(), Gold, h * 0.9f, new Vector3(0f, 0f, -0.01f));
            return plate;
        }

        private static void Text(Transform parent, string name, string text, Color colour, float height, Vector3 at) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var t = go.AddComponent<TextMesh>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
            t.text = text;
            t.color = colour;
            t.anchor = TextAnchor.MiddleCenter;
            t.fontSize = 64;
            t.characterSize = height / 6.4f;
        }
    }
}
