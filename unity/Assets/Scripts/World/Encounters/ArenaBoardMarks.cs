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
    /// this component rides on the root, asks the model what to draw every frame and rebuilds its meshes
    /// only when the answer changed. Plates face the camera, which the player can orbit to any angle.
    /// </remarks>
    public sealed class ArenaBoardMarks : MonoBehaviour {
        private const float GroundLift = 0.06f;
        private const float LiftStep = 0.015f;
        private const float WorldUnitsPerCellDefault = 3f;

        private static readonly Color Dark = new Color(0.08f, 0.07f, 0.06f, 0.88f);
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.25f);

        private Func<List<HotspotService.BoardShape>> _shapes;
        private Func<List<HotspotService.FoePlate>> _plates;
        private EnhancedCombatLayout _layout;
        private float _unitsPerCell = WorldUnitsPerCellDefault;
        private Material _material;
        private string _shapeKey = "";
        private readonly List<GameObject> _meshes = new();
        private readonly Dictionary<int, GameObject> _plateObjects = new();
        private string _plateKey = "";

        /// <summary>The plate of the foe at this roster index, or null.</summary>
        public GameObject PlateOf(int index) => _plateObjects.TryGetValue(index, out GameObject p) ? p : null;

        /// <summary>The drawn board shapes' kinds, for tests.</summary>
        public IEnumerable<string> DrawnKinds() {
            foreach (GameObject g in _meshes) {
                yield return g.name;
            }
        }

        public void Bind(Func<List<HotspotService.BoardShape>> shapes,
            Func<List<HotspotService.FoePlate>> plates, EnhancedCombatLayout layout, float unitsPerCell) {
            _shapes = shapes;
            _plates = plates;
            _layout = layout;
            _unitsPerCell = unitsPerCell > 0f ? unitsPerCell : WorldUnitsPerCellDefault;
            if (_material != null) {
                Destroy(_material);
            }
            // Sprites/Default is in GraphicsSettings' always-included shaders (fileID 10753), so a
            // player build keeps it; the arena grid overlay finds the same shader.
            _material = new Material(Shader.Find("Sprites/Default"));
        }

        private void Update() {
            if (_shapes == null || _layout == null) {
                return;
            }
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
        }

        private void LateUpdate() {
            Camera cam = Camera.main;
            if (cam == null) {
                return;
            }
            foreach (GameObject plate in _plateObjects.Values) {
                if (plate != null) {
                    plate.transform.rotation = cam.transform.rotation;
                }
            }
        }

        private static Color ColourOf(ArenaOverlayKind kind) => kind switch {
            ArenaOverlayKind.ReachFill => new Color(0.45f, 1f, 0.55f, 0.22f),
            ArenaOverlayKind.Actor => new Color(0.45f, 1f, 0.55f, 0.45f),
            ArenaOverlayKind.ThrustMark => new Color(1f, 0.5f, 0.2f, 0.5f),
            ArenaOverlayKind.SwingMark => new Color(1f, 0.85f, 0.2f, 0.65f),
            ArenaOverlayKind.Target => new Color(1f, 1f, 1f, 0.3f),
            _ => Color.white,
        };

        private void RefreshShapes() {
            List<HotspotService.BoardShape> shapes = _shapes() ?? new List<HotspotService.BoardShape>();
            var key = new System.Text.StringBuilder();
            foreach (HotspotService.BoardShape s in shapes) {
                key.Append((int)s.Kind).Append(':');
                foreach ((long x, long y) in s.Points) {
                    key.Append(x).Append(',').Append(y).Append(' ');
                }
                key.Append(';');
            }
            if (key.ToString() == _shapeKey) {
                return;
            }
            _shapeKey = key.ToString();
            DestroyWithMesh(_meshes);
            _meshes.Clear();

            // One mesh per kind, drawn in the kind's own layer of height so the fills never z-fight.
            foreach (ArenaOverlayKind kind in new[] { ArenaOverlayKind.ReachFill, ArenaOverlayKind.Actor,
                ArenaOverlayKind.ThrustMark, ArenaOverlayKind.SwingMark, ArenaOverlayKind.Target,
                ArenaOverlayKind.Path }) {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                float lift = GroundLift + ((int)kind - (int)ArenaOverlayKind.ReachFill) * LiftStep;
                foreach (HotspotService.BoardShape s in shapes) {
                    if (s.Kind != kind) {
                        continue;
                    }
                    if (kind == ArenaOverlayKind.Path) {
                        AddDots(s.Points, lift, verts, tris);
                    } else {
                        int at = verts.Count;
                        foreach ((long x, long y) in s.Points) {
                            verts.Add(BakCoordinateConverter.ConvertPosition((int)x, (int)y, 0) + (Vector3.up * lift));
                        }
                        tris.AddRange(new[] { at, at + 2, at + 1, at, at + 3, at + 2 });
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
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
                _meshes.Add(go);
            }
        }

        /// <summary>Small squares along the walk, every <see cref="EnhancedCombatLayout.PathDotSpacingCells"/>.</summary>
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
                    Vector3 c = Vector3.Lerp(a, b, d / (float)dots);
                    int at = verts.Count;
                    verts.Add(c + new Vector3(-size, 0f, -size));
                    verts.Add(c + new Vector3(-size, 0f, size));
                    verts.Add(c + new Vector3(size, 0f, size));
                    verts.Add(c + new Vector3(size, 0f, -size));
                    tris.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
                }
            }
            Vector3 end = Ground(points[points.Length - 1]);
            int e = verts.Count;
            verts.Add(end + new Vector3(-size * 2, 0f, -size * 2));
            verts.Add(end + new Vector3(-size * 2, 0f, size * 2));
            verts.Add(end + new Vector3(size * 2, 0f, size * 2));
            verts.Add(end + new Vector3(size * 2, 0f, -size * 2));
            tris.AddRange(new[] { e, e + 1, e + 2, e, e + 2, e + 3 });
        }

        /// <summary>
        /// A dark plate with the foe's gold letter above its drawn top, and its attack marks: one sword
        /// for a thrust, two when a swing is also open. The "-N seen" line is the slot Task 5's
        /// knowledge store fills; it is empty until then.
        /// </summary>
        private void RefreshPlates() {
            List<HotspotService.FoePlate> plates = _plates?.Invoke() ?? new List<HotspotService.FoePlate>();
            var key = new System.Text.StringBuilder();
            foreach (HotspotService.FoePlate p in plates) {
                key.Append(p.Index).Append(p.Letter).Append(p.Thrust ? 't' : '-').Append(p.Swing ? 's' : '-')
                    .Append(p.Hovered ? 'h' : '-').Append(p.Seen).Append(';');
            }
            // Markers appear a redraw after the model, so the plates also wait on them.
            ArenaCombatant[] markers = GetComponentsInChildren<ArenaCombatant>();
            key.Append(markers.Length);
            if (key.ToString() == _plateKey) {
                return;
            }
            _plateKey = key.ToString();
            DestroyWithMesh(_plateObjects.Values);
            _plateObjects.Clear();
            foreach (HotspotService.FoePlate p in plates) {
                foreach (ArenaCombatant m in markers) {
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

            Text(plate.transform, "Letter", p.Letter.ToString(), Gold, h * 0.9f, new Vector3(-w * 0.2f, 0f, -0.01f));
            string swords = p.Swing ? "\u2694\u2694" : p.Thrust ? "\u2694" : "";
            Text(plate.transform, "Swords", swords, Color.white, h * 0.4f, new Vector3(w * 0.2f, 0f, -0.01f));
            Text(plate.transform, "Seen", p.Seen > 0
                ? GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.EnhancedCombatSeen, ("count", p.Seen))
                : "", Color.white, h * 0.3f, new Vector3(0f, -h * 0.65f, -0.01f));
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
