namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections.Generic;
    using BakAgain.UI;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;

    /// <summary>
    /// Esc on the options menu: MenuLayerHost sends every REQ screen's Esc as action 1 (the original's
    /// scancode), and the options page's own Cancel is 18.
    /// </summary>
    /// <remarks>
    /// MAINMENU.C:283-287: <c>case 0x01: if (reentering != 0) { result = 0; bRunning = 0; }</c> — in game,
    /// Esc leaves the menu exactly as Cancel (0x12) does; on the title menu it does nothing. Android's
    /// Back arrives as Esc, so without this Back could not close the in-game menu at all (TASK-1041).
    /// </remarks>
    public class MenuEscapeTests {
        [Test]
        public void EscInGame_ReturnsToTheGame() {
            var navigator = new CountingNavigator();
            Handler(MenuMode.InGame, navigator).Primary(1);
            Assert.AreEqual(1, navigator.Pops, "Esc on the in-game options menu pops it, as Cancel does");
        }

        [Test]
        public void EscOnTheTitleMenu_DoesNothing() {
            var navigator = new CountingNavigator();
            Handler(MenuMode.Title, navigator).Primary(1);
            Assert.AreEqual(0, navigator.Pops, "the title menu has no game to return to");
        }

        private static MenuActionHandler Handler(MenuMode mode, IScreenNavigator navigator) =>
            new MenuActionHandler(mode, dialogManager: null, preferencesMenu: null, loadGameMenu: null,
                saveGameMenu: null, contentsMenu: null, flow: null, navigator, NullLogger.Instance);

        private sealed class CountingNavigator : IScreenNavigator {
            private readonly List<IScreen> _screens = new();
            public int Pops;

            public IScreen Current => _screens.Count == 0 ? null : _screens[_screens.Count - 1];
            public UniTask ResetTo(IScreen root) => UniTask.CompletedTask;
            public UniTask Push(IScreen screen) {
                _screens.Add(screen);
                return UniTask.CompletedTask;
            }
            public UniTask PushAndWaitAsync(IScreen screen) => Push(screen);
            public UniTask Pop() {
                Pops++;
                return UniTask.CompletedTask;
            }
            public UniTask Replace(IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }
    }
}
