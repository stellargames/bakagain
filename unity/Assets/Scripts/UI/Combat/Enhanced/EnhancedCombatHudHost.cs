namespace BakAgain.UI.Combat.Enhanced {
    using System;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Decides, once a frame, whether the Enhanced combat HUD or the Classic combat menu is up, and
    /// shows the chosen one (TASK-972). The choice is taken at a turn's start and kept for the turn:
    /// turning Enhanced combat off mid-fight reverts at the next turn without losing this one.
    /// </summary>
    public sealed class EnhancedCombatHudHost {
        private readonly VisualElement _root;
        private readonly EnhancedCombatLayout _layout;
        private readonly Func<bool> _phone;
        private readonly Func<int, string> _stoneImage;
        private object _turn;
        private bool _latched;
        private bool _active;
        private VisualElement _hiddenMenu;
        private float _poll;

        /// <param name="root">The document root the HUD is laid over.</param>
        /// <param name="phone">Phone docks when true, the desktop band otherwise (read once, when the HUD is built).</param>
        public EnhancedCombatHudHost(VisualElement root, EnhancedCombatLayout layout, Func<bool> phone = null,
            Func<int, string> stoneImage = null) {
            _root = root;
            _layout = layout;
            _phone = phone ?? (() => Application.isMobilePlatform);
            _stoneImage = stoneImage;
        }

        /// <summary>The HUD, once built (the first Enhanced fight turn).</summary>
        public ICombatHud Hud { get; private set; }

        /// <summary>Raised once when the HUD is built, so the owner can subscribe to its events.</summary>
        public event Action<ICombatHud> Built;

        /// <summary>Whether this turn is played with the Enhanced HUD.</summary>
        public bool Active => _active;

        /// <summary>
        /// The latch alone: whether this turn is Enhanced. <paramref name="turn"/> identifies the turn
        /// (who acts, which round); the option is read again only when it changes, or when the fight
        /// starts. Null <paramref name="turn"/> follows the option directly.
        /// </summary>
        public bool Decide(bool optionOn, bool inFight, object turn) {
            if (!inFight) {
                _latched = false;
                _turn = null;
                return _active = false;
            }
            if (turn == null || !_latched || !Equals(turn, _turn)) {
                _active = optionOn;
                _latched = turn != null;
                _turn = turn;
            }
            return _active;
        }

        /// <summary>
        /// Once a frame. Shows the HUD with the model (polled at 4 Hz, and at once on a new turn) and
        /// hides the Classic combat menu's document while Enhanced; reverts both otherwise.
        /// </summary>
        /// <param name="classicMenu">The Classic combat menu's document root; null when there is none.</param>
        /// <param name="shown">False hides the HUD without reverting (a screen is pushed over the fight).</param>
        public void Sync(bool optionOn, bool inFight, object turn, Func<CombatHudModel> model,
            VisualElement classicMenu, bool shown = true) {
            object before = _turn;
            bool wasActive = _active;
            bool active = Decide(optionOn, inFight, turn);
            if (!active) {
                Hud?.Hide();
                RestoreMenu(classicMenu);
                return;
            }
            if (Hud == null) {
                Hud = _phone() ? new PhoneCombatHud(_root, _layout, _stoneImage) : new DesktopCombatHud(_root, _layout, _stoneImage);
                Built?.Invoke(Hud);
            }
            HideMenu(classicMenu);
            if (!shown) {
                Hud.Hide();
                return;
            }
            _poll -= Time.unscaledDeltaTime;
            if (_poll > 0f && Hud.Shown && wasActive && Equals(before, _turn)) {
                return;
            }
            // ponytail: 4 Hz poll; the controller (Task 9) shows on every change instead.
            _poll = 0.25f;
            Hud.Show(model?.Invoke());
        }

        private void HideMenu(VisualElement menu) {
            if (menu != _hiddenMenu) {
                RestoreMenu(_hiddenMenu);
            }
            // Every frame: the menu's document builds a new root each time it is enabled.
            if (menu != null && menu.style.display != DisplayStyle.None) {
                menu.style.display = DisplayStyle.None;
            }
            _hiddenMenu = menu;
        }

        private void RestoreMenu(VisualElement menu) {
            menu ??= _hiddenMenu;
            if (menu != null && menu == _hiddenMenu) {
                menu.style.display = StyleKeyword.Null;
                _hiddenMenu = null;
            }
        }
    }
}
