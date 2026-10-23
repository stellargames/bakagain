namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Inventory;
    using BakAgain.UI.Navigation;
    using BakAgain.UI.Puzzle;
    using BakAgain.UI.Rest;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <b>No click on a raised screen reaches a HUD beneath it.</b> Every screen the game raises over
    /// the travel HUD, the combat HUD or a location is swept with a grid of picks across the whole
    /// window, in faithful and in Enhanced, and every pick must land on that screen's own document.
    /// </summary>
    /// <remarks>
    /// <para>Three bugs of this one shape in a day: the Enhanced cast screen (TASK-954), the travel HUD
    /// under the combat HUD (TASK-957) and the Enhanced inventory (TASK-960, a click on its empty lower
    /// right pressed the combat HUD's Defend and passed Owyn's turn). The combat HUD (tier -1) and a
    /// location (tier -3) are not on the navigator's stack, so a pushed screen does not hide them; and
    /// the REQ stage is non-picking (TASK-267), so a screen only covered what its own entries covered.
    /// The original runs each screen's own menu loop and never hit-tests the HUD's widgets while it is
    /// up (COMBAT.C:1736 polls g_combat_menu only between screens).</para>
    /// <para>A pick that hits NOTHING is a leak too: in the game that point is the HUD or the world.
    /// The HUD bases themselves are the documents clicks must pass through, and are listed with why.
    /// Raised the way the game raises them: a navigator screen through its own ShowAsync, an overlay
    /// panel (camp, inn, puzzle, locator) by activation, which is all their Open does.</para>
    /// </remarks>
    public class RaisedScreensCoverTheHudTests {
        /// <summary>Documents that are not raised over a HUD, or ARE the HUD, and why.</summary>
        private static readonly Dictionary<string, string> NotARaisedScreen = new() {
            ["InGameScreen"] = "the travel HUD: the base every screen is raised over; the world is clicked through it",
            ["CombatMenu"] = "the combat HUD: an overlay on the travel HUD that must let arena clicks through (TASK-267)",
            ["ShootMenu"] = "the quarrel picker: the combat HUD's own panel, arena clicks pass through it",
            ["LocationScreen"] = "a location: the backdrop raised screens open over (tier -3)",
            ["DialogManager"] = "dialogs carry their own full-window modal scrim (DialogManager.EnsureModalScrim); "
                + "a dialog that waits for nothing is click-through by design (the inventory's More Info)",
            ["CursorManager"] = "the software cursor: always on top, click-through by design",
            ["CreditsView"] = "only in the attract loop, before any HUD exists (GameFlow.Boot)",
        };

        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() => _stash.Restore();

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EveryRaisedScreenTakesEveryClickInTheWindow([Values(false, true)] bool enhanced) =>
            UniTask.ToCoroutine(async () => {
                GameOptions.Enhanced = enhanced;
                var scope = UnityEngine.Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
                if (scope == null) {
                    Assert.Ignore("no LifetimeScope: DI has not built the screens");
                }
                // RegisterComponentInNewPrefab instantiates on first resolve: resolve every screen
                // field first (ScreenReachabilityTests' way), so the sweep covers the container.
                var screens = new List<MonoBehaviour>();
                foreach (System.Reflection.FieldInfo field in scope.GetType().GetFields(
                             System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)) {
                    if (!typeof(MonoBehaviour).IsAssignableFrom(field.FieldType)) continue;
                    try {
                        if (scope.Container.Resolve(field.FieldType) is MonoBehaviour mb
                            && mb.GetComponent<UIDocument>() != null && !screens.Contains(mb)) {
                            screens.Add(mb);
                        }
                    } catch (Exception) {
                        // not a registration
                    }
                }

                var leaks = new List<string>();
                var checkedNames = new List<string>();
                var skipped = new List<string>();
                foreach (MonoBehaviour screen in screens) {
                    string name = screen.GetType().Name;
                    if (NotARaisedScreen.ContainsKey(name)) continue;
                    GameObject go = screen.gameObject;
                    bool wasActive = go.activeSelf;
                    try {
                        if (!await RaiseAsync(screen)) {
                            skipped.Add($"{name} (did not come up)");
                            continue;
                        }
                        string leak = Sweep(screen);
                        checkedNames.Add(name);
                        if (leak != null) leaks.Add($"{name}: {leak}");
                    } catch (Exception e) {
                        skipped.Add($"{name} threw {e.GetType().Name}: {e.Message}");
                    } finally {
                        await LowerAsync(screen, wasActive);
                    }
                }

                Debug.Log($"[cover] enhanced={enhanced} checked {checkedNames.Count}: {string.Join(", ", checkedNames)}; " +
                    $"not raised: {(skipped.Count == 0 ? "none" : string.Join("; ", skipped))}");
                Assert.IsEmpty(leaks, $"enhanced={enhanced}: clicks that miss the raised screen:\n  " + string.Join("\n  ", leaks));
                Assert.IsEmpty(skipped, "every raised screen must come up to be checked");
                Assert.IsNotEmpty(checkedNames, "no screen was examined");
            });

        // generated/REQ/COMBAT.json: the combat HUD's buttons (panel at 0,0), name -> (x, y, w, h).
        private static readonly (string Name, int X, int Y, int W, int H)[] Combat = {
            ("imagebutton_46", 1000, 786, 160, 156), ("imagebutton_33", 1185, 786, 160, 156),
            ("imagebutton_32", 1365, 786, 160, 156), ("imagebutton_22", 75, 822, 250, 270),
            ("imagebutton_30", 1365, 990, 160, 156), ("imagebutton_47", 1000, 990, 160, 156),
            ("imagebutton_19", 1185, 990, 160, 156),
        };

        /// <summary>
        /// TASK-960 as reported: Owyn's pack open over a fight in Enhanced, and a click at canonical
        /// (1450,930), the pack's empty lower right, pressed the combat HUD's Defend (id 32).
        /// </summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheEnhancedPackOverAFightTakesTheClicksMeantForTheCombatHud() => UniTask.ToCoroutine(async () => {
            GameOptions.Enhanced = true;
            var scope = UnityEngine.Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: DI has not built the screens");
            var inventory = (InventoryMenu)scope.Container.Resolve(typeof(InventoryMenu));
            UIDocument packDocument = inventory.GetComponent<UIDocument>();

            // The combat HUD stand-in: COMBAT.json's buttons at tier -1 on the shared panel.
            var hud = new GameObject("~CombatHud");
            UIDocument hudDocument = hud.AddComponent<UIDocument>();
            hudDocument.panelSettings = packDocument.panelSettings;
            hudDocument.sortingOrder = -1;
            VisualElement hudStage = BakAgain.UI.CanonicalStage.GetOrCreate(hudDocument.rootVisualElement,
                new GameData.Resources.Layout.DesignFrame {
                    Width = BakAgain.Graphics.Canonical.Width, Height = BakAgain.Graphics.Canonical.Height,
                    Fit = GameData.Resources.Layout.LayoutFit.Contain,
                });
            foreach ((string name, int x, int y, int w, int h) in Combat) {
                hudStage.Add(new VisualElement {
                    name = name, pickingMode = PickingMode.Position,
                    style = { position = Position.Absolute, left = x, top = y, width = w, height = h },
                });
            }
            bool wasActive = inventory.gameObject.activeSelf;
            try {
                await UniTask.Yield();
                IPanel panel = hudDocument.rootVisualElement.panel;
                Vector2 At(float x, float y) {
                    Rect box = hudStage.worldBound;
                    float scale = box.width / BakAgain.Graphics.Canonical.Width;
                    return new Vector2(box.x + x * scale, box.y + y * scale);
                }
                // Control: with nothing raised, the reported point is the HUD's Defend.
                Assert.AreEqual("imagebutton_32", panel.Pick(At(1450, 930))?.name, "the HUD's Defend, before the pack");

                Assert.IsTrue(await RaiseAsync(inventory), "the pack came up");
                VisualElement root = packDocument.rootVisualElement;
                for (int frame = 0; frame < 120 && root.Q("enhanced-inv") == null; frame++) await UniTask.Yield();
                Assert.NotNull(root.Q("enhanced-inv"), "the Enhanced layout is applied, as in the report");

                VisualElement picked = panel.Pick(At(1450, 930));
                Assert.IsTrue(Within(root, picked), $"the reported click is the pack's (picked {picked?.name ?? "nothing"})");
                foreach ((string name, int x, int y, int w, int h) in Combat) {
                    picked = panel.Pick(At(x + w / 2f, y + h / 2f));
                    Assert.IsTrue(Within(root, picked), $"a click at the HUD's {name} is the pack's (picked {picked?.name ?? "nothing"})");
                }
                // The pack's own controls still take their clicks: Exit and Use, moved into the bar.
                foreach (string own in new[] { "imagebutton_1", "imagebutton_22" }) {
                    VisualElement e = root.Q(own);
                    Assert.NotNull(e, own);
                    picked = panel.Pick(e.worldBound.center);
                    Assert.IsTrue(picked != null && (picked == e || e.Contains(picked)), $"{own} takes its own click (picked {picked?.name})");
                }
            } finally {
                await LowerAsync(inventory, wasActive);
                UnityEngine.Object.DestroyImmediate(hud);
            }
        });

        /// <summary>Raises a screen through the call the game uses: the navigator's ShowAsync for a
        /// navigator screen, the overlay's own Open/Run for the rest.</summary>
        private async UniTask<bool> RaiseAsync(MonoBehaviour screen) {
            switch (screen) {
                case InventoryMenu inventory:
                    SeatAParty(inventory);
                    if (!inventory.SetMember(0)) return false;
                    break;
                case InnScreen inn:
                    inn.Open(wakeHour: 6);
                    break;
                case PuzzleScreen puzzle:
                    puzzle.Open(null);
                    break;
                case LocatorMapScreen locator:
                    locator.RunAsync(GameData.Resources.Spells.FieldSpells.LocatorTarget.Valuables).Forget();
                    break;
            }
            if (screen is IScreen navigated) {
                UniTask show = navigated.ShowAsync(fade: false);
                int finished = await UniTask.WhenAny(show, UniTask.Delay(5000, ignoreTimeScale: true));
                if (finished != 0) return false;
            } else if (!screen.gameObject.activeSelf) {
                screen.gameObject.SetActive(true); // CampMenu.Open is exactly this
            }
            UserInterfaceLoader loader = screen.GetComponent<UserInterfaceLoader>();
            for (int frame = 0; frame < 120 && loader != null && !loader.IsBuilt; frame++) {
                await UniTask.Yield();
            }
            if (loader != null && !loader.IsBuilt) return false;
            for (int i = 0; i < 3; i++) await UniTask.Yield();
            return screen.gameObject.activeInHierarchy;
        }

        private async UniTask LowerAsync(MonoBehaviour screen, bool wasActive) {
            if (screen == null) return;
            if (screen is LocatorMapScreen) {
                typeof(LocatorMapScreen).GetMethod("Close", Any).Invoke(screen, null);
                await UniTask.Yield();
            }
            if (screen is IScreen navigated && !wasActive) {
                await navigated.HideAsync(fade: false);
            }
            screen.gameObject.SetActive(wasActive);
            if (screen is InventoryMenu && _realSession != null) {
                SessionField.SetValue(screen, _realSession);
                _realSession = null;
            }
            await UniTask.Yield();
        }

        private const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        private static readonly System.Reflection.FieldInfo SessionField = typeof(InventoryMenu).GetField("_gameSession", Any);
        private GameSession _realSession;

        /// <summary>The pack needs a member to show; the container's own session has none in a test
        /// run, so the screen gets a party of its own for the sweep and its session back after.</summary>
        private void SeatAParty(InventoryMenu inventory) {
            _realSession = (GameSession)SessionField.GetValue(inventory);
            var session = new GameSession();
            session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            for (int id = 0; id < 3; id++) {
                var stats = new GameData.Resources.Character.ActorStat[16];
                for (int i = 0; i < stats.Length; i++) stats[i] = new GameData.Resources.Character.ActorStat { Base = 30, Max = 40 };
                session.SetActorStatsForTest(id, stats);
                session.SetActorInventoryForTest(id, new GameData.Resources.Inventory.RuntimeContainer {
                    Capacity = 4, ContainerType = GameData.Resources.Data.SaveGameContainerType.Inventory,
                });
            }
            SessionField.SetValue(inventory, session);
        }

        /// <summary>Null when every pick in a 32x24 grid over the window lands in the screen's document;
        /// otherwise how many missed and where (as a fraction of the window), and what they hit.</summary>
        private static string Sweep(MonoBehaviour screen) {
            VisualElement root = screen.GetComponent<UIDocument>().rootVisualElement;
            IPanel panel = root.panel;
            Rect window = panel.visualTree.worldBound;
            const int cols = 32, rows = 24;
            var misses = new List<string>();
            int count = 0;
            for (int r = 0; r < rows; r++) {
                for (int c = 0; c < cols; c++) {
                    var at = new Vector2(window.x + (c + 0.5f) * window.width / cols,
                        window.y + (r + 0.5f) * window.height / rows);
                    VisualElement picked = panel.Pick(at);
                    if (Within(root, picked)) continue;
                    count++;
                    if (misses.Count < 6) {
                        misses.Add($"({(c + 0.5f) / cols:0.00},{(r + 0.5f) / rows:0.00})->{picked?.name ?? "nothing"}");
                    }
                }
            }
            return count == 0 ? null : $"{count}/{cols * rows} picks missed, e.g. {string.Join(" ", misses)}";
        }

        private static bool Within(VisualElement root, VisualElement e) {
            for (; e != null; e = e.parent) {
                if (ReferenceEquals(e, root)) return true;
            }
            return false;
        }
    }
}
