namespace BakAgain.UI.Rest {
    using BakAgain.Graphics;
    using GameData.Resources.Config;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The shadow a sundial's gnomon casts across the rest dial —
    /// <c>encamp_drawSundialShadow</c> @0x70c9b.
    /// </summary>
    /// <remarks>
    /// <b>A filled triangle, which is why it could not be another positioned sprite.</b> The
    /// original fills a three-point polygon in pen 0: the gnomon's tip, the dial's centre, and a
    /// third vertex that walks the arc as the sun crosses the sky. UI Toolkit will not draw an
    /// arbitrary polygon from styles, so this goes through <c>generateVisualContent</c> — the
    /// framework's own seam for exactly this, and cheaper than a shader or twenty-four
    /// pre-rendered sprites.
    ///
    /// <para><b>AND IT IS NOT A FILL.</b> The original swaps the polygon filler's span callback for
    /// one that reads each pixel and writes <c>table[pixel]</c> back — a per-colour SUBSTITUTION
    /// that darkens palette entries 0..111 and leaves everything above them alone (see
    /// <see cref="EncampShadow"/>). A flat triangle, translucent or not, washes the moon and the
    /// stars with the ground and darkens uniformly where the palette has nothing darker to give.
    /// So the wedge is drawn as a TEXTURED triangle sampling a pre-shaded copy of the artwork at
    /// the same place, which is the same picture with no per-frame work.</para>
    ///
    /// <para>A flat fill remains as the fallback for a caller with no artwork to shade — better a
    /// wedge in the wrong shade than no wedge — and says so at the call site.</para>
    ///
    /// <para>Which arc point, and when there is no shadow at all, is
    /// <see cref="EncampDial.ShadowArcPointFor"/>: only between dawn and dusk, and never at noon.</para>
    /// </remarks>
    public sealed class RestDialShadow : VisualElement {
        /// <summary>Name of the shadow layer, so a rebuild replaces it rather than stacking.</summary>
        public const string LayerName = "BakRestDialShadow";

        private Vector2 _apex;
        private Vector2 _pivot;
        private Vector2 _tip;
        private Color _ink = Color.black;
        private bool _visible;
        private Texture2D _shadedArt;

        public RestDialShadow() {
            name = LayerName;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>
        /// Points the shadow at the time of day, or hides it when the sun is down.
        /// </summary>
        /// <param name="ticksOfDay">Time within the day, in the clock's two-second units.</param>
        /// <returns>Whether a shadow is drawn at all.</returns>
        /// <param name="shadedArt">
        /// The screen's artwork with the remap already applied, drawn through the wedge. Null falls
        /// back to a flat fill.
        /// </param>
        public bool SetTime(EncampData encamp, int ticksOfDay,
            GameData.Resources.Palette.PaletteResource palette, Texture2D shadedArt = null) {
            _shadedArt = shadedArt;
            int arcPoint = EncampDial.ShadowArcPointFor(ticksOfDay);
            _visible = encamp?.NeedleEntries != null
                && arcPoint >= 0
                && arcPoint < encamp.NeedleEntries.Count;

            if (_visible) {
                _apex = PointAt(encamp, EncampDial.ShadowApexEntry);
                _pivot = PointAt(encamp, EncampDial.ShadowPivotEntry);
                _tip = PointAt(encamp, arcPoint);
                _ink = PaletteColors.ResolvePen(palette, EncampDial.ShadowPen, Color.black);
            }

            MarkDirtyRepaint();

            return _visible;
        }

        private static Vector2 PointAt(EncampData encamp, int index) {
            EncampPoint p = encamp.NeedleEntries[index];

            return new Vector2(p.X, p.Y);
        }

        private void OnGenerateVisualContent(MeshGenerationContext context) {
            if (!_visible) {
                return;
            }

            if (_shadedArt != null) {
                DrawShaded(context);

                return;
            }

            Painter2D painter = context.painter2D;
            painter.fillColor = _ink;
            painter.BeginPath();
            painter.MoveTo(_apex);
            painter.LineTo(_pivot);
            painter.LineTo(_tip);
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>
        /// The wedge as a textured triangle over the shaded artwork.
        /// </summary>
        /// <remarks>
        /// <b>The UVs come from the vertices' own canonical positions.</b> This element covers the
        /// whole stage and the artwork is drawn across the whole canonical frame, so a vertex at
        /// canonical (x, y) samples exactly the pixel it sits on — which is what makes the wedge
        /// reveal the shaded copy in place rather than a shifted piece of it.
        ///
        /// <para>UVs are remapped through <c>uvRegion</c> because UI Toolkit may have atlased the
        /// texture; sampling raw 0..1 would read a neighbour's pixels.</para>
        /// </remarks>
        private void DrawShaded(MeshGenerationContext context) {
            MeshWriteData mesh = context.Allocate(3, 3, _shadedArt);
            Rect region = mesh.uvRegion;

            WriteVertex(mesh, _apex, region);
            WriteVertex(mesh, _pivot, region);
            WriteVertex(mesh, _tip, region);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
        }

        private static void WriteVertex(MeshWriteData mesh, Vector2 point, Rect region) {
            var uv = new Vector2(
                point.x / Canonical.Width,
                1f - (point.y / Canonical.Height));

            mesh.SetNextVertex(new Vertex {
                position = new Vector3(point.x, point.y, Vertex.nearZ),
                tint = Color.white,
                uv = new Vector2(
                    region.x + (uv.x * region.width),
                    region.y + (uv.y * region.height)),
            });
        }
    }
}
