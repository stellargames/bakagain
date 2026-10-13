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
        private PortraitRingView[] _rings;
        private float _pollClock;
        private readonly List<Saved> _saved = new();
        private VisualElement _hud;
        private Label _status;
        private bool _warned;

        private readonly struct Saved {
            public readonly VisualElement Element, Parent;
            public readonly int Index;
            public readonly StyleEnum<Position> Position;
            public readonly StyleLength Left, Top, Width, Height;
            public readonly StyleBackground Background;
            public readonly StyleTranslate Translate;
            public Saved(VisualElement e) {
                Element = e; Parent = e.parent; Index = e.parent.IndexOf(e);
                Position = e.style.position;
                Left = e.style.left; Top = e.style.top; Width = e.style.width; Height = e.style.height;
                Background = e.style.backgroundImage; Translate = e.style.translate;
            }
        }

        /// <param name="statusFontFraction">The status line's font size as a fraction of the window's height.</param>
        public EnhancedHudLayout(VisualElement root, GameSession session, Core.Services.GameClock clock, float statusFontFraction,
                Elements elements) {
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
                SetStatus(GameData.Resources.Text.UiTemplates.Format(
                    GameData.Resources.Text.UiTemplates.EnhancedStatusKey,
                    ("day", _clock.Day), ("hour", _clock.HourOfDay)));
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
            VisualElement portraits = Box("enhanced-hud__portraits");
            VisualElement buttons = Box("enhanced-hud__buttons");
            VisualElement top = Box("enhanced-hud__top");
            _status = new Label { name = "enhanced-hud-status", pickingMode = PickingMode.Ignore };
            _status.AddToClassList("enhanced-hud__status");
            _hud.Add(portraits); _hud.Add(buttons); _hud.Add(top);
            _root.Add(_hud);

            foreach (string n in _elements.Portraits) Move(_root.Q(n), portraits, "enhanced-hud__portrait", clearSize: true);
            // Buttons and the compass keep their REQ size: a button's face is its icon at native size
            // (inline, re-applied on every press), the compass strips are sized from the window in
            // panel px. The theme scales the button column as a whole instead.
            foreach (string n in _elements.Buttons) Move(_root.Q(n), buttons, "enhanced-hud__button", clearSize: false);
            // The spell plaque sits left of the compass on the theme's translucent bar instead of its
            // parchment, with the runes inside it rather than centred on the stage.
            if (_elements.SpellBar) {
                VisualElement bar = _root.Q(SpellBar);
                Move(bar, top, "enhanced-hud__spellbar", clearSize: false);
                bar.style.backgroundImage = StyleKeyword.Null;
                VisualElement runes = _root.Q(SpellRunes);
                Move(runes, bar, "enhanced-hud__spellrunes", clearSize: false);
                runes.style.translate = StyleKeyword.Null;
            }
            Move(_root.Q(Compass), top, "enhanced-hud__compass", clearSize: false);
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
            for (int i = _saved.Count - 1; i >= 0; i--) {
                Saved s = _saved[i];
                s.Element.RemoveFromClassList("enhanced-hud__portrait");
                s.Element.RemoveFromClassList("enhanced-hud__button");
                s.Element.RemoveFromClassList("enhanced-hud__compass");
                s.Element.RemoveFromClassList("enhanced-hud__spellbar");
                s.Element.RemoveFromClassList("enhanced-hud__spellrunes");
                s.Parent.Insert(Mathf.Min(s.Index, s.Parent.childCount), s.Element);
                s.Element.style.position = s.Position;
                s.Element.style.left = s.Left; s.Element.style.top = s.Top;
                s.Element.style.width = s.Width; s.Element.style.height = s.Height;
                s.Element.style.backgroundImage = s.Background; s.Element.style.translate = s.Translate;
            }
            _saved.Clear();
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

        private void Move(VisualElement e, VisualElement into, string cls, bool clearSize) {
            _saved.Add(new Saved(e));
            // Inline REQ placement beats any class, so it has to go for the theme to apply.
            e.style.position = StyleKeyword.Null;
            e.style.left = StyleKeyword.Null; e.style.top = StyleKeyword.Null;
            if (clearSize) {
                e.style.width = StyleKeyword.Null; e.style.height = StyleKeyword.Null;
            }
            e.AddToClassList(cls);
            into.Add(e);
        }

        private static VisualElement Box(string cls) {
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList(cls);
            return c;
        }
    }
}
