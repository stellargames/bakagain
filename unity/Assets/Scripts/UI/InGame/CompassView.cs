namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Faithful scrolling compass. Reproduces <c>drawCompass</c> (ovr139 @ 0x4691f): the
    /// COMPASS.BMX strip (256 VGA px wide = a full 360°) is windowed to the HUD compass region
    /// and offset horizontally by the party heading, blitted twice (0x100 apart) so it wraps.
    /// Here a clipped window holds two strip copies (offset, offset+width) for the same seamless
    /// wrap; the heading→offset mapping is <see cref="CompassMath.ScrollFraction"/>. Reads the
    /// heading from <see cref="GameSession"/> (the source of truth).
    /// </summary>
    public sealed class CompassView {
        // The arc of the compass visible in the slot, in degrees. Replaces the original's
        // VGA 256px-strip / 31px-window ratio with a semantic constant; tune in-Editor.
        private const float CompassArcDegrees = 43.6f;
        private const string CompassAddress = "COMPASS.BMX#0";
        // Sign of the scroll vs heading. Verified against drawCompass (ovr139 @0x4691f 2026-06-26):
        // the original blits the strip at x = (heading>>8) - 256 + 0x90, so the strip x-offset
        // INCREASES (scrolls right) as heading rises. With `left = -offset`, that needs offset to
        // decrease with heading → ScrollSign = -1 (the +1 here scrolled it the wrong way).
        private const float ScrollSign = -1f;

        /// <summary>Name of the clipping window this view adds to the stage.</summary>
        /// <remarks>Public so a screen that rebuilds onto a persistent stage can drop the old one.</remarks>
        public const string WindowName = "BakCompassWindow";

        private readonly GameSession _session;
        private readonly IResourceProviderService _resources;
        private VisualElement _window;
        private bool _hidden;
        private VisualElement _stripA;
        private VisualElement _stripB;
        private ushort _lastHeading = 0xFFFF;

        private float _stripW;   // displayed full-360° strip width (set from the slot)
        private float _slotH;    // slot height (strip height)

        public CompassView(GameSession session, IResourceProviderService resources) {
            _session = session;
            _resources = resources;
        }

        /// <summary>Build the compass into a canonical slot rect (no VGA knowledge).</summary>
        public async UniTask BuildAsync(VisualElement stage, Rect slot, object owner) {
            Sprite strip = await _resources.LoadAssetAsync<Sprite>(CompassAddress, owner);
            if (strip == null) {
                return;
            }
            _slotH = slot.height;
            _stripW = slot.width * (360f / CompassArcDegrees);
            _window = new VisualElement {
                name = WindowName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = slot.x, top = slot.y, width = slot.width, height = slot.height,
                    overflow = Overflow.Hidden,
                    display = _hidden ? DisplayStyle.None : DisplayStyle.Flex,
                },
            };
            _stripA = MakeStrip(strip);
            _stripB = MakeStrip(strip);
            _window.Add(_stripA);
            _window.Add(_stripB);
            stage.Add(_window);
            Refresh(force: true);
        }

        /// <summary>
        /// Show or hide the compass.
        /// </summary>
        /// <remarks>
        /// <b>A fight hides it.</b> The original draws the compass only on the travel frame
        /// (<c>screen_render_main_frame</c> and the turn/move paths call <c>uiwidget_compass_draw</c>);
        /// the arena loads its own <c>cframe.scx</c> (<c>COMBAT.C:879</c>) and never calls it.
        /// Remembered, so a hide asked for before the async build lands still applies. Called every
        /// frame, so only a change does anything.
        /// </remarks>
        public void SetVisible(bool visible) {
            if (_hidden == !visible) {
                return;
            }
            _hidden = !visible;
            if (_window != null) {
                _window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private VisualElement MakeStrip(Sprite strip) => new VisualElement {
            name = "BakCompassStrip",
            pickingMode = PickingMode.Ignore,
            style = {
                position = Position.Absolute,
                top = 0,
                height = _slotH,
                width = _stripW,
                backgroundImage = Background.FromSprite(strip),
                backgroundSize = new BackgroundSize(new Length(_stripW), new Length(_slotH)),
            },
        };

        /// <summary>Scroll the strip to the current heading. Cheap; call each frame while shown.</summary>
        public void Refresh(bool force = false) {
            if (_stripA == null) {
                return;
            }
            ushort heading = unchecked((ushort)_session.Rotation);
            if (!force && heading == _lastHeading) {
                return;
            }
            _lastHeading = heading;
            // One full turn scrolls exactly one strip width; the two copies (offset, offset+StripW)
            // wrap seamlessly — the UI Toolkit equivalent of the original's double blit.
            // Wrap into [0, StripW): C#'s % keeps the dividend's sign, so with ScrollSign = -1 every
            // non-north heading gave a NEGATIVE offset, which pushed both copies to the right of the
            // window and left it blank everywhere except due north. A sign-safe modulo keeps one copy
            // covering the left of the window and the other the right.
            float raw = CompassMath.ScrollFraction(heading) * _stripW * ScrollSign;
            float offset = ((raw % _stripW) + _stripW) % _stripW;
            _stripA.style.left = -offset;
            _stripB.style.left = -offset + _stripW;
        }
    }
}
