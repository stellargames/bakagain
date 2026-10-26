namespace BakAgain.UI {
    using BakAgain.ResourceManagement;
    using BakAgain.Core;
    using Cysharp.Threading.Tasks;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>The Enhanced screens' shared art: the HD stone, marble, copper and parchment at the
    /// Addressables addresses <c>Enhanced/Stone/*</c> (see <c>docs/asset-origins.md</c>).</summary>
    public static class EnhancedArt {
        public const string Folder = "Enhanced/Stone/";

        private static readonly Dictionary<string, Texture2D> Loaded = new();
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(EnhancedArt));

        /// <summary>
        /// Loads every <see cref="Folder"/> address the catalog has, so <see cref="Texture"/> finds
        /// them loaded: screens are often laid out inside another load's <c>Completed</c> callback,
        /// where a synchronous load throws. One that fails (a mod's broken PNG) is drawn as nothing,
        /// as a missing texture always was, and never stops the boot.
        /// </summary>
        public static async UniTask PreloadAsync() {
            string[] addresses = Addressables.ResourceLocators.SelectMany(l => l.Keys).OfType<string>()
                .Where(k => k.StartsWith(Folder, System.StringComparison.Ordinal)).Distinct().ToArray();
            // ponytail: one at a time; in parallel, a focused run of BuiltInAssetOverrideTests races the
            // scene boot's own preload. Parallel once tests can wait for the boot.
            foreach (string address in addresses) {
                await PreloadOneAsync(address);
            }
        }

        private static async UniTask PreloadOneAsync(string address) {
            string name = address[Folder.Length..];
            AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(address);
            try {
                Loaded[name] = await handle.ToUniTask();
            } catch (System.Exception e) {
                Logger.LogError("Enhanced art '{Address}' failed to load, drawn as nothing: {Reason}", address, e.Message);
                Loaded[name] = null;
                if (handle.IsValid()) {
                    Addressables.Release(handle);
                }
            }
        }

        /// <summary>
        /// Loaded once: the screens ask for the same few textures on every redraw. A failed preload
        /// stays drawn as nothing; a texture destroyed since (fake null) is loaded again.
        /// </summary>
        public static Texture2D Texture(string name) {
            // A real null is a failed preload; a destroyed texture compares equal to null but is not one.
            if (Loaded.TryGetValue(name, out Texture2D t) && (t is null || t != null)) {
                return t;
            }
            // Not preloaded (a test building a screen without the boot). Inside another load's
            // Completed callback WaitForCompletion throws; the boot's preload keeps the game out of
            // here, so that is drawn as nothing, once, rather than breaking the screen.
            AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(Folder + name);
            try {
                t = handle.ResultOrWait();
            } catch (System.Exception e) {
                Logger.LogWarning("Enhanced art '{Name}' could not be loaded here, drawn as nothing: {Reason}", name, e.Message);
                t = null;
                if (handle.IsValid()) {
                    Addressables.Release(handle);
                }
            }
            return Loaded[name] = t;
        }

        public static StyleBackground Load(string name) {
            Texture2D t = Texture(name);
            return t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }
    }

    /// <summary>The Enhanced screens' shared geometry.</summary>
    public static class EnhancedSheet {
        /// <summary>Scales and moves <paramref name="sheet"/> (laid out in canonical units) so its
        /// <paramref name="region"/> fits <paramref name="field"/>, keeping its shape.</summary>
        public static void Fit(VisualElement sheet, Rect field, Rect region) {
            if (sheet == null || field.width <= 0f || field.height <= 0f || float.IsNaN(field.width)) {
                return;
            }
            (float scale, float x, float y) = GameData.Resources.Layout.FieldFit.Fit(
                region.x, region.y, region.width, region.height, field.x, field.y, field.width, field.height);
            sheet.style.translate = new Translate(x, y);
            sheet.style.scale = new Scale(new Vector2(scale, scale));
        }

        /// <summary>The rect round both; an empty <paramref name="a"/> is nothing yet.</summary>
        public static Rect Union(Rect a, Rect b) => a.width <= 0f && a.height <= 0f ? b
            : Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }

    /// <summary>
    /// FRAME.SCX's frame for an Enhanced screen, as ONE piece of stone: a band round a box and,
    /// optionally, a bar down its middle where two panels meet — mitred, worn round along its length,
    /// lit from the top-left, with its shadow — drawn by the CastFrame shader at the size it is shown;
    /// and the copper straps holding it down, each straddling its bar. Owned by a layout, drawn into its
    /// host (in the host's local units) whenever the layout is fitted.
    /// </summary>
    public sealed class StoneFrame {
        /// <summary>How far a strap runs past each edge of the bar it straddles.</summary>
        public const float StrapOverhang = 8f;
        public const float StrapThickness = 14f;

        /// <summary>Room round the frame for the shadow it throws.</summary>
        private const float ShadowMargin = 32f;

        private readonly VisualElement _host;
        private readonly VisualElement _under;
        private readonly List<VisualElement> _straps = new();
        private VisualElement _art;
        private RenderTexture _texture;
        private Material _material;
        private Vector2Int _texels;
        private (Rect Outer, float Band, float Middle) _drawn; // what the texture holds

        /// <param name="host">Where the frame is drawn, in its local units.</param>
        /// <param name="under">Draw the stone just beneath this child of the host instead of over
        /// everything: what the screen draws inside the frame (a dragged item, a quantity picker)
        /// then stays above it.</param>
        public StoneFrame(VisualElement host, VisualElement under = null) {
            _host = host;
            _under = under;
        }

        /// <summary>Draws the frame: <paramref name="outer"/> is its outside edge, <paramref name="band"/>
        /// its width, <paramref name="middleX"/> the centre of a middle bar (none when negative).</summary>
        public void Draw(Rect outer, float band, float middleX = -1f) {
            if (_art == null) {
                _art = new VisualElement { name = "enhanced_stone_frame", pickingMode = PickingMode.Ignore };
                _art.style.position = Position.Absolute;
                if (_under != null && _under.parent == _host) {
                    _host.Insert(_host.IndexOf(_under), _art);
                } else {
                    _host.Add(_art); // over the panels it frames, under the straps that follow it
                }
            }
            Rect r = new(outer.x - ShadowMargin, outer.y - ShadowMargin, outer.width + 2f * ShadowMargin, outer.height + 2f * ShadowMargin);
            _art.style.left = r.x;
            _art.style.top = r.y;
            _art.style.width = r.width;
            _art.style.height = r.height;
            float k = Mathf.Clamp(Screen.height / Mathf.Max(1f, _host.panel?.visualTree.layout.height ?? 1200f), 0.5f, 3f);
            var texels = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(r.width * k), 16, 4096), Mathf.Clamp(Mathf.RoundToInt(r.height * k), 16, 4096));
            if (_material == null) {
                Shader shader = Shader.Find("BakAgain/CastFrame");
                if (shader == null) return;
                _material = new Material(shader);
            }
            if (_texture == null || texels != _texels) {
                if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
                _texture = new RenderTexture(texels.x, texels.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _texels = texels;
                _drawn = default;
                _art.style.backgroundImage = Background.FromRenderTexture(_texture);
            }
            // A layout pass refits from several elements' geometry events: draw only what changed.
            if (_drawn == (outer, band, middleX) && _texture.IsCreated()) {
                return;
            }
            _drawn = (outer, band, middleX);
            float sx = texels.x / r.width, sy = texels.y / r.height;
            _material.SetVector("_Size", new Vector4(texels.x, texels.y, 0f, 0f));
            _material.SetVector("_Outer", new Vector4(ShadowMargin * sx, ShadowMargin * sy, (ShadowMargin + outer.width) * sx, (ShadowMargin + outer.height) * sy));
            _material.SetVector("_Bar", new Vector4(band * sx, middleX < 0f ? -1f : (middleX - r.x) * sx, 0f, 0f));
            _material.SetVector("_Tile", new Vector4(720f * sx, 480f * sy, 0f, 0f)); // the slab at the theme's tile size
            _material.SetVector("_Shadow", new Vector4(8f * sx, 10f * sy, 14f * sx, 0.7f));
            // Blit leaves its destination active; releasing it on the next resize then warned.
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(EnhancedArt.Texture("slab"), _texture, _material);
            RenderTexture.active = active;
            _art.MarkDirtyRepaint();
        }

        /// <summary>Places straps straddling the bars at these centres (vertical = across a horizontal bar).</summary>
        public void Strap(IReadOnlyList<(float X, float Y, bool Vertical)> spots, float band) {
            float len = band + 2f * StrapOverhang;
            while (_straps.Count < spots.Count) {
                var strap = new VisualElement { pickingMode = PickingMode.Ignore };
                strap.AddToClassList("enhanced-strap");
                _host.Add(strap);
                _straps.Add(strap);
            }
            for (int i = 0; i < _straps.Count; i++) {
                VisualElement strap = _straps[i];
                if (i >= spots.Count) { strap.style.display = DisplayStyle.None; continue; }
                (float x, float y, bool vertical) = spots[i];
                float w = vertical ? StrapThickness : len, h = vertical ? len : StrapThickness;
                strap.style.display = DisplayStyle.Flex;
                strap.style.backgroundImage = EnhancedArt.Load(vertical ? "strap-v" : "strap-h");
                strap.style.left = x - w / 2f;
                strap.style.top = y - h / 2f;
                strap.style.width = w;
                strap.style.height = h;
            }
        }

        /// <summary>Two straps across each side of a frame's outer edge, at 15% and 85% along it.</summary>
        public static void AddSides(List<(float X, float Y, bool Vertical)> spots, Rect outer, float band, bool top = true,
            bool bottom = true, bool left = true, bool right = true) {
            foreach (float f in new[] { 0.15f, 0.85f }) {
                float x = Mathf.Lerp(outer.xMin, outer.xMax, f), y = Mathf.Lerp(outer.yMin, outer.yMax, f);
                if (top) spots.Add((x, outer.yMin + band / 2f, true));
                if (bottom) spots.Add((x, outer.yMax - band / 2f, true));
                if (left) spots.Add((outer.xMin + band / 2f, y, false));
                if (right) spots.Add((outer.xMax - band / 2f, y, false));
            }
        }

        /// <summary>Two straps across a middle bar at <paramref name="middleX"/>, as on the sides.</summary>
        public static void AddMiddle(List<(float X, float Y, bool Vertical)> spots, Rect outer, float middleX) {
            foreach (float f in new[] { 0.15f, 0.85f }) spots.Add((middleX, Mathf.Lerp(outer.yMin, outer.yMax, f), false));
        }

        public void Release() {
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
            if (_material != null) Object.Destroy(_material);
            _art?.RemoveFromHierarchy();
            foreach (VisualElement s in _straps) s.RemoveFromHierarchy();
            _straps.Clear();
            _texture = null;
            _material = null;
            _art = null;
            _texels = default;
            _drawn = default;
        }
    }

    /// <summary>
    /// A REQ button dressed as an HD stone disc: its faithful face is tinted out (its icon loader keeps
    /// re-applying the face, so it cannot be replaced) and a bare disc, or one the caller draws a figure
    /// on, is laid over it. Returns the disc so the layout can take it off again.
    /// </summary>
    public static class StoneButton {
        public const string DiscName = "enhanced-stone-disc";

        public static VisualElement Dress(VisualElement button) {
            button.style.unityBackgroundImageTintColor = Color.clear;
            var disc = new VisualElement { name = DiscName, pickingMode = PickingMode.Ignore };
            disc.AddToClassList("enhanced-stone");
            disc.style.backgroundImage = EnhancedArt.Load("disc");
            button.Add(disc);
            return disc;
        }

        public static void Undress(VisualElement button) {
            if (button == null) return;
            button.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            button.Q(DiscName)?.RemoveFromHierarchy();
        }
    }
}
