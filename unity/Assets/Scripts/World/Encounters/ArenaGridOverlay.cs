namespace BakAgain.World.Encounters {
    using System;
    using BakAgain.World.Converters;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// The combat tactical overlay — the 8x13 grid outline and the crystal links, toggled with G.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are one feature in the original.</b> <c>renderCombatGridScene</c> @0x5DEA8
    /// calls <c>drawCombatGridOutline</c> @0x2E8A4 and <c>crystalChain_drawLinks</c> @0x2FD88 behind
    /// a single flag, <c>combatGridOverlayShown</c>, which the combat command dispatcher xors on
    /// action 34. So the grid and the links appear and disappear together, and are drawn here by one
    /// component for the same reason.
    ///
    /// <para><b>Off by default</b>, as the original's flag is — but the ACTING-CELL ring it also
    /// draws is not part of that flag. The grid is a ruler the player asks for; the ring says whose
    /// turn it is, which they need every turn (TASK-435).</para>
    ///
    /// <para><b>Rebuilt only when the segments change.</b> The arena is torn down and rebuilt on
    /// every combat redraw, so this rides on the arena root and rebuilds with it; between redraws
    /// the lines are static and toggling only flips their visibility.</para>
    ///
    /// <para>Colours follow the original: the grid is drawn in the arena's green and the links in
    /// pen 175, a near-white. They are deliberately different — the links are information about the
    /// puzzle, the grid is a ruler.</para>
    /// </remarks>
    public sealed class ArenaGridOverlay : MonoBehaviour {
        /// <summary>
        /// How thick a grid line is, in the ORIGINAL's pixels — one, like every line it draws.
        /// </summary>
        /// <remarks>
        /// <b>A fixed world-space width was the bug.</b> <c>LineWidth = 0.05f</c> in world units
        /// came out around one device pixel at 1280x1024 and foreshortened with depth, so the
        /// axis-aligned rows survived and the near-diagonal columns stippled into dots — the grid
        /// stopped reading as 13x8 squares at all. The original draws in SCREEN space: every line is
        /// one VGA pixel whatever its distance.
        ///
        /// <para>So the width is computed per endpoint instead, from the camera, to land on this
        /// many device pixels wherever that end happens to be. One VGA pixel is
        /// <c>screenHeight / 200</c> device pixels, which is also six canonical units — the same
        /// conversion the rest of the UI uses.</para>
        /// </remarks>
        private const float LineWidthVgaPixels = 1f;

        /// <summary>The original's vertical resolution, which one VGA pixel is measured against.</summary>
        private const float VgaHeight = 200f;

        /// <summary>
        /// How far above the ground the grid is drawn, in BaK units.
        /// </summary>
        /// <remarks>
        /// <b>Coplanar lines z-fight, and the symptom looks exactly like a line that is too thin.</b>
        /// Drawn at height 0 the grid shares the terrain's plane, so it wins the depth test only on
        /// some pixels and comes out DASHED — which is what the column lines were doing after their
        /// width was already correct.
        ///
        /// <para>A lift rather than a disabled depth test, because the original's grid is drawn
        /// BEFORE the actors (CACTOR.C walks the rows and draws combatants after the grid call), so
        /// it belongs under the sprites and over the ground — which is what a small offset gives and
        /// <c>ZTest Always</c> would not.</para>
        /// </remarks>
        private const int GroundLift = 6;

        private static readonly Color GridColour = new Color(0.35f, 0.85f, 0.35f);
        private static readonly Color LinkColour = new Color(0.95f, 0.95f, 0.92f);

        /// <summary>The ring under whoever is acting.</summary>
        /// <remarks>
        /// <b>Chosen to read well in the port, not matched to the original's pen.</b> The original
        /// draws this outline in the same green as its grid, which measures (52,130,69) in zone 1;
        /// ours is a brighter green picked to stay legible over grass and road alike. Graphics are
        /// being improved rather than reproduced — see the project note of 2026-09-12.
        /// </remarks>
        private static readonly Color ActingCellColour = new Color(0.45f, 1f, 0.55f);

        /// <summary>The ring under the enemy a click would hit.</summary>
        /// <remarks>
        /// <b>Yellow against the acting cell's green</b>, which is the pairing the original uses —
        /// the target outlines yellow while the actor's own stays green. Picked bright for the same
        /// reason the green above is: legible over grass and road alike (TASK-586).
        /// </remarks>
        private static readonly Color TargetCellColour = new Color(1f, 0.92f, 0.25f);

        private System.Func<List<(long X0, long Y0, long X1, long Y1, ArenaOverlayKind Kind)>> _segments;

        /// <summary>The ring's own source, asked every frame because it moves with the turn.</summary>
        private System.Func<List<(long X0, long Y0, long X1, long Y1, ArenaOverlayKind Kind)>>
            _actingCellSegments;

        /// <summary>Where the ring currently sits, so it is only rebuilt when it actually moves.</summary>
        private (long X, long Y, int Count)? _actingCellAnchor;
        private System.Func<bool> _togglePressed;

        /// <summary>Reads and writes the flag that OUTLIVES an arena redraw.</summary>
        /// <remarks>
        /// <b>This component is re-added to the arena root on every redraw, so its own
        /// <see cref="Visible"/> cannot be the flag.</b> The original's is
        /// <c>g_bCombatGridLinesEnabled</c>, a global — press G and the grid stays up across turns.
        /// Keeping the state here instead would drop it on the next redraw, which is one step per
        /// turn.
        /// </remarks>
        private System.Func<bool> _isEnabled;
        private System.Action<bool> _setEnabled;

        /// <summary>The camera the widths are computed against.</summary>
        private Camera _camera;
        private readonly List<GameObject> _lines = new();

        /// <summary>The acting-cell ring, which the G toggle does not own.</summary>
        private readonly List<GameObject> _actingCellLines = new();
        private bool _built;

        /// <summary>Whether the overlay is showing. Off to start, like the original's flag.</summary>
        public bool Visible { get; private set; }

        /// <summary>Bind the geometry source and the toggle edge.</summary>
        public void Bind(
            System.Func<List<(long X0, long Y0, long X1, long Y1, ArenaOverlayKind Kind)>> segments,
            System.Func<bool> togglePressed,
            System.Func<bool> isEnabled = null,
            System.Action<bool> setEnabled = null,
            Camera camera = null,
            System.Func<List<(long X0, long Y0, long X1, long Y1, ArenaOverlayKind Kind)>>
                actingCellSegments = null) {
            _segments = segments;
            _actingCellSegments = actingCellSegments;
            _togglePressed = togglePressed;
            _isEnabled = isEnabled;
            _setEnabled = setEnabled;
            _camera = camera;

            // Pick the flag back up after a redraw destroyed the previous instance.
            Apply(_isEnabled?.Invoke() ?? false);
        }

        private void Update() {
            // *** THE RING IS POLLED, NOT BUILT ONCE. *** Bind runs from the arena draw, which
            // happens BEFORE the encounter has decided whose turn it is — built there the ring came
            // out empty every time, which is how this was caught in the Editor rather than in a
            // test. Four segments a frame is nothing; rebuilding only when the anchor moves is what
            // keeps it from churning GameObjects.
            RefreshActingCell();

            if (_togglePressed == null || !_togglePressed()) {
                return;
            }
            Apply(!Visible);
            _setEnabled?.Invoke(Visible);
        }

        private void RefreshActingCell() {
            if (_actingCellSegments == null) {
                return;
            }
            List<(long X0, long Y0, long X1, long Y1, ArenaOverlayKind Kind)> segments =
                _actingCellSegments();
            // *** THE ANCHOR HAS TO NOTICE THE TARGET RING, NOT JUST THE ACTING ONE. *** This
            // compared the first segment alone, which is the acting cell -- so with the actor
            // standing still the rebuild was skipped and the yellow ring could not follow the
            // cursor from one enemy to the next. Counting the segments and taking the LAST one's
            // start covers both rings without hashing the lot (TASK-586).
            (long X, long Y, int Count)? anchor = segments.Count > 0
                ? (segments[0].X0, segments[^1].X1, segments.Count)
                : ((long, long, int)?)null;
            if (Nullable.Equals(anchor, _actingCellAnchor)) {
                return;
            }
            _actingCellAnchor = anchor;

            foreach (GameObject line in _actingCellLines) {
                if (line != null) {
                    Destroy(line);
                }
            }
            _actingCellLines.Clear();
            if (segments.Count == 0) {
                return;
            }
            var material = new Material(Shader.Find("Sprites/Default"));
            foreach ((long x0, long y0, long x1, long y1, ArenaOverlayKind kind) in segments) {
                _actingCellLines.Add(CreateLine(x0, y0, x1, y1, kind, material));
            }
        }

        /// <summary>Show or hide the overlay, building it the first time it is asked for.</summary>
        private void Apply(bool visible) {
            Visible = visible;
            // *** THE ACTING-CELL RING IS NOT PART OF THE TOGGLE. *** The grid and the links are one
            // feature behind one flag and default to off; the ring says whose turn it is and where
            // they stand, which the player needs every turn. So the overlay is built on bind rather
            // than on first reveal, and the ring's lines ignore Visible.
            if (!_built) {
                Build();
            }
            foreach (GameObject line in _lines) {
                if (line != null) {
                    line.SetActive(Visible);
                }
            }
            foreach (GameObject line in _actingCellLines) {
                if (line != null) {
                    line.SetActive(true);
                }
            }
        }

        // Built on first reveal rather than on bind: a fight that never asks for the overlay — which
        // is most of them, the flag being off by default — pays nothing for it.
        private void Build() {
            _built = true;
            if (_segments == null) {
                return;
            }
            var material = new Material(Shader.Find("Sprites/Default"));
            foreach ((long x0, long y0, long x1, long y1, ArenaOverlayKind kind) in _segments()) {
                (kind == ArenaOverlayKind.ActingCell ? _actingCellLines : _lines)
                    .Add(CreateLine(x0, y0, x1, y1, kind, material));
            }
        }

        private GameObject CreateLine(long x0, long y0, long x1, long y1,
            ArenaOverlayKind kind, Material material) {
            {
                var go = new GameObject(kind switch {
                    ArenaOverlayKind.Link => "CrystalLink",
                    ArenaOverlayKind.ActingCell => "ActingCellEdge",
                    ArenaOverlayKind.TargetCell => "TargetCellEdge",
                    _ => "GridLine",
                });
                go.transform.SetParent(transform, worldPositionStays: false);
                var line = go.AddComponent<LineRenderer>();
                line.material = material;
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.numCornerVertices = 0;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                Color colour = kind switch {
                    ArenaOverlayKind.Link => LinkColour,
                    ArenaOverlayKind.ActingCell => ActingCellColour,
                    ArenaOverlayKind.TargetCell => TargetCellColour,
                    _ => GridColour,
                };
                line.startColor = colour;
                line.endColor = colour;
                Vector3 a = BakCoordinateConverter.ConvertPosition((int)x0, (int)y0, GroundLift);
                Vector3 bEnd = BakCoordinateConverter.ConvertPosition((int)x1, (int)y1, GroundLift);
                line.SetPositions(new[] { a, bEnd });

                // *** ONE VGA PIXEL AT EACH END, NOT ONE WORLD-SPACE WIDTH ALONG THE WHOLE LINE. ***
                // A grid line runs away from the camera, so a single world width is too fat near
                // and sub-pixel far — which is what broke the column lines into dots. Setting the
                // start and end widths separately makes the rendered thickness constant on screen,
                // the way the original's screen-space lines are.
                line.widthMultiplier = 1f;
                float wa = WorldWidthAt(go.transform.TransformPoint(a));
                float wb = WorldWidthAt(go.transform.TransformPoint(bEnd));
                line.widthCurve = new AnimationCurve(
                    new Keyframe(0f, wa), new Keyframe(1f, wb));
                return go;
            }
        }

        /// <summary>
        /// The world-space width that projects to <see cref="LineWidthVgaPixels"/> VGA pixels at
        /// this point.
        /// </summary>
        /// <remarks>
        /// Perspective: a span of <c>w</c> world units at distance <c>d</c> covers
        /// <c>w * H / (2 d tan(fov/2))</c> device pixels, so solving for <c>w</c> gives this. An
        /// orthographic camera has no distance term — its world-per-pixel is fixed.
        ///
        /// <para>Falls back to the old constant when there is no camera, which is what a headless
        /// test has; the lines are not looked at there.</para>
        /// </remarks>
        private float WorldWidthAt(Vector3 worldPoint) {
            const float Fallback = 0.05f;
            if (_camera == null || Screen.height <= 0) {
                return Fallback;
            }

            float devicePixels = LineWidthVgaPixels * (Screen.height / VgaHeight);
            if (_camera.orthographic) {
                return devicePixels * (2f * _camera.orthographicSize) / Screen.height;
            }

            float distance = Vector3.Dot(worldPoint - _camera.transform.position,
                _camera.transform.forward);
            if (distance <= 0.01f) {
                return Fallback;
            }

            float worldPerPixel = 2f * distance
                * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

            return devicePixels * worldPerPixel;
        }
    }
}
