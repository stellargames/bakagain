namespace BakAgain.UI.Character {
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.UI.Layout;
    using GameData.Resources.Layout;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Dialog;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The temple's healing service — <c>charscreen_temple_heal_menu</c> @0x5877e. Cure player,
    /// Next player, Done, over the afflicted member's character sheet.
    /// </summary>
    /// <remarks>
    /// <b>It is a character sheet with three buttons on it.</b> The loop redraws
    /// <see cref="CharacterSheetView"/> for the shown member on every pass rather than owning a
    /// display of its own, which is why it borrows INVENTOR.PAL and why the portrait carries the
    /// character's own help text. The sheet is the compact form; this screen is what that form
    /// exists for.
    ///
    /// <para><b>Healing a party is one pass that closes itself.</b> Curing someone advances to the
    /// next member who needs something, Next skips anyone who needs nothing, and running off the end
    /// leaves — so in the ordinary case the player never presses Next or Done at all. See
    /// <see cref="TempleHealMenu.NextNeedy"/>.</para>
    ///
    /// <para>The rules — what a cure costs, what it does, where next goes — are all
    /// <see cref="TempleHealMenu"/>'s. This class is the screen they drive.</para>
    /// </remarks>
    public class TempleHealScreen : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        /// <summary>The palette the original loads for this screen (0x58859) — not one of its own.</summary>
        private const string Palette = "INVENTOR.PAL";

        private ILogger _logger;
        private IResourceCache _resources;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private IDialogManager _dialogs;
        private Core.GameSession _session;
        private Core.Services.GameClock _clock;
        private UserInterfaceLoader _ui;
        private readonly CharacterSheetView _sheet = new();
        private GameData.Resources.Palette.PaletteResource _palette;
        private GameTextBlock _quote;

        /// <summary>Slot in the ACTIVE party being shown — not a character index.</summary>
        private int _slot;

        private int _priceMultiplier = DefaultMultiplier;
        private int _mode;

        [VContainer.Inject]
        public void Construct(IResourceCache resources,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IDialogManager dialogs,
            Core.GameSession session, Core.Services.GameClock clock) {
            _resources = resources;
            _navigator = navigator;
            _dialogs = dialogs;
            _session = session;
            _clock = clock;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<TempleHealScreen>();
        }

        private void Awake() => _ui = GetComponent<UserInterfaceLoader>();

        /// <summary>
        /// Ask the temple for healing, and run the screen if it agrees to see you.
        /// </summary>
        /// <param name="priceMultiplier">
        /// The temple's own price percentage — the container's <c>ShopkeeperSkill</c> byte, which
        /// for a temple is what its services cost. It is also what the needs-healing test is asked
        /// with (one function, two jobs), so it decides who is offered a cure as well as for how
        /// much.
        /// </param>
        /// <param name="mode">
        /// The temple's <c>ShopType</c>. Its only effect here is whether plain wounds are treated —
        /// and billed for. See <see cref="TempleHealEntry.ModeThatTreatsWounds"/>.
        /// </param>
        /// <remarks>
        /// <b>The refusals are part of the service, not a guard in front of it.</b> A temple with
        /// nothing to treat says so, and says it two different ways: nothing wrong at all, or wounds
        /// that are not a temple's business. Only the second tells the player where to go instead,
        /// so a single "nothing to do" would throw that line away.
        /// </remarks>
        public async UniTask RunAsync(int priceMultiplier, int mode) {
            SetService(priceMultiplier, mode);
            TempleHealOpening opening = TempleHealEntry.Decide(AnyoneAfflicted(), AnyoneWounded(), mode);
            if (opening != TempleHealOpening.Screen) {
                await _dialogs.ShowById(TempleHealEntry.DialogFor(opening));

                return;
            }

            _closed = new UniTaskCompletionSource();
            await _navigator.Push(this);
            await _closed.Task;
            await _navigator.Pop();
        }

        /// <summary>Whether anyone in the party is carrying something the temple would charge for.</summary>
        private bool AnyoneAfflicted() {
            for (var slot = 0; slot < ActiveCount; slot++) {
                if (NeedsHealing(slot)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether anyone is below their full health-and-stamina pool.</summary>
        private bool AnyoneWounded() {
            for (var slot = 0; slot < ActiveCount; slot++) {
                if (HealthDeficitOf(CharacterIndex(slot)) > 0) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Completes when the screen is finished with, so <see cref="RunAsync"/> can pop it.</summary>
        private UniTaskCompletionSource _closed;

        /// <summary>
        /// Set the service parameters and pick the member to open on, without showing anything.
        /// </summary>
        /// <remarks>
        /// Typed setter before the push, rather than arguments on the navigator: the screen is a
        /// singleton prefab and the navigator knows nothing of temples.
        /// </remarks>
        public void SetService(int priceMultiplier, int mode) {
            _priceMultiplier = priceMultiplier;
            _mode = mode;
            _slot = FirstNeedySlot();
        }

        /// <summary>
        /// Which member the screen opens on: the first who needs something, or the first of the
        /// party when nobody does (the merely-wounded case a generous temple still sees).
        /// </summary>
        private int FirstNeedySlot() {
            int slot = TempleHealMenu.NextNeedy(-1, ActiveCount, NeedsHealing);

            return slot < ActiveCount ? slot : 0;
        }

        private void OnEnable() {
            if (_ui == null) {
                return;
            }
            _ui.Built += OnBuilt;
            // The loader's build is cached and async, so it can finish before this subscribes and
            // the event never arrives — the same reconciliation CastScreen and MenuLayerHost do.
            if (_ui.IsBuilt) {
                OnBuilt(_ui.CurrentNavWidgets);
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnBuilt;
            }
            _sheet.Clear();
            _sheet.ForgetMarks();
            (_quote?.parent ?? _quote)?.RemoveFromHierarchy();
            _quote = null;
        }

        private void OnBuilt(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) =>
            RedrawAsync().Forget();

        // ---- the redraw -----------------------------------------------------------------------

        /// <summary>
        /// Draw the shown member's sheet and the priest's quote for them.
        /// </summary>
        /// <remarks>
        /// Everything the screen shows is rebuilt from the session on every pass, exactly as the
        /// original's loop does — which is what makes a cure visible without the screen tracking
        /// what changed.
        /// </remarks>
        private async UniTask RedrawAsync() {
            VisualElement stage = Stage();
            if (stage == null || _session == null) {
                return;
            }
            _palette ??= await _resources.GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(Palette);

            await _sheet.RenderAsync(stage, CharacterIndex(_slot), _session, _resources, _palette,
                _logger);
            await ShowQuoteAsync(stage);
        }

        /// <summary>
        /// The priest's price, with the bill in the dialog's <c>@0</c> slot.
        /// </summary>
        /// <remarks>
        /// <b>Drawn onto this screen, not shown as a dialog.</b> The original calls
        /// <c>RenderDialogText</c> directly rather than <c>dialog_Show</c>, so the quote is text
        /// over the sheet with no panel of its own — showing it through the dialog overlay would put
        /// an opaque parchment box over the ratings the player is being charged for, and take input
        /// besides.
        /// </remarks>
        private async UniTask ShowQuoteAsync(VisualElement stage) {
            (_quote?.parent ?? _quote)?.RemoveFromHierarchy();
            _quote = null;
            if (_dialogs == null) {
                return;
            }
            // *** AND THE SHOWN MEMBER IS THE DIALOG'S ACTOR. *** CHARSCRN.C:471 sets
            // `nEvtArgActor0 = activeParty[slot]` immediately before drawing the quote, so the
            // sentence's bare `@` names whoever the sheet is showing — and Next changes it.
            // Measured at the Chapel of Ishap 2026-09-13: the original opened on Locklear and said
            // "Locklear was studied"; we said "Gorath", the byte the save was written with. TASK-493.
            _session.EventActor = CharacterIndex(_slot);
            // The bill reaches the text through the same global the original writes it to
            // (global_30014 at 0x5891d), which is where the @0 slot reads it from.
            _session.SetGlobalValue(BillTextVariableKey, (int)BillFor(_slot));
            DialogPlay play = await _dialogs.ResolveById(TempleHealEntry.PriceQuoteDialog);
            if (play?.Entry == null) {
                return;
            }

            LayoutHint area = DialogManager.ResolveArea(play.Entry, fallback: null);
            if (area == null) {
                _logger.LogWarning("TempleHealScreen: quote {Id} states no area; it is not drawn.",
                    TempleHealEntry.PriceQuoteDialog);

                return;
            }

            // *** THE PRICE IS THE CALLER'S SLOT TO FILL, NOT THE ENTRY'S. *** The quote's own action
            // writes the money into a slot its sentence never reads; what @0 reads is written by
            // whoever computed the bill — SetTextVariable_0(slot 0, kind 19) at 0x5893e, right
            // before the render. Without it the priest quotes a party member's NAME as the price,
            // which reads as a sentence and is why it would survive a screenshot.
            DialogSlotPopulator.Assign(play.Slots, QuoteAmountSlot,
                DialogSlotPopulator.SourceQuotedMoney, aux: 0, play.Context);

            // *** NO BOX OF ITS OWN. *** CHARSCRN.C:484-485 renders the record twice, and both are
            // text: the pen-1 shadow one pixel down-left, then the pen-10 face. The parchment behind
            // the screen is the sheet's own full-frame blit (CharacterSheetLayout.Parchment).

            // *** THE CHARACTER SCREEN'S STYLE, CENTRED IN THE RECT. *** The loop sets
            // g_dialog_in_char_screen (CHARSCRN.C:463), so dialog_resolve_style gives this record style 6,
            // and dialog_render_text_with_tokens insets the resize rect by that row's pads and draws
            // with its 0x10 vertical-centre bit (TEXTWRAP.C:127-131). Laid top-left on the bare rect, the
            // sentence sat on the ratings box's bottom edge, 81 canonical px above the original's first
            // line (TASK-544). The same arrangement as DialogPanelBuilder.AddDialogBody, in a box of its own.
            DialogStyleTable styles = await _resources.GetOrLoadAsync<DialogStyleTable>(DialogStyleTable.ResourceId);
            DialogStyle style = styles?.Get(DialogTypeResolver.ResolveEffectiveStyleId(CharacterScreen, play.Entry));
            var box = new VisualElement {
                name = "BakTempleHealQuoteBox",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            LayoutApplier.Apply(box, area);
            var quote = new GameTextBlock {
                name = "BakTempleHealQuote",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = style?.TextPadLeft ?? 0f,
                    right = style?.TextPadRight ?? 0f,
                    top = 0,
                    bottom = 0,
                    justifyContent = Justify.Center,
                    unityTextAlign = TextAnchor.MiddleLeft,
                },
            };
            box.Add(quote);
            stage.Add(box);
            quote.SetPageBox(style?.TextPadTop ?? 0f, style?.TextPadBottom ?? 0f);
            quote.SetContent(
                DialogTextFormatter.Prepare(
                    TextVariableResolver.Substitute(play.Entry.Text, play.Slots,
                        play.Context?.NameOf(play.Context.CurrentActorId)),
                    centered: false),
                _palette?.Colors.ToUnity(), QuotePen, TextAnchor.MiddleLeft,
                GameFontText.DialogLineGapVgaRows);
            _quote = quote;
        }

        // ---- the buttons ----------------------------------------------------------------------

        /// <inheritdoc/>
        public void PrimaryAction(int menuEntryActionId) {
            switch (menuEntryActionId) {
                case TempleHealEntry.CureActionId:
                    CureAsync().Forget();

                    return;
                case TempleHealEntry.NextActionId:
                    Advance();

                    return;
                case TempleHealEntry.DoneActionId:
                    Leave();

                    return;
            }

            int slot = TempleHealMenu.PartySlotForAction(menuEntryActionId);
            if (slot >= 0 && slot < ActiveCount && !InCombat) {
                _slot = slot;
                RedrawAsync().Forget();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Right-clicking the person tells you about the person; everything else explains the
        /// buttons. The portrait carries Next's own action id, so both answer the same way without
        /// this having to know which one was clicked.
        /// </remarks>
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            if (_dialogs == null) {
                return;
            }
            int help = TempleHealMenu.HelpDialogFor(menuEntryActionId);
            if (help == TempleHealMenu.CharacterDescriptionDialog) {
                // The description reads the member out of the same global the original sets before
                // showing it (global_30000 at 0x589eb).
                _session?.SetGlobalValue(DescribedCharacterKey, CharacterIndex(_slot));
            }

            await _dialogs.ShowById(help).AsTask();
        }

        /// <summary>
        /// Pay for and perform the cure.
        /// </summary>
        /// <remarks>
        /// <b>It ends by advancing.</b> The original's cure arm sets the pending action to Next and
        /// falls into it, which is what makes healing a party a single pass — and what closes the
        /// screen once the last of them is done.
        /// </remarks>
        private async UniTask CureAsync() {
            long bill = BillFor(_slot);
            if (_session.PartyGold < bill) {
                await _dialogs.ShowById(TempleHealMenu.CannotAffordDialog);
                await RedrawAsync();

                return;
            }

            _session.PartyGold -= (int)bill;
            await _dialogs.ShowById(TempleHealMenu.CuredDialog);

            int character = CharacterIndex(_slot);
            ActorConditions conditions = _session.ConditionsOf(character);
            for (var condition = 0; condition < TempleHealMenu.ConditionCount; condition++) {
                ConditionEngine.Apply(conditions, (ActorCondition)condition,
                    TempleHealMenu.CureAmountFor(condition, _mode));
            }
            if (TempleHealMenu.RestoresHealth(_mode)) {
                // Saturating rather than a fixed amount: the original passes 0x7FFF, which is "to
                // full" however far down they were.
                CharacterHeal.Apply(_session.StatsOf(character), conditions, HealToFull);
            }
            // The visit is stamped on the game clock, as the original does at the end of the cure.
            _session.SetGlobalValue(LastServiceTimeKey, (int)(_clock?.Ticks ?? 0));

            Advance();
        }

        /// <summary>Move to the next member with something to cure, or leave if there is none.</summary>
        private void Advance() {
            if (InCombat) {
                return; // in combat the screen shows one combatant and Next is refused
            }
            int next = TempleHealMenu.NextNeedy(_slot, ActiveCount, NeedsHealing);
            if (TempleHealMenu.ClosesAfter(next, ActiveCount)) {
                Leave();

                return;
            }
            _slot = next;
            RedrawAsync().Forget();
        }

        /// <summary>
        /// Finish with the screen.
        /// </summary>
        /// <remarks>
        /// It signals rather than popping: <see cref="RunAsync"/> pushed, so <see cref="RunAsync"/>
        /// pops. An unbalanced pop from in here is what strands the screen beneath when the caller
        /// pops as well.
        /// </remarks>
        private void Leave() {
            _dialogs?.ClearDialog();
            _closed?.TrySetResult();
        }

        // ---- what a member costs --------------------------------------------------------------

        /// <summary>The bill for a slot: their afflictions, plus their wounds when the temple treats those.</summary>
        private long BillFor(int slot) {
            int character = CharacterIndex(slot);

            return TempleHealMenu.BillFor(
                TempleHeal.Price(_session.ConditionsOf(character), _priceMultiplier),
                HealthDeficitOf(character), _mode);
        }

        /// <summary>
        /// How far below maximum a member's health-and-stamina sits, in points.
        /// </summary>
        /// <remarks>
        /// The combined pool, not health alone — the original reads
        /// <see cref="ActorAttribute.HealthStaminaCombo"/> against its own maximum, so stamina
        /// counts towards the surcharge exactly as wounds do. The pool itself is
        /// <see cref="StatEngine"/>'s to define: it has no stored slot, and summing the pair here
        /// would be a second opinion on what the combo is.
        /// </remarks>
        private int HealthDeficitOf(int character) {
            // *** THE EFFECTIVE POOL, NOT THE STORED PAIR. *** CHARSCRN.C:476-479 adds
            // `stat_actor_get(actor, 0x10, 1) - stat_actor_get(actor, 0x10, 0)` to the price --
            // mode 1 against mode ZERO -- so a member held below full by a modifier owes for that
            // too. Reading the stored pair under-billed them, and since this bill is also the test
            // at line 119 for "is there anything to cure", a member whose only shortfall was a
            // modifier read as healthy and the priest skipped them entirely.
            return _session.StatsOf(character) == null
                ? 0
                : StatEngine.HealthPoolDeficit(
                    _session.EffectivePool(character), _session.EffectivePoolMax(character));
        }

        /// <summary>Whether a slot has anything the priest would charge for.</summary>
        /// <remarks>
        /// <b>The price IS the test.</b> A zero bill means nothing to cure, which is what Next
        /// skips on — so this must ask with the temple's own multiplier rather than a nominal one.
        /// </remarks>
        private bool NeedsHealing(int slot) => BillFor(slot) != 0;

        private int ActiveCount => _session?.ActivePartyIndices?.Length ?? 0;

        /// <summary>
        /// Whether switching character is refused because a fight is on.
        /// </summary>
        /// <remarks>
        /// <b>Always false for now, because there is no combat to be in.</b> The rule is real —
        /// both the portrait row and Next are gated on it, so in combat the screen shows one
        /// combatant and the only ways out are curing them or Done — but nothing in this project
        /// tracks that state yet (TASK-94). Named here so the gate is a lookup waiting for its
        /// source rather than a rule nobody wrote down.
        /// </remarks>
        private bool InCombat => TempleHealMenu.SelectionIsLockedInCombat && false;

        /// <summary>The party roster position a portrait slot names.</summary>
        private int CharacterIndex(int slot) {
            byte[] active = _session?.ActivePartyIndices;

            return active != null && slot >= 0 && slot < active.Length ? active[slot] : -1;
        }

        private VisualElement Stage() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) {
                return null;
            }
            // *** THE ROOT HAS TO BE OPAQUE, AND THIS SCREEN IS THE ONE THAT LEARNED WHY. ***
            // REQ_HEAL paints a panel, not a page, so everything the layout does not cover is
            // transparent — and a UI Toolkit panel shows whatever document sits below it. From the
            // character sheet that is nothing; opened from a TEMPLE it is the location, which sits
            // at sortingOrder -3 and stayed active underneath. The Chapel of Ishap's backdrop ran
            // straight through the middle of the priest's quote (measured 2026-09-13); the
            // inventory screen has blacked its own root for the same reason since the shop landed.
            root.style.backgroundColor = Color.black;

            return CanonicalStage.GetOrCreate(root, _ui.Frame);
        }

        /// <summary>Where the bill goes so the quote's <c>@0</c> slot can read it.</summary>
        private const int BillTextVariableKey = 30014;

        /// <summary>The slot the quote's sentence reads its price from.</summary>
        private const int QuoteAmountSlot = 0;

        /// <summary>Where the described member goes for the right-click description dialog.</summary>
        private const int DescribedCharacterKey = 30000;

        /// <summary>Where the visit's time is stamped.</summary>
        private const int LastServiceTimeKey = 30006;

        /// <summary>
        /// The dialog state the heal loop runs under — <c>g_dialog_in_char_screen</c> set (CHARSCRN.C:463).
        /// </summary>
        /// <remarks>Either full-screen flag makes the resolver answer 6; this one mirrors the byte flag.</remarks>
        private static readonly DialogContext CharacterScreen = new(false, false, true);

        /// <summary>The pen the quote is drawn in (<c>textColor 0x0A</c> at 0x5896a).</summary>
        private const int QuotePen = 0x0A;

        /// <summary>A heal large enough to be "to full" — the original's saturating 0x7FFF.</summary>
        private const int HealToFull = short.MaxValue;

        /// <summary>A temple with no multiplier of its own charges the plain price.</summary>
        private const int DefaultMultiplier = 100;
    }
}
