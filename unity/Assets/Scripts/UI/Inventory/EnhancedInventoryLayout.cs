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
        private PortraitRingView[] _rings;
        private Label _purse, _hint;
        private bool _warned;

        public EnhancedInventoryLayout(VisualElement root, GameSession session) {
            _root = root;
            _session = session;
        }

        public bool Applied => _layout != null;

        /// <summary>The canonical-frame surface the menu draws its item area on while applied.</summary>
        public VisualElement Sheet { get; private set; }

        public Button ExamineButton { get; private set; }
        public Button GiveButton { get; private set; }

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

            _layout = MovedElements.Box("enhanced-inv");
            _layout.name = "enhanced-inv";
            Texture2D table = Resources.Load<Texture2D>("BookSpread/table");
            if (table != null) {
                _layout.style.backgroundImage = Background.FromTexture2D(table);
            }
            VisualElement column = MovedElements.Box("enhanced-inv__portraits");
            VisualElement bar = MovedElements.Box("enhanced-inv__bar");
            VisualElement frame = MovedElements.Box("enhanced-inv__frame");
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
            _purse = new Label { pickingMode = PickingMode.Ignore };
            _purse.AddToClassList("enhanced-inv__purse-text");
            purse.Add(_purse);
            _hint = new Label { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("enhanced-inv__hint");
            bar.Add(_hint);
            ExamineButton = ActionButton(bar);
            GiveButton = ActionButton(bar);
            _moved.Move(_root.Q(UseName), bar, "enhanced-inv__button", clearSize: false);
            _moved.Move(_root.Q(ExitName), bar, "enhanced-inv__button", clearSize: false);

            _reqStage = reqStage;
            if (_reqStage != null) {
                _reqStage.style.display = DisplayStyle.None;
            }
            _field.RegisterCallback<GeometryChangedEvent>(_ => FitSheet());
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
            _moved.RestoreAll();
            _purse?.RemoveFromHierarchy();
            if (_reqStage != null) {
                _reqStage.style.display = StyleKeyword.Null;
            }
            _layout.RemoveFromHierarchy();
            _layout = _field = Sheet = _reqStage = null;
            _purse = _hint = null;
            ExamineButton = GiveButton = null;
            Array.Clear(_portraits, 0, 3);
            Array.Clear(_names, 0, 3);
            Array.Clear(_nameOverrides, 0, 3);
        }

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
                _names[i].text = _nameOverrides[i] ?? name;
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
            if (GameOptions.IsOn(EnhancedFeature.PortraitRings) != (_rings != null)) {
                SyncRings();
            }
            if (_rings == null) return;
            foreach (PortraitRingView r in _rings) r.Refresh();
        }

        private void SyncRings() {
            RemoveRings();
            if (!Applied || _session == null || !GameOptions.IsOn(EnhancedFeature.PortraitRings)) {
                return;
            }
            _rings = new PortraitRingView[3];
            for (int i = 0; i < 3; i++) {
                _rings[i] = new PortraitRingView(_session, _root.Q(PortraitNames[i]), i);
                _rings[i].Refresh();
            }
        }

        private void RemoveRings() {
            if (_rings == null) return;
            foreach (PortraitRingView r in _rings) r.Remove();
            _rings = null;
        }

        private void FitSheet() {
            if (_field == null || Sheet == null) {
                return;
            }
            Rect field = _field.contentRect;
            (float scale, float x, float y) = GameData.Resources.Layout.FieldFit.Fit(
                _itemBox.x, _itemBox.y, _itemBox.width, _itemBox.height,
                0f, 0f, field.width, field.height);
            Sheet.style.translate = new Translate(x, y);
            Sheet.style.scale = new Scale(new Vector2(scale, scale));
        }

        private Button ActionButton(VisualElement bar) {
            var b = new Button();
            b.AddToClassList("enhanced-inv__action");
            bar.Add(b);
            return b;
        }
    }
}
