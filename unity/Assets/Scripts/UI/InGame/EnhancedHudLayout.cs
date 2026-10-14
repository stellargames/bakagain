namespace BakAgain.UI.InGame {
    using System.Collections.Generic;
    using BakAgain.Core;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced full-screen travel's HUD (spec 2026-10-08 §3), and the overhead map's: the REQ elements themselves,
    /// reparented into one container on the document root. Every element keeps its action id, so
    /// clicks go through InGameScreen's existing Primary/SecondaryAction. All geometry is in the
    /// theme (<c>.enhanced-hud*</c>); this class only moves elements and clears their inline REQ placement.
    /// It also owns what only the HUD shows: the portrait rings and the day/hour status line.
    /// </summary>
    /// <remarks>
    /// The container is added to the document root AFTER the canonical stage, so it sits above the
    /// world's click area (hotspot_192), which full-screen stretches over the whole window.
    /// </remarks>
    public sealed class EnhancedHudLayout {
        /// <summary>The REQ elements one screen's HUD moves: travel's REQ_MAIN or the map's REQ_MAP.</summary>
        public sealed class Elements {
            public string[] Portraits, Buttons, Arrows;
            /// <summary>Travel only: REQ_MAP has no spell plaque.</summary>
            public bool SpellBar;
        }

        // Both REQs: follow-road is a Toggle, so it is toggle_19, not imagebutton_19.
        private static readonly string[] PortraitNames = { "hotspot_2", "hotspot_3", "hotspot_4" };
        private static readonly string[] ArrowNames = { "imagebutton_72", "imagebutton_80", "imagebutton_75", "imagebutton_77" };

        public static readonly Elements Travel = new() {
            Portraits = PortraitNames, Arrows = ArrowNames, SpellBar = true,
            Buttons = new[] { "toggle_19", "imagebutton_50", "imagebutton_46", "imagebutton_48", "imagebutton_18", "imagebutton_24" },
        };

        public static readonly Elements Map = new() {
            Portraits = PortraitNames, Arrows = ArrowNames, SpellBar = false,
            Buttons = new[] { "toggle_19", "imagebutton_33", "imagebutton_73", "imagebutton_81", "imagebutton_18", "imagebutton_50" },
        };

        private const string Compass = CompassView.WindowName;
        private const string SpellBar = SpellEffectCaptionView.PlaqueName;
        private const string SpellRunes = SpellEffectCaptionView.SymbolsName;

        private readonly VisualElement _root;
        private readonly Elements _elements;
        private readonly GameSession _session;
        private readonly Core.Services.GameClock _clock;
        private readonly float _statusFontFraction;
        private readonly System.Func<string> _place;
        private PortraitRingView[] _rings;
        private float _pollClock;
        private VisualElement _hud;
        private Label _status;
        private bool _warned;
        private readonly MovedElements _moved = new();

        /// <param name="statusFontFraction">The status line's font size as a fraction of the window's height.</param>
        /// <param name="place">The town the party is at, shown before the day (TASK-866); null for none.</param>
        public EnhancedHudLayout(VisualElement root, GameSession session, Core.Services.GameClock clock, float statusFontFraction,
                Elements elements, System.Func<string> place = null) {
            _place = place;
            _root = root;
            _elements = elements;
            _session = session;
            _clock = clock;
            _statusFontFraction = statusFontFraction;
        }

        public bool Applied => _hud != null;

        /// <summary>Whether the last <see cref="Sync"/> left the HUD in sight; chrome beside it follows this.</summary>
        public bool Shown { get; private set; }

        /// <summary>
        /// Once a frame: applied while <paramref name="wanted"/> (the world is full-window), reverted to
        /// the faithful REQ layout otherwise, and hidden while not <paramref name="shown"/> or while a
        /// dialog page is up (<see cref="DialogManager.PageOverTheWorld"/>). The rings
        /// follow their option; they and the status line are polled at 4 Hz.
        /// </summary>
        public void Sync(bool wanted, bool shown) {
            if (wanted && !Applied) {
                if (!Apply()) {
                    return;
                }
                _pollClock = 0f;
            } else if (!wanted) {
                Revert();
                return;
            }
            Shown = shown && !DialogManager.PageOverTheWorld;
            SetVisible(Shown);
            if (GameOptions.IsOn(EnhancedFeature.PortraitRings) != (_rings != null)) {
                SyncRings();
                _pollClock = 0f;
            }
            // ponytail: 4 Hz poll; switch to a party-stats change event if one appears.
            _pollClock -= Time.unscaledDeltaTime;
            if (_pollClock > 0f) {
                return;
            }
            _pollClock = 0.25f;
            if (_rings != null) {
                foreach (PortraitRingView r in _rings) {
                    r.Refresh();
                }
            }
            if (_clock != null) {
                string status = GameData.Resources.Text.UiTemplates.Format(
                    GameData.Resources.Text.UiTemplates.EnhancedStatusKey,
                    ("day", _clock.Day), ("hour", _clock.HourOfDay));
                string place = _place?.Invoke();
                SetStatus(string.IsNullOrEmpty(place) ? status : place + " · " + status);
            }
        }

        private void SyncRings() {
            RemoveRings();
            if (!GameOptions.IsOn(EnhancedFeature.PortraitRings) || _session == null) {
                return;
            }
            _rings = new PortraitRingView[_elements.Portraits.Length];
            for (int i = 0; i < _rings.Length; i++) {
                _rings[i] = new PortraitRingView(_session, _root.Q(_elements.Portraits[i]), i);
            }
        }

        private void RemoveRings() {
            if (_rings == null) {
                return;
            }
            foreach (PortraitRingView r in _rings) {
                r.Remove();
            }
            _rings = null;
        }

        /// <summary>
        /// Moves the elements into the HUD. False, with nothing moved, when one is missing (the
        /// compass is built asynchronously, so an early call can find it absent); warns once.
        /// </summary>
        public bool Apply() {
            if (Applied) return true;
            var all = new List<string>(_elements.Portraits);
            all.AddRange(_elements.Buttons);
            all.Add(Compass);
            if (_elements.SpellBar) {
                all.Add(SpellBar);
                all.Add(SpellRunes);
            }
            all.AddRange(_elements.Arrows);
            foreach (string name in all) {
                if (_root.Q(name) == null) {
                    if (!_warned) {
                        Debug.LogWarning($"EnhancedHud: REQ element '{name}' missing; keeping the faithful layout.");
                        _warned = true;
                    }
                    return false;
                }
            }

            _hud = new VisualElement { name = "enhanced-hud", pickingMode = PickingMode.Ignore };
            _hud.AddToClassList("enhanced-hud");
            VisualElement portraits = MovedElements.Box("enhanced-hud__portraits");
            VisualElement buttons = MovedElements.Box("enhanced-hud__buttons");
            VisualElement top = MovedElements.Box("enhanced-hud__top");
            _status = new Label { name = "enhanced-hud-status", pickingMode = PickingMode.Ignore };
            _status.AddToClassList("enhanced-hud__status");
            _hud.Add(portraits); _hud.Add(buttons); _hud.Add(top);
            _root.Add(_hud);

            foreach (string n in _elements.Portraits) _moved.Move(_root.Q(n), portraits, "enhanced-hud__portrait", clearSize: true);
            // Buttons and the compass keep their REQ size: a button's face is its icon at native size
            // (inline, re-applied on every press), the compass strips are sized from the window in
            // panel px. The theme scales the button column as a whole instead.
            foreach (string n in _elements.Buttons) _moved.Move(_root.Q(n), buttons, "enhanced-hud__button", clearSize: false);
            // The spell plaque sits left of the compass on the theme's translucent bar instead of its
            // parchment, with the runes inside it rather than centred on the stage.
            if (_elements.SpellBar) {
                VisualElement bar = _root.Q(SpellBar);
                _moved.Move(bar, top, "enhanced-hud__spellbar", clearSize: false);
                bar.style.backgroundImage = StyleKeyword.Null;
                VisualElement runes = _root.Q(SpellRunes);
                _moved.Move(runes, bar, "enhanced-hud__spellrunes", clearSize: false);
                runes.style.translate = StyleKeyword.Null;
            }
            _moved.Move(_root.Q(Compass), top, "enhanced-hud__compass", clearSize: false);
            top.Add(_status);
            foreach (string n in _elements.Arrows) _root.Q(n).style.display = DisplayStyle.None;

            // Square portraits from the theme's heights, and the status font from the window height —
            // sizes derived from layout data, not coordinates.
            _hud.RegisterCallback<GeometryChangedEvent>(_ => {
                if (_hud == null) return;
                foreach (VisualElement e in _hud.Query(className: "enhanced-hud__portrait").ToList()) e.style.width = e.resolvedStyle.height;
                _status.style.fontSize = _hud.resolvedStyle.height * _statusFontFraction;
            });
            return true;
        }

        public void Revert() {
            if (!Applied) return;
            RemoveRings();
            _moved.RestoreAll();
            foreach (string n in _elements.Arrows) {
                VisualElement a = _root.Q(n);
                if (a != null) a.style.display = StyleKeyword.Null;
            }
            _hud.RemoveFromHierarchy();
            _hud = null;
            _status = null;
        }

        /// <summary>Hides the HUD without undoing it, e.g. while a dialog shows the world through its hole.</summary>
        private void SetVisible(bool visible) {
            // display, not visibility: the rings set their own visibility, which beats an inherited one.
            if (_hud != null) _hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetStatus(string text) {
            if (_status != null && _status.text != text) _status.text = text;
        }
    }
}
