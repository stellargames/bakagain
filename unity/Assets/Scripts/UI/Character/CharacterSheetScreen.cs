namespace BakAgain.UI.Character {
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement.Loaders;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Spells;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using PaletteResource = GameData.Resources.Palette.PaletteResource;

    /// <summary>
    /// The character screen — <c>charscreen_info_loop</c> @0x58378. A member's full sheet, the
    /// party walked with the portrait, and a rating marked for study with a click.
    /// </summary>
    /// <remarks>
    /// <b>The full sheet, not the compact one.</b> The temple's healer draws the same character in
    /// its short form; this is the form with the lower half, which is what its twelve click areas
    /// are for.
    ///
    /// <para><b>Clicking a rating is the only way to choose what a character studies.</b> The mark
    /// it leaves is the one already drawn at the end of that rating's bar — see
    /// <see cref="SkillEmphasis"/> — so the screen is not a read-out with a decoration on it.</para>
    /// </remarks>
    public class CharacterSheetScreen : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        /// <summary>The palette the original loads for this screen (0x583f4).</summary>
        private const string Palette = CharacterSheetLayout.ScreenPalette;

        /// <summary>The page data behind the Spells button.</summary>
        private const string SpellBookLayout = "INVSPELL.DAT";

        private ILogger _logger;
        private IResourceCache _resources;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private IDialogManager _dialogs;
        private Core.GameSession _session;
        private UserInterfaceLoader _ui;
        private readonly CharacterSheetView _sheet = new();
        private PaletteResource _palette;
        private UniTaskCompletionSource _closed;

        /// <summary>Slot in the ACTIVE party being shown — not a character index.</summary>
        private int _slot;

        [VContainer.Inject]
        public void Construct(IResourceCache resources,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IDialogManager dialogs,
            Core.GameSession session) {
            _resources = resources;
            _navigator = navigator;
            _dialogs = dialogs;
            _session = session;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<CharacterSheetScreen>();
        }

        private void Awake() => _ui = GetComponent<UserInterfaceLoader>();

        /// <summary>Show the sheet for a party slot, and return when the player leaves.</summary>
        public async UniTask RunAsync(int partySlot) {
            if (!SetMember(partySlot)) {
                return;
            }

            _closed = new UniTaskCompletionSource();
            await _navigator.Push(this);
            ShowMember();
            FadeInAsync().Forget();
            await _closed.Task;
            // *** NO FADE HERE. *** charscreen_info_loop fades on the way IN only — the loop ends,
            // the saved palette goes back and the caller redraws. See
            // CharacterSheetLayout.FadesInOnEntryOnly.
            await _navigator.Pop();
        }

        /// <summary>
        /// Brings the sheet up out of black, the way the original does.
        /// </summary>
        /// <remarks>
        /// The original fades the OUTGOING screen to black, draws the sheet under a zeroed palette
        /// and fades that up. Ours is a screen of its own over a hidden one, so fading the sheet's
        /// own root from transparent lands in the same place with none of the palette machinery —
        /// the architecture converts indexed to RGBA at load, so a palette fade is not available and
        /// is not wanted (see memory palette_architecture).
        ///
        /// <para><b>Counted in frames, not seconds.</b>
        /// <see cref="CharacterSheetLayout.FadeFrames"/> is eight presented frames — about a seventh
        /// of a second, and deliberately much snappier than the half-second the full-map screen
        /// uses. Stepping per frame rather than over a wall-clock duration keeps that meaning
        /// whatever the display is doing.</para>
        /// </remarks>
        private async UniTaskVoid FadeInAsync() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) {
                return;
            }
            for (var frame = 0; frame < CharacterSheetLayout.FadeFrames; frame++) {
                root.style.opacity = (frame + 1) / (float)CharacterSheetLayout.FadeFrames;
                await UniTask.NextFrame();
            }
            // Cleared rather than left at 1: an explicit opacity on the root would otherwise sit
            // there forever, and the next show starts by setting it anyway.
            root.style.opacity = StyleKeyword.Null;
        }

        /// <summary>Choose the member without showing anything — the typed pre-push setter.</summary>
        public bool SetMember(int partySlot) {
            if (partySlot < 0 || partySlot >= ActiveCount) {
                return false;
            }
            _slot = partySlot;

            return true;
        }

        private void OnEnable() {
            if (_ui == null) {
                return;
            }
            _ui.Built += OnBuilt;
            // The loader's build is cached and async, so it can finish before this subscribes.
            if (_ui.IsBuilt) {
                OnBuilt(_ui.CurrentNavWidgets);
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnBuilt;
            }
            _bookScrim?.RemoveFromHierarchy();
            _bookScrim = null;
            _bookOpen = false;
            _book.Clear();
            _sheet.Clear();
            _sheet.ForgetMarks();
        }

        /// <summary>
        /// The panel was (re)built: draw the sheet over it.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately does not touch the panel's entries.</b> <c>SetEntryState</c> rebuilds,
        /// and a rebuild raises this — so a Built handler that changes an entry calls itself until
        /// the stack runs out, which kills the Editor outright rather than throwing. Which entries
        /// are visible is decided when the MEMBER changes, which is the only thing that can change
        /// the answer, and the extra draw that the rebuild then triggers is harmless.
        /// </remarks>
        private void OnBuilt(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) =>
            DrawAsync().Forget();

        /// <summary>Draw the shown member's sheet.</summary>
        private async UniTask DrawAsync() {
            VisualElement stage = Stage();
            if (stage == null || _session == null) {
                return;
            }
            _palette ??= await _resources.GetOrLoadAsync<PaletteResource>(Palette);

            await _sheet.RenderAsync(stage, CharacterIndex(), _session, _resources, _palette,
                _logger, fullSheet: true);
            DrawNamePlate(stage);
            // The REQ's widgets have to stay reachable over the sheet — "Spells" shares an edge
            // with the ratings panel and was buried by it, invisible and unclickable.
            _sheet.SendBehindPanelWidgets();
        }

        private const string NamePlateClass = "sheet-name-plate";

        /// <summary>
        /// The member's name, on a plate under the portrait.
        /// </summary>
        /// <remarks>
        /// <b>It is the "Spells" widget drawn a second time.</b> <c>UI_attributes_screen</c> @0x581ee
        /// copies REQ_INFO's element index 2 into a local, swaps its label for the actor's name and
        /// draws it offset — so the plate carries that widget's chrome, and it is sourced from the
        /// widget here for the same reason rather than being given coordinates of its own. See
        /// <see cref="CharacterSheetLayout.NamePlateOffsetX"/> for where the offset comes from.
        ///
        /// <para>Drawn after the sheet and before <c>SendBehindPanelWidgets</c>, so it sits with the
        /// sheet's chrome rather than over the REQ's live entries.</para>
        /// </remarks>
        private void DrawNamePlate(VisualElement stage) {
            foreach (VisualElement stale in stage.Query<VisualElement>(className: NamePlateClass).ToList()) {
                stale.RemoveFromHierarchy();
            }
            string[] names = _session?.PartyActorNames;
            int index = CharacterIndex();
            if (names == null || index < 0 || index >= names.Length || _ui == null
                || !_ui.TryGetElementRect(SpellsActionId, out Rect source)) {
                return;
            }

            var plate = new VisualElement {
                name = "sheet_name_plate",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = source.x + CharacterSheetLayout.NamePlateOffsetX,
                    top = source.y + CharacterSheetLayout.NamePlateOffsetY,
                    width = source.width,
                    height = source.height,
                },
            };
            plate.AddToClassList("text-button");
            plate.AddToClassList(NamePlateClass);
            BakAgain.UI.GameFontText.Caption(plate, names[index]);
            stage.Add(plate);
        }

        /// <summary>
        /// Show the member, and offer the spellbook only to someone who can use it.
        /// </summary>
        /// <remarks>
        /// The original hides that entry per member rather than per screen (0x58457), so walking the
        /// party with the portrait shows and hides the button as it goes. Called on a member change
        /// and nowhere else — see <see cref="OnBuilt"/> for why not from the build.
        /// </remarks>
        private void ShowMember() {
            bool caster = IsCaster(CharacterIndex());
            _ui?.SetEntryState(SpellsActionId, caster, caster);
            DrawAsync().Forget();
        }

        // ---- the buttons ----------------------------------------------------------------------

        /// <inheritdoc/>
        public void PrimaryAction(int menuEntryActionId) {
            // *** ANY INPUT CLOSES THE BOOK. *** charscreen_draw_spell_book_actor waits on
            // dialog_poll_arrow_or_button (CHARSCRN.C:93-95): any key but an arrow, or any button.
            // The sheet's entries are hidden under it and must not act (TASK-754).
            if (_bookOpen) {
                CloseSpellBook();

                return;
            }
            int row = SkillEmphasis.RowForAction(menuEntryActionId);
            if (row >= 0) {
                ToggleEmphasis(row);

                return;
            }

            switch (menuEntryActionId) {
                case ExitActionId:
                    // The book is the screen's other view, so leaving it comes before leaving the
                    // screen — the original's page waits for its own dismissal first.
                    if (_bookOpen) {
                        CloseSpellBook();

                        return;
                    }
                    _closed?.TrySetResult();

                    return;

                case PortraitActionId:
                case PortraitAreaActionId:
                    ShowNextMember();

                    return;

                case SpellsActionId:
                    ShowSpellBookAsync().Forget();

                    return;

                default:
                    return;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Asking about a rating explains what clicking it does; asking about the person describes
        /// the person. Each publishes what its dialog reads — the ROW for the rating help and the
        /// CHARACTER for the description, which is why the two ids are computed differently.
        /// </remarks>
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            if (_dialogs == null) {
                return;
            }
            int row = SkillEmphasis.RowForAction(menuEntryActionId);
            if (row >= 0) {
                _session?.SetGlobalValue(DialogSubjectGlobal, row);
                await _dialogs.ShowById(SkillEmphasis.HelpDialog).AsTask();

                return;
            }

            if (menuEntryActionId == PortraitActionId || menuEntryActionId == PortraitAreaActionId) {
                _session?.SetGlobalValue(DialogSubjectGlobal, CharacterIndex());
                await _dialogs.ShowById(CharacterDescriptionDialog).AsTask();
            }
        }

        /// <summary>
        /// Shows the spellbook page over the sheet, until the player dismisses it.
        /// </summary>
        /// <remarks>
        /// <b>The page is modal and it replaces the sheet's content rather than sitting beside
        /// it.</b> The original redraws the whole screen as the book, waits on a key or a click, and
        /// then redraws the sheet — so the two are alternating views of one screen, not two panels.
        /// </remarks>
        private async UniTask ShowSpellBookAsync() {
            VisualElement stage = Stage();
            if (stage == null || _bookOpen) {
                return;
            }
            SpellBookPage page = await _resources.GetOrLoadAsync<SpellBookPage>(SpellBookLayout);
            ushort[] known = _session?.KnownSpellsOf(CharacterIndex());
            if (page == null || known == null) {
                _logger.LogWarning("CharacterSheetScreen: no spellbook page for this member.");

                return;
            }

            _bookOpen = true;
            _sheet.Clear();
            _sheet.ForgetMarks();
            SetSheetControlsShown(stage, false);
            await _book.RenderAsync(stage, page, known, _resources, _palette, _logger);
            // The page is the whole screen in the original: a click anywhere dismisses it.
            _bookScrim = new VisualElement {
                name = "BakSpellBookScrim",
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            _bookScrim.RegisterCallback<PointerUpEvent>(_ => CloseSpellBook());
            GetComponent<UIDocument>()?.rootVisualElement.Add(_bookScrim);
        }

        private VisualElement _bookScrim;

        /// <summary>The sheet's REQ entries and name plate, hidden while the book is the screen.</summary>
        private void SetSheetControlsShown(VisualElement stage, bool shown) {
            if (!shown) {
                foreach (VisualElement plate in stage.Query<VisualElement>(className: NamePlateClass).ToList()) {
                    plate.RemoveFromHierarchy();
                }
            }
            if (_ui?.CurrentNavWidgets == null) {
                return;
            }
            foreach (BakAgain.UI.InputCore.NavWidget widget in _ui.CurrentNavWidgets) {
                if (widget?.Element != null) {
                    widget.Element.style.visibility = shown ? StyleKeyword.Null : Visibility.Hidden;
                }
            }
        }

        /// <summary>Whether the book is showing instead of the sheet.</summary>
        private bool _bookOpen;

        private readonly SpellBookPageView _book = new();

        /// <summary>Puts the sheet back after the book.</summary>
        private void CloseSpellBook() {
            if (!_bookOpen) {
                return;
            }
            _bookOpen = false;
            _bookScrim?.RemoveFromHierarchy();
            _bookScrim = null;
            _book.Clear();
            VisualElement stage = Stage();
            if (stage != null) {
                SetSheetControlsShown(stage, true);
            }
            DrawAsync().Forget();
        }

        /// <summary>
        /// Marks a rating for study, or takes the mark off.
        /// </summary>
        /// <remarks>
        /// <b>A rating the character never had is not refused, it is ignored.</b> The original drops
        /// the click when the maximum is zero rather than saying anything — the same never-had case
        /// that prints N/A where a percentage would go.
        /// </remarks>
        private void ToggleEmphasis(int row) {
            int attribute = SkillEmphasis.AttributeForRow(row);
            ActorStat[] stats = _session?.StatsOf(CharacterIndex());
            if (stats == null || attribute >= stats.Length) {
                return;
            }
            if (!SkillEmphasis.CanEmphasise(stats[attribute].Max)) {
                return;
            }

            int key = SkillEmphasis.FlagFor(CharacterIndex(), attribute);
            _session.SetGlobalValue(key, SkillEmphasis.Toggled(_session.GetGlobalValue(key) ?? 0));
            // Only the sheet changes: the mark it draws is what moved, not which buttons exist.
            DrawAsync().Forget();
        }

        /// <summary>Walk to the next active member, wrapping at the end.</summary>
        private void ShowNextMember() {
            int count = ActiveCount;
            if (count <= 0) {
                return;
            }
            _slot = (_slot + 1) % count;
            ShowMember();
        }

        private bool IsCaster(int characterIndex) {
            ActorStat[] stats = _session?.StatsOf(characterIndex);

            return stats != null
                && GameData.Resources.Spells.SpellCasting.IsCaster(
                    stats[(int)ActorAttribute.AccuracyCasting].Max);
        }

        private int ActiveCount => _session?.ActivePartyIndices?.Length ?? 0;

        /// <summary>The party roster position the shown slot names.</summary>
        private int CharacterIndex() {
            byte[] active = _session?.ActivePartyIndices;

            return active != null && _slot >= 0 && _slot < active.Length ? active[_slot] : -1;
        }

        private VisualElement Stage() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;

            return root == null ? null : CanonicalStage.GetOrCreate(root, _ui.Frame);
        }

        /// <summary>Leave.</summary>
        private const int ExitActionId = 1;

        /// <summary>The spellbook page, offered to casters only.</summary>
        private const int SpellsActionId = 31;

        /// <summary>The portrait's own click area.</summary>
        private const int PortraitActionId = 49;

        /// <summary>The sheet area that shares the portrait's behaviour.</summary>
        private const int PortraitAreaActionId = 57;

        /// <summary>The character's own description.</summary>
        private const int CharacterDescriptionDialog = 105;

        /// <summary>Where a dialog's subject is published, as everywhere else.</summary>
        private const int DialogSubjectGlobal = 30000;
    }
}
