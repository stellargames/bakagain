namespace BakAgain.UI.Inventory {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.UI.InGame;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced inventory's full-window layout (spec 2026-10-13): the item field on black, the
    /// portraits in a column beside it and the buttons in a bar beneath, over the table texture.
    /// </summary>
    /// <remarks>
    /// <para><b>The sheet.</b> Everything the faithful screen draws in its item area is placed in the
    /// REQ's canonical frame, and its hit tests work in that frame. <see cref="Sheet"/> is an element
    /// of that frame's size, scaled into the field so that the item box fills it; the menu draws on the
    /// sheet exactly as it draws on the stage.</para>
    /// <para><b>The chrome</b> is the REQ's own elements, moved here (the travel HUD's pattern), so
    /// every click still reaches the menu's Primary/SecondaryAction by its action id. All placement is
    /// in the theme (<c>.enhanced-inv*</c>).</para>
    /// </remarks>
    public sealed class EnhancedInventoryLayout {
        public const string TargetClass = "enhanced-inv__portrait--target";
        public const string HolderClass = "enhanced-inv__portrait--holder";
        public const string HoverClass = "enhanced-inv__portrait--hover";
        /// <summary>Whose pack or paperdoll is shown: a bright ring at the socket's rim, outside the
        /// health and stamina rings that cover the original's ring on the face.</summary>
        public const string ActiveClass = "enhanced-inv__portrait--active";
        public const string ShadeClass = "enhanced-inv__shade";

        private static readonly string[] PortraitNames = { "hotspot_2", "hotspot_3", "hotspot_4" };
        private const string WindowName = "hotspot_32";
        private const string PurseName = "hotspot_34";
        private const string UseName = "imagebutton_22";
        private const string ExitName = "imagebutton_1";

        private readonly VisualElement _root;
        private readonly GameSession _session;
        private Rect _itemBox;
        private readonly MovedElements _moved = new();
        private VisualElement _layout, _field, _reqStage;
        private readonly VisualElement[] _portraits = new VisualElement[3];
        private readonly Label[] _names = new Label[3];
        private readonly string[] _nameOverrides = new string[3];
        private PortraitRingSet _rings;
        private Label _purse, _hint;
        private bool _warned;

        public EnhancedInventoryLayout(VisualElement root, GameSession session) {
            _root = root;
            _session = session;
        }

        public bool Applied => _layout != null;

        /// <summary>The canonical-frame surface the menu draws its item area on while applied.</summary>
        public VisualElement Sheet { get; private set; }

        /// <summary>
        /// Moves the chrome and shows the layout. False, with nothing moved, while a REQ element is
        /// missing (the REQ builds asynchronously); warns once.
        /// </summary>
        /// <param name="reqStage">The REQ's own stage, hidden while applied.</param>
        /// <param name="itemBox">The faithful item area in the canonical frame
        /// (<c>InventoryLayout.FullItemsBox</c>): what the field shows.</param>
        public bool Apply(VisualElement reqStage, Rect itemBox) {
            if (Applied && _layout.panel == null) {
                Revert(); // the REQ rebuilt the root under us; apply to the new elements
            }
            if (Applied) {
                return true;
            }
            if (itemBox.width <= 0f || itemBox.height <= 0f) {
                if (!_warned) {
                    Debug.LogWarning("EnhancedInventory: no px item box in the layout; keeping the faithful layout.");
                    _warned = true;
                }
                return false;
            }
            _itemBox = itemBox;
            var names = new List<string>(PortraitNames) { WindowName, PurseName, UseName, ExitName };
            foreach (string n in names) {
                if (_root.Q(n) == null) {
                    if (!_warned) {
                        Debug.LogWarning($"EnhancedInventory: REQ element '{n}' missing; keeping the faithful layout.");
                        _warned = true;
                    }
                    return false;
                }
            }

            // The casting screen's materials: one slab of dark marble, the item field held in a stone
            // frame strapped down with copper, the members in stone sockets, stone buttons.
            _layout = MovedElements.Box("enhanced-inv");
            _layout.name = "enhanced-inv";
            _layout.style.backgroundImage = EnhancedArt.Load("marble");
            VisualElement column = MovedElements.Box("enhanced-inv__portraits");
            VisualElement bar = MovedElements.Box("enhanced-inv__bar");
            VisualElement frame = MovedElements.Box("enhanced-inv__frame");
            _frameBox = frame;
            _field = MovedElements.Box("enhanced-inv__field");
            Sheet = new VisualElement { name = "enhanced-inv-sheet", pickingMode = PickingMode.Ignore };
            Sheet.AddToClassList("enhanced-inv__sheet");
            _field.Add(Sheet);
            frame.Add(_field);
            // The field goes last so what is drawn on the sheet — the dragged item, the drop flight,
            // the quantity picker's scrim — is above the column and the bar.
            _layout.Add(column);
            _layout.Add(bar);
            _layout.Add(frame);
            _root.Add(_layout);

            for (int i = 0; i < 3; i++) {
                VisualElement wrap = MovedElements.Box("enhanced-inv__portrait");
                wrap.style.backgroundImage = EnhancedArt.Load("socket");
                column.Add(wrap);
                _portraits[i] = wrap;
                _moved.Move(_root.Q(PortraitNames[i]), wrap, "enhanced-inv__head", clearSize: true);
                _names[i] = new Label { pickingMode = PickingMode.Ignore };
                _names[i].AddToClassList("enhanced-inv__name");
                wrap.Add(_names[i]);
            }
            _moved.Move(_root.Q(WindowName), bar, "enhanced-inv__window", clearSize: false);
            VisualElement purse = _root.Q(PurseName);
            _moved.Move(purse, bar, "enhanced-inv__purse", clearSize: false);
            purse.style.backgroundImage = EnhancedArt.Load("parchment");
            _purse = new Label { pickingMode = PickingMode.Ignore };
            _purse.AddToClassList("enhanced-inv__purse-text");
            purse.Add(_purse);
            _hint = new Label { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("enhanced-inv__hint");
            bar.Add(_hint);
            _moved.Move(_root.Q(UseName), bar, "enhanced-inv__button", clearSize: false);
            _moved.Move(_root.Q(ExitName), bar, "enhanced-inv__button", clearSize: false);
            // Use and Exit wear the original's own faces (TASK-941: the engraved words were too small
            // and dark to read); only a Use with no face gets a stone, see ShowUseFace.
            ShowUseFace(_useHasFace);

            _reqStage = reqStage;
            if (_reqStage != null) {
                _reqStage.style.display = DisplayStyle.None;
            }
            _field.RegisterCallback<GeometryChangedEvent>(_ => FitSheet());
            _layout.RegisterCallback<GeometryChangedEvent>(_ => DrawFrame());
            SyncRings();
            RefreshNames();
            return true;
        }

        /// <summary>Puts every moved element back with its REQ placement. Tolerates a root that was
        /// cleared underneath (the REQ loader clears it when the screen goes away).</summary>
        public void Revert() {
            if (!Applied) {
                return;
            }
            RemoveRings();
            _root.Q(UseName)?.Q(BlankStoneName)?.RemoveFromHierarchy();
            _frame?.Release();
            _frame = null;
            _frameBox = null;
            _moved.RestoreAll();
            _purse?.RemoveFromHierarchy();
            if (_reqStage != null) {
                _reqStage.style.display = StyleKeyword.Null;
            }
            _layout.RemoveFromHierarchy();
            _layout = _field = Sheet = _reqStage = null;
            _purse = _hint = null;
            Array.Clear(_portraits, 0, 3);
            Array.Clear(_names, 0, 3);
            Array.Clear(_nameOverrides, 0, 3);
        }

        public const string BlankStoneName = "enhanced-inv-blank-stone";

        /// <summary>The original's blank stone face (the one a disabled button wears), loaded by the
        /// menu; until it arrives the face-less Use shows nothing.</summary>
        public Sprite BlankStone { get; set; }

        /// <summary>Whether the Use button wears a face now (the hand, or the shop's next-page arrow).
        /// Without one the faithful screen shows the SCX's bare stone, which this layout hides, so the
        /// original's blank stone stands in for it: a child, because the icon loader clears the
        /// button's own face on every press.</summary>
        public void ShowUseFace(bool hasFace) {
            _useHasFace = hasFace;
            VisualElement use = _root.Q(UseName);
            if (!Applied || use == null) return;
            use.Q(BlankStoneName)?.RemoveFromHierarchy();
            if (hasFace || BlankStone == null) return;
            var stone = new VisualElement { name = BlankStoneName, pickingMode = PickingMode.Ignore };
            stone.style.position = Position.Absolute;
            stone.style.left = stone.style.top = stone.style.right = stone.style.bottom = 0;
            stone.SetBackgroundSpriteNativeSizeTopLeft(BlankStone);
            use.Add(stone);
        }

        private bool _useHasFace = true;
        private VisualElement _frameBox;
        private StoneFrame _frame;

        /// <summary>The stone frame round the item field, held down by straps on every side.</summary>
        private void DrawFrame() {
            if (_frameBox == null || _frameBox.panel == null) return;
            Rect wb = _frameBox.worldBound;
            if (float.IsNaN(wb.width) || wb.width <= 0f) return;
            float band = _frameBox.resolvedStyle.paddingLeft;
            // Hug the panels: the sheet keeps its shape inside the field, so on a screen wider than
            // 16:9 the field's own box would leave marble between the frame and the panels.
            Rect panels = PanelsBound();
            if (panels.width > 0f) {
                wb = Rect.MinMaxRect(panels.xMin - band, panels.yMin - band, panels.xMax + band, panels.yMax + band);
            }
            Vector2 o = _layout.WorldToLocal(wb.position);
            var outer = new Rect(o.x, o.y, wb.width, wb.height);
            _frame ??= new StoneFrame(_layout, under: _frameBox); // the sheet's picker and drag stay on top
            // A member's pack is two panels, the paperdoll and the pack, side by side: the frame's middle
            // bar lies over the gap between them, as the casting screen's joins its two panels.
            float middle = MiddleBetweenPanels();
            _frame.Draw(outer, band, middle);
            var spots = new List<(float X, float Y, bool Vertical)>();
            StoneFrame.AddSides(spots, outer, band);
            if (middle >= 0f) StoneFrame.AddMiddle(spots, outer, middle);
            _frame.Strap(spots, band);
        }

        /// <summary>Redraws the frame after a render: the panels it frames change with the view.</summary>
        /// <remarks>Driven by the panels' own layout: a redraw scheduled from the render ran before the
        /// new panels had a position, and the middle bar never appeared.</remarks>
        public void RefreshFrame() {
            if (Sheet == null) return;
            foreach (VisualElement p in Sheet.Query(PanelName).ToList()) {
                if (p.userData == this) continue;
                p.userData = this;
                p.RegisterCallback<GeometryChangedEvent>(_ => DrawFrame());
            }
            _layout?.schedule.Execute(DrawFrame);
        }

        /// <summary>The centre of the gap between exactly two side-by-side item panels, in the layout's
        /// units; -1 when the view has one panel (a chest, a shop, the keys).</summary>
        private float MiddleBetweenPanels() {
            List<VisualElement> panels = Sheet?.Query(PanelName).Where(p => p.resolvedStyle.display != DisplayStyle.None).ToList();
            if (panels == null || panels.Count != 2) return -1f;
            Rect a = panels[0].worldBound, b = panels[1].worldBound;
            if (float.IsNaN(a.width) || float.IsNaN(b.width)) return -1f;
            if (a.x > b.x) (a, b) = (b, a);
            if (b.xMin <= a.xMax) return -1f;
            return _layout.WorldToLocal(new Vector2((a.xMax + b.xMin) / 2f, a.center.y)).x;
        }

        /// <summary>The world rect round every item panel; empty when none has a layout yet.</summary>
        private Rect PanelsBound() {
            Rect bound = Rect.zero;
            if (Sheet == null) return bound;
            foreach (VisualElement p in Sheet.Query(PanelName).Where(e => e.resolvedStyle.display != DisplayStyle.None).ToList()) {
                Rect r = p.worldBound;
                if (float.IsNaN(r.width) || r.width <= 0f) continue;
                bound = EnhancedSheet.Union(bound, r);
            }
            return bound;
        }

        /// <summary>The menu's name for a black item panel (InventoryMenu draws them on the sheet).</summary>
        private const string PanelName = "panel_bg";

        public void SetPurse(string text) {
            if (_purse != null) _purse.text = text;
        }

        public void SetHint(string text) {
            if (_hint != null) _hint.text = text ?? string.Empty;
        }

        /// <summary>The portrait's wrapper (head + name), for the state classes.</summary>
        public VisualElement Portrait(int slot) => slot is >= 0 and < 3 ? _portraits[slot] : null;

        /// <summary>Shows <paramref name="text"/> under a portrait instead of the member's name; null
        /// puts the name back.</summary>
        public void SetPortraitLabel(int slot, string text) {
            if (slot is < 0 or >= 3) return;
            _nameOverrides[slot] = text;
            RefreshNames();
        }

        public void RefreshNames() {
            IReadOnlyList<string> names = _session?.ActivePartyMemberNames;
            for (int i = 0; i < 3; i++) {
                if (_names[i] == null) continue;
                string name = names != null && i < names.Count ? names[i] : string.Empty;
                // No names under the faces — the faces say who — only what a drop would do.
                _names[i].text = _nameOverrides[i] ?? string.Empty;
                _names[i].style.display = _nameOverrides[i] == null ? DisplayStyle.None : DisplayStyle.Flex;
                _portraits[i].style.display = string.IsNullOrEmpty(name) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>The member's name for a portrait, for labels that name people.</summary>
        public string NameOf(int slot) {
            IReadOnlyList<string> names = _session?.ActivePartyMemberNames;
            return names != null && slot >= 0 && slot < names.Count ? names[slot] : string.Empty;
        }

        /// <summary>Health rings while that option is on too (polled by the menu).</summary>
        public void RefreshRings() {
            if (Applied) _rings?.Refresh();
        }

        private void SyncRings() {
            RemoveRings();
            _rings = new PortraitRingSet(_session, 3, i => _root.Q(PortraitNames[i]));
            _rings.Refresh();
        }

        private void RemoveRings() {
            _rings?.Remove();
            _rings = null;
        }

        private void FitSheet() {
            if (_field == null || Sheet == null) {
                return;
            }
            Rect field = _field.contentRect;
            EnhancedSheet.Fit(Sheet, new Rect(0f, 0f, field.width, field.height), _itemBox);
        }
    }
}
