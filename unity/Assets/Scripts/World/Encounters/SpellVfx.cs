namespace BakAgain.World.Encounters {
    using System;
    using System.Collections.Generic;
    using BakAgain.Audio;
    using Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Combat;
    using GameData.Resources.Spells;
    using UnityEngine;

    /// <summary>
    /// Plays combat spell visuals — TASK-117. The rules pick WHAT (<see cref="SpellVisuals"/>) and
    /// step the particles (<see cref="SpellParticles"/>); this only draws them.
    /// </summary>
    /// <remarks>
    /// <b>One at a time, in the order the rules raised them.</b> The original blocks its whole frame
    /// loop on each sequence, so a Mad God's Rage round is strike, strike, strike — not a pile-up.
    /// The rules have already resolved by the time anything plays, so the queue only orders the
    /// pictures; nothing waits on it.
    ///
    /// <para><b>Anchored to the combatant, not to a GameObject.</b> The arena is rebuilt on every
    /// redraw, so an effect parented to a sprite would die with it. Effects live under the zone root
    /// and look their combatant's sprite up every frame through <c>anchorOf</c>.</para>
    ///
    /// <para><b>Modern look, original cadence.</b> Points and 1-px lines become soft additive glows,
    /// but they move on the original's 59 ms combat frame (<see cref="SpellVisuals.FrameSeconds"/>)
    /// with its counts, speeds and colours.</para>
    /// </remarks>
    public sealed class SpellVfx {
        private readonly Func<Combatant, Transform> _anchorOf;
        private readonly Func<Transform> _root;
        private readonly Func<Color[]> _palette;
        private readonly Func<Camera> _camera;
        private readonly Func<int, Transform, UniTask<GameObject>> _buildEffect;
        private readonly Func<Quaternion> _arenaRotation;
        private readonly Func<Combatant, IReadOnlyList<Vector3>> _crystalRun;
        private readonly Queue<(SpellVisual Visual, Combatant From, Combatant To)> _queue = new();
        private bool _draining;

        private static readonly Func<int, int> Rnd = n => n <= 0 ? 0 : UnityEngine.Random.Range(0, n);
        private static readonly int FlashId = Shader.PropertyToID("_BakFlash");
        private static readonly int SpriteFlashId = Shader.PropertyToID("_FlashColor");

        /// <summary>Soft-dot size of a spark, in world units. ponytail: chosen, not measured — the
        /// original plots one pixel; tune here if the glows read too big or too small.</summary>
        public const float SparkSize = 40f;

        public SpellVfx(Func<Combatant, Transform> anchorOf, Func<Transform> root, Func<Color[]> palette,
            Func<Camera> camera, Func<int, Transform, UniTask<GameObject>> buildEffect,
            Func<Quaternion> arenaRotation, Func<Combatant, IReadOnlyList<Vector3>> crystalRun) {
            _arenaRotation = arenaRotation;
            _crystalRun = crystalRun;
            _anchorOf = anchorOf;
            _root = root;
            _palette = palette;
            _camera = camera;
            _buildEffect = buildEffect;
        }

        /// <summary>Visuals played so far — observable for a test, as <c>ProjectileFlight.Progress</c> is.</summary>
        public int Played { get; private set; }

        /// <summary>Queue a visual raised by the rules.</summary>
        public void Enqueue(SpellVisual visual, Combatant from, Combatant to) {
            if (visual.Kind == SpellVisualKind.None || to == null) {
                return;
            }
            _queue.Enqueue((visual, from, to));
            if (!_draining) {
                DrainAsync().Forget();
            }
        }

        private async UniTaskVoid DrainAsync() {
            _draining = true;
            try {
                while (_queue.Count > 0) {
                    (SpellVisual v, Combatant from, Combatant to) = _queue.Dequeue();
                    try {
                        // The cast's own redraw runs right after the rules raise this; let it land
                        // first, or the effect finds the sprites it is about to replace.
                        await UniTask.Yield();
                        await PlayAsync(v, from, to);
                    } catch (Exception e) {
                        // A missing sprite or a torn-down arena must never stall the queue.
                        Debug.LogWarning($"SpellVfx: {v.Kind} failed: {e.Message}");
                    }
                    Played++;
                }
            } finally {
                _draining = false;
                SetWorldFlash(Color.clear, 0f);
            }
        }

        private static UniTask Frames(int n = 1) =>
            UniTask.Delay(TimeSpan.FromSeconds(SpellVisuals.FrameSeconds * n));

        private static UniTask IrqTicks(int n) =>
            UniTask.Delay(TimeSpan.FromSeconds(SpellVisuals.IrqSeconds * n));

        private UniTask PlayAsync(SpellVisual v, Combatant from, Combatant to) => v.Kind switch {
            SpellVisualKind.PaletteFlash => PaletteFlashAsync(to, v.Colour),
            SpellVisualKind.StormFlash => StormAsync(to),
            SpellVisualKind.SparkBurst => SparkBurstAsync(to, v.Colour, v.Spread, v.Tint),
            SpellVisualKind.FluxVortex => OrbitAsync(to, SpellParticles.Orbit.Vortex(Rnd), v.Colour),
            SpellVisualKind.ParticleBlast => ParticleBlastAsync(to, v.Colour),
            SpellVisualKind.Sink => SinkAsync(to),
            SpellVisualKind.Whirlwind => WhirlwindAsync(to),
            SpellVisualKind.WhirlwindFlight => FlyAsync(4, from, to, 100, grow: true),
            SpellVisualKind.HopBurst => HopBurstAsync(from, to),
            SpellVisualKind.Rebound => ReboundAsync(from, to),
            SpellVisualKind.CrystalZap => CrystalZapAsync(to),
            _ => UniTask.CompletedTask,
        };

        // ------------------------------------------------------------------ palette flash

        /// <summary><c>cspell_play_palette_flash</c> (CSPELL.C:336): red target, the world jumps 52 %
        /// toward the colour, holds 40 IRQ ticks, and fades back in 17 frames.</summary>
        private async UniTask PaletteFlashAsync(Combatant target, int colour) {
            Color c = PaletteColour(colour);
            TintFor(target, TintRed, 2);
            await Frames();
            SetWorldFlash(c, 1f - 30f / 63f);
            await IrqTicks(0x28);
            for (int w = 0x1f; w < 0x40; w += 2) {
                SetWorldFlash(c, 1f - w / 63f);
                await Frames();
            }
            SetWorldFlash(c, 0f);
        }

        /// <summary>Drives the lighting shaders' flash term; the sky follows through
        /// <c>WorldLightingService.Apply</c>, which reads the same global.</summary>
        public static void SetWorldFlash(Color colour, float amount) =>
            Shader.SetGlobalVector(FlashId, new Vector4(colour.r, colour.g, colour.b, Mathf.Clamp01(amount)));

        // ------------------------------------------------------------------ storm

        /// <summary><c>cspell_storm_flash_sequence</c> (CSPELL.C:609): white target, static held,
        /// four frames of bolts each with a thunder and an RND(40)-tick gap, then a flash toward
        /// colour 3. The storm's cues play here so they land on their bolts.</summary>
        private async UniTask StormAsync(Combatant target) {
            MenuSoundService sfx = MenuSoundService.Instance;
            LingeringSpellVfx bolts = Follow(target, new LingeringVisual(LingeringVisualKind.Lightning));
            sfx?.Play(SpellEffectArmSound.StaticCue);
            for (var i = 0; i < SpellEffectArmSound.StormFlashCount; i++) {
                TintFor(target, TintWhite, 2);
                await Frames();
                sfx?.Play(SpellEffectArmSound.ThunderCue);
                await IrqTicks(SpellEffectArmSound.FlashGapTicks(Rnd));
            }
            sfx?.Stop(SpellEffectArmSound.StaticCue);
            // The Skyfire status stays on the target until the flash has faded (the arm removes it
            // after cspell_storm_flash_sequence returns), so the bolts keep striking through the
            // flash — seen in the original, where they outlast the four thunder frames.
            await PaletteFlashAsync(target, SpellVisuals.StormFlashColour);
            if (bolts != null) {
                UnityEngine.Object.Destroy(bolts.gameObject);
            }
        }

        // ------------------------------------------------------------------ particles

        private async UniTask SparkBurstAsync(Combatant target, int colour, int spread, int tint) {
            // The burst is the impact: wait for the projectile ProjectileFlight is flying to land.
            await UniTask.Delay(TimeSpan.FromSeconds(ProjectileFlight.Seconds));
            // The struck target glows through its remap for ten frames while the sparks fly —
            // seen in the original as a solid red Flamecast victim.
            if (tint != 0) {
                TintFor(target, RemapTint(tint), SpellVisuals.ImpactTintFrames);
            }
            var burst = new SpellParticles.SparkBurst(spread, Rnd);
            using var cloud = new GlowCloud(_root(), "SparkBurst");
            using var shadows = new GlowCloud(_root(), "SparkShadows", GlowCloud.ShadowMaterial);
            Color[] pal = _palette();
            Color shadow = PaletteColour(SpellParticles.SparkBurst.ShadowPen);
            shadow.a = 0.8f;
            do {
                if (!Place(cloud, target) || !Place(shadows, target)) {
                    return;
                }
                cloud.Draw(burst.Points, _ => colour == 0 ? RandomPen(pal) : PaletteColour(colour), SparkSize);
                shadows.Draw(burst.Shadows, _ => shadow, SparkSize * 0.6f);
                await Frames();
            } while (burst.Step());
        }

        private async UniTask OrbitAsync(Combatant target, SpellParticles.Orbit orbit, int colour) {
            using var cloud = new GlowCloud(_root(), "Orbit");
            Color c = PaletteColour(colour);
            do {
                if (!Place(cloud, target)) {
                    return;
                }
                if (orbit.RollZap()) {
                    // A zap: the target flashes red and cue 4 plays (WORLDFX.C:196-205).
                    TintFor(target, TintRed, 2);
                    MenuSoundService.Instance?.Play(4);
                }
                cloud.Draw(orbit.Points, _ => c, SparkSize);
                await Frames();
            } while (orbit.Step());
        }

        /// <summary>Mind Melt (CSPELL.C:828): logostar; the target shakes 10..24 frames (the blast
        /// swaps its status list to spell 0x13, Wrath of Killian, whose arm is the shake —
        /// WORLDFX.C:97); a ring; thunder and a flash; a cut-short second thunder; a second blast.</summary>
        private async UniTask ParticleBlastAsync(Combatant target, int colour) {
            MenuSoundService sfx = MenuSoundService.Instance;
            sfx?.Play(SpellEffectArmSound.LogostarCue);
            await BlastAsync(target, colour);
            sfx?.Play(SpellEffectArmSound.ThunderCue);
            await PaletteFlashAsync(target, colour);
            sfx?.Stop(SpellEffectArmSound.ThunderCue);
            sfx?.Play(SpellEffectArmSound.ThunderCue);
            await BlastAsync(target, colour);
        }

        private async UniTask BlastAsync(Combatant target, int colour) {
            await ShakeAsync(target, 10 + Rnd(15));
            await OrbitAsync(target, SpellParticles.Orbit.Blast(Rnd), colour);
        }

        /// <summary>The held-tile shake (CACTOR.C:964-970) for a number of frames, re-finding the
        /// sprite each frame so a redraw mid-shake does not strand it off its cell.</summary>
        private async UniTask ShakeAsync(Combatant target, int frames) {
            Transform last = null;
            Vector3 home = default;
            for (var i = 0; i < frames; i++) {
                Transform a = _anchorOf(target);
                if (a != last) {
                    if (last != null) {
                        last.localPosition = home;
                    }
                    last = a;
                    home = a != null ? a.localPosition : default;
                }
                if (a != null) {
                    a.localPosition = home + LingeringSpellVfx.ShakeOffset();
                }
                await Frames();
            }
            if (last != null) {
                last.localPosition = home;
            }
        }

        // ------------------------------------------------------------------ sprite effects

        /// <summary>Final Rest (CSPELL.C:628): the sprite slides down through the floor. The floor
        /// writes depth, so the part below ground is hidden without the original's clip.</summary>
        private async UniTask SinkAsync(Combatant target) {
            // The kill has already happened in the rules; wait for the redraw to put up the body.
            await UniTask.Yield();
            Transform anchor = _anchorOf(target);
            if (anchor == null) {
                return;
            }
            float height = SpriteHeight(anchor);
            // ponytail: 25 frames is "2 px a frame" for a ~50 px sprite; the original's px-to-world
            // ratio varies with depth, so this is the calibration knob.
            const int frames = 25;
            for (var i = 1; i <= frames && anchor != null; i++) {
                anchor.localPosition += Vector3.down * (height / frames);
                await Frames();
            }
        }

        /// <summary>Winds of Eortis on an immune target (CSPELL.C:824, WORLDFX.C:256): the whirlwind
        /// (COMBAT.TBL 4) spins on it for 100 frames, flipping at random.</summary>
        private async UniTask WhirlwindAsync(Combatant target) {
            GameObject wind = await _buildEffect(4, _root());
            if (wind == null) {
                return;
            }
            Vector3 scale = wind.transform.localScale;
            for (var i = 0; i < 100 && wind != null; i++) {
                Transform anchor = _anchorOf(target);
                if (anchor == null) {
                    break;
                }
                wind.transform.position = anchor.position;
                wind.transform.localScale = new Vector3(Rnd(2) == 0 ? scale.x : -scale.x, scale.y, scale.z);
                await Frames();
            }
            if (wind != null) {
                UnityEngine.Object.Destroy(wind);
            }
        }

        /// <summary>Evil Seek, one hop (CSPELL.C:456): shapes 5, 3, 3 fly at random speeds, then two
        /// white/red flickers.</summary>
        private async UniTask HopBurstAsync(Combatant from, Combatant to) {
            await FlyAsync(5, from, to, 200 + Rnd(200));
            await FlyAsync(3, from, to, 220 + Rnd(200));
            await FlyAsync(3, from, to, 250 + Rnd(200));
            for (var i = 0; i < 2; i++) {
                TintFor(to, TintWhite, 1);
                await Frames();
                TintFor(to, TintRed, 1);
                await Frames();
            }
        }

        /// <summary>Strength Drain (CSPELL.C:441): shape 0x26 rolls out, the target turns white, and
        /// it rolls back.</summary>
        private async UniTask ReboundAsync(Combatant caster, Combatant target) {
            await FlyAsync(0x26, caster, target, 100);
            Renderer r = RendererOf(target);
            SetSpriteFlash(r, TintWhite);
            await FlyAsync(0x26, target, caster, 100);
            SetSpriteFlash(RendererOf(target), Color.clear);
        }

        /// <summary>
        /// Crystal ground discharging (CACTOR.C:1078, WORLDFX.C:539): ten frames of the walker in
        /// white with the run crackling at z=235 in pen 0xAF — each middle point jittered ±200
        /// across the run every frame — and a zap per frame. The rules sounded the first zap.
        /// </summary>
        private async UniTask CrystalZapAsync(Combatant walker) {
            IReadOnlyList<Vector3> run = _crystalRun?.Invoke(walker);
            using var beam = new GlowCloud(_root(), "CrystalBeam");
            var line = new GameObject("Line").AddComponent<LineRenderer>();
            line.transform.SetParent(beam.Root, false);
            line.sharedMaterial = GlowCloud.LineMaterial;
            line.alignment = LineAlignment.View;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.12f;
            line.startColor = line.endColor = PaletteColour(0xaf);
            const float z = SpellHeights.CrystalBeam / BakCoordinateConverter.WorldScale;
            const float jitter = 200f / BakCoordinateConverter.WorldScale;
            for (var frame = 0; frame < 10; frame++) {
                TintFor(walker, TintWhite, 1);
                if (frame > 0) {
                    MenuSoundService.Instance?.Play(CombatWalk.CrystalGroundSoundId);
                }
                if (run is { Count: > 1 }) {
                    Vector3 along = (run[run.Count - 1] - run[0]).normalized;
                    Vector3 side = Vector3.Cross(along, Vector3.up);
                    var pts = new List<Vector3>();
                    for (var i = 0; i < run.Count; i++) {
                        pts.Add(run[i] + Vector3.up * z);
                        if (i + 1 < run.Count) {
                            Vector3 mid = (run[i] + run[i + 1]) / 2f + Vector3.up * z;
                            pts.Add(mid + side * UnityEngine.Random.Range(-jitter, jitter));
                        }
                    }
                    line.positionCount = pts.Count;
                    line.SetPositions(pts.ToArray());
                }
                await Frames();
            }
        }

        /// <summary>A COMBAT.TBL shape flying between two combatants at the original's speed, in
        /// world units per combat frame (<c>world_rndr_ranged_attack_anim</c>, WORLDHIT.C:585).</summary>
        /// <param name="grow">Shape 4's rule: a quarter size for the first quarter of the flight,
        /// then growing to full, rising out of the ground as it does.</param>
        private async UniTask FlyAsync(int shape, Combatant from, Combatant to, int speed, bool grow = false) {
            Transform a = _anchorOf(from), b = _anchorOf(to);
            if (a == null || b == null) {
                return;
            }
            GameObject sprite = await _buildEffect(shape, _root());
            if (sprite == null) {
                return;
            }
            Vector3 start = a.position, end = b.position;
            Vector3 full = sprite.transform.localScale;
            float world = Vector3.Distance(start, end) * BakCoordinateConverter.WorldScale;
            int steps = Mathf.Max(1, Mathf.CeilToInt(world / Mathf.Max(1, speed)));
            float lift = shape == 4 || grow ? 0f : SpellHeights.Flight / BakCoordinateConverter.WorldScale;
            for (var s = 1; s <= steps && sprite != null; s++) {
                float t = s / (float)steps;
                float k = !grow ? 1f : t < 0.25f ? 0.25f : Mathf.Lerp(0.25f, 1f, (t - 0.25f) / 0.75f);
                sprite.transform.localScale = full * k;
                sprite.transform.position = Vector3.Lerp(start, end, t) + Vector3.up * lift;
                await Frames();
            }
            if (sprite != null) {
                UnityEngine.Object.Destroy(sprite);
            }
        }

        // ------------------------------------------------------------------ lingering

        /// <summary>A per-frame look that follows a combatant across redraws, for a one-shot's
        /// duration. The caller destroys its GameObject when the sequence ends.</summary>
        private LingeringSpellVfx Follow(Combatant who, LingeringVisual look) {
            var go = new GameObject($"Follow {look.Kind}");
            go.transform.SetParent(_root(), false);
            LingeringSpellVfx vfx = go.AddComponent<LingeringSpellVfx>();
            vfx.Bind(look, false, _palette(), _camera, _arenaRotation?.Invoke() ?? Quaternion.identity,
                () => _anchorOf(who));
            return vfx;
        }

        /// <summary>Put a per-frame look on a combatant's sprite for as long as it lives.</summary>
        public LingeringSpellVfx Attach(Transform sprite, LingeringVisual look, bool shake = false) {
            var vfx = sprite.gameObject.AddComponent<LingeringSpellVfx>();
            vfx.Bind(look, shake, _palette(), _camera, _arenaRotation?.Invoke() ?? Quaternion.identity);
            return vfx;
        }

        // ------------------------------------------------------------------ helpers

        internal static readonly Color TintRed = new(0.8f, 0.36f, 0.14f, 0.6f);   // RED.RMP reads orange-brown
        internal static readonly Color TintWhite = new(1f, 1f, 1f, 0.45f);
        internal static readonly Color TintBlue = new(0.3f, 0.45f, 1f, 0.55f);
        internal static readonly Color TintGreen = new(0.25f, 1f, 0.3f, 0.55f);

        /// <summary>The four remap tables a pose or halo picks — RED/GREEN/WHITE/BLUE.RMP
        /// (COMBAT.C:174-189): 1 red, 2 green, 3 white, 4 blue.</summary>
        internal static Color RemapTint(int remap) => remap switch {
            1 => TintRed, 2 => TintGreen, 4 => TintBlue, _ => TintWhite,
        };

        private Color PaletteColour(int index) {
            Color[] pal = _palette();
            return pal != null && index >= 0 && index < pal.Length ? pal[index] : Color.white;
        }

        private static Color RandomPen(Color[] pal) =>
            pal is { Length: > 0 } ? pal[Rnd(pal.Length)] : Color.white;

        private bool Place(GlowCloud cloud, Combatant target) {
            Transform anchor = _anchorOf(target);
            if (anchor == null || cloud.Root == null) {
                return false;
            }
            cloud.Root.position = anchor.position;
            return true;
        }

        private Renderer RendererOf(Combatant c) {
            Transform t = _anchorOf(c);
            return t == null ? null : t.GetComponent<Renderer>();
        }

        /// <summary>Tint a combatant for a number of combat frames, then clear it.</summary>
        private void TintFor(Combatant c, Color tint, int frames) {
            Renderer r = RendererOf(c);
            if (r == null) {
                return;
            }
            SetSpriteFlash(r, tint);
            ClearAfterAsync(r, frames).Forget();
        }

        private static async UniTaskVoid ClearAfterAsync(Renderer r, int frames) {
            await Frames(frames);
            SetSpriteFlash(r, Color.clear);
        }

        internal static void SetSpriteFlash(Renderer r, Color tint) {
            if (r == null) {
                return;
            }
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(SpriteFlashId, tint);
            r.SetPropertyBlock(block);
        }

        internal static float SpriteHeight(Transform sprite) {
            Renderer r = sprite.GetComponent<Renderer>();
            return r != null ? r.bounds.size.y : 4f;
        }
    }

    /// <summary>
    /// A lingering spell's per-frame look on one sprite — the <c>CACTOR.C:903-1076</c> arms: bolts,
    /// the wire box, twinkles, a halo, and the held-tile shake. Rebuilt with the arena.
    /// </summary>
    public sealed class LingeringSpellVfx : MonoBehaviour {
        private LingeringVisual _look;
        private bool _shake;
        private Color[] _palette;
        private Func<Camera> _camera;
        private Func<Transform> _follow;
        private GlowCloud _cloud;
        private readonly List<LineRenderer> _lines = new();
        private GlowCloud _halo;
        private Quaternion _arena;
        private Vector3 _home;
        private float _nextFrame;
        private int _age;

        private static readonly Func<int, int> Rnd = n => n <= 0 ? 0 : UnityEngine.Random.Range(0, n);

        /// <summary>Frames drawn — observable for a test.</summary>
        public int Age => _age;

        public LingeringVisualKind Kind => _look.Kind;

        public void Bind(LingeringVisual look, bool shake, Color[] palette, Func<Camera> camera,
            Quaternion arena, Func<Transform> follow = null) {
            _look = look;
            _follow = follow;
            _arena = arena;
            _shake = shake;
            _palette = palette;
            _camera = camera;
            _home = transform.localPosition;
            if (look.Kind != LingeringVisualKind.None) {
                _cloud = new GlowCloud(transform.parent, $"Lingering {look.Kind}");
            }
            if (look.Kind == LingeringVisualKind.Halo) {
                // The original blits the sprite enlarged through a remap table and then the sprite
                // over it (WORLDFX.C:519), so the colour shows only round the edges. A soft glow
                // placed just behind the sprite does the same: the sprite's depth hides its middle.
                _halo = new GlowCloud(transform.parent, "Halo");
                Color tint = SpellVfx.RemapTint(_look.Colour);
                // Measured against the original: the rim is a faint edge, not an aura — the sprite is
                // only enlarged by 3x2 px under the remap. Kept soft and low so it reads the same.
                tint.a = 0.3f;
                float height = SpellVfx.SpriteHeight(transform) * BakCoordinateConverter.WorldScale;
                _halo.Draw(new[] { new ParticlePoint(0, 0, height / 2f) }, _ => tint, height * 1.02f);
            }
        }

        private void Update() {
            if (Time.time < _nextFrame) {
                return;
            }
            _nextFrame = Time.time + (float)SpellVisuals.FrameSeconds;
            _age++;
            if (_follow != null && _follow() is { } target) {
                transform.position = target.position;
            }
            if (_shake) {
                transform.localPosition = _home + ShakeOffset();
            }
            if (_halo != null) {
                Camera cam = _camera?.Invoke();
                _halo.Root.position = transform.position
                                      + (cam != null ? cam.transform.forward * 0.3f : Vector3.zero);
            }
            if (_cloud == null) {
                return;
            }
            _cloud.Root.position = transform.position;
            _cloud.Root.rotation = _arena;
            switch (_look.Kind) {
                case LingeringVisualKind.Sparkle:
                    DrawSparkles(Pen(_look.Colour));
                    break;
                case LingeringVisualKind.Halo:
                    DrawSparkles(Pen(SpellVisuals.HaloSparkleColour));
                    break;
                case LingeringVisualKind.WireBox:
                    DrawBox();
                    break;
                case LingeringVisualKind.Lightning:
                    DrawBolts();
                    break;
            }
        }

        /// <summary>CACTOR.C:964-970: X += RND(3), Y -= RND2(2) screen px — about 12 world units a px.</summary>
        public static Vector3 ShakeOffset() => new(Rnd(3) * 0.12f, -Rnd(2) * 0.12f, 0f);

        private Color Pen(int index) =>
            _palette != null && index >= 0 && index < _palette.Length ? _palette[index] : Color.white;

        private void DrawSparkles(Color c) {
            var points = new List<ParticlePoint>();
            var sizes = new List<float>();
            foreach ((ParticlePoint at, bool cross) in SpellParticles.Sparkles(Rnd)) {
                points.Add(at);
                sizes.Add(cross ? SpellVfx.SparkSize * 2.2f : SpellVfx.SparkSize);
            }
            _cloud.Draw(points, _ => c, SpellVfx.SparkSize, sizes);
        }

        private void DrawBox() {
            EnsureLines(SpellParticles.BoxEdges.Length);
            for (var e = 0; e < SpellParticles.BoxEdges.Length; e++) {
                (int a, int b) = SpellParticles.BoxEdges[e];
                // The first half of the edge list is the back of the box, the second the front.
                Color c = Pen(SpellParticles.BoxColour(_age, front: e >= 6));
                SetLine(_lines[e], new[] { Local(SpellParticles.BoxCorners[a]), Local(SpellParticles.BoxCorners[b]) }, c, 0.06f);
            }
        }

        private void DrawBolts() {
            int count = SpellParticles.BoltCount(Rnd);
            EnsureLines(3);
            Camera cam = _camera?.Invoke();
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            // The original draws bolts both behind and in FRONT of the sprite (CACTOR.C:939, 1063);
            // at the sprite's own depth the sprite hides the bolt's root, so pull it toward the eye.
            Vector3 front = cam != null ? -cam.transform.forward * 0.3f : Vector3.zero;
            // The original's bolt climbs from its chest base to the top of the screen in 10 px rises,
            // each ending ±10 px sideways; SpellHeights says how big a pixel is taken to be.
            const int segments = 20;
            const float px = SpellHeights.BoltUnitsPerPixelAcross / BakCoordinateConverter.WorldScale;
            for (var i = 0; i < _lines.Count; i++) {
                if (i >= count) {
                    _lines[i].enabled = false;
                    continue;
                }
                List<(int SidewaysPx, float Up)> bolt = SpellParticles.Bolt(Rnd, segments);
                var pts = new Vector3[bolt.Count];
                for (var v = 0; v < bolt.Count; v++) {
                    pts[v] = transform.position + front + right * (bolt[v].SidewaysPx * px)
                             + Vector3.up * ((SpellHeights.BoltBase + bolt[v].Up * segments * 10f * SpellHeights.BoltUnitsPerPixelUp)
                                             / BakCoordinateConverter.WorldScale);
                }
                _lines[i].enabled = true;
                SetLine(_lines[i], pts, Pen(0xaf), 0.12f, world: true);
            }
        }

        private Vector3 Local(ParticlePoint p) => BakCoordinateConverter.ConvertPosition((int)p.X, (int)p.Y, (int)p.Z);

        private void EnsureLines(int n) {
            while (_lines.Count < n) {
                var go = new GameObject("Line");
                go.transform.SetParent(_cloud.Root, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = GlowCloud.LineMaterial;
                lr.alignment = LineAlignment.View;
                lr.numCapVertices = 2;
                _lines.Add(lr);
            }
        }

        private static void SetLine(LineRenderer lr, Vector3[] pts, Color c, float width, bool world = false) {
            lr.useWorldSpace = world;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = width;
        }

        private void OnDestroy() {
            transform.localPosition = _home;
            _cloud?.Dispose();
            _halo?.Dispose();
        }
    }

    /// <summary>
    /// Soft additive points drawn as one mesh of quads, billboarded in <c>BakAgain/SpellGlow</c>.
    /// Positions are the original's world units relative to <see cref="Root"/>.
    /// </summary>
    internal sealed class GlowCloud : IDisposable {
        private static Material s_dot, s_line, s_shadow;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new();
        private readonly List<Vector2> _uv = new(), _corner = new();
        private readonly List<Color> _c = new();
        private readonly List<int> _i = new();

        public Transform Root { get; }

        public GlowCloud(Transform parent, string name, Material material = null) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Root = go.transform;
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material != null ? material : DotMaterial;
        }

        public void Draw(IEnumerable<ParticlePoint> points, Func<ParticlePoint, Color> colour, float size,
            IList<float> sizes = null) {
            _v.Clear(); _uv.Clear(); _corner.Clear(); _c.Clear(); _i.Clear();
            var n = 0;
            foreach (ParticlePoint p in points) {
                Vector3 at = BakCoordinateConverter.ConvertPosition((int)p.X, (int)p.Y, (int)p.Z);
                float h = (sizes != null && n < sizes.Count ? sizes[n] : size) / BakCoordinateConverter.WorldScale / 2f;
                Color c = colour(p);
                int b = _v.Count;
                for (var k = 0; k < 4; k++) {
                    var corner = new Vector2(k is 0 or 3 ? -1 : 1, k < 2 ? -1 : 1);
                    _v.Add(at);
                    _uv.Add((corner + Vector2.one) * 0.5f);
                    _corner.Add(corner * h);
                    _c.Add(c);
                }
                _i.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                n++;
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _corner);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_i, 0);
            // Billboarding happens in the shader, so pad the bounds by the largest quad.
            _mesh.RecalculateBounds();
            Bounds bounds = _mesh.bounds;
            bounds.Expand(1f);
            _mesh.bounds = bounds;
        }

        public static Material DotMaterial => s_dot ??= Make(RadialTexture(), billboard: true);
        public static Material LineMaterial => s_line ??= Make(LineTexture(), billboard: false);

        /// <summary>The dot, alpha-blended instead of added, so a dark pen darkens the ground.</summary>
        public static Material ShadowMaterial {
            get {
                if (s_shadow == null) {
                    s_shadow = Make(RadialTexture(), billboard: true);
                    s_shadow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                }
                return s_shadow;
            }
        }

        private static Material Make(Texture2D tex, bool billboard) {
            Shader shader = Shader.Find("BakAgain/SpellGlow");
            var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { mainTexture = tex };
            m.SetFloat("_Billboard", billboard ? 1f : 0f);
            return m;
        }

        private static Texture2D RadialTexture() {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++) {
                for (var x = 0; x < n; x++) {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D LineTexture() {
            const int n = 16;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++) {
                float d = Mathf.Abs(y - n / 2f + 0.5f) / (n / 2f);
                tex.SetPixel(0, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d)));
            }
            tex.Apply();
            return tex;
        }

        public void Dispose() {
            if (Root != null) {
                UnityEngine.Object.Destroy(Root.gameObject);
            }
            UnityEngine.Object.Destroy(_mesh);
        }
    }
}
