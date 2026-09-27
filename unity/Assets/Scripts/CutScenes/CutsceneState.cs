namespace BakAgain.CutScenes {
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.Utility;
    using GameData.Resources.Animation;
    using GameData.Resources.Audio;
    using JetBrains.Annotations;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class CutsceneState : IDisposable {
        private const int NrOfBuffers = 4;
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<CutsceneState>();
        private static readonly int PaletteTex = Shader.PropertyToID("_PaletteTex");
        public readonly int BackgroundBufferIndex = 2;
        private BufferPair[] _buffers; // Array of buffer pairs (A, B, C, X)
        private int _currentPaletteSlot;
        private Texture2D _currentPaletteTexture;
        private readonly List<PaletteCycle> _paletteCycles = new(3);
        // Working copy the cycle rotates and uploads, so the palette stored in
        // PaletteSlots (which may be a shared/cached resource array) is never mutated.
        private Color[] _cyclePalette;

        public CutsceneState(UiImage image, Color[] defaultPalette) {
            if (!image.Texture) {
                throw new ArgumentNullException(nameof(image.Texture), "Image texture must not be null.");
            }
            ContainerImage = image;
            DefaultPalette = defaultPalette;

            // Load shader and create material
            var indexedShader = Shader.Find("Custom/CutScenes/IndexedTexture");
            var passthroughShader = Shader.Find("Custom/TextureWithZeroClip");
            var cutoutShader = Shader.Find("Custom/CutScenes/CutOut");

            if (!indexedShader || !passthroughShader || !cutoutShader) {
                string missingShaders = "";
                if (!indexedShader) {
                    missingShaders += "IndexedTexture ";
                }
                if (!passthroughShader) {
                    missingShaders += "TextureWithZeroClip ";
                }
                if (!cutoutShader) {
                    missingShaders += "CutOut ";
                }
                Logger.LogError(
                    "Constructor: Required shader(s) not found: {MissingShaders}. Ensure they are imported and included in the build.",
                    missingShaders.Trim());

                throw new InvalidOperationException(
                    $"Required shader(s) not found: {missingShaders.Trim()}! Make sure shaders are properly imported and included in the build.");
            }
            Logger.LogDebug("Constructor: All required shaders found.");

            IndexedMaterial = new Material(indexedShader);
            PassThroughMaterial = new Material(passthroughShader);
            DirectMaterial = new Material(passthroughShader);
            CutOutMaterial = new Material(cutoutShader);
            Logger.LogDebug("Constructor: Materials created.");

            Initialize();
        }

        public Material PassThroughMaterial { get; set; }
        public Material IndexedMaterial { get; }
        public Material DirectMaterial { get; set; }
        public Material CutOutMaterial { get; set; }
        public UiImage ContainerImage { get; }
        public Color[] DefaultPalette { get; set; }
        public Texture2D Canvas { get; set; }
        public Dictionary<int, Color[]> PaletteSlots { get; } = new(6);
        public Dictionary<int, string> ImageSlots { get; set; } = new(6);

        public int CurrentPaletteSlot {
            get => _currentPaletteSlot;
            set {
                if (value == _currentPaletteSlot) {
                    return;
                }
                _currentPaletteSlot = value;
                if (PaletteSlots.TryGetValue(value, out Color[] palette)) {
                    RebaseCycles(palette);
                    UpdatePaletteTexture(palette);
                }
            }
        }

        public int CurrentImageSlot { get; set; }
        public int ForegroundColorIndex { get; set; }
        public int BackgroundColorIndex { get; set; }
        public int FramesDuration { get; set; }
        public Color[] CurrentPalette => PaletteSlots[CurrentPaletteSlot];
        public Color ForegroundColor => CurrentPalette[ForegroundColorIndex];
        public Color BackgroundColor => CurrentPalette[BackgroundColorIndex];
        public int CurrentDrawBufferIndex { get; private set; }
        public int TargetBufferIndex { get; set; }
        public RenderTexture CurrentIndexedBuffer => _buffers[CurrentDrawBufferIndex].IndexedBuffer;
        public RenderTexture CurrentDirectBuffer => _buffers[CurrentDrawBufferIndex].DirectBuffer;
        public RenderTexture BackgroundIndexedBuffer => _buffers[BackgroundBufferIndex].IndexedBuffer;
        public RenderTexture BackgroundDirectBuffer => _buffers[BackgroundBufferIndex].DirectBuffer;
        public RenderTexture TargetBufferIndexed => _buffers[TargetBufferIndex].IndexedBuffer;
        public RenderTexture TargetBufferDirect => _buffers[TargetBufferIndex].DirectBuffer;

        /// <summary>
        ///     Current area to draw in
        /// </summary>
        public Rect ClipArea { get; set; }

        /// <summary>
        /// The frame tag a <c>GotoFrame</c> command asked to jump to, or 0 for "no jump pending".
        /// </summary>
        /// <remarks>
        /// Written by <c>GotoFrameExtensions</c> and consumed by <c>CutscenePlayer</c>'s frame
        /// loop, which scans for the matching tag and then clears this back to 0. (It carried a
        /// "TODO: use this" for a long time after it was wired.)
        /// </remarks>
        public int GotoTag { get; set; }
        public ResourceSet Resources { get; set; }
        public RenderTexture OutputBuffer { get; set; }
        public IArea[] Areas { get; set; } = new IArea[NrOfBuffers];

        public MidiPlaybackManager MidiPlayer { get; set; }

        /// <summary>
        /// Canvas size over CANONICAL size — <b>not</b> over the original 320x200, despite the name.
        /// </summary>
        /// <remarks>
        /// Every area that reaches the drawing layer is already canonical: the extractors scale VGA
        /// coordinates on the way out, so a shipped TTM <c>FillArea</c> reads <c>X=70 Width=1455</c>
        /// rather than <c>X=14 Width=291</c>. So this is the last hop, canonical -> buffer, and with
        /// a 1600x1200 canvas it is (1,1).
        ///
        /// <para><b>Reading it as a VGA scale makes correct code look wrong.</b> The x5/y6 VGA
        /// factors live in the extractor, not here.</para>
        /// </remarks>
        public Vector2 ScaleFromOriginal { get; set; }

        /// <summary>
        ///     Flag to indicate that the scene should end
        /// </summary>
        public bool EndScene { get; set; }

        public Queue<AudioResource> RequestedAudio { get; set; } = new(8);
        public Queue<CutsceneDialogRequest> RequestedDialogs { get; set; } = new(4);
        public (int Start, int End) Range1 { get; set; }
        public (int Start, int End) Range2 { get; set; }
        public (int Start, int End) Range3 { get; set; }

        public bool HasActivePaletteCycles => _paletteCycles.Count > 0;

        /// <summary>The cycles currently running, in the order they were installed.</summary>
        /// <remarks>
        /// A read seam for the tests on <c>StartPaletteCycle</c>: which RANGE a cycle took its
        /// bounds from is otherwise unobservable, because <see cref="AdvancePaletteCycles"/> rotates
        /// a private working copy and pushes it to the palette texture without writing back to
        /// <see cref="CurrentPalette"/>. A range mix-up would cycle the wrong slice and look like
        /// nothing at all until someone watched the shimmer.
        /// </remarks>
        internal IReadOnlyList<PaletteCycle> ActivePaletteCyclesForTest => _paletteCycles;

        public void Dispose() {
            Logger.LogDebug("Dispose: Releasing resources.");
            ReleaseBuffers();

            UnityObjectUtil.Destroy(IndexedMaterial);
            UnityObjectUtil.Destroy(PassThroughMaterial);
            UnityObjectUtil.Destroy(DirectMaterial);
            UnityObjectUtil.Destroy(CutOutMaterial);
            UnityObjectUtil.Destroy(_currentPaletteTexture);
            _currentPaletteTexture = null;
            Logger.LogDebug("Dispose: Finished.");
        }

        // Releases and destroys the render-texture buffers and the output buffer.
        // Called from Dispose and from Initialize so a Reset() can't leak the
        // previously-allocated GPU buffers by overwriting the fields.
        private void ReleaseBuffers() {
            if (_buffers != null) {
                for (int i = 0; i < _buffers.Length; i++) {
                    ReleaseRenderTexture(_buffers[i].IndexedBuffer);
                    ReleaseRenderTexture(_buffers[i].DirectBuffer);
                }
                _buffers = null;
            }
            ReleaseRenderTexture(OutputBuffer);
            OutputBuffer = null;
        }

        private static void ReleaseRenderTexture(RenderTexture texture) {
            if (!texture) {
                return;
            }
            texture.Release();
            UnityObjectUtil.Destroy(texture);
        }

        private void Initialize() {
            Logger.LogDebug("Initialize: Entered.");
            // Release any buffers from a previous Initialize (Reset) before
            // reallocating, otherwise the old GPU render textures leak.
            ReleaseBuffers();
            int width = ContainerImage.Texture!.width;
            int height = ContainerImage.Texture.height;
            Logger.LogDebug("Initialize: ContainerImage dimensions: {Width}x{Height}.", width, height);

            _buffers = new BufferPair[NrOfBuffers]; // A, B, C, X buffers
            Logger.LogDebug("Initialize: _buffers array created with size {NrOfBuffers}.", NrOfBuffers);

            // Initialize all buffer pairs
            for (int i = 0; i < _buffers.Length; i++) {
                Logger.LogDebug("Initialize: Creating BufferPair {BufferIndex}.", i);
                _buffers[i] = new BufferPair {
                    IndexedBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear),
                    DirectBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                };
                Logger.LogTrace("Initialize: BufferPair {BufferIndex} RenderTextures created.", i); // Was LogVerbose

                _buffers[i].IndexedBuffer.filterMode = FilterMode.Point;
                _buffers[i].DirectBuffer.filterMode = FilterMode.Point;
                // Areas[] live in canonical space: CopyArea(src) scales them by
                // ScaleFromOriginal, and CopyToTargetBuffer stores canonical TTM
                // areas here — the default must match that space (not buffer px).
                Areas[i] = new Area(0, 0, Canonical.Width, Canonical.Height);
                Logger.LogTrace("Initialize: BufferPair {BufferIndex} properties set.", i); // Was LogVerbose
            }
            OutputBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Logger.LogDebug("Initialize: OutputBuffer created.");

            FramesDuration = 5;
            // Full-frame default clip in canonical space. NOTE: ClipArea abuses
            // Rect as (xMin, yMin, xMax, yMax) with inclusive bounds — see
            // DrawingUtils.CalculateRects — hence the -1. TTM SetClipArea
            // commands are canonical too.
            ClipArea = new Rect(0, 0, Canonical.Width - 1, Canonical.Height - 1);
            Resources = new ResourceSet();
            ImageSlots.Clear();
            PaletteSlots.Clear();
            RequestedAudio.Clear();
            RequestedDialogs.Clear();
            CurrentPaletteSlot = 0;
            CurrentImageSlot = 0;
            CurrentDrawBufferIndex = 1;
            _paletteCycles.Clear();
            _cyclePalette = null;
            SetPalette(DefaultPalette);
            Logger.LogDebug("Initialize: Default palette set.");
            Canvas = ContainerImage.Texture;
            ScaleFromOriginal = new Vector2(
                Canvas.width / (float)Canonical.Width, Canvas.height / (float)Canonical.Height);
            Logger.LogInformation("Initialize: Completed successfully.");
        }

        /// <summary>
        /// The 256x1 palette actually uploaded to the indexed material, or null before the first
        /// upload. Internal so a test can assert what the shader is really sampling rather than
        /// what a slot array happens to hold — the two diverge exactly when a cycle is running.
        /// </summary>
        internal Texture2D PaletteTexture => _currentPaletteTexture;

        private void UpdatePaletteTexture(Color[] palette) {
            // *** A DISPOSED PALETTE HAS NOTHING TO UPLOAD, AND UPLOADING IT THREW. ***
            // DisposeCurrentPalette is SetPalette(null), and SetPixels on a 256x1 texture with no
            // data raises "size of data to be filled was larger than the size of data available".
            // The frame processor catches per-command exceptions, so this never killed a cutscene —
            // it logged an error every time one disposed its palette, in 12 places across 10 of the
            // 42 shipped scripts, which is noise that hides real errors.
            //
            // Returning early is the whole fix: SetPalette has ALREADY cleared the slot by the time
            // this is called, so the command's effect lands either way and there is simply no
            // texture write to make. The texture keeps its last contents until something uploads a
            // real palette, which is what the original does too — freeing the palette does not
            // repaint the DAC.
            if (palette == null || palette.Length == 0) {
                return;
            }

            if (!_currentPaletteTexture) {
                _currentPaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            _currentPaletteTexture.SetPixels(palette);
            _currentPaletteTexture.Apply();

            if (IndexedMaterial) {
                IndexedMaterial.SetTexture(PaletteTex, _currentPaletteTexture);
            }
        }

        public void SetPalette(Color[] colors) {
            PaletteSlots[CurrentPaletteSlot] = colors;
            RebaseCycles(colors);
            UpdatePaletteTexture(colors);
        }

        /// <summary>
        /// Points a running cycle at the palette that is live NOW.
        /// </summary>
        /// <remarks>
        /// <b>The original cycles the live palette, not a copy of one.</b>
        /// <c>palette_cycle_tick</c> (PALDRV.C:159) copies
        /// <c>g_graphics_context.pPaletteScratchBuf</c> aside, rotates the windows back into that
        /// same buffer and re-uploads it — and loading a palette writes that same buffer. So a cycle
        /// started before a palette load carries on over the NEW colours.
        ///
        /// <para>Here the rotation works on <see cref="_cyclePalette"/>, a snapshot taken when the
        /// cycle was armed, so without this a palette loaded afterwards is overwritten by that
        /// snapshot on the very next tick — the load lands and is undone a frame later.</para>
        ///
        /// <para><b>Measured: it is why every goods store in the game was unreadable.</b> SHOP3's
        /// init script arms its cycle (<c>SetRange1 249..251</c>, <c>StartPaletteCycle</c>) BEFORE it
        /// loads <c>SHOP3.PAL</c>, so the snapshot was the town palette the location had come from.
        /// Romney's Port Exchange drew its shelves and doors in exactly the right places and painted
        /// them in <c>g_romney.PAL</c> — skin pinks, sky teals, foliage greens scattered pixel by
        /// pixel through an indoor scene. Fifteen GDS scenes use <c>shop3</c> (TASK-427).</para>
        /// </remarks>
        private void RebaseCycles(Color[] colors) {
            if (!HasActivePaletteCycles) {
                return;
            }
            _cyclePalette = colors == null ? null : (Color[])colors.Clone();
        }

        // Replaces the active set of palette-cycle windows (TTM cmd 0x2402).
        // Each window is an inclusive [Start..End] palette-index range plus a
        // signed rotation offset. Snapshots the current palette so rotation
        // never mutates the (possibly cached) slot array.
        //
        // Replacing rather than appending is faithful: the original's 0x2402 calls
        // palette_cycle_add(-1, 0, 0) first, which resets the whole cycle list, before
        // adding the bands the range mask selects.
        //
        // A window narrower than two entries is dropped, matching palette_cycle_add's
        // `count <= 1` rejection.
        public void SetPaletteCycles(IReadOnlyList<PaletteCycle> cycles) {
            _paletteCycles.Clear();
            if (cycles != null) {
                for (int i = 0; i < cycles.Count; i++) {
                    PaletteCycle cycle = cycles[i];
                    if (cycle.Direction != 0 && cycle.End > cycle.Start) {
                        _paletteCycles.Add(cycle);
                    }
                }
            }
            _cyclePalette = HasActivePaletteCycles ? (Color[])CurrentPalette.Clone() : null;
        }

        // Rotates each active window by its direction and uploads the result.
        // Returns true if a cycle was applied (caller should re-render output).
        public bool AdvancePaletteCycles() {
            if (!HasActivePaletteCycles || _cyclePalette == null) {
                return false;
            }
            foreach (PaletteCycle cycle in _paletteCycles) {
                RotateSlice(_cyclePalette, cycle.Start, cycle.End, cycle.Direction);
            }
            UpdatePaletteTexture(_cyclePalette);

            return true;
        }

        /// <summary>
        /// Circularly rotates the inclusive <c>[start..end]</c> slice of a palette by
        /// <paramref name="direction"/> entries — <c>palette_cycle_tick</c>
        /// (<c>SRC/GFX/DRIVER/PALDRV.C</c>).
        /// </summary>
        /// <remarks>
        /// <b>A positive offset moves colours toward LOWER indices</b>: the entry that was at
        /// <c>start + offset</c> ends up at <c>start</c>. The original's tick reads the saved copy at
        /// <c>start + target</c> and writes it to <c>start</c>, wrapping the leading
        /// <c>target</c> entries around to the end. Rotating the other way makes every cycling
        /// animation — water, fire, torchlight — run backwards, which looks plausible enough to ship
        /// unnoticed.
        ///
        /// <para>A negative offset is <b>normalised, not reversed</b>: <c>palette_cycle_add</c> does
        /// <c>target = count + target</c>, so -1 means "rotate by count-1", which comes out as one
        /// step toward higher indices. Same arithmetic, and it is why this takes a signed offset
        /// rather than a direction plus a magnitude.</para>
        /// </remarks>
        public static void RotateSlice(Color[] palette, int start, int end, int direction) {
            if (palette == null) {
                return;
            }
            start = Mathf.Clamp(start, 0, palette.Length - 1);
            end = Mathf.Clamp(end, 0, palette.Length - 1);
            int count = end - start + 1;
            if (count <= 1) {
                return;
            }
            int shift = ((direction % count) + count) % count;
            if (shift == 0) {
                return;
            }
            var rotated = new Color[count];
            for (int i = 0; i < count; i++) {
                rotated[i] = palette[start + ((i + shift) % count)];
            }
            Array.Copy(rotated, 0, palette, start, count);
        }

        public void SetCurrentImageName(string images) {
            ImageSlots[CurrentImageSlot] = images;
        }

        /// <summary>
        /// The image a draw command refers to, loading it if preloading missed it.
        /// </summary>
        /// <remarks>
        /// <b>Preloading cannot be trusted to have the right image.</b> It walks every frame's
        /// commands in file order, so a <c>DrawImage</c> is resolved against whatever filename the
        /// LAST <c>LoadImageResource</c> in the file put in that slot — not what the slot holds when
        /// the draw actually runs. Playing a cutscene from a tag makes the two disagree: the shipped
        /// town scenes load a different bitmap per tag and share one generic draw, so preloading
        /// fetched the last town in the file and every location drew nothing.
        ///
        /// <para>Falling back to a direct load here fixes all nine draw commands in one place rather
        /// than making each of them async, and it is the same blocking load the editor-side preload
        /// already uses. Preloading stays what it claims to be — an optimisation.</para>
        /// </remarks>
        [CanBeNull]
        public IndexedTexture GetImage(int slot, int number) {
            if (!ImageSlots.TryGetValue(slot, out string filename) || filename == null || number < 0) {
                return null;
            }

            string key = $"{filename}#{number.ToString()}";
            if (Resources.ContainsKey(key)) {
                return Resources.Get<IndexedTexture>(key);
            }

            try {
                var loaded = Addressables.LoadAssetAsync<IndexedTexture>(key).WaitForCompletion();
                if (loaded != null) {
                    Resources.Add(key, loaded);
                }
                return loaded;
            } catch (Exception e) {
                Logger.LogWarning(e, "Could not load image {Key} on demand.", key);
                return null;
            }
        }

        public void Reset() {
            Initialize();
        }

        public Texture GetIndexedBuffer(int src) {
            return _buffers[src].IndexedBuffer;
        }

        public Texture GetDirectBuffer(int src) {
            return _buffers[src].DirectBuffer;
        }

        private struct BufferPair {
            public RenderTexture IndexedBuffer;
            public RenderTexture DirectBuffer;
        }

        // One palette-cycle window: the inclusive [Start..End] palette-index
        // range to rotate, and the per-tick Direction (+1/-1) derived from the
        // sign of the TTM Step argument.
        public struct PaletteCycle {
            public int Start;
            public int End;
            public int Direction;
        }
    }
}