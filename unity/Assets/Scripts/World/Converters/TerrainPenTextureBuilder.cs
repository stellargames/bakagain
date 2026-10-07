namespace BakAgain.World.Converters {
    using BakAgain.ResourceManagement.Converters;
    using GameData.Resources.World;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Identifies a terrain pen material slot. Each value maps to a unique tileable
    /// texture and material. Faces with the same <see cref="TerrainPen"/> share a
    /// sub-mesh and material within a terrain entity.
    /// </summary>
    public enum TerrainPen {
        /// <summary>Flat vertex-color fill — no texture, use face color.</summary>
        FlatFill = -1,
        Ground = 0,
        Road = 1,
        Path = 2,
        River = 3,
        Dirt = 4,
        Waterfall = 5,
        Horizon1 = 6,
        Horizon2 = 7,
        /// <summary>LOD-2 ground variants (pens 0xE0..0xE7, 0xF7..0xFF).
        /// All share one material since their SCX strips are nearly identical.</summary>
        GroundLod = 8,
    }

    /// <summary>
    /// Bakes the tileable terrain textures from the player's own <c>Z##L.SCX</c> and maps TBL
    /// face pen colors to <see cref="TerrainPen"/> values. <see cref="TerrainPen.FlatFill"/> has
    /// no texture.
    /// </summary>
    /// <remarks>
    /// <b>Per zone</b>: the original loads the strip image of the zone it enters
    /// (<c>zone_load_scx_image</c>, ZONE.C:355). This baked Z01L.SCX once for the whole session, so
    /// zone 9's brown dirt was drawn as zone 1's grass (TASK-745).
    /// </remarks>
    public static class TerrainPenTextures {
        private const float NoiseAmplitude = 0.015f;

        /// <summary>The strip image and the palette it is drawn in, for one zone.</summary>
        public static (string Scx, string Palette) SourceKeys(int zone) =>
            ($"Z{zone:D2}L.SCX", $"Z{zone:D2}.PAL");

        /// <summary>Each pen's strip of the SCX: first DOS row and height. Order matters — the
        /// strips are baked in this order from one seeded random sequence.</summary>
        private static readonly (TerrainPen Pen, int DosRow, int Height)[] Strips = {
            (TerrainPen.Ground, 10, 55),
            (TerrainPen.Road, 70, 20),
            (TerrainPen.Path, 110, 30),
            (TerrainPen.River, 162, 27),
            (TerrainPen.Dirt, 142, 20),
            (TerrainPen.Waterfall, 90, 20),
            (TerrainPen.Horizon1, 0, 48),
            (TerrainPen.Horizon2, 50, 49),
            (TerrainPen.GroundLod, 0, 50),
        };

        private static readonly Dictionary<int, Dictionary<TerrainPen, Texture2D>> _baked = new();

        /// <summary>
        /// Every textured pen's texture for <paramref name="zone"/>, baked once per zone per session.
        /// If the SCX cannot be loaded each pen falls back to white.
        /// </summary>
        public static Dictionary<TerrainPen, Texture2D> LoadAll(int zone = 1) {
            if (_baked.TryGetValue(zone, out Dictionary<TerrainPen, Texture2D> cached)) {
                return cached;
            }
            var baked = new Dictionary<TerrainPen, Texture2D>();
            _baked[zone] = baked;
            (string scxKey, string paletteKey) = SourceKeys(zone);
            UnityEngine.Color[] scx = ScxTileBaker.LoadPixels(scxKey, paletteKey);
            var rng = new System.Random(42);
            foreach (var (pen, dosRow, height) in Strips) {
                baked[pen] = scx == null
                    ? Texture2D.whiteTexture
                    : ScxTileBaker.Bake(scx, 0, dosRow, 320, height, NoiseAmplitude, rng);
            }
            return baked;
        }

        /// <summary>
        /// Map a TBL face pen color byte to the corresponding <see cref="TerrainPen"/>.
        /// </summary>
        public static TerrainPen PenFromFaceColor(byte faceColor) {
            // WHICH pens sample the terrain strip is RE knowledge and lives on PolygonFace now.
            // What is left here is the port's material choice, which is ours to make:
            switch (PolygonFace.StripSamplingFor(faceColor)) {
                case TerrainStripSampling.None:
                    return TerrainPen.FlatFill;
                case TerrainStripSampling.LevelOfDetail:
                    // The 0xE0-0xE7 and 0xF7-0xFF ramps are two parallel 8-entry shade ramps in the
                    // original; we give them ONE material because their SCX strips are nearly
                    // identical. A deliberate simplification, not something the data says.
                    return TerrainPen.GroundLod;
                default:
                    // Likewise 3/4 and 6/7 are distinct pens that we pair onto one material each.
                    return faceColor switch {
                        0x00 => TerrainPen.Ground,
                        0x01 => TerrainPen.Road,
                        0x02 => TerrainPen.Path,
                        0x03 or 0x04 => TerrainPen.River,
                        0x05 => TerrainPen.Dirt,
                        0x06 or 0x07 => TerrainPen.Waterfall,
                        0x08 => TerrainPen.Horizon1,
                        _ => TerrainPen.Horizon2,
                    };
            }
        }
    }
}
