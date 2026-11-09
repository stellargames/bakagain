namespace BakAgain.UI.Combat.Enhanced {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Combat;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced combat's quarrel picker (TASK-1043, owner 2026-11-09): one row with a stone per carried
    /// quarrel kind, each with how many are left, then Exit. No MORE and no blank stones. It is modal until
    /// a kind is picked: a full-window overlay takes every other click. After the pick the row stays up
    /// (it is the HUD the shot is aimed under, as the Classic menu is) and the field takes the target click.
    /// Every press goes through <see cref="ShootMenu.PrimaryAction"/>, so the shot itself is the Classic path.
    /// </summary>
    public sealed class EnhancedShootRow {
        public const string OverlayName = "ec-shoot";
        public const string ButtonClass = "ec-shoot__button";
        public const string CountClass = "ec-shoot__count";
        public const string ChosenClass = "ec-shoot__button--chosen";

        private readonly Func<int, string> _icon;
        private VisualElement _overlay;

        /// <param name="icon">Action id -> its SHOOT.DAT stone's image address (null: none).</param>
        public EnhancedShootRow(Func<int, string> icon) => _icon = icon;

        /// <summary>A stone was pressed: a quarrel kind's action id, or Exit's (0x21).</summary>
        public event Action<int> Pressed;

        /// <summary>A stone was right-clicked (describe).</summary>
        public event Action<int> Described;

        /// <summary>The pointer is over a stone (its action id), or left it (-1).</summary>
        public event Action<int> Hovered;

        public void Show(VisualElement root, IReadOnlyList<int> quarrelsOfKind) {
            Hide();
            _overlay = new VisualElement { name = OverlayName, pickingMode = PickingMode.Position };
            _overlay.AddToClassList(OverlayName);
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("ec-shoot__row");
            _overlay.Add(row);
            for (var kind = 0; kind < CombatMenuSlots.ActionIdByQuarrelKind.Length; kind++) {
                int count = quarrelsOfKind != null && kind < quarrelsOfKind.Count ? quarrelsOfKind[kind] : 0;
                if (count > 0) {
                    row.Add(Button(CombatMenuSlots.ActionIdByQuarrelKind[kind], count.ToString()));
                }
            }
            row.Add(Button(CombatMenuSlots.DistinctEnableGateActionId, null));
            root.Add(_overlay);
        }

        public void Hide() {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
        }

        /// <summary>What a click on the stone for <paramref name="actionId"/> does.</summary>
        internal void Press(int actionId) {
            if (_overlay == null) return;
            Mark(actionId);
            Pressed?.Invoke(actionId);
        }

        /// <summary>A quarrel kind was picked (by a click or a key): highlight it and stop being modal.</summary>
        public void Mark(int actionId) {
            if (_overlay == null || CombatMenuSlots.QuarrelKindFor(actionId) < 0) return;
            foreach (VisualElement b in _overlay.Query(className: ButtonClass).ToList()) {
                b.EnableInClassList(ChosenClass, (int)b.userData == actionId);
            }
            _overlay.pickingMode = PickingMode.Ignore;   // the target click goes to the field now
        }

        /// <summary>Resolve the stones' images again (the SHOOT.DAT layout builds after the first open).</summary>
        public void RefreshIcons() {
            if (_overlay == null) return;
            foreach (VisualElement b in _overlay.Query(className: ButtonClass).ToList()) {
                string address = _icon?.Invoke((int)b.userData);
                var disc = b.Q<ArchiveImage>();
                if (address != null && disc.address != address) disc.address = address;
            }
        }

        public bool Shown => _overlay != null;

        private VisualElement Button(int actionId, string count) {
            var button = new VisualElement { pickingMode = PickingMode.Position, userData = actionId };
            button.AddToClassList(ButtonClass);
            var disc = new ArchiveImage { sizeToImage = false, pickingMode = PickingMode.Ignore };
            disc.AddToClassList("ec-shoot__disc");
            string address = _icon?.Invoke(actionId);
            if (address != null) disc.address = address;
            button.Add(disc);
            if (count != null) {
                var label = new Label(count) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(CountClass);
                button.Add(label);
            }
            button.RegisterCallback<ClickEvent>(_ => Press(actionId));
            button.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) Described?.Invoke(actionId); });
            button.RegisterCallback<PointerEnterEvent>(_ => { button.AddToClassList("ec-shoot__button--hover"); Hovered?.Invoke(actionId); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { button.RemoveFromClassList("ec-shoot__button--hover"); Hovered?.Invoke(-1); });
            return button;
        }
    }
}
