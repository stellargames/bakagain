namespace BakAgain.World.Encounters {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// The Enhanced trap board's frame (TASK-972): a low stone rim along the near edge and both sides of the
    /// cells up to the exit row, and everything beside and before it dimmed. The far side stays open: past the
    /// exit row is the way out (owner, round 6: no back wall, the sides stop at the exit).
    /// </summary>
    /// <remarks>
    /// <para>Built once in <see cref="Bind"/>: the arena root is rebuilt on every redraw, and the grid does not
    /// change in a fight. Rides on the root only when the turn is Enhanced, as <see cref="ArenaBoardMarks"/> does.</para>
    /// <para><b>The dim is two tall walls and a flat apron.</b> The walls stand on the rim's outer edges, left and
    /// right, running from behind the camera to past the scenery, and are shown only from the board's side, so
    /// everything seen past them, scenery and sky alike, is dimmed while the board in front is not. The apron
    /// dims the ground between the camera and the near rim. Sprites/Default culls nothing, so the walls'
    /// culling is done here: a wall whose outside the camera is on is hidden, and a strip beside the board with a
    /// wall farther out takes its place (the trap camera can stand outside a side wall at its yaw limit). A combatant in front of a
    /// wall is not dimmed because its sprite writes depth (AlphaTest, ZWrite on), not because of the queue.</para>
    /// </remarks>
    public sealed class ArenaTrapFrame : MonoBehaviour {
        private const float LiftStep = 0.015f;          // ArenaBoardMarks' layer step
        private const float DimReachCells = 400f;       // how far the walls and apron run: past the scenery
        // ponytail: fixed side margin; the trap camera was measured at most 7.5 cells outside a side wall
        // (yaw 15, pitch 25, farthest zoom, phone 844x390). A pose past it leaves that side undimmed again.
        private const float SideReachCells = 12f;
        private const int DimQueue = 2990, FrameQueue = 3000;

        private Func<Camera> _camera;
        private Material _material, _dimMaterial;
        private readonly List<Mesh> _meshes = new();
        private readonly List<GameObject> _parts = new();
        private Renderer _left, _right;
        // What stands in for a side wall once the camera is outside it: a floor strip and a wall farther out.
        private Renderer[] _leftOuter, _rightOuter;
        private float _dimLift;

        // The board frame (across, away) to the world: the world centre of cell (0,0) and one cell along each axis.
        private Vector3 _origin, _across, _away;
        private double _cellSize, _originX, _originY;
        private double _x0, _x1;   // the rim's outer edges

        /// <summary>The drawn parts' names, for tests: TrapRim and TrapOutsideDim.</summary>
        public IEnumerable<string> DrawnKinds() {
            foreach (GameObject part in _parts) {
                if (part != null && part.activeSelf) {
                    yield return part.name;
                }
            }
        }

        /// <param name="boardCorners"><see cref="ArenaOrbit.BoardCorners"/> up to the exit row: the outer corners
        /// of the cells the party crosses, the far edge being the exit row's.</param>
        /// <param name="cellCentreWorld">A cell's centre in the world (<c>HotspotService.CellCentreWorld</c>).</param>
        /// <param name="camera">The world camera, for the walls' culling; without one every wall shows.</param>
        public void Bind((double X, double Y)[] boardCorners, Func<int, int, Vector3?> cellCentreWorld,
            EnhancedCombatLayout layout, float unitsPerCell, Func<Camera> camera = null) {
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

            double rim = layout.TrapRimCells * _cellSize;
            double ix0 = boardCorners[0].X, ix1 = boardCorners[1].X, iy0 = boardCorners[0].Y, y1 = boardCorners[2].Y;
            _x0 = ix0 - rim; _x1 = ix1 + rim;
            double oy0 = iy0 - rim;

            // The rim: three raised strips, left, right and near, each its top and its sides.
            float height = layout.TrapRimCells * u;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (Vector3[] strip in new[] { Rect(_x0, oy0, ix0, y1), Rect(ix1, oy0, _x1, y1), Rect(ix0, oy0, ix1, iy0) }) {
                Vector3[] top = Up(strip, height);
                int at = verts.Count;
                verts.AddRange(top);
                tris.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
                Band(strip, top, verts, tris);
            }
            Part("TrapRim", MakeMesh(verts, tris, Vertex(layout.TrapRimRgba)), _material);

            // The dim: the apron on the ground before the board, then the two side walls.
            float lift = (layout.MarkLiftCells * u) - LiftStep;
            _dimLift = lift;
            double reach = DimReachCells * _cellSize;
            float tall = DimReachCells * u;
            Color dim = Vertex(layout.TrapOutsideDimRgba);
            GameObject dimRoot = Part("TrapOutsideDim", Quad(Rect(_x0, oy0 - reach, _x1, oy0), lift, dim), _dimMaterial);
            // The side walls run on past the exit row: ending there left a hard edge in the sky (seen live), and
            // running on they leave the way out ahead a lit corridor.
            double fy = y1 + reach;
            _left = Wall(dimRoot, "Left", ToWorld(_x0, oy0 - reach), ToWorld(_x0, fy), tall, dim);
            _right = Wall(dimRoot, "Right", ToWorld(_x1, oy0 - reach), ToWorld(_x1, fy), tall, dim);
            double side = SideReachCells * _cellSize, sx0 = _x0 - side, sx1 = _x1 + side;
            _leftOuter = new[] {
                Floor(dimRoot, "LeftFloor", Rect(sx0, oy0 - reach, _x0, fy), lift, dim),
                Wall(dimRoot, "LeftOuter", ToWorld(sx0, oy0 - reach), ToWorld(sx0, fy), tall, dim) };
            _rightOuter = new[] {
                Floor(dimRoot, "RightFloor", Rect(_x1, oy0 - reach, sx1, fy), lift, dim),
                Wall(dimRoot, "RightOuter", ToWorld(sx1, oy0 - reach), ToWorld(sx1, fy), tall, dim) };
            Outside(_leftOuter, false);
            Outside(_rightOuter, false);
        }

        // Back-face culling for the walls, by which side of each the camera stands.
        private void LateUpdate() {
            Camera cam = _camera?.Invoke();
            if (cam == null || _left == null) {
                return;
            }
            Vector3 d = cam.transform.position - _origin;
            double x = _originX + (_cellSize * Vector3.Dot(d, _across) / _across.sqrMagnitude);
            // Outside a side wall, that wall would face away and hide; the strip beside the board and a wall
            // farther out take its place, so nothing is dimmed twice.
            _left.enabled = x > _x0;
            _right.enabled = x < _x1;
            Outside(_leftOuter, !_left.enabled);
            Outside(_rightOuter, !_right.enabled);
        }

        private void OnDestroy() {
            foreach (Mesh m in _meshes) {
                Destroy(m);
            }
            foreach (Material m in new[] { _material, _dimMaterial }) {
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

        private static void Outside(Renderer[] parts, bool on) {
            foreach (Renderer r in parts) {
                r.enabled = on;
            }
        }

        private Renderer Floor(GameObject parent, string name, Vector3[] rect, float lift, Color colour) {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad(rect, lift, colour);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _dimMaterial;
            return r;
        }

        private Renderer Wall(GameObject parent, string name, Vector3 from, Vector3 to, float tall, Color colour) {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MakeMesh(
                // The walls stand on the dim floor's height, not the ground: from the ground up they and the
                // floor's edge both covered a thin band, a dark hairline along every join (seen live).
                new List<Vector3> { from + (Vector3.up * _dimLift), to + (Vector3.up * _dimLift),
                    to + (Vector3.up * tall), from + (Vector3.up * tall) },
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

        // The layout's RGBA is sRGB, as authored; a vertex colour reaches the shader unconverted, so in a
        // linear project it is converted here or the stone rim reads near white.
        private static Color Vertex(float[] c) {
            Color colour = c == null || c.Length < 4 ? Color.white : new Color(c[0], c[1], c[2], c[3]);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;
        }
    }
}
