namespace BakAgain.World.Rendering {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Utility;
    using ResourceExtraction.Imaging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// A mod's HD picture for one billboard frame (mod system spec 4.2, HD billboard extension): the
    /// soft-lit <see cref="Base"/>, and the four sun-lit versions the Enhanced sprite shader blends by
    /// where the sun stands. A side the mod does not ship is null.
    /// </summary>
    public sealed class HdFrame {
        public Texture2D Base;
        public Texture2D Front, Left, Right, Back;

        /// <summary>Per-instance size and colour variation from the frame's <c>&lt;i&gt;.json</c>, 0 = none.</summary>
        public float VarySize, VaryColor;

        /// <summary>All four sun-lit versions are there; the shader blends none unless it has them all.</summary>
        public bool HasSun => Front != null && Left != null && Right != null && Back != null;
    }

    /// <summary>
    /// The HD billboard frames the active mods carry, read once per frame key: the mods are fixed until
    /// the next start.
    /// </summary>
    public static class HdFrames {
        /// <summary>The cut-out threshold HD frames are drawn and mipmapped at.</summary>
        public const float AlphaCutoff = 0.5f;

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(HdFrames));
        private static readonly Dictionary<string, HdFrame> Cache = new();
        private static readonly string[] Sides = { "front", "left", "right", "back" };

        /// <summary>
        /// The mod's picture for <paramref name="resourceKey"/> (<c>Z01SLOT0.BMX#3</c>, or a creature
        /// in a colour set, <c>MOR1_CS2#7</c>): <c>BMX/&lt;stem&gt;_CS&lt;n&gt;/&lt;i&gt;.png</c>, else
        /// <c>BMX/&lt;stem&gt;/&lt;i&gt;.png</c>, with its <c>&lt;i&gt;.front.png</c> (and left, right,
        /// back) beside it. A broken file costs only that frame.
        /// </summary>
        public static bool TryLoad(string resourceKey, out HdFrame frame) {
            if (!Cache.TryGetValue(resourceKey, out frame)) {
                try {
                    frame = Load(resourceKey);
                } catch (Exception e) {
                    Logger.LogError("HD frame for {Key} skipped: {Reason}", resourceKey, e.Message);
                    frame = null;
                }
                Cache[resourceKey] = frame;
            }
            return frame != null;
        }

        /// <summary>For tests only: forget (and free) every frame read.</summary>
        internal static void ClearCache() {
            foreach (HdFrame f in Cache.Values.Where(f => f != null)) {
                foreach (Texture2D t in new[] { f.Base, f.Front, f.Left, f.Right, f.Back }) {
                    UnityObjectUtil.Destroy(t);
                }
            }
            Cache.Clear();
        }

        private static HdFrame Load(string resourceKey) {
            int hash = resourceKey.IndexOf('#');
            if (hash < 0) return null;
            string stem = Path.GetFileNameWithoutExtension(resourceKey[..hash]);
            string index = resourceKey[(hash + 1)..];
            (string shared, int colorSet) = CreatureColorSet.ParseVariantKey(stem);
            string path = null;
            foreach (string set in colorSet >= 0 ? new[] { stem, shared } : new[] { stem }) {
                // The highest layer's file; the per-root alias fallback included.
                if (!OverrideResourceLocator.TryMapKey($"{set}.BMX#{index}", typeof(Texture2D), out string directory,
                        out IReadOnlyList<string> filenames)) {
                    return null;
                }
                path = OverrideResourceLocator.FindAll(directory, filenames[0]).LastOrDefault();
                if (path != null) break;
            }
            if (path == null) return null;

            // The sun-lit versions and the sidecar sit beside the file found, so an alias brings them too.
            string sibling = path[..^".png".Length];
            OverrideResourceProvider.MetaData? meta = OverrideResourceProvider.ReadMetaData(path);
            Texture2D image = Build(path);
            Texture2D[] sides = Sides.Select(side => File.Exists($"{sibling}.{side}.png") ? Build($"{sibling}.{side}.png") : null).ToArray();
            return new HdFrame {
                Base = image,
                Front = sides[0], Left = sides[1], Right = sides[2], Back = sides[3],
                VarySize = (float)(meta?.VarySize ?? 0),
                VaryColor = (float)(meta?.VaryColor ?? 0),
            };
        }

        private static Texture2D Build(string path) {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true) {
                name = Path.GetFileName(path),
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!t.LoadImage(File.ReadAllBytes(path))) {
                UnityObjectUtil.Destroy(t);
                throw new InvalidDataException($"'{path}' is not a decodable PNG.");
            }
            t.Apply(updateMipmaps: true);
            PreserveCoverage(t, AlphaCutoff);
            t.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return t;
        }

        /// <summary>
        /// Plain mips average alpha, so a cut-out thins and vanishes with distance. Scales each mip's
        /// alpha so as much of it reaches <paramref name="cutoff"/> as of the full-size image.
        /// </summary>
        internal static void PreserveCoverage(Texture2D t, float cutoff) {
            float threshold = cutoff * 255;
            float coverage = Coverage(t.GetPixels32(0), threshold, 1f);
            for (int mip = 1; mip < t.mipmapCount; mip++) {
                Color32[] px = t.GetPixels32(mip);
                // Binary search for the smallest alpha scale that covers as much as mip 0.
                float lo = 0.5f, hi = 8f;
                for (int i = 0; i < 14; i++) {
                    float scale = (lo + hi) / 2;
                    if (Coverage(px, threshold, scale) < coverage) lo = scale;
                    else hi = scale;
                }
                // Rounded, not truncated: a truncated 127.9 would drop below the cutoff it was scaled to reach.
                for (int i = 0; i < px.Length; i++) {
                    px[i].a = (byte)Mathf.Min(255, Mathf.Round(px[i].a * hi));
                }
                t.SetPixels32(px, mip);
            }
        }

        private static float Coverage(Color32[] px, float threshold, float scale) {
            int n = 0;
            foreach (Color32 c in px) {
                if (c.a * scale >= threshold) n++;
            }
            return n / (float)px.Length;
        }
    }
}
