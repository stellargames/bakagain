namespace BakAgain.World.Rendering {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using UnityEngine;

    /// <summary>
    /// The world's surface shaders, in two sets: <c>Classic*</c> for the faithful game and <c>Enhanced*</c>
    /// for Enhanced mode (owner, 2026-10-19). Both sets compile the same pass (<c>*Pass.hlsl</c>); only the
    /// Enhanced one defines <c>BAK_ENHANCED</c>, so no Enhanced code reaches the faithful shaders.
    /// </summary>
    /// <remarks>
    /// <b>The Enhanced switch is live,</b> so the set is not fixed when a zone is built: every material made
    /// here (or copied from one, then passed to <see cref="Track"/>) has its shader swapped for its twin
    /// when <see cref="Follow"/> sees the switch change. Twins share property names, so a swap keeps the
    /// material's textures and values. The individual Enhanced features stay runtime switches inside the
    /// Enhanced shaders.
    /// </remarks>
    public static class WorldShaders {
        public enum Surface { Terrain, Polygon, Sprite, TexturedPolygon, Horizon }

        private static Shader[] s_classic;
        private static Shader[] s_enhanced;
        private static readonly List<Material> s_tracked = new();
        private static int s_pruneAt = 256;

        /// <summary>The set the tracked materials are in.</summary>
        public static bool Enhanced { get; private set; }

        /// <summary>A new material on <paramref name="surface"/>'s shader for the current mode, tracked.</summary>
        public static Material Create(Surface surface) {
            Follow(GameOptions.Enhanced);
            return Track(new Material(Set(Enhanced)[(int)surface]));
        }

        /// <summary>Puts a material made from a tracked one (<c>new Material(m)</c>) under the swap too.</summary>
        public static Material Track(Material material) {
            if (s_tracked.Count >= s_pruneAt) { // materials die with their zone; forget them
                s_tracked.RemoveAll(m => m == null);
                s_pruneAt = Math.Max(256, s_tracked.Count * 2);
            }
            s_tracked.Add(material);
            return material;
        }

        /// <summary>Moves every tracked material to the set for <paramref name="enhanced"/>. Cheap when
        /// nothing changed, so the lighting refresh calls it every frame.</summary>
        public static void Follow(bool enhanced) {
            if (enhanced == Enhanced) return;
            Enhanced = enhanced;
            Shader[] from = Set(!enhanced), to = Set(enhanced);
            s_tracked.RemoveAll(m => m == null);
            foreach (Material m in s_tracked) {
                int i = Array.IndexOf(from, m.shader);
                if (i < 0) continue;
                int queue = m.renderQueue; // the layered terrain materials set their own
                m.shader = to[i];
                m.renderQueue = queue;
            }
        }

        private static Shader[] Set(bool enhanced) {
            s_classic ??= Find("Classic");
            s_enhanced ??= Find("Enhanced");
            return enhanced ? s_enhanced : s_classic;
        }

        private static Shader[] Find(string prefix) =>
            Array.ConvertAll((Surface[])Enum.GetValues(typeof(Surface)), s => Shader.Find($"BakAgain/{prefix}{s}"));
    }
}
