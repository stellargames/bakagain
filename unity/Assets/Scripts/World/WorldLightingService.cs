namespace BakAgain.World {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// The world's day/night lighting — <c>ApplyDynamicLighting</c> (seg031 @0x2cdd9) and the
    /// sources that feed it.
    /// </summary>
    /// <remarks>
    /// <b>The original mutates the palette; we run the same chain per pixel.</b> Its blends walk
    /// every lit palette entry toward the dragon's-breath colour, then a tint, then black. Our
    /// palettes are RGBA long before anything renders, so the identical arithmetic applied to the
    /// rendered colour lands in the same place — see <c>BakLighting.hlsl</c>, which does the three
    /// lerps and nothing else. Every rule stays here, in C#, where it can be tested.
    ///
    /// <para><b>What it does NOT reproduce is the protected range.</b> The original keeps palette
    /// entries below 112 (or 16) unlit because the interface shares the display with the world. Our
    /// interface is drawn separately and never sees these shaders, so the protection is structural
    /// rather than something to port — see <see cref="DynamicLighting.FirstLitEntry"/> for the rule
    /// it replaces.</para>
    ///
    /// <para><b>Measured 2026-09-07, and it is not only the interface.</b> The world runs in
    /// <see cref="DynamicLighting.ModeZone"/>, whose first lit entry is <b>112</b> — mode 2's 16 is
    /// for a screen that has queued its own palette, not for underground, and the two are easy to
    /// confuse because the ZONE KIND from Z##DEF.DAT is also called mode 2. Across the twelve zone
    /// palettes, entries <b>0..111 are byte-identical</b> and 112..255 differ (only six of twelve
    /// match). So the protected range is the SHARED set — interface plus common art — and the lit
    /// range is exactly the zone's own colours.
    ///
    /// <para>That makes the omission wider than "the interface": world geometry that draws from the
    /// shared range is lit here and is NOT lit in the original. Underground with a candle the
    /// original shows a green band over otherwise brown walls, which is precisely the 112..255
    /// surfaces tinting while the shared ones do not; ours tints the whole frame. See TASK-372 —
    /// still open, because the shared/zone distinction is knowable at conversion time (the range is
    /// fixed across every zone) but nothing carries it to the shader yet.</para></para>
    /// </remarks>
    public sealed class WorldLightingService {
        private static readonly int DragonsBreathId = Shader.PropertyToID("_BakLightDragonsBreath");
        private static readonly int TintId = Shader.PropertyToID("_BakLightTint");
        private static readonly int DarkenId = Shader.PropertyToID("_BakLightDarken");

        private readonly GameSession _session;
        private readonly IGameClock _clock;

        public WorldLightingService(GameSession session, IGameClock clock) {
            _session = session;
            _clock = clock;
        }

        /// <summary>Whether the party's zone ignores the clock — see <see cref="Refresh"/>.</summary>
        public bool Underground { get; set; }

        /// <summary>The lighting as of the last <see cref="Refresh"/>.</summary>
        public DynamicLighting.Lighting Current { get; private set; } =
            new DynamicLighting.Lighting(DynamicLighting.Tint.None, 0,
                DynamicLighting.MaximumLight, DynamicLighting.Mode1FirstLitEntry);

        /// <summary>Dragon's breath as of the last <see cref="Refresh"/>.</summary>
        public int DragonsBreathLevel { get; private set; }

        /// <summary>
        /// Recomputes the lighting from the clock and the running light-source timers.
        /// </summary>
        /// <remarks>
        /// <b>A light source's level is the square of the minutes it has left</b>, so the sources
        /// come from timer remainders rather than from inventory or from which spells are active —
        /// the same timers <see cref="FieldSpells"/>' lighting spells schedule.
        ///
        /// <para>Dragon's breath is the odd one: it builds to its peak at expiry instead of fading,
        /// and it is the only source that reaches the top of the scale.</para>
        /// </remarks>
        public void Refresh() {
            int candle = LevelOf(LightSourceDecay.Source.CandleGlow);
            int stardusk = LevelOf(LightSourceDecay.Source.Stardusk);
            int item = LevelOf(LightSourceDecay.Source.Item);
            DragonsBreathLevel = LevelOf(LightSourceDecay.Source.DragonsBreath);

            int hourTicks = (int)(_session?.GameTimeIn2Seconds ?? 0);
            int daylight = DaylightLevel.At(hourTicks);
            DynamicLighting.Tint tint = Underground
                ? DynamicLighting.Tint.Candle
                : stardusk > 0 ? DynamicLighting.Tint.Stardusk : DynamicLighting.Tint.ItemLight;

            Current = DynamicLighting.Resolve(Underground, DynamicLighting.ModeZone, daylight,
                candle, stardusk, item,
                DaylightLevel.WithFloor(hourTicks, DynamicLighting.TintFloorFor(tint)));

            Publish();
        }

        /// <summary>
        /// Binds the zone's sky so it is lit with everything else.
        /// </summary>
        /// <remarks>
        /// <b>The sky is the camera's clear colour, not a rendered surface, so no shader reaches
        /// it.</b> In the original it is an ordinary palette entry inside the lit range and darkens
        /// with the rest of the world — leaving it alone gives a midnight scene a noon sky, which is
        /// the one part of the picture that makes the whole effect look broken.
        /// </remarks>
        public void BindSky(Camera camera, Color zoneSky) {
            _skyCamera = camera;
            _zoneSky = zoneSky;
        }

        private Camera _skyCamera;
        private Color _zoneSky = Color.black;

        /// <summary>
        /// The three movements applied to a colour — the same chain the shader runs.
        /// </summary>
        /// <remarks>
        /// Kept here rather than duplicated in HLSL for the sky's benefit: one implementation of
        /// the chain, two consumers.
        /// </remarks>
        public Color Apply(Color source) {
            Color lit = Blend(source, _dragonsBreath);
            lit = Blend(lit, _tint);
            lit = Blend(lit, _darken);

            // A combat spell's palette flash, which SpellVfx drives through the same global the
            // shaders read — the sky is inside the flashed range too (0x70..0xFE).
            return Blend(lit, Shader.GetGlobalVector(FlashId));
        }

        private static Color Blend(Color source, Vector4 movement) =>
            Color.Lerp(source, new Color(movement.x, movement.y, movement.z, source.a), movement.w);

        private static readonly int FlashId = Shader.PropertyToID("_BakFlash");
        private Vector4 _dragonsBreath;
        private Vector4 _tint;
        private Vector4 _darken;

        /// <summary>Hands the three movements to the shaders.</summary>
        /// <remarks>
        /// <b>Both ends of the blend are pass-through</b>, so a level of 0 and a level of 64 both
        /// mean "leave it alone". Expressed as a movement here rather than in HLSL so the shader
        /// cannot get the complement the wrong way round — which inverts every lighting effect in
        /// the game at once.
        /// </remarks>
        private void Publish() {
            _dragonsBreath = Movement(DynamicLighting.Colors.DragonsBreath, DragonsBreathLevel);
            _tint = Current.AppliesTint
                ? Movement(DynamicLighting.ColorOf(Current.Tint), Current.TintStrength)
                : Movement(DynamicLighting.Colors.Black, PaletteBlend.Scale);
            _darken = Movement(DynamicLighting.Colors.Black, Current.Light);

            Shader.SetGlobalVector(DragonsBreathId, _dragonsBreath);
            Shader.SetGlobalVector(TintId, _tint);
            Shader.SetGlobalVector(DarkenId, _darken);

            if (_skyCamera != null) {
                _skyCamera.backgroundColor = Apply(_zoneSky);
            }
        }

        /// <summary>A blend target and how far to move toward it, 0..1.</summary>
        private static Vector4 Movement((int R, int G, int B) color, int lightLevel) {
            float t = PaletteBlend.IsPassThrough(lightLevel)
                ? 0f
                : PaletteBlend.EffectOf(lightLevel) / (float)PaletteBlend.Scale;

            return new Vector4(
                color.R / (float)PaletteBlend.MaxChannel,
                color.G / (float)PaletteBlend.MaxChannel,
                color.B / (float)PaletteBlend.MaxChannel,
                t);
        }

        /// <summary>Clock ticks in a game minute — the unit the decay curve counts in.</summary>
        private const int TicksPerMinute = GameData.Resources.GameState.GameTime.UnitsPerHour / 60;

        private int LevelOf(LightSourceDecay.Source source) {
            long remaining = _clock?.RemainingTicks(TimerType.Light, (int)source) ?? 0;
            if (remaining <= 0) {
                return 0;
            }

            return LightSourceDecay.LevelFor(source,
                (int)(remaining / TicksPerMinute),
                flickerBit: Time.frameCount & 1);
        }
    }
}
