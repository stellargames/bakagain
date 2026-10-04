namespace BakAgain.UI.Cheats {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Palette;
    using GameData.Resources.World;
    using UnityEngine;
    using Color = UnityEngine.Color;
    using UnityEngine.UIElements;
    using VContainer;
    using Microsoft.Extensions.Logging;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The shipped game's hidden "-> CHEAT CENTRAL &lt;-" menus (TASK-692) — see <see cref="CheatCentral"/>.
    /// </summary>
    /// <remarks>
    /// One screen for both: the REQ (REQ_KNOC or REQ_CHET) is the prefab's address, and the subclass
    /// says which menu's cases apply. The backdrop is DIALOG.SCX and the captions are drawn over it,
    /// as both originals do; leaving pops back to whatever raised it.
    /// </remarks>
    public abstract class CheatCentralScreen : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        private const string CaptionClass = "cheat-central-caption";

        /// <summary>DIALOG.SCX's palette (PaletteMapping: DIALOG -&gt; OPTIONS.PAL).</summary>
        private const string Palette = "OPTIONS.PAL";

        /// <summary>The captions' pens: invui_draw_text_aligned_shadow(..., 10, 1).</summary>
        private const int CaptionPen = 10;
        private const int CaptionShadowPen = 1;

        protected GameSession Session;
        protected IDialogManager Dialogs;
        protected BakAgain.UI.Navigation.IScreenNavigator Navigator;
        protected BakAgain.UI.Inventory.InventoryMenu Inventory;
        protected BakAgain.UI.Puzzle.PuzzleService Puzzles;
        protected BakAgain.UI.InputCore.ICheatInput Keys;
        private IResourceCache _resources;
        protected ILogger Logger;
        private BakAgain.ResourceManagement.Loaders.UserInterfaceLoader _ui;
        private bool _busy;

        [Inject]
        public void Construct(GameSession session, IDialogManager dialogs,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IResourceCache resources,
            BakAgain.UI.Inventory.InventoryMenu inventory, BakAgain.UI.Puzzle.PuzzleService puzzles,
            BakAgain.UI.InputCore.ICheatInput keys) {
            Puzzles = puzzles;
            Keys = keys;
            Session = session;
            Dialogs = dialogs;
            Navigator = navigator;
            _resources = resources;
            Inventory = inventory;
            Logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger(LogManager.LoggerFactory, GetType());
        }

        private void OnEnable() {
            _ui = GetComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
            if (_ui != null) {
                _ui.Built += OnBuilt;
                if (_ui.IsBuilt) {
                    OnBuilt(_ui.CurrentNavWidgets);
                }
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnBuilt;
            }
        }

        private void OnBuilt(System.Collections.Generic.IReadOnlyList<BakAgain.UI.InputCore.NavWidget> _) =>
            DrawCaptionsAsync().Forget();

        private async UniTask DrawCaptionsAsync() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || _ui == null) {
                return;
            }
            VisualElement stage = CanonicalStage.GetOrCreate(root, _ui.Frame);
            PaletteResource palette = await _resources.GetOrLoadAsync<PaletteResource>(Palette);
            foreach (VisualElement stale in stage.Query<VisualElement>(className: CaptionClass).ToList()) {
                stale.RemoveFromHierarchy();
            }
            AddCaption(stage, CheatCentral.Title, CheatCentral.TitleY, palette);
            AddCaption(stage, CheatCentral.Subtitle, CheatCentral.SubtitleY, palette);
        }

        private static void AddCaption(VisualElement stage, string text, int y, PaletteResource palette) {
            float x = CheatCentral.CaptionCentreX;
            AddLabel(stage, text, x + CheatCentral.CaptionShadowX, y + CheatCentral.CaptionShadowY,
                PaletteColors.ResolvePen(palette, CaptionShadowPen, Color.black));
            AddLabel(stage, text, x, y, PaletteColors.ResolvePen(palette, CaptionPen, Color.white));
        }

        private static void AddLabel(VisualElement stage, string text, float x, float y, Color colour) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute, left = x, top = y, color = colour,
                    translate = new StyleTranslate(new Translate(Length.Percent(-50f), 0f)),
                },
            };
            label.AddToClassList(CaptionClass);
            GameFontText.Apply(label);
            stage.Add(label);
        }

        /// <summary>Which of the menu's effects this action id runs.</summary>
        protected abstract CheatCentral.Effect EffectFor(int actionId);

        /// <summary>The menu's item chest: the fixed object at zone 0 the menu names.</summary>
        protected abstract GameData.Resources.Inventory.RuntimeContainer ItemChest();

        public void PrimaryAction(int actionId) {
            if (_busy) {
                return;
            }
            RunAsync(EffectFor(actionId)).Forget();
        }

        public Awaitable SecondaryAction(int actionId) => default;

        private async UniTaskVoid RunAsync(CheatCentral.Effect effect) {
            _busy = true;
            try {
                await ApplyAsync(effect);
            } finally {
                _busy = false;
            }
        }

        protected async UniTask ApplyAsync(CheatCentral.Effect effect) {
            switch (effect) {
                case CheatCentral.Effect.AddGold:
                    Session.PartyGold += CheatCentral.GoldBonus;
                    break;
                case CheatCentral.Effect.HealParty:
                    Session.HealActiveParty(GameData.Resources.Character.CharacterHeal.FullHealAmount);
                    break;
                case CheatCentral.Effect.LearnAllSpells:
                    foreach (byte member in Session.ActivePartyIndices ?? System.Array.Empty<byte>()) {
                        ushort[] known = Session.KnownSpellsOf(member);
                        for (int i = 0; known != null && i < known.Length && i < 3; i++) {
                            known[i] = CheatCentral.AllSpellsMask;
                        }
                    }
                    break;
                case CheatCentral.Effect.UnlockAllTeleports:
                    for (int i = 0; i < CheatCentral.TownCount; i++) {
                        Session.SetGlobalValue(CheatCentral.TownVisitedBase + i, 1);
                    }
                    break;
                case CheatCentral.Effect.PlayLine:
                    await Dialogs.ShowById(CheatCentral.KnockKnockLine);
                    break;
                case CheatCentral.Effect.OpenItemChest:
                    GameData.Resources.Inventory.RuntimeContainer chest = ItemChest();
                    if (chest == null || Inventory == null) {
                        Logger.LogWarning("CHEAT CENTRAL: no item chest for this menu.");
                        break;
                    }
                    // A spawned fixed object, no world item behind it: the picture falls to the
                    // selector's default, the bag (INVMISC#0) — as the original shows.
                    Inventory.SetContainer(chest, WorldEntityType.Ground);
                    int chapter = Session.Chapter;
                    Session.SetChapter(ChestChapter ?? chapter);
                    try {
                        await Navigator.PushAndWaitAsync(Inventory);
                    } finally {
                        Session.SetChapter(chapter);
                    }
                    break;
                case CheatCentral.Effect.SkipChapter:
                    if (await ConfirmSkipAsync()) {
                        Session.ChapterTransitionPending = 1;
                        await Navigator.Pop();
                    }
                    break;
                case CheatCentral.Effect.Leave:
                    await Navigator.Pop();
                    break;
            }
        }

        /// <summary>The chapter the chest runs as, when it is not the party's own.</summary>
        protected virtual int? ChestChapter => null;

        /// <summary>The knock-knock menu skips at once; the chest asks first (0x249f1f).</summary>
        protected virtual UniTask<bool> ConfirmSkipAsync() => UniTask.FromResult(true);
    }
}
