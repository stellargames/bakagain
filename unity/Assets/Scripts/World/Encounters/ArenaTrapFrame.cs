namespace BakAgain.World.Encounters {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// The Enhanced board's frame (TASK-972). In a trap: a low stone rim round the playable cells, everything
    /// outside the board dimmed, and a faint gold tint on the goal cells. In every Enhanced fight: a brief red
    /// flash of the board edge when a tap lands off the board (<see cref="Flash"/>).
    /// </summary>
    /// <remarks>
    /// <para>Built once in <see cref="Bind"/>: the arena root is rebuilt on every redraw, and the grid does not
    /// change in a fight. Rides on the root only when the turn is Enhanced, as <see cref="ArenaBoardMarks"/> does.</para>
    /// <para><b>The dim is three tall walls and a flat apron.</b> The walls stand on the rim's outer edge -- left,
    /// right (both running back past the camera) and far -- and are shown only from the board's side, so
    /// everything seen past them, scenery and sky alike, is dimmed while the board in front is not. The apron
    /// dims the ground between the camera and the near rim. Sprites/Default culls nothing, so the walls'
    /// culling is done here: a wall whose outside the camera is on is hidden. Drawn in a queue before the
    /// other transparents, so a combatant whose sprite rises over a wall is never dimmed.</para>
    /// </remarks>
    public sealed class ArenaTrapFrame : MonoBehaviour {
        private const float LiftStep = 0.015f;          // ArenaBoardMarks' layer step
        private const float DimReachCells = 400f;       // how far the walls and apron run: past the scenery
        private const int DimQueue = 2990, FrameQueue = 3000, FlashQueue = 3001;

        private EnhancedCombatLayout _layout;
        private Func<Camera> _camera;
        private Material _material, _dimMaterial, _flashMaterial;
        private readonly List<Mesh> _meshes = new();
        private readonly List<GameObject> _parts = new();
        private GameObject _flash;
        private Renderer _left, _right, _far;
        private float _flashUntil;
        private Color _flashColour;

        // The board frame (across, away) to the world: the world centre of cell (0,0) and one cell along each axis.
        private Vector3 _origin, _across, _away;
        private double _cellSize, _originX, _originY;
        private double _x0, _x1, _y1;   // the rim's outer edges

        /// <summary>The drawn parts' names, for tests: TrapRim, TrapOutsideDim, TrapGoal, and EdgeFlash while it shows.</summary>
        public IEnumerable<string> DrawnKinds() {
            foreach (GameObject part in _parts) {
                if (part != null && part.activeSelf) {
                    yield return part.name;
                }
            }
        }

        /// <param name="boardCorners"><see cref="ArenaOrbit.BoardCorners"/>: the playable cells' outer corners.</param>
        /// <param name="cellCentreWorld">A cell's centre in the world (<c>HotspotService.CellCentreWorld</c>).</param>
        /// <param name="camera">The world camera, for the walls' culling; without one every wall shows.</param>
        public void Bind(bool trap, (double X, double Y)[] boardCorners, IReadOnlyList<(int Column, int Row)> goalCells,
            Func<int, int, Vector3?> cellCentreWorld, EnhancedCombatLayout layout, float unitsPerCell,
            Func<Camera> camera = null) {
            _layout = layout;
            _camera = camera;
            if (boardCorners == null || boardCorners.Length < 4 || cellCentreWorld == null
                || cellCentreWorld(0, 0) is not Vector3 o || cellCentreWorld(1, 0) is not Vector3 a
                || cellCentreWorld(0, 1) is not Vector3 b) {
                return;
            }
            _origin = o;
            _across = a - o;
            _away = b - o;
            _cellSize = (boardCorners[1].X - boardCorners[0].X) / CombatGrid.Width;
            _originX = boardCorners[0].X + (_cellSize / 2);
            _originY = boardCorners[0].Y + (_cellSize / 2);
            float u = unitsPerCell > 0f ? unitsPerCell : _across.magnitude;

            // Sprites/Default is in GraphicsSettings' always-included shaders, as ArenaBoardMarks notes.
            Shader shader = Shader.Find("Sprites/Default");
            _material = new Material(shader) { renderQueue = FrameQueue };
            _dimMaterial = new Material(shader) { renderQueue = DimQueue };
            _flashMaterial = new Material(shader) { renderQueue = FlashQueue };
            // A material colour is converted to the colour space by Unity; vertex colours are not (Vertex()).
            _flashColour = Rgba(layout.EdgeFlashRgba);

            double rim = layout.TrapRimCells * _cellSize;
            double ix0 = boardCorners[0].X, ix1 = boardCorners[1].X, iy0 = boardCorners[0].Y, iy1 = boardCorners[2].Y;
            _x0 = ix0 - rim; _x1 = ix1 + rim; _y1 = iy1 + rim;
            double oy0 = iy0 - rim;

            // The rim: a raised frame, its top and both sides. The flash is the same frame drawn over it in red.
            float height = layout.TrapRimCells * u;
            Vector3[] inner = Rect(ix0, iy0, ix1, iy1), outer = Rect(_x0, oy0, _x1, _y1);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Band(Up(inner, height), Up(outer, height), verts, tris);
            Band(inner, Up(inner, height), verts, tris);
            Band(outer, Up(outer, height), verts, tris);
            Mesh rimMesh = MakeMesh(verts, tris, Color.white);
            if (trap) {
                Part("TrapRim", MakeMesh(verts, tris, Vertex(layout.TrapRimRgba)), _material);
            }
            _flash = Part("EdgeFlash", rimMesh, _flashMaterial);
            _flash.SetActive(false);
            if (!trap) {
                return;
            }

            // The dim: the apron on the ground before the board, then the three walls.
            float lift = (layout.MarkLiftCells * u) - LiftStep;
            double reach = DimReachCells * _cellSize;
            float tall = DimReachCells * u;
            Color dim = Vertex(layout.TrapOutsideDimRgba);
            GameObject dimRoot = Part("TrapOutsideDim", Quad(Rect(_x0, oy0 - reach, _x1, oy0), lift, dim), _dimMaterial);
            _left = Wall(dimRoot, "Left", ToWorld(_x0, oy0 - reach), ToWorld(_x0, _y1), tall, dim);
            _right = Wall(dimRoot, "Right", ToWorld(_x1, oy0 - reach), ToWorld(_x1, _y1), tall, dim);
            _far = Wall(dimRoot, "Far", ToWorld(_x0 - reach, _y1), ToWorld(_x1 + reach, _y1), tall, dim);

            // The goal: one flat quad per standable goal cell, under the reach fill's layer.
            var goalRoot = new GameObject("TrapGoal");
            goalRoot.transform.SetParent(transform, false);
            _parts.Add(goalRoot);
            if (goalCells == null || goalCells.Count == 0) {
                return;
            }
            Vector3 hx = _across / 2, hy = _away / 2;
            Mesh cell = MakeMesh(new List<Vector3> { -hx - hy, -hx + hy, hx + hy, hx - hy },
                new List<int> { 0, 1, 2, 0, 2, 3 }, Vertex(layout.TrapGoalRgba));
            foreach ((int column, int row) in goalCells) {
                if (cellCentreWorld(column, row) is not Vector3 centre) {
                    continue;
                }
                var quad = new GameObject($"Goal {column},{row}");
                quad.transform.SetParent(goalRoot.transform, false);
                quad.transform.localPosition = centre + (Vector3.up * lift);
                quad.AddComponent<MeshFilter>().sharedMesh = cell;
                quad.AddComponent<MeshRenderer>().sharedMaterial = _material;
            }
        }

        /// <summary>Starts, or restarts, the red flash of the board edge for <see cref="EnhancedCombatLayout.EdgeFlashSeconds"/>.</summary>
        public void Flash() {
            if (_flash == null) {
                return;
            }
            _flashUntil = Time.time + _layout.EdgeFlashSeconds;
            _flashMaterial.color = _flashColour;
            _flash.SetActive(true);
        }

        private void Update() {
            if (_flash == null || !_flash.activeSelf) {
                return;
            }
            float left = _flashUntil - Time.time;
            if (left <= 0f) {
                _flash.SetActive(false);
                return;
            }
            Color c = _flashColour;
            c.a *= left / Mathf.Max(_layout.EdgeFlashSeconds, 1e-3f);
            _flashMaterial.color = c;
        }

        // Back-face culling for the walls, by which side of each the camera stands.
        private void LateUpdate() {
            Camera cam = _camera?.Invoke();
            if (cam == null || _left == null) {
                return;
            }
            Vector3 d = cam.transform.position - _origin;
            double x = _originX + (_cellSize * Vector3.Dot(d, _across) / _across.sqrMagnitude);
            double y = _originY + (_cellSize * Vector3.Dot(d, _away) / _away.sqrMagnitude);
            _left.enabled = x > _x0;
            _right.enabled = x < _x1;
            _far.enabled = y < _y1;
        }

        private void OnDestroy() {
            foreach (Mesh m in _meshes) {
                Destroy(m);
            }
            foreach (Material m in new[] { _material, _dimMaterial, _flashMaterial }) {
                if (m != null) {
                    Destroy(m);
                }
            }
        }

        private Vector3 ToWorld(double x, double y) =>
            _origin + (_across * (float)((x - _originX) / _cellSize)) + (_away * (float)((y - _originY) / _cellSize));

        // Corners in order round the rectangle, on the ground.
        private Vector3[] Rect(double x0, double y0, double x1, double y1) =>
            new[] { ToWorld(x0, y0), ToWorld(x1, y0), ToWorld(x1, y1), ToWorld(x0, y1) };

        private static Vector3[] Up(Vector3[] points, float by) => Array.ConvertAll(points, p => p + (Vector3.up * by));

        // Quads joining each edge of one closed loop to the same edge of another.
        private static void Band(Vector3[] a, Vector3[] b, List<Vector3> verts, List<int> tris) {
            int at = verts.Count, n = a.Length;
            verts.AddRange(a);
            verts.AddRange(b);
            for (int i = 0; i < n; i++) {
                int j = (i + 1) % n;
                tris.AddRange(new[] { at + i, at + j, at + n + j, at + i, at + n + j, at + n + i });
            }
        }

        private Mesh Quad(Vector3[] rect, float lift, Color colour) =>
            MakeMesh(new List<Vector3>(Up(rect, lift)), new List<int> { 0, 1, 2, 0, 2, 3 }, colour);

        private Mesh MakeMesh(List<Vector3> verts, List<int> tris, Color colour) {
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var colours = new Color[verts.Count];
            Array.Fill(colours, colour);
            mesh.SetColors(colours);
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            return mesh;
        }

        private Renderer Wall(GameObject parent, string name, Vector3 from, Vector3 to, float tall, Color colour) {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MakeMesh(
                new List<Vector3> { from, to, to + (Vector3.up * tall), from + (Vector3.up * tall) },
                new List<int> { 0, 1, 2, 0, 2, 3 }, colour);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _dimMaterial;
            return r;
        }

        private GameObject Part(string name, Mesh mesh, Material material) {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            _parts.Add(go);
            return go;
        }

        private static Color Rgba(float[] c) => c == null || c.Length < 4 ? Color.white : new Color(c[0], c[1], c[2], c[3]);

        // The layout's RGBA is sRGB, as authored; a vertex colour reaches the shader unconverted, so in a
        // linear project it is converted here or the stone rim reads near white and the tint far too strong.
        private static Color Vertex(float[] c) {
            Color colour = Rgba(c);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;
        }
    }
}
