namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.Layout;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Dialog.Branches;
    using GameData.Resources.GameState;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    // Aliased (not a namespace import) so UnityEngine.Color stays unambiguous —
    // GameData.Resources.Palette also defines a Color type.
    using PaletteResource = GameData.Resources.Palette.PaletteResource;

    /// <summary>
    /// Renders DDX dialogs into a persistent <c>UIDocument</c> overlay.
    /// Lives on its own prefab (<c>DialogOverlay.prefab</c>) so its lifetime
    /// is independent of any state-controlled UI — callers can show a dialog
    /// from any state without relying on the MainMenu (or other UI host)
    /// being active. Also serves the cutscene path via
    /// <see cref="ShowEntry"/> / <see cref="DisplayEntry"/>, replacing the
    /// parallel uGUI/TMP overlay that used to live in <c>CutSceneView</c>.
    /// </summary>
    public class DialogManager : MonoBehaviour, IDialogManager {
        private UIDocument _rootDocument;
        private ILogger _logger;
        private DialogResourceLoader _resources;
        private bool _initialized;
        private VisualElement _activePanel;

        // Who the entry being rendered actually speaks as. ActorNumber is a sentinel as often as an
        // id (255 alone appears 221 times), so nothing may read it raw — see DialogSpeakerSentinel.
        private readonly DialogSpeakerSentinel _speakers = new DialogSpeakerSentinel();
        private int _speakerActor;

        // Choice/confirm dialog buttons, wired into an Exclusive NavigableLayer (AddConfirmButtons) so
        // device/agent nav + Enter + Esc + first-letter route there; the golden focus tint follows via
        // each button's FocusInEvent → SetChoiceFocus, alongside the existing mouse-click path.
        private System.Collections.Generic.List<Button> _choiceButtons;
        // Set by a button click OR by the dialog layer (Activate/Cancel/accelerator) to resolve the
        // dialog. The ShowEntryCore await loop watches this each frame — done this way (not
        // MonoBehaviour.Update) because the panel renders into a shared always-active document while
        // DialogManager's own GameObject is inactive, so Update would never run.
        /// <summary>
        /// One show's answer box. <b>A dialog can only ever be answered by its OWN buttons.</b>
        /// </summary>
        /// <remarks>
        /// This used to be a bare <c>int?</c> field shared by every show, and shows overlap — which
        /// is the same shape as <c>_dialogLayer</c> and <c>_activePanel</c>, both of which are now
        /// captured per call for the same reason. The failure it produced was worse than theirs
        /// because the wait is a POLL: a choice wait that outlived its own dialog kept reading the
        /// shared field, so the next unrelated dialog answered with its FIRST button set the field
        /// to 0 and completed the stale wait too. `CrossZoneAsync` read that 0 as "cross" and
        /// teleported the party out of the chapter-1 route — measured three times on 2026-09-15:
        /// decline the zone-3 crossing, then say Yes to an innkeeper or Accept a shop offer, and the
        /// party lands in zone 5 while the dialog it actually answered never continues (TASK-556).
        ///
        /// <para>A fresh box per menu makes that structurally impossible rather than guarded: the
        /// stale wait is left polling a box nobody can reach any more, and the panel guard in
        /// <c>ShowEntryCore</c> (TASK-563) ends it.</para>
        /// </remarks>
        private sealed class ChoiceAnswer {
            internal int? Value;
        }

        /// <summary>The box the CURRENT menu's buttons write to. Each builder installs a new one.</summary>
        private ChoiceAnswer _choiceResult = new ChoiceAnswer();
        private int _choiceFocusIndex = -1;

        // Software cursor, injected so the choice keyboard nav can warp it onto the focused button
        // (consistent with REQ screens). NullCursorManager until the CursorOverlay prefab is wired.
        private ICursorManager _cursorManager;

        // The input-ownership stack: dialogs push an Exclusive layer so the menu beneath is blocked
        // structurally (no ModalActive flag). DialogManager is a registered component, so VContainer
        // injects this via Construct.
        private BakAgain.UI.InputCore.InputLayerStack _stack;

        // Menu sound service — plays the default pound on choice-button select (mouse or keyboard).
        private BakAgain.Audio.MenuSoundService _menuSound;

        // The pushed input layer for the active dialog (Exclusive). Choice dialogs nav their buttons;
        // narrative dialogs dismiss on Activate/Cancel. Popped in RemovePanel/Deactivate.
        private BakAgain.UI.InputCore.IInputLayer _dialogLayer;

        // Live session state — supplies the active party names / gold for @N text-variable
        // substitution (DialogSlotPopulator / TextVariableResolver). Branch-walk conditions and
        // effects are the DialogExecutor's business.
        private BakAgain.Core.GameSession _gameSession;

        // Per-topic activation actions, so the nav layer runs exactly what a click runs.
        private System.Collections.Generic.List<System.Action> _topicActions;

        // Dialog game-logic (id → leaf-entry resolution incl. the effect-applying branch walk).
        private BakAgain.Core.Services.DialogExecutor _executor;

        [VContainer.Inject]
        public void Construct(ICursorManager cursorManager, BakAgain.UI.InputCore.InputLayerStack stack,
            BakAgain.Audio.MenuSoundService menuSound, BakAgain.Core.GameSession gameSession,
            BakAgain.Core.Services.DialogExecutor executor,
            BakAgain.Core.Services.IPreferencesService preferences,
            BakAgain.World.IWorldViewport worldViewport = null,
            BakAgain.CutScenes.IResourceCache sprites = null) {
            _worldViewport = worldViewport;   // optional: a harness with no world still shows dialogs
            _sprites = sprites;               // ...and one with no sprite cache still shows text
            _cursorManager = cursorManager;
            _stack = stack;
            _menuSound = menuSound;
            _gameSession = gameSession;
            _executor = executor;
            _preferences = preferences;
        }

        // Read per dialog rather than cached, so changing text speed in Preferences takes effect
        // on the very next panel.
        private BakAgain.Core.Services.IPreferencesService _preferences;
        // Full-screen transparent layer added beneath the dialog content while a
        // modal dialog is up. The DialogOverlay UIDocument shares one runtime
        // panel with the menus/screens (same PanelSettings) at a higher
        // sortingOrder, so this layer makes the dialog genuinely modal: every
        // pointer event in the dialog's region is consumed here instead of
        // falling through to the button beneath that opened the dialog.
        /// <summary>
        /// Hands over the two calls that turn the world view and put it back.
        /// </summary>
        /// <remarks>
        /// <b>Not a <c>Construct</c> parameter.</b> That method is VContainer's injection point and
        /// every parameter of it has to be resolvable from the container; a delegate is not. The
        /// world hands these over when it builds, the same way <c>HotspotService</c> receives its
        /// callbacks, and a session with no world simply never calls this.
        /// </remarks>
        public void SetBackdropCameraSeam(System.Action<int> turnToSpeaker, System.Action restore) {
            _turnCameraToSpeaker = turnToSpeaker;
            _restoreCamera = restore;
        }

        // The world view behind a backdrop dialog is rendered from a DIFFERENT bearing when the
        // speaker walks with the party — see DialogBackdropCamera. Optional for the same reason the
        // viewport is: a harness with no world still shows dialogs, it just does not turn anything.
        private System.Action<int> _turnCameraToSpeaker;
        private System.Action _restoreCamera;

        // Whether this dialog turned the camera, so only a dialog that turned it puts it back.
        private bool _cameraTurned;

        private VisualElement _modalScrim;
        // The active panel's dialog area as layout data. Every shipped style states it as
        // absolute design-frame px in the canonical 1600×1200 space — the PanelSettings scaler
        // (reference resolution 1600×1200) maps that to the screen, so no per-resize re-fit is
        // needed — while an override may state it in percentages or anchor it, which UI Toolkit
        // then resolves against the stage on its own.
        private LayoutHint _activeArea;
        // The active palette text/chrome pens resolve against, supplied by
        // whoever owns the current rendering environment (the cutscene path
        // pushes it via SetActivePalette). Null falls back to _defaultPalette.
        private Color[] _activePalette;
        // NOTE: dialog placement no longer reads IGameViewport — areas are
        // absolute canonical px and the panel scaler handles screen mapping.
        // GameViewportRegistry + its providers stay in place for potential
        // enhanced-mode re-anchoring (Phase 4); see the canonical-spine plan.

        // The DialogOverlay prefab is instantiated *inactive* and stays that
        // way except while a dialog is being shown. That's why we don't rely
        // on Awake to grab the UIDocument — it wouldn't fire on an inactive
        // GameObject. Initializing lazily here keeps the prefab dormant
        // (avoiding the URP-overlay rendering conflict where a second active
        // UIDocument blacks out the lower-sortOrder panel) until needed.
        private void EnsureInitialized() {
            if (_initialized) {
                return;
            }
            _rootDocument = GetComponent<UIDocument>();
            _logger = LogManager.LoggerFactory.CreateLogger<DialogManager>();
            _resources = new DialogResourceLoader(_logger);

            // PanelSettings.clearColor=true makes UI Toolkit clear the entire
            // framebuffer to its clear-color value before drawing the panel.
            // For an overlay that's supposed to sit on top of the cutscene /
            // game view, that turns the whole screen the clear color (black,
            // by default) as soon as the UIDocument activates. Force the
            // clear off so the dialog panel composites onto whatever uGUI /
            // RawImage layer is below it.
            if (_rootDocument != null && _rootDocument.panelSettings != null) {
                _rootDocument.panelSettings.clearColor = false;
            }
            _initialized = true;
        }

        // Initialise (if needed) and report whether the manager has a UIDocument
        // to render into. Every public entry point gates on this; failing here
        // means the prefab is mis-set-up and we can't show anything.
        private bool TryEnsureReady() {
            EnsureInitialized();
            if (_rootDocument == null) {
                _logger.LogError("DialogManager requires a UIDocument component on the same GameObject.");
                return false;
            }
            return true;
        }

        // Remove any active panel from the UI tree without changing the
        // overlay's GameObject active state. Used at the start of a show call
        // (a new panel is about to take its place).
        private BakAgain.World.IWorldViewport _worldViewport;

        /// <summary>Sprite cache for the speaker portrait — distinct from the DDX loader above.</summary>
        private BakAgain.CutScenes.IResourceCache _sprites;

        /// <summary>The speaker's portrait, when the entry has one.</summary>
        private VisualElement _speakerFace;
        private VisualElement _speakerNamePill;
        private VisualElement _speakerNamePillShadow;

        /// <summary>
        /// Draws the speaking actor's portrait — <c>ExecuteDialog</c>'s call into
        /// <c>ShowDialogWithFace</c> with mode 0 (@0x49986).
        /// </summary>
        /// <remarks>
        /// <b>Mode 0 is "centred horizontally, bottom-aligned to the RENDER VIEW".</b> Not to the
        /// dialog panel and not to the window — the portrait stands in the world viewport with the
        /// text box below it, which is why it moves when the viewport does and why nothing here
        /// states a position of its own. The two insets come from
        /// <see cref="BakAgain.World.IWorldViewport.CanonicalRect"/>; the size comes from the art.
        ///
        /// <para>A speaker of 0 is "nobody speaks" rather than actor zero, and actors at or above
        /// 49 have no portrait — <see cref="ActorFaceView"/> answers false for both and nothing is
        /// drawn, which is an ordinary outcome for most dialog in the game.</para>
        /// </remarks>
        /// <summary>
        /// The full-screen parchment some dialogs are drawn on — <c>DIALOG.SCX</c>, with the world
        /// view left showing through it.
        /// </summary>
        /// <remarks>
        /// <b>The flag decides it, not the dialog type.</b> <see cref="DialogBackdrop"/> has carried
        /// that rule since it was ported and nothing consumed it, so every dialog drew straight over
        /// whatever was on screen — right for the ones with no flag, wrong for the ones with it.
        ///
        /// <para><b>The world view is NOT covered.</b> The parchment is a full-screen blit in the
        /// original, but the world's viewport is redrawn on top of it: the green behind the speaker
        /// is the terrain, not a painted field. Our world view is a RenderTexture element on a
        /// DIFFERENT UIDocument and the dialog's panel sorts above it, so covering that rect hides
        /// the world however this element is ordered within its own stage — hence a hole.</para>
        ///
        /// <para><b>Four clipped windows onto ONE sheet, not four stretched pieces.</b> Each strip
        /// clips a full-frame copy of the image positioned at its own negative origin, so the torn
        /// edges stay at the frame's edges and the grain runs continuously across the seams. Four
        /// independently-stretched pieces would put a torn border around every strip.</para>
        ///
        /// <para><b>Failure is silent and harmless.</b> A missing image means the dialog draws as it
        /// did before rather than not at all.</para>
        /// </remarks>
        private async UniTask AddFullScreenBackdropAsync(VisualElement stage, DialogEntry entry,
            Color[] palette) {
            if (stage == null || !DialogBackdrop.DrawsFullScreenBackdrop(entry)) {
                return;
            }

            Sprite parchment = await _resources.GetSpriteAsync(DialogBackdrop.Resource);
            if (parchment == null) {
                _logger?.LogDebug($"{DialogBackdrop.Resource} unavailable; dialog drawn without it.");
                return;
            }

            var backdrop = new VisualElement {
                name = "BakDialogBackdrop",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                },
            };
            stage.Insert(0, backdrop);

            // The canonical frame comes from the stage the dialog was built with, never a constant:
            // a hardcoded 1600x1200 is wrong for any resource that ships another.
            if (!CanonicalStage.TryGetFrame(stage, out DesignFrame frame, out _)
                || frame.Width <= 0 || frame.Height <= 0) {
                AddBackdropWindow(backdrop, parchment, 0, 0, Length.Percent(100), Length.Percent(100),
                    0, 0, Length.Percent(100), Length.Percent(100));
                return;
            }

            float w = frame.Width;
            float h = frame.Height;
            // *** Only the FLAG path re-renders the world over the parchment. *** ExecuteDialog's
            // flagged arm blits the parchment and immediately paints the viewport back on top
            // (0x49a98), which is what leaves the parchment as a border around the world. The
            // type-6 arm (0x49b3b-0x49b6f) falls straight through to the text with the parchment
            // still covering the screen. Cutting the hole for both would punch a viewport-shaped
            // gap through the narrative the player is meant to read.
            Rect? hole = DialogBackdrop.RedrawsWorldViewport(entry.Flags) ? WorldViewportHole() : null;
            if (hole == null) {
                AddBackdropWindow(backdrop, parchment, 0, 0, w, h, 0, 0, w, h);
                return;
            }

            Rect v = hole.Value;
            float rightX = v.xMax;
            float belowY = v.yMax;

            // above, below, left-of and right-of the viewport — between them exactly the frame minus
            // the hole, with no overlap.
            AddBackdropWindow(backdrop, parchment, 0, 0, w, v.yMin, 0, 0, w, h);
            AddBackdropWindow(backdrop, parchment, 0, belowY, w, h - belowY, 0, -belowY, w, h);
            AddBackdropWindow(backdrop, parchment, 0, v.yMin, v.xMin, v.height, 0, -v.yMin, w, h);
            AddBackdropWindow(backdrop, parchment, rightX, v.yMin, w - rightX, v.height,
                -rightX, -v.yMin, w, h);
            AddViewportOutline(backdrop, v, palette);
        }

        /// <summary>
        /// The frame around the world view — the rect the original draws after re-rendering it.
        /// </summary>
        /// <remarks>
        /// <b>Outside the viewport, not on its edge.</b> The original switches fill and clipping
        /// off and draws one px out on every side, so the line frames the world instead of eating
        /// its outermost row of pixels. Drawn as a border on an element inset by that same px so
        /// the two cannot drift apart.
        /// </remarks>
        private static void AddViewportOutline(VisualElement backdrop, Rect hole, Color[] palette) {
            float x = DialogBackdrop.ViewportOutlineInset * GameData.Resources.Layout.OriginalPixel.Width;
            float y = DialogBackdrop.ViewportOutlineInset * GameData.Resources.Layout.OriginalPixel.Height;
            Color pen = PaletteColors.ResolvePen(palette, DialogBackdrop.ViewportOutlinePen);
            var outline = new VisualElement {
                name = "BakDialogViewportOutline",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = hole.xMin - x, top = hole.yMin - y,
                    width = hole.width + (2 * x), height = hole.height + (2 * y),
                    borderLeftWidth = x, borderRightWidth = x,
                    borderTopWidth = y, borderBottomWidth = y,
                    borderLeftColor = pen, borderRightColor = pen,
                    borderTopColor = pen, borderBottomColor = pen,
                },
            };
            backdrop.Add(outline);
        }

        /// <summary>
        /// The rect the world view occupies, or null when there is no world on screen.
        /// </summary>
        /// <remarks>
        /// Null for a dialog raised over a menu or a cutscene — there is nothing to see through, and
        /// a hole would expose whatever happened to be behind the panel.
        /// </remarks>
        private Rect? WorldViewportHole() {
            if (_worldViewport == null) {
                return null;
            }
            BakAgain.Graphics.Area view = _worldViewport.CanonicalRect;
            return view.Width <= 0 || view.Height <= 0
                ? (Rect?)null
                : new Rect(view.X, view.Y, view.Width, view.Height);
        }

        /// <summary>
        /// One clipped window onto the parchment: a box at
        /// (<paramref name="x"/>, <paramref name="y"/>) holding a full-sheet copy offset to
        /// (<paramref name="imgX"/>, <paramref name="imgY"/>).
        /// </summary>
        private static void AddBackdropWindow(VisualElement parent, Sprite parchment,
            Length x, Length y, Length width, Length height,
            Length imgX, Length imgY, Length imgW, Length imgH) {
            var clip = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x, top = y, width = width, height = height,
                    overflow = Overflow.Hidden,
                },
            };
            clip.Add(new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = imgX, top = imgY, width = imgW, height = imgH,
                    backgroundImage = Background.FromSprite(parchment),
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            });
            parent.Add(clip);
        }

        private void ShowSpeakerFace(VisualElement stage, DialogEntry entry) {
            RemoveSpeakerFace();
            if (stage == null || entry == null || _worldViewport == null || _sprites == null) {
                return;
            }

            // The resolved speaker, never entry.ActorNumber: 255 is "the party member speaking",
            // and taking it literally is what left the ask-about page with no portrait.
            int actor = _speakerActor;
            BakAgain.Graphics.Area view = _worldViewport.CanonicalRect;

            var face = new VisualElement {
                name = "BakDialogSpeakerFace",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = view.X + (view.Width / 2f),
                    top = view.Y + view.Height,
                    // Centred on that x and sitting ON that y: the element sizes itself to the
                    // portrait, so the shift is expressed as a fraction of itself rather than as a
                    // number this class would have to know.
                    translate = new Translate(Length.Percent(-50), Length.Percent(-100)),
                },
            };
            stage.Add(face);
            _speakerFace = face;

            // *** THE SURROUND IS THE SCREEN'S PALETTE, NOT THE ACTOR'S. *** An ACT###.PAL defines
            // nothing outside ActorFaceCache.FaceRangeFirst..End, and DIALOG.C:1043 passes a null
            // pal_buf, which makes askabout_actor_spr_blit_pal_swap read the LIVE screen palette
            // for the rest — so without a host the face sits on a black rectangle (TASK-363).
            //
            // Null when a cutscene installed its own via SetActivePalette: that path hands over a
            // Color[] with no name, and the key needs a name. Naming OPTIONS.PAL there anyway would
            // be worse than leaving it — it would confidently composite against the wrong screen.
            string hostPalette = _activePalette == null || _activePalette.Length == 0
                ? DialogResourceLoader.DefaultDialogPaletteKey
                : null;
            ActorFaceView.ApplyAsync(face, actor, _sprites, alternate: false, logger: _logger,
                    sizeToSprite: true, hostPalette: hostPalette)
                .ContinueWith(drew => {
                    if (!drew && _speakerFace == face) {
                        RemoveSpeakerFace();
                    }
                })
                .Forget();
        }

        private void RemoveSpeakerFace() {
            if (_speakerFace != null) {
                _speakerFace.RemoveFromHierarchy();
                _speakerFace = null;
            }
            RemoveSpeakerNamePill();
        }

        private void RemoveSpeakerNamePill() {
            if (_speakerNamePill != null) {
                _speakerNamePill.RemoveFromHierarchy();
                _speakerNamePill = null;
            }
            if (_speakerNamePillShadow != null) {
                _speakerNamePillShadow.RemoveFromHierarchy();
                _speakerNamePillShadow = null;
            }
        }

        /// <summary>
        /// The rounded name plate under the portrait — <c>dialog_draw_speech_bubble</c>.
        /// </summary>
        /// <remarks>
        /// <b>Drawn AFTER the panel, and on the stage.</b> Its coordinates are screen positions and
        /// it deliberately straddles the bottom edge of the world viewport, overlapping both the
        /// world above and the panel below — inside the panel it would be offset by the panel's
        /// origin and clipped at its top edge. The original draws it last for the same reason.
        ///
        /// <para><b>Two whole plates, not a plate plus a text shadow.</b> The original lays a
        /// shadow pass under everything at +1,+1 — the cap arcs, the lower rule AND the label — so
        /// one offset copy of the entire plate reproduces it exactly: the parts of the copy that are
        /// not the bottom-right rim are covered by the plate itself. Building it as a rim plus a
        /// separate text shadow is more code for the same pixels.</para>
        /// </remarks>
        private async UniTask ShowSpeakerNamePillAsync(VisualElement stage, DialogEntry entry,
            Color[] palette, bool keywordGrid = false) {
            RemoveSpeakerNamePill();
            if (stage == null || !DialogSpeakerNamePill.ShowsFor(_speakerActor, entry.Flags)) {
                return;
            }

            string name = await ResolveSpeakerNameAsync(_speakerActor);
            if (string.IsNullOrEmpty(name)) {
                // The original's draw routine no-ops on an empty string rather than drawing a bare
                // plate, so an unresolved name shows nothing at all.
                return;
            }

            // *** THE ASK-ABOUT PROMPT IS THIS PLATE, NOT A SEPARATE HEADING. *** ShowKeywordDialog
            // builds "<name> asked about:" by concatenation and hands it to
            // dialog_draw_speech_bubble — the same routine that draws the ordinary name. So the
            // prompt is the plate with a longer string, and looking for a heading element above the
            // grid finds nothing to write to.
            //
            // NINETEEN OF THE TWENTY-ONE SHIPPED GRIDS CARRY ActorNumber 255, and this used to
            // read that as "no plate, faithfully". *** It is a sentinel, not an id. ***
            // ExecuteDialog substitutes the running party speaker for it before the name is looked
            // up (DIALOG.C:885), so the original always has a name to concatenate and every one of
            // the nineteen gets its "<name> asked about:" heading. Resolving happens once, in
            // ShowEntryCore; by here _speakerActor is already concrete.
            if (keywordGrid) {
                name = KeywordPrompt.PromptFor(name);
            }

            _speakerNamePillShadow = BuildNamePlate(name,
                PaletteColors.ResolvePen(palette, DialogSpeakerNamePill.ShadowPen),
                PaletteColors.ResolvePen(palette, DialogSpeakerNamePill.ShadowPen),
                edge: null,
                offsetX: DialogSpeakerNamePill.ShadowOffsetX,
                offsetY: DialogSpeakerNamePill.ShadowOffsetY);
            _speakerNamePill = BuildNamePlate(name,
                PaletteColors.ResolvePen(palette, DialogSpeakerNamePill.FillPen),
                PaletteColors.ResolvePen(palette, DialogSpeakerNamePill.LabelPen),
                edge: PaletteColors.ResolvePen(palette, DialogSpeakerNamePill.EdgePen),
                offsetX: 0, offsetY: 0);
            stage.Add(_speakerNamePillShadow);
            stage.Add(_speakerNamePill);
        }

        /// <summary>One plate: a capsule that sizes itself to its label.</summary>
        /// <remarks>
        /// <b>The padding is deliberately asymmetric.</b> The plate is centred on
        /// <see cref="DialogSpeakerNamePill.CentreX"/> but the label on
        /// <see cref="DialogSpeakerNamePill.LabelCentreX"/>, two original px to its right. Moving
        /// that offset from one pad to the other keeps the total width — and therefore the plate's
        /// centre — exactly where it was, which a margin on the label would not.
        /// </remarks>
        private static VisualElement BuildNamePlate(string name, Color fill, Color textColor,
            Color? edge, int offsetX, int offsetY) {
            int shift = DialogSpeakerNamePill.LabelCentreX - DialogSpeakerNamePill.CentreX;
            float pad = (DialogSpeakerNamePill.LabelPadding / 2f) + DialogSpeakerNamePill.CapRadius;
            var plate = new VisualElement {
                name = "BakDialogSpeakerNamePill",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = DialogSpeakerNamePill.CentreX + offsetX,
                    top = DialogSpeakerNamePill.Top + offsetY,
                    height = DialogSpeakerNamePill.Height,
                    minWidth = DialogSpeakerNamePill.OuterWidth(0),
                    // Sizes to the label, then re-centres itself on that width — so the plate holds
                    // its screen centre for any name without anything measuring the text.
                    translate = new Translate(Length.Percent(-50), 0),
                    paddingLeft = pad + shift,
                    paddingRight = pad - shift,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    backgroundColor = fill,
                    borderTopLeftRadius = DialogSpeakerNamePill.CapRadius,
                    borderTopRightRadius = DialogSpeakerNamePill.CapRadius,
                    borderBottomLeftRadius = DialogSpeakerNamePill.CapRadius,
                    borderBottomRightRadius = DialogSpeakerNamePill.CapRadius,
                },
            };
            if (edge is Color rim) {
                plate.style.borderLeftWidth = plate.style.borderRightWidth = GameData.Resources.Layout.OriginalPixel.Width;
                plate.style.borderTopWidth = plate.style.borderBottomWidth = GameData.Resources.Layout.OriginalPixel.Height;
                plate.style.borderLeftColor = plate.style.borderRightColor = rim;
                plate.style.borderTopColor = plate.style.borderBottomColor = rim;
            }
            var label = new Label(name) {
                pickingMode = PickingMode.Ignore,
                style = { color = textColor, unityTextAlign = TextAnchor.MiddleCenter },
            };
            GameFontText.Apply(label);
            plate.Add(label);
            return plate;
        }

        /// <summary>
        /// The speaker's caption — <c>askabout_name_or_keyword_lookup</c>.
        /// </summary>
        /// <remarks>
        /// <b>Two sources, split at id 7.</b> The low ids name characters and are read out of the
        /// party's own name list; everything above reads the keyword table at an OFFSET index.
        /// Implementing only the table captions every companion with a topic word, and implementing
        /// only the party leaves every other speaker uncaptioned.
        /// </remarks>
        private async UniTask<string> ResolveSpeakerNameAsync(int speakerId) {
            if (DialogSpeakerNamePill.IsPartySpeaker(speakerId)) {
                string[] names = _gameSession?.PartyActorNames;
                int index = DialogSpeakerNamePill.PartyIndexOf(speakerId);
                return names != null && index >= 0 && index < names.Length ? names[index] : null;
            }
            KeywordList keywords = await _resources.GetKeywordsAsync();
            return keywords?.Keywords != null
                && keywords.Keywords.TryGetValue(DialogSpeakerNamePill.KeywordIndexOf(speakerId),
                    out string text)
                ? text
                : null;
        }

        /// <summary>
        /// Turns the world view to the speaker's bearing while a backdrop dialog is up.
        /// </summary>
        /// <remarks>
        /// <b>Only the yaw moves.</b> The original writes height and pitch too, but as
        /// <c>defaultCameraZ + ground</c> and <c>defaultCameraPitch</c> — which is the walking eye
        /// it already had. See <see cref="DialogBackdropCamera"/>.
        ///
        /// <para>Only for a speaker who walks with the party: a townsman or a narrator leaves the
        /// view on whatever the player was looking at.</para>
        /// </remarks>
        private void TurnCameraForBackdrop(DialogEntry entry) {
            // The re-point lives inside the FLAGGED arm (0x49a10-0x49a98), immediately before the
            // world render it exists to frame. The type-6 arm renders no world, so there is nothing
            // for a turn to aim and the only thing it could do is show on the way back out.
            if (_turnCameraToSpeaker == null || _gameSession == null
                || !DialogBackdrop.RedrawsWorldViewport(entry.Flags)) {
                return;
            }
            // The RESOLVED speaker: the original's camera block tests the same rewritten
            // wSpeaker_id the portrait does, so a 255 record frames the party member speaking.
            int slot = DialogBackdropCamera.SpeakerSlot(_gameSession.ActivePartyIndices, _speakerActor);
            if (slot < 0) {
                return;
            }
            _turnCameraToSpeaker(slot);
            _cameraTurned = true;
        }

        // Put the view back wherever a panel goes away — every teardown path runs through here, so a
        // dialog cancelled or cleared restores it just as a dismissed one does.
        private void RestoreCameraIfTurned() {
            if (!_cameraTurned) {
                return;
            }
            _cameraTurned = false;
            _restoreCamera?.Invoke();
        }

        /// <summary>
        /// Tear the panel down and take its input layer off the stack.
        /// </summary>
        /// <param name="mine">
        /// The layer THIS show pushed. <b>Not optional in spirit</b>: <c>_dialogLayer</c> is one
        /// field and <c>ShowEntryCore</c> is re-entrant, so two overlapping shows both write it.
        /// The outer one's teardown then removed the INNER one's layer and nulled the field, and
        /// the inner one — finding it null — removed nothing: an Exclusive `dialog-narrative`
        /// stranded on the stack with no panel behind it, consuming every Activate and popping for
        /// nobody. Measured 2026-09-10 at Romney's bridge, where it left the game unplayable.
        ///
        /// <para>Null means "whatever is current", which is what <see cref="ClearDialog"/> wants —
        /// it is the caller saying "take down what is on screen", not a show tidying up after
        /// itself.</para>
        /// </param>
        private void RemovePanel(BakAgain.UI.InputCore.IInputLayer mine) {
            RestoreCameraIfTurned();
            RemoveSpeakerFace();
            if (_activePanel != null) {
                _activePanel.RemoveFromHierarchy();
                _activePanel = null;
            }
            if (_modalScrim != null) {
                _modalScrim.RemoveFromHierarchy();
                _modalScrim = null;
            }
            _choiceButtons = null;
            // A NEW box, not a cleared one: the old box is what a wait that outlived this panel is
            // still polling, and leaving it unreachable is the point. Blanking a shared value here
            // is what let the next dialog's answer complete that wait (TASK-556).
            _choiceResult = new ChoiceAnswer();
            _choiceFocusIndex = -1;
            BakAgain.UI.InputCore.IInputLayer target = mine ?? _dialogLayer;
            if (target != null) {
                _stack?.Remove(target);
                // Cleared only when it IS the current one — a show tidying up its own layer must
                // not blank the field out from under a show that is still running.
                if (ReferenceEquals(_dialogLayer, target)) {
                    _dialogLayer = null;
                }
            }
        }

        /// <summary>
        /// Make the overlay document click-through everywhere except its actual content.
        ///
        /// <para>The DialogOverlay is a full-screen UIDocument on a higher <c>sortingOrder</c> than
        /// the screens beneath it, so with default picking its root absorbs every pointer event on
        /// the panel for as long as any dialog is up — including the ones aimed at the screen that
        /// opened the dialog. That is what made the inventory's More Info button unclickable, and it
        /// would silently do the same to any future screen that draws its own controls under a
        /// non-modal dialog.</para>
        ///
        /// <para><see cref="PickingMode.Ignore"/> on an element does not disable its descendants, so
        /// the dialog box, its buttons and the modal scrim all still pick normally. Modality is the
        /// scrim's job (<see cref="EnsureModalScrim"/>) — it should never have been a side effect of
        /// the document being on top.</para>
        /// </summary>
        private static void EnsureOverlayIsClickThrough(VisualElement root, VisualElement stage) {
            if (root != null) {
                root.pickingMode = PickingMode.Ignore;
            }
            if (stage != null) {
                stage.pickingMode = PickingMode.Ignore;
            }
        }

        // Create the full-screen, transparent, pickable modal scrim that makes
        // the dialog modal against the menu/screen sharing this panel beneath it.
        // It swallows every pointer event that reaches it (StopPropagation) so a
        // click can never fall through to the lower-sortingOrder button behind
        // that opened the dialog.
        //
        // <paramref name="onTop"/> places it ABOVE the dialog content (the
        // narrative / tooltip path — no interactive elements, so the scrim owns
        // every click and is the surface the dismiss handlers attach to). When
        // false it goes BEHIND the content (the confirm path — the Yes/No buttons
        // must stay clickable on top; the scrim only swallows clicks that miss
        // them). Returns the scrim so the narrative path can attach dismissal.
        private VisualElement EnsureModalScrim(VisualElement root, bool onTop) {
            if (_modalScrim != null) {
                return _modalScrim;
            }
            _modalScrim = new VisualElement {
                name = "BakDialogModalScrim",
                pickingMode = PickingMode.Position,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                    backgroundColor = new StyleColor(Color.clear)
                }
            };
            _modalScrim.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            _modalScrim.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            if (onTop) {
                root.Add(_modalScrim);
            } else {
                root.Insert(0, _modalScrim);
            }
            return _modalScrim;
        }

        /// <summary>
        /// The id prefix every layer this manager pushes shares — `dialog-narrative`,
        /// `dialog-choice`, `dialog-keywords`.
        /// </summary>
        internal const string LayerIdPrefix = "dialog-";

        /// <summary>
        /// Whatever disables the overlay takes this manager's input layers with it.
        /// </summary>
        /// <remarks>
        /// <b>The teardown path is not the only way the panel goes away.</b> <see cref="Deactivate"/>
        /// removes the layer and then disables the GameObject, so in the ordinary case this finds
        /// nothing. The case it is here for is the overlay being disabled by somebody else — a
        /// combat trigger firing over a narrative dialog — which abandons the show's await with its
        /// layer still pushed. The `finally` in <c>ShowEntryCore</c> cannot help: the await never
        /// returns, so the `finally` never runs.
        ///
        /// <para>An Exclusive layer left with no panel behind it consumes every Activate and pops
        /// for nobody: the compass dies with nothing on screen to dismiss, and it took
        /// <c>Stack.Remove</c> from a debugger to clear. Unity calls <c>OnDisable</c> however the
        /// GameObject is disabled, which is the property that makes this unskippable.</para>
        /// </remarks>
        private void OnDisable() {
            int dropped = _stack?.RemoveWithIdPrefix(LayerIdPrefix) ?? 0;
            if (dropped > 0) {
                _logger?.LogWarning(
                    "DialogManager: the overlay was disabled with {Count} dialog input layer(s) still "
                    + "pushed; dropped them so input is not captured by a panel that is gone.", dropped);
                _dialogLayer = null;
            }
        }

        // Full teardown: drop the panel and disable the overlay GameObject.
        // Used after a dialog completes (or is cancelled / cleared) so the
        // overlay no longer captures input or composites a transparent layer.
        /// <summary>
        /// A SkipWait page is left DRAWN for whatever repaints over it next; only the input it held
        /// is released. <see cref="ClearDialog"/>, or the next show, takes the panel down.
        /// </summary>
        private void ReleaseInputKeepingPanel(BakAgain.UI.InputCore.IInputLayer mine) {
            if (_modalScrim != null) {
                _modalScrim.RemoveFromHierarchy();
                _modalScrim = null;
            }
            if (mine != null) {
                _stack?.Remove(mine);
                if (ReferenceEquals(_dialogLayer, mine)) {
                    _dialogLayer = null;
                }
            }
        }

        private void Deactivate(BakAgain.UI.InputCore.IInputLayer mine = null) {
            RemovePanel(mine); // also pops the dialog's input layer
            if (gameObject.activeSelf) {
                gameObject.SetActive(false);
            }
        }

        // *** WHICH PLAY IS STILL OPEN (TASK-641). *** The wrappers below decrement in a finally, so
        // a count that never returns to 0 is a play whose await never completes. Each open play is
        // kept with what started it, so a stuck count can be read back instead of guessed at.
        private readonly System.Collections.Generic.Dictionary<int, string> _openPlays = new();
        private int _nextPlayToken;

        private int OpenPlay(string label) {
            int token = ++_nextPlayToken;
            _openPlays[token] = label + " @" + UnityEngine.Time.frameCount;
            return token;
        }

        /// <summary>The plays still awaiting their end, oldest first — for diagnosing a stuck count.</summary>
        public string OpenPlaysDescription => string.Join("; ", _openPlays.Values);

        /// <summary>
        /// Brackets a whole play — every page, menu and chain of it — on the session, so the world
        /// can tell a conversation between pages from a conversation that is over.
        /// </summary>
        /// <remarks>
        /// <b>The original's dialog is modal; ours is a series of panels.</b> dialog_play_record
        /// returns only when the conversation ends, so the world loop never runs inside one and a
        /// sub-action-12 hotspot request is consumed after it (WORLDLP.C:185). Here the overlay is
        /// torn down after every page, and for that one frame the travel screen owned input: the
        /// request fired mid-conversation. Navon's Sword record sets 7415 and asks for a pass on its
        /// FIRST page, so his trap fight opened there and the rest of the confrontation ("Now it's
        /// time to see if you can match a true Nighthawk in combat") played after he was dead.
        /// </remarks>
        private async UniTask<T> Playing<T>(System.Func<UniTask<T>> play, string label) {
            if (_gameSession != null) {
                _gameSession.DialogsPlaying++;
            }
            int token = OpenPlay(label);
            try {
                return await play();
            } finally {
                _openPlays.Remove(token);
                // Clamped: a load resets the count (GameSession), and a play that ends after it
                // must not take it below 0 -- a negative count reads as "nothing playing" to the
                // world loop's `> 0` test for the rest of the session.
                if (_gameSession != null && _gameSession.DialogsPlaying > 0) {
                    _gameSession.DialogsPlaying--;
                }
            }
        }

        private async UniTask Playing(System.Func<UniTask> play, string label) {
            if (_gameSession != null) {
                _gameSession.DialogsPlaying++;
            }
            int token = OpenPlay(label);
            try {
                await play();
            } finally {
                _openPlays.Remove(token);
                // Clamped: a load resets the count (GameSession), and a play that ends after it
                // must not take it below 0 -- a negative count reads as "nothing playing" to the
                // world loop's `> 0` test for the rest of the session.
                if (_gameSession != null && _gameSession.DialogsPlaying > 0) {
                    _gameSession.DialogsPlaying--;
                }
            }
        }

        /// <summary>What a dialog answers when nothing set a return value.</summary>
        /// <remarks>
        /// Zero, and <c>GdsSceneRules.OutcomeFor</c> leaves the scene's outcome untouched for it —
        /// which is how a dialog that merely said something falls through without disturbing
        /// anything. The five values that DO mean something are all negative.
        /// </remarks>
        public const int NoDialogResult = 0;

        public UniTask<int> ShowById(int id, CancellationToken cancellationToken = default) => Playing(() => ShowByIdCore(id, cancellationToken), "ShowById " + id);

        private async UniTask<int> ShowByIdCore(int id, CancellationToken cancellationToken = default) {
            DialogPlay play = await LoadPlayById(id);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return NoDialogResult;
            }
            // A walk that ends with nothing to show displays nothing — skip rather than render an
            // empty panel. The original's guard is `speaker_id != 0 || body_len != 0`
            // (DIALOG.C:965), so a record with neither draws nothing there either. Of 8203 entries
            // across the 32 DDX files, 2271 are text-less and 2250 of those have no speaker.
            //
            // *** The other two clauses are not padding. *** A record with a SPEAKER and no body is
            // one the original draws (the plate and the portrait are the whole content), and a
            // record carrying ChoiceMenu runs its topic menu from OUTSIDE that guard entirely —
            // `if ((record->wFlags & 0x400) != 0) askabout_dialog_run(...)` sits after the drawing
            // block, so a menu page opens whether or not there is anything to print above it.
            //
            // An earlier audit here concluded the text-only test was safe because the 21
            // speaker-without-body records "all live in DIAL_Z20, and that file is driven by
            // chapter-setup rather than by ShowById". *** That premise is void. *** DIAL_Z20 is
            // exactly where the shared ask-about pages live, and now that id-addressed branches are
            // followed (DialogBranchWalker.IdAddressedTargetOf) every NPC who offers topics resolves
            // straight into one. Under the text-only test those pages returned here silently, which
            // is why Squire Phillip's conversation ended where his topic list belongs.
            bool hasSpeaker = (entry.ActorNumber & 0xFF) != 0;
            bool opensMenu = (entry.Flags & DialogEntryFlags.ChoiceMenu) != 0;
            if (string.IsNullOrEmpty(entry.Text) && !hasSpeaker && !opensMenu) {
                // *** A DIALOG THAT ONLY SETS A RETURN VALUE STILL ANSWERS IT. ***
                // Drawing nothing and saying nothing are different things. `dialog_play_record`
                // returns `nResult` whatever the record drew — the drawing guard is DIALOG.C:965
                // and the return is the function's, not that block's — so a text-less router whose
                // whole job is `SetReturnValue` has an answer even though there is no panel.
                //
                // Returning 0 here threw it away, and that is the entire GDS override mechanism:
                // `GdsSceneRules.OutcomeFor` translates -1/-2/-3/-4/-5 and leaves everything else
                // alone, so every text-less router fell through to "the dialog merely said
                // something". Measured 2026-09-13 at Northwarden: dialog 1500156 resolves to DIAL_Z15
                // offset 25857, whose only actions are SetVar 17 and SetReturnValue(-4) — the walk
                // reaches it and the executor holds -4 — and ShowById answered 0, so the Great Hall
                // hotspot took its own action code instead of the -4 sub-scene.
                //
                // The ones with TEXT were never affected, which is why the -1 and -2 families drove
                // clean: the Six Toe Tavern's 3000042 speaks a line and offers a choice, so it never
                // reached this guard.
                return _executor.EntryReturnValue ?? NoDialogResult;
            }

            return await RunChainAsync(play, entry, answerWithFirstPick: false, cancellationToken);
        }

        /// <summary>
        /// Play a conversation from <paramref name="entry"/> onward and answer what it ends on.
        /// </summary>
        /// <remarks>
        /// A CONVERSATION IS A CHAIN, NOT A LINE. Most spoken dialog in the game continues past its
        /// first entry — each line is its own record with its own speaker, and the branch out of it
        /// means "then say this". Showing only the first is what left the rest to surface elsewhere
        /// and read as a stray cutscene.
        ///
        /// <para>Paged as an await loop rather than as the original's queue-blit-wait-poll: the
        /// BEHAVIOUR to reproduce is "a line at a time, advanced by the player", and awaiting the
        /// existing dismissal is how that is spelled here.</para>
        ///
        /// <para><paramref name="answerWithFirstPick"/> is <c>dialog_play_record</c>'s own contract:
        /// <c>nResult</c> STARTS as the index of the branch the player picked (DIALOG.C:1357) and a
        /// later <c>SetReturnValue</c> only overrides it. <see cref="ShowChoiceIndexById"/> is the
        /// caller that speaks in positions, so it asks for that. <see cref="ShowById"/> answers the
        /// LAST pick, or <see cref="NoDialogResult"/> when there was none — the inn reads 0 as a Yes the
        /// party could pay for (MODALSCR.C:772, TASK-496).</para>
        /// </remarks>
        private async UniTask<int> RunChainAsync(DialogPlay play, DialogEntry entry,
            bool answerWithFirstPick, CancellationToken cancellationToken) {
            int firstPick = NoDialogResult;
            var pickCaptured = false;
            int lastPick = NoDialogResult;
            while (entry != null) {
                bool isMenu = (entry.Flags & DialogEntryFlags.ChoiceMenu) != 0;
                // *** 0x200 IS A CHOICE TOO, AND IT IS NOT THE ASK-ABOUT GRID. *** DIALOG.C:1342
                // gives `wFlags & 0x200` its own arm — print the body, then
                // `askabout_menu_page_run_selection(record)` and take THAT branch's target. 0x400
                // (ChoiceMenu) is the keyword grid, whose branches are topics filtered by
                // askabout_dispatch_topic; 0x200 offers the branches themselves, which is how every
                // Yes/No in the game is authored (flag 256 = Yes, 257 = No).
                //
                // The flag was modelled and never read, so these entries rendered as narrative and
                // the walk then looked for a branch whose condition held — neither 256 nor 257 is
                // set until the player answers, so none did and the conversation ended mid-offer.
                // That is the silent refusal TASK-406 recorded, and it BLOCKS chapter 2: Limm's
                // "worth a hundred sovereigns if it's worth a pence. Deal?" is one of these, and
                // the Glazer's Guild Seal is behind it.
                bool isConfirm = (entry.Flags & DialogEntryFlags.TextWithChoice) != 0;

                // *** THE SAME GUARD AS ABOVE, PER LINE. *** A record with neither speaker nor body
                // draws nothing in the original AND does not wait: `dialog_wait_for_acknowledge`
                // sits INSIDE the `speaker_id != 0 || body_len != 0` block (DIALOG.C:965), so such
                // a record just follows its branch. Applying it only to the first entry was enough
                // for a one-line dialog and wrong for a chain — the ask-about answers end on two of
                // these, and the first painted an empty parchment over the whole viewport that a
                // player reads as "the conversation ended on a blank canvas". 5259 of the 8203
                // shipped entries have no speaker, so this is the common shape, not an edge case.
                int picked = -1;
                if (isMenu || isConfirm || !string.IsNullOrEmpty(entry.Text)
                    || (entry.ActorNumber & 0xFF) != 0) {
                    picked = await ShowEntryCore(entry, play, waitForInput: true,
                        renderChoices: isConfirm, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) {
                        return NoDialogResult;
                    }
                    if (answerWithFirstPick && !pickCaptured && (isMenu || isConfirm)) {
                        firstPick = picked;
                        pickCaptured = true;
                    }
                    // nResult is overwritten by EVERY pick (DIALOG.C:1357), so ShowById answers the
                    // last one. Our own Esc (-1) is not a pick the original can make, and is left out
                    // so it cannot reach GdsSceneRules as its -1 override.
                    if ((isMenu || isConfirm) && picked >= 0) {
                        lastPick = picked;
                    }
                }

                // *** SetReturnValue ENDS THE DIALOG, it does not just report on the way out. ***
                // DIALOG.C's third pass sets `done = 1` alongside `nResult`, so an entry carrying
                // one is the last thing said whatever branch would have followed it. Checked here
                // rather than inside the walk because this loop is what "done" means for us.
                if (_executor.EntryReturnValue is int answered) {
                    return answered;
                }

                if (isMenu || isConfirm) {
                    // *** A topic menu ends the conversation or REPLACES the line, it never
                    // continues from itself. *** Its branches are the topics, one per keyword, and
                    // the player's pick selects among them — NextLine would look for a
                    // default/conditional branch, find none, and read the menu as the last thing
                    // said. Farewell is the negative result, and the original agrees: picking it
                    // leaves record_key at 0, which is how its play loop stops.
                    //
                    // A 0x200 confirm continues the same way — its picked branch IS the
                    // continuation (`record_key = choices[nResult].dwTarget_key`). The original
                    // offers no cancel on one of these, so a -1 can only come from our own Esc
                    // affordance, and ending the conversation is the honest reading of it.
                    DialogPlay chosen = picked < 0 ? null : await _executor.FollowChoiceAsync(play, picked);
                    // No next record ends this tree, and the original then plays what an earlier
                    // entry pushed (DIALOG.C:1466): the farewell after GoodBye (TASK-543).
                    play = chosen ?? await _executor.ResumePushedAsync(play);
                    entry = play?.Entry;
                    continue;
                }

                // *** ASYNC BECAUSE THE NEXT LINE MAY LIVE IN ANOTHER DDX. *** A chain can hand
                // off to a shared ask-about page by id rather than by offset, and following that
                // needs a load. See DialogExecutor.NextLineAsync — Squire Phillip's conversation
                // ended on an empty panel because only the initial resolve crossed dialogs.
                (DialogPlay continued, DialogEntry nextEntry) = await _executor.NextLineAsync(play);
                entry = nextEntry;
                play = entry == null
                    ? play
                    : (ReferenceEquals(continued, play)
                        ? new DialogPlay(entry, play.Slots, play.Context, play.Dialog, play.Pushed)
                        : continued);
                if (entry == null) {
                    // The chain ran out: pop what an earlier entry pushed — a topic page after its
                    // answer, a farewell after a menu (TASK-543).
                    DialogPlay resumed = await _executor.ResumePushedAsync(play);
                    if (resumed != null) {
                        play = resumed;
                        entry = resumed.Entry;
                    }
                }
            }

            // The conversation simply ran out of lines. Zero is what a caller reads as "the dialog
            // said something and changed nothing", which is the overwhelming majority of them —
            // unless the caller asked for the picked index, which is what the engine answers.
            return answerWithFirstPick ? firstPick : lastPick;
        }

        // Dialog game-logic (effects, chapter setup, the branch walk) lives in the Core
        // DialogExecutor — this class only renders resolved entries. See the 2026-07-12
        // screen-navigation architecture doc §3.3.

        public UniTask<bool> ShowConfirmById(int id, CancellationToken cancellationToken = default) => Playing(() => ShowConfirmByIdCore(id, cancellationToken), "ShowConfirmById " + id);

        private async UniTask<bool> ShowConfirmByIdCore(int id, CancellationToken cancellationToken = default) {
            DialogPlay play = await LoadPlayById(id);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return false;
            }
            // *** A LINE THAT LEADS TO ITS QUESTION IS PLAYED AS A CHAIN. *** The walk stops at the
            // first record with text, and some confirms open with one: a grave's epitaph (100035
            // "Baby Irisa…") carries no choice of its own and continues by a default branch to 196,
            // "Shall we dig up this grave?". The original plays the record with dialog_play_record and
            // digs on 0 (WCURSOR.C:952), so the epitaph is read, then asked. Rendering the epitaph's
            // branches instead gave one button labelled "1" and no way to say No. TASK-535.
            if ((entry.Flags & (DialogEntryFlags.TextWithChoice | DialogEntryFlags.ChoiceMenu)) == 0) {
                int answer = await RunChainAsync(play, entry, answerWithFirstPick: false, cancellationToken);
                return !cancellationToken.IsCancellationRequested && answer == 0;
            }
            // The affirmative outcome is the entry's FIRST branch (a
            // ConditionalBranch whose FlagCondition.Flag is 256 = "Yes" for
            // DDX 114); ExecuteDialog returns 0 for it, which the
            // original dialog_Preferences treats as confirm.
            //
            // *** AND THE PICKED BRANCH IS WALKED, NOT JUST COUNTED. *** This arm used to stop at
            // ShowEntryCore and answer the index, so everything BEHIND the answer was thrown away:
            // the branch's own narration never appeared and its Actions never ran. The zone
            // crossings are where that shows — 2700006's Yes branch (dial_z27:4410) is
            // *"Days passed... they emerged near the town of Hawk's Hollow"* with a single
            // `AdvanceTime: 432000`, five game days, and the party crossed byte-identical: no
            // rations eaten, no exhaustion, and no daily near-death recovery, which is the only
            // route out of near-death without a temple. Reproduced on def_zone:7 (two days) and
            // def_zone:10 (four days), so it was the method and not one record (TASK-564).
            //
            // The ORIGINAL walks it because there is no other kind of play: every confirm in the
            // game is `dialog_play_record(key, 0) == 0` — HOTSPOT.C's
            // hotspotevt_action_try_enter_zone does exactly that — and dialog_play_record is the
            // full player, branches and actions included. There is no show-choice-and-return-index
            // primitive to be faithful to.
            //
            // `answerWithFirstPick: true` is what KEEPS the answer while walking: nResult starts as
            // the picked index (DIALOG.C:1357), so the confirm still answers the player's own pick
            // rather than whatever a later line happens to leave behind. A SetReturnValue further
            // down still overrides it, and that too is dialog_play_record's contract — the caller
            // reads `== 0` off the whole play, exactly as the original's `*pOut_accepted` does.
            //
            // This is TASK-497's fix reaching its third entry point. That task moved ShowById and
            // ShowChoiceIndexById onto RunChainAsync and its notes say "both entry points use it";
            // ShowConfirmById was the one not counted, and sweeps missed it because what was
            // missing was a place the dialog should have GONE, not a condition it got wrong.
            int confirmed = await RunChainAsync(play, entry, answerWithFirstPick: true, cancellationToken);
            return !cancellationToken.IsCancellationRequested && confirmed == 0;
        }

        /// <inheritdoc/>
        public UniTask<int> ShowChoiceById(int id, CancellationToken cancellationToken = default) => Playing(() => ShowChoiceByIdCore(id, cancellationToken), "ShowChoiceById " + id);

        private async UniTask<int> ShowChoiceByIdCore(int id, CancellationToken cancellationToken = default) {
            DialogPlay play = await LoadPlayById(id);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return -1;
            }

            int chosen = await ShowEntryCore(entry, play, waitForInput: true, renderChoices: true, cancellationToken);
            // The branch's own flag, not its position: the original branches on GetGlobalValue(260)
            // and GetGlobalValue(262) by number, and the order they appear in the entry is not
            // something a caller should have to know.
            return chosen >= 0 && entry.Branches != null && chosen < entry.Branches.Count
                && entry.Branches[chosen] is ConditionalBranch cb
                && cb.Condition is FlagCondition fc
                ? fc.Flag
                : -1;
        }

        /// <inheritdoc/>
        public UniTask<int> ShowChoiceIndexById(int id, CancellationToken cancellationToken = default) => Playing(() => ShowChoiceIndexByIdCore(id, cancellationToken), "ShowChoiceIndexById " + id);

        private async UniTask<int> ShowChoiceIndexByIdCore(int id, CancellationToken cancellationToken = default) {
            DialogPlay play = await LoadPlayById(id);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return -1;
            }

            // *** AN ENTRY WITH NO BRANCHES ANSWERS 0, NOT -1. *** `dialog_play_record` opens with
            // `nResult = 0` (DIALOG.C:837) and only a chosen branch or a SetReturnValue changes it,
            // so a plain narration played through this path answers 0 — which callers that gate on
            // `== 0` read as a yes, and the original does too. Rendering it as a menu instead would
            // draw a button row with no buttons in it and then wait for a pick that can never come.
            if (entry.Branches == null || entry.Branches.Count == 0) {
                await ShowEntryCore(entry, play, waitForInput: true, renderChoices: false,
                    cancellationToken);

                return _executor.EntryReturnValue ?? 0;
            }

            // *** AND THE PICKED BRANCH IS STILL FOLLOWED. *** The index is only where
            // `dialog_play_record` STARTS (DIALOG.C:1357 seeds nResult with it and then walks to
            // `choices[nResult].dwTarget_key`) — answering the index and stopping threw the whole
            // conversation behind the option away.
            //
            // Measured at the Chapel of Ishap, Malac's Cross, 2026-09-13: the temple's service menu
            // offers Talk / Cure / Bless / Done, and "Talk" leads into the temple's own tree —
            // *"The attendant priest looked nervous… I don't know where Abbot Graves might be"* —
            // which the original plays before re-showing the menu. Ours said nothing and closed.
            return await RunChainAsync(play, entry, answerWithFirstPick: true, cancellationToken);
        }

        public async UniTask<Color[]> ResolvePaletteAsync() {
            EnsureInitialized();
            Color[] palette = _activePalette;
            return palette == null || palette.Length == 0
                ? await _resources.GetDefaultPaletteAsync()
                : palette;
        }

        public void SetActivePalette(Color[] palette) {
            _activePalette = palette;
        }

        // Shared id-to-entry resolver for both ShowById and ShowConfirmById: the DialogExecutor
        // owns the load + effect-applying branch walk (the faithful "shown once" mechanism);
        // this class only renders what comes back. Null on any failure (logged by the executor)
        // so the caller can early-return.
        /// <inheritdoc/>
        public async UniTask<DialogPlay> ResolveById(int id, CancellationToken cancellationToken = default) =>
            await LoadPlayById(id);

        private async UniTask<DialogPlay> LoadPlayById(int id) {
            if (!TryEnsureReady()) {
                return null;
            }
            // A conversation starts here, and its speaker latches start with it (DIALOG.C:834).
            _speakers.Begin(ChapterSpeakerId);
            return await _executor.ResolvePlayAsync(id);
        }

        /// <summary>Global 30005, the chapter's designated speaker — the seed for the party latch.</summary>
        private int ChapterSpeakerId => _gameSession?.GetGlobalValue(ChapterSpeakerGlobal) ?? 0;

        private const int ChapterSpeakerGlobal = 30005;

        // Anchored choice-button row near the panel's bottom — one button per
        // dialog branch, labelled from KEYWORD.DAT via the branch's keyword index
        // (see ResolveChoiceLabel; 256="Yes", 257="No", …). Clicking resolves with
        // the branch index. Buttons carry the same .text-button chrome as the
        // REQ user-interface buttons (ClassicTheme.tss: wooden fill, beveled
        // border, theme font + text shadow) so dialogs match the menus.
        /// <param name="partyPicker">
        /// Build the "which of you?" row instead of the branch labels —
        /// <see cref="GameData.Resources.Dialog.PartyMemberPicker"/>. The GEOMETRY is identical
        /// either way, which is the original's arrangement too: <c>ProcessKeywordSelection</c>
        /// (@0x4b3ab) computes its row with the same instructions as the choice row and differs
        /// only in what it puts in it.
        /// </param>
        private void AddConfirmButtons(VisualElement panel,
            System.Collections.Generic.List<DialogBranchBase> branches, KeywordList keywords,
            bool partyPicker = false, DialogSlotTable slots = null, DialogSlotContext slotContext = null) {
            _choiceButtons = new System.Collections.Generic.List<Button>();
            // *** THIS MENU'S OWN ANSWER BOX. *** Captured in a local so every handler below closes
            // over THIS box: once a later menu installs its own, these buttons still answer the
            // dialog they were built for and can no longer complete anybody else's wait.
            ChoiceAnswer answer = _choiceResult = new ChoiceAnswer();
            // The row is a bare full-size layer: every button inside is absolutely placed from
            // DialogButtonRow, so the container contributes no layout of its own.
            var row = new VisualElement {
                name = "BakDialogConfirmRow",
                style = {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0
                }
            };

            System.Collections.Generic.List<string> labels = partyPicker
                ? PartyPickerLabels()
                : null;
            if (labels == null) {
                labels = new System.Collections.Generic.List<string>();
                if (branches == null || branches.Count == 0) {
                    labels.Add(GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.Ok)); // TASK-775
                } else {
                    for (int i = 0; i < branches.Count; i++) {
                        labels.Add(ResolveChoiceLabel(keywords, branches[i], i));
                    }
                }
            }

            // *** THE ROW'S GEOMETRY IS RESEARCHED, NOT INVENTED. ***
            // This used to be a centred flex row with marginLeft/Right 30 and padding 50/12 — six
            // constants that match nothing in the original, sitting beside DialogButtonRow, which
            // carries the actual arithmetic and had no caller (TASK-257). The original divides the
            // panel into count+1 parts and centres a uniform button on each division, sized from
            // the WIDEST label, anchored up from the panel's bottom edge.
            //
            // The measurement is the game's own bitmap font, not the rendered SDF text: UI
            // Toolkit's MeasureTextSize reads 0 before the first layout pass and the row has to be
            // placed as it is built. The two agree to about a percent, because
            // GameFontText.FontSizePx (55) is derived from these very advances.
            int widestLabel = BakAgain.Book.BakFontData.WidestRaw(
                labels, BakAgain.Book.BakFontData.GameFontIndex);
            (int panelWidth, int panelHeight) = CanonicalPanelSize();

            // *** EVERY CANDIDATE LATCH IS CLEARED BEFORE THE MENU APPEARS. ***
            // CreateMenuEntriesFromDialogData writes 0 to each branch's global key on the way past
            // (@0x4b288), so for a Yes/No menu BOTH keys are cleared, not just the one about to be
            // chosen. Without it a latch left set by an earlier menu auto-matches the moment this
            // one opens and the player never sees the question — and DialogBranchWalker really does
            // read these keys, so it is not a theoretical concern.
            //
            // Scoped to THIS menu's branches: there is no global clear, which is what lets the same
            // key mean something durable elsewhere.
            if (!partyPicker && branches != null) {
                foreach (DialogBranchBase branch in branches) {
                    if (KeywordKeyOf(branch) is { } clearKey) {
                        _gameSession?.SetGlobalValue(clearKey,
                            GameData.Resources.Dialog.DialogChoiceEntries.ClearedValue);
                    }
                }
            }

            // ASKABOUT.C:514-531, one resolver for click, keyboard and Escape: an ordinary choice latches its
            // branch key and answers its index; the party picker's last entry (Cancel) answers 1, and a member
            // answers 0 after putting activeParty[selected] + 1 into text-variable slot 0 so '@0' names them.
            void ResolveChoice(int index) {
                if (partyPicker && index >= 0 && labels.Count > 0) {
                    bool cancelled = index == labels.Count - 1;
                    byte[] active = _gameSession?.ActivePartyIndices;
                    if (!cancelled && active != null && index < active.Length) {
                        DialogSlotPopulator.Assign(slots, 0,
                            GameData.Resources.Dialog.DialogChoiceMenu.TextVariableForMember(active[index]), 0, slotContext);
                    }
                    answer.Value = GameData.Resources.Dialog.DialogChoiceMenu.PartyPickerResult(cancelled);
                    return;
                }
                LatchChoice(branches, partyPicker, index);
                answer.Value = index;
            }

            void AddButton(string text, int resultIndex) {
                var button = new Button(() => {
                    _menuSound?.Play(BakAgain.Audio.MenuSoundService.PoundSoundId);
                    ResolveChoice(resultIndex);
                });
                button.AddToClassList("text-button");
                GameFontText.Caption(button, text);
                button.style.position = Position.Absolute;
                if (panelWidth > 0 && panelHeight > 0) {
                    (int x, int y, int w, int h) =
                        GameData.Resources.Dialog.DialogButtonRow.ButtonRectOnCanonicalPanel(
                            resultIndex, panelWidth, panelHeight, labels.Count, widestLabel,
                            BakAgain.Book.BakFontData.GetFontHeight(BakAgain.Book.BakFontData.GameFontIndex));
                    button.style.left = x;
                    button.style.top = y;
                    button.style.width = w;
                    button.style.height = h;
                } else {
                    // No usable panel rect — keep the old centred row rather than stacking every
                    // button at the origin. See CanonicalPanelSize for when this happens.
                    button.style.position = Position.Relative;
                    button.style.marginLeft = 30;
                    button.style.marginRight = 30;
                    button.style.paddingLeft = 50;
                    button.style.paddingRight = 50;
                }
                row.Add(button);
                _choiceButtons.Add(button);
            }

            for (int i = 0; i < labels.Count; i++) {
                AddButton(labels[i], i);
            }
            if (panelWidth <= 0 || panelHeight <= 0) {
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.Center;
                row.style.alignItems = Align.Center;
                row.style.top = StyleKeyword.Null;
                row.style.bottom = Length.Percent(8);
            }
            panel.Add(row);
            SetChoiceFocus(0); // tint the first (affirmative) button so Enter/typeahead can act (no cursor warp on open)

            // Wire the choice buttons into an Exclusive NavigableLayer so device/agent nav + Enter +
            // Esc + first-letter route here (the menu beneath is blocked). Mouse clicks still resolve
            // via each button's own click handler (answer.Value = resultIndex). The golden focus tint
            // follows whatever moves focus, via each button's FocusInEvent → SetChoiceFocus.
            var widgets = new System.Collections.Generic.List<BakAgain.UI.InputCore.NavWidget>();
            for (int i = 0; i < _choiceButtons.Count; i++) {
                int idx = i;
                Button b = _choiceButtons[i];
                b.RegisterCallback<FocusInEvent>(_ => SetChoiceFocus(idx));
                // The label, not b.text: GameFontText.Caption draws the caption in a child, so the
                // button's own text is empty and no first letter ever matched.
                widgets.Add(new BakAgain.UI.InputCore.NavWidget(
                    b, idx < labels.Count ? labels[idx] : b.text, () => ChoiceButtonCanonicalRect(b),
                    () => {
                        _menuSound?.Play(BakAgain.Audio.MenuSoundService.PoundSoundId);
                        // The same latch the pointer path writes. Keyboard and agent activation go
                        // through here instead of the button's own handler, so leaving it out
                        // would make a choice latch only when clicked — the shape of bug the
                        // keyword grid's "ONE action for both" comment already records.
                        ResolveChoice(idx);
                    }, null));
            }
            var layer = new BakAgain.UI.InputCore.NavigableLayer(
                "dialog-choice", BakAgain.UI.InputCore.CaptureMode.Exclusive, widgets,
                // ASKABOUT.C:494-496: Escape presses the LAST entry (a Yes/No confirm reads it as No).
                () => ResolveChoice(_choiceButtons.Count - 1), _cursorManager,
                ambiguousLetterSelectsNothing: true);
            _stack?.Push(layer);
            _dialogLayer = layer;
            if (widgets.Count > 0) {
                layer.FocusIndex(0);
            }
        }

        /// <summary>
        /// Latch the chosen branch's global key — <c>ShowDialogChoiceMenu</c> (@0x4b689).
        /// </summary>
        /// <remarks>
        /// <b>A choice selection is not a jump; it writes a flag and lets the ordinary branch
        /// dispatch find its way there.</b> Nothing wrote this latch, so a dialog whose later
        /// condition tests the choice key read whatever an earlier menu had left — the immediate
        /// continuation worked because it is driven by the returned branch INDEX, which is exactly
        /// what makes the gap invisible until something downstream asks.
        ///
        /// <para>The party picker writes nothing: its buttons are party members, not branches, and
        /// there is no key to latch. Neither does the keyword grid, which records an asked-about
        /// flag and jumps straight to its target instead — see
        /// <c>KeywordPrompt.BranchTargetOffsetFor</c>. Three menus, three different mechanisms,
        /// sharing one builder.</para>
        /// </remarks>
        private void LatchChoice(System.Collections.Generic.List<DialogBranchBase> branches,
            bool partyPicker, int index) {
            if (partyPicker || branches == null || index < 0 || index >= branches.Count) {
                return;
            }
            if (KeywordKeyOf(branches[index]) is { } key) {
                _gameSession?.SetGlobalValue(key,
                    GameData.Resources.Dialog.DialogChoiceMenu.ChosenValue);
            }
        }

        /// <summary>
        /// The "which of you?" row's labels: one per active party member, then Cancel.
        /// </summary>
        /// <remarks>
        /// <b>Nothing checked <c>DialogEntryFlags.PartyMemberSelection</c> at all.</b> The flag was
        /// declared and the model written, and three shipped dialogs carry it — including the
        /// shopkeeper's "Which of you would be interested in purchasing?" — so those entries
        /// rendered as an ordinary two-branch choice and the player was never asked.
        ///
        /// <para><b>Cancel keeps the action id its slot would have had</b>, one past the last
        /// member, rather than getting a distinct code — see <c>PartyMemberPicker.CancelIndex</c>.
        /// A caller that expects a special cancel value reads it as a fourth party member.</para>
        ///
        /// <para>Returns null when the party is not loaded, which sends the caller back to the
        /// branch labels rather than putting a lone Cancel button on screen.</para>
        /// </remarks>
        private System.Collections.Generic.List<string> PartyPickerLabels() {
            string[] names = _gameSession?.PartyActorNames;
            byte[] active = _gameSession?.ActivePartyIndices;
            if (names == null || active == null || active.Length == 0) {
                _logger?.LogWarning(
                    "Party-member selection asked for with no party loaded; showing the branch labels.");
                return null;
            }

            var labels = new System.Collections.Generic.List<string>(
                GameData.Resources.Dialog.PartyMemberPicker.ButtonCount(active.Length));
            foreach (byte member in active) {
                labels.Add(member < names.Length ? names[member] : "?");
            }
            labels.Add(GameData.Resources.Dialog.PartyMemberPicker.CancelLabel);
            return labels;
        }

        /// <summary>
        /// The active panel's size in canonical px, or (0, 0) when it is not expressed as a rect.
        /// </summary>
        /// <remarks>
        /// <b>Read from the LayoutHint rather than measured.</b> The panel's own resolved layout is
        /// not available yet — the row is built during the same pass that creates the panel — and
        /// the hint is where the size actually comes from: the extractor stores the dialog's VGA
        /// rect and <c>CanonicalSpace.Apply</c> rescales it, so these px ARE canonical.
        ///
        /// <para><b>A percentage-sized panel yields (0, 0) deliberately.</b> Nothing in the shipped
        /// data uses one — <c>ResizeDialogActionBuilder</c> says so, and the styles' default areas
        /// are rects — but a mod author's override document may, and there is no honest px answer
        /// for it here. The caller keeps its old centred row in that case, which is what every
        /// dialog had until now.</para>
        /// </remarks>
        private (int Width, int Height) CanonicalPanelSize() {
            GameData.Resources.Layout.LayoutHint area = _activeArea;
            if (area == null
                || area.Width.Unit != GameData.Resources.Layout.LayoutLengthUnit.Px
                || area.Height.Unit != GameData.Resources.Layout.LayoutLengthUnit.Px) {
                return (0, 0);
            }
            return ((int)area.Width.Value, (int)area.Height.Value);
        }

        // Highlight the focused button (golden tint on the wooden fill) and revert the others. Driven
        // by each choice button's FocusInEvent (see AddConfirmButtons), so it follows whatever moves
        // focus — the InputAdapter, the agent, or a pointer.
        private void SetChoiceFocus(int index) {
            if (_choiceButtons == null || _choiceButtons.Count == 0) {
                return;
            }
            int n = _choiceButtons.Count;
            _choiceFocusIndex = ((index % n) + n) % n;
            for (int i = 0; i < n; i++) {
                _choiceButtons[i].style.unityBackgroundImageTintColor = i == _choiceFocusIndex
                    ? new StyleColor(new Color(1f, 0.82f, 0.45f))
                    : new StyleColor(StyleKeyword.Null);
            }
            // No cursor warp here: the cursor follows keyboard focus via the choice NavigableLayer
            // (gated to keyboard nav). SetChoiceFocus also runs on a mouse click (the button's
            // FocusInEvent), where warping would yank the OS pointer mid-click and break the click.
        }

        // The choice button's rect in canonical 1600×1200 space, for the NavigableLayer's spatial
        // (arrow) nav and cursor warp. Read live (the NavWidget holds this as a Func) because geometry
        // only resolves after layout — a value captured when the widget is built would be the pre-layout
        // (0,0,0,0), which collapses every button's centre to the origin and breaks arrow nav. The
        // stage shares the cursor's coordinate frame, so this canonical centre is exactly where the
        // choice NavigableLayer warps the cursor on keyboard nav.
        private Rect ChoiceButtonCanonicalRect(VisualElement button) {
            Rect wb = button.worldBound;
            VisualElement stage = CanonicalStage.Find(_rootDocument.rootVisualElement);
            if (stage == null || wb.width <= 0f || float.IsNaN(wb.x)) {
                return Rect.zero;
            }
            Vector2 min = stage.WorldToLocal(new Vector2(wb.xMin, wb.yMin));
            Vector2 max = stage.WorldToLocal(new Vector2(wb.xMax, wb.yMax));
            return new Rect(min, max - min);
        }


        // The ask-about topic grid — ShowKeywordDialog (0x4b0fd), reached when an entry carries
        // DialogEntryFlags.ChoiceMenu. Four columns of topics with GoodBye last, laid out from
        // KeywordMenu.SlotRect (already canonical, and it applies the farewell's forced x).
        //
        // NOT the same thing as AddConfirmButtons, which is the Yes/No row. Both are now laid out
        // from their own model at the original's positions — DialogButtonRow there, KeywordMenu
        // here — and they differ in SHAPE (a spread row along the panel's bottom against a fixed
        // four-column grid) and in what a click MEANS: a confirm button reports a branch index, a
        // topic also records that it has been asked about.
        private void AddKeywordGrid(VisualElement stage,
            System.Collections.Generic.List<DialogBranchBase> branches, KeywordList keywords) {
            _choiceButtons = new System.Collections.Generic.List<Button>();
            _topicActions = new System.Collections.Generic.List<System.Action>();
            // The keyword grid's own box — same reason as AddConfirmButtons above.
            ChoiceAnswer answer = _choiceResult = new ChoiceAnswer();

            var topics = new System.Collections.Generic.List<(int Branch, int Key)>();
            for (int i = 0; branches != null && i < branches.Count; i++) {
                if (KeywordKeyOf(branches[i]) is not { } key) {
                    continue;
                }
                if (TopicIsAvailable(key)) {
                    topics.Add((i, key));
                }
            }

            // *** No available topic means no menu at all — not even a farewell. *** The original
            // returns without building anything, so there is nothing to click; the conversation is
            // simply over. Putting an empty box on screen is the failure mode this guards.
            if (!KeywordMenu.Opens(topics.Count)) {
                answer.Value = -1;
                return;
            }

            int slots = KeywordMenu.SlotCount(topics.Count);
            int farewellSlot = KeywordMenu.FarewellSlot(topics.Count);

            var grid = new VisualElement {
                name = "BakKeywordGrid",
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 }
            };

            void Place(Button button, int slot) {
                (int x, int y, int w, int h) = KeywordMenu.SlotRect(slot, topics.Count);
                button.style.position = Position.Absolute;
                button.style.left = x;
                button.style.top = y;
                button.style.width = w;
                button.style.height = h;
                button.style.paddingLeft = 0;
                button.style.paddingRight = 0;
                button.style.paddingTop = 0;
                button.style.paddingBottom = 0;
                grid.Add(button);
                _choiceButtons.Add(button);
            }

            for (int slot = 0; slot < slots && slot < topics.Count; slot++) {
                (int branchIndex, int key) = topics[slot];
                bool asked = KeywordMenu.AlreadyAsked(_gameSession?.GetGlobalValue(KeywordMenu.AskedFlag(key)) ?? 0);
                // ONE action for both the pointer and the nav layer. Routing the nav layer through a
                // synthetic submit event instead does NOT invoke the button — keyboard and agent
                // activation silently did nothing until an in-Editor click test caught it.
                void Pick() {
                    _menuSound?.Play(BakAgain.Audio.MenuSoundService.PoundSoundId);
                    // The only flag this path writes, and the same one read back above to draw a
                    // topic as covered — the two ends of one mechanism.
                    _gameSession?.SetGlobalFlag(KeywordMenu.AskedFlag(key), true);
                    answer.Value = branchIndex;
                }
                _topicActions.Add(Pick);
                var button = new Button(Pick);
                button.AddToClassList("text-button");
                GameFontText.Caption(button, ResolveKeyword(keywords, key));
                if (asked) {
                    // *** An asked-about topic STAYS on the menu, still presses, and only its TEXT
                    // changes. *** The original builds it as widget type 8, which shares
                    // widget_draw_text_button with type 6 and differs only in the unpressed branch:
                    // text pen 0x0a -> 1, drop shadow dropped (WIDGET.C:373-376). Same chrome, same
                    // fill. The pressed branch does not read the type at all, which is what keeps it
                    // clickable — dropping the entry, or disabling it, would rewrite the
                    // conversation.
                    //
                    // Was an opacity of 0.6 standing in for "a different element kind"; the class
                    // carried no rule at all, so the inline style was the whole effect.
                    button.AddToClassList("keyword-asked");
                }
                Place(button, slot);
            }

            void Farewell() {
                _menuSound?.Play(BakAgain.Audio.MenuSoundService.PoundSoundId);
                answer.Value = -1;
            }
            _topicActions.Add(Farewell);
            var farewell = new Button(Farewell);
            farewell.AddToClassList("text-button");
            GameFontText.Caption(farewell, KeywordMenu.FarewellLabel);
            Place(farewell, farewellSlot);

            stage.Add(grid);
            SetChoiceFocus(0);

            var widgets = new System.Collections.Generic.List<BakAgain.UI.InputCore.NavWidget>();
            for (int i = 0; i < _choiceButtons.Count; i++) {
                int idx = i;
                Button b = _choiceButtons[i];
                b.RegisterCallback<FocusInEvent>(_ => SetChoiceFocus(idx));
                System.Action pick = _topicActions[i];
                // *** NO LABEL, SO NO FIRST-LETTER KEY — ON PURPOSE. *** The topic grid is
                // askabout_dialog_run -> menupage_run (ASKABOUT.C, flag 0x400), not the choice row's
                // askabout_menu_page_run_selection: it has no letter scan, and a key only matches an
                // entry's action id. Topics are 0x80+, which no key produces. Compared live at LaMut's
                // Blue Wheel Inn (TASK-538): S does nothing in either game.
                widgets.Add(new BakAgain.UI.InputCore.NavWidget(
                    b, null, () => ChoiceButtonCanonicalRect(b), () => pick(), null));
            }
            // Escape is scancode 1, which is GoodBye's action id, so it presses GoodBye: -1.
            var layer = new BakAgain.UI.InputCore.NavigableLayer(
                "dialog-keywords", BakAgain.UI.InputCore.CaptureMode.Exclusive, widgets,
                () => answer.Value = -1, _cursorManager);
            _stack?.Push(layer);
            _dialogLayer = layer;
            if (widgets.Count > 0) {
                layer.FocusIndex(0);
            }
        }

        // Whether a topic is offered. The general rule is the two flags KeywordAvailability models:
        // the topic's own value, and a suppression flag that has the LAST word.
        //
        // *** THE FIFTEEN HAND-WRITTEN GATES ARE NOT ANSWERED HERE, and that is deliberate. ***
        // They need plumbing this layer does not have — item counts, the chapter, a named
        // character's spellbook — and KeywordAvailability records them as data rather than
        // predicates for exactly that reason. Applying the general rule and logging is honest;
        // silently treating them as available would offer topics at the wrong times, which reads as
        // a content bug rather than a missing feature.
        /// <summary>
        /// Whether a topic is offered — the general two-flag rule plus its hand-written gate.
        /// </summary>
        /// <remarks>
        /// <b>The gates NARROW.</b> Twelve of the fifteen withdraw a topic whose extra condition is
        /// unmet, so leaving them unapplied — as this did until 2026-08-25 — OFFERS topics the
        /// original hides. The two redirects are the exception and replace the tested flag instead.
        ///
        /// <para><b>Item and spell gates are still unevaluable</b> and say so rather than guessing:
        /// their object ids are recorded by symbol name and never resolved against the object table.
        /// Those topics keep the old behaviour, which is the safe direction (offered, not hidden)
        /// and is logged.</para>
        /// </remarks>
        private bool TopicIsAvailable(int keywordKey) {
            int own = _gameSession?.GetGlobalValue(keywordKey) ?? 0;
            int suppressed = _gameSession?.GetGlobalValue(KeywordAvailability.SuppressedFlag(keywordKey)) ?? 0;

            KeywordAvailability.Decision decision = KeywordAvailability.Evaluate(
                keywordKey, own, suppressed,
                flagValue: key => _gameSession?.GetGlobalValue(key) ?? 0,
                chapter: _gameSession?.Chapter ?? 0,
                partyCarriesItem: PartyCarriesItem,
                spellsKnownWord: SpellsKnownWord,
                partyHasDamagedArmour: PartyHasDamagedArmour);

            if (decision.Unevaluated is { } gate) {
                _logger?.LogDebug(
                    "Keyword {Key}: the {Gate} gate needs data we do not have; general rule only.",
                    keywordKey, gate);
            }
            return decision.Available;
        }

        /// <summary>Every active member's pack. Empty when there is no session to ask.</summary>
        /// <remarks>
        /// <b>The gates ask about the PARTY, not the speaker.</b> The Waani, rations and Abbot's
        /// Journal topics turn on whether anyone is carrying the thing, so a per-character reading
        /// would hide a topic whenever the wrong member was displayed.
        /// </remarks>
        private IEnumerable<RuntimeContainer> PartyPacks() =>
            _gameSession?.ActivePartyPacks ?? System.Linq.Enumerable.Empty<RuntimeContainer>();

        /// <summary>
        /// Whether the party carries an object — and, as a side effect, who has it.
        /// </summary>
        /// <remarks>
        /// <b>Through the global, not through the packs.</b> The original asks these gates with
        /// <c>itemtbl_partySize_by_kind</c> (ASKABOUT.C:196, 199, 231, 235), which WRITES
        /// <c>nEvtArgActor0</c> on its way past — so merely opening a topic menu re-points the
        /// actor global at somebody holding the thing. <see cref="InventoryQuery.AnyHolds"/> could
        /// not do that and said so in its own remarks; asking global 50000+id does, because
        /// <c>GameSession</c> answers it with the party-wide count.
        ///
        /// <para>It is not a detail. Limm's hub seeds slot 0 from the actor global, and the seal it
        /// hands over goes to whoever that slot names.</para>
        /// </remarks>
        private bool PartyCarriesItem(int objectId) =>
            (_gameSession?.GetGlobalValue(
                GameData.Resources.Dialog.DialogBranchWalker.ItemCountGlobalBase + objectId) ?? 0) > 0;

        /// <summary>
        /// Whether anyone is carrying armour below full condition.
        /// </summary>
        /// <remarks>
        /// Equipped or not — see <see cref="InventoryQuery.CountNeedingRepair"/>. It is why an
        /// armourer has something to say, and why the topic goes away once it is mended.
        /// </remarks>
        private bool PartyHasDamagedArmour() =>
            InventoryQuery.CountNeedingRepair(PartyPacks(), _gameSession?.ObjectInfo) > 0;

        /// <summary>
        /// One word of Owyn's spellbook, by ZERO-BASED index.
        /// </summary>
        /// <remarks>
        /// <b>The character is fixed in the original's code, not carried by the case</b> — both
        /// spell gates read <c>characters[CHR_OWYN].spellsKnown[n]</c>, so
        /// <see cref="KeywordAvailability.SpellGateCharacter"/> is who to ask and the case's first
        /// field is the WORD. Asking the case for a character id reads the wrong table.
        /// </remarks>
        private int SpellsKnownWord(int wordIndex) {
            ushort[] words = _gameSession?.KnownSpellsOf(KeywordAvailability.SpellGateCharacter);
            return words != null && wordIndex >= 0 && wordIndex < words.Length
                ? words[wordIndex]
                : 0;
        }

        // The keyword a branch names, if it names one.
        private static int? KeywordKeyOf(DialogBranchBase branch) => branch switch {
            KeywordChoiceBranch keyword => keyword.Keyword,
            ConditionalBranch cb when cb.Condition is FlagCondition fc => fc.Flag,
            _ => null,
        };

        // Resolve a choice button's label. Only branches that reference a
        // keyword carry one: KeywordChoiceBranch (true menu options) and the
        // Yes/No confirm dialogs, whose ConditionalBranch carries a
        // FlagCondition whose Flag doubles as the keyword index (256 -> "Yes",
        // 257 -> "No"). Any other branch type has no label and falls back to
        // its 1-based position.
        private static string ResolveChoiceLabel(KeywordList keywords, DialogBranchBase branch, int index) {
            return KeywordKeyOf(branch) is { } key
                ? ResolveKeyword(keywords, key)
                : (index + 1).ToString();
        }

        // A keyword index K labels its button with keyword table entry K-1 (the
        // table is 0-indexed; the documented "Key" column is 1-based, so 256 ->
        // "Yes"). Falls back to the numeric key if absent.
        private static string ResolveKeyword(KeywordList keywords, int keywordKey) {
            if (keywords?.Keywords != null
                && keywords.Keywords.TryGetValue(keywordKey - 1, out string text)
                && !string.IsNullOrEmpty(text)) {
                return text;
            }
            return keywordKey.ToString();
        }

        public UniTask ShowEntry(DialogEntry entry, CancellationToken cancellationToken = default) => Playing(() => ShowEntryOnce(entry, cancellationToken), "ShowEntry " + entry?.Id);

        public UniTask ShowEntry(DialogEntry entry, System.Action<VisualElement, LayoutHint> decorate,
            CancellationToken cancellationToken = default) =>
            Playing(async () => {
                if (!TryEnsureReady() || entry == null) {
                    return;
                }
                await ShowEntryCore(entry, play: null, waitForInput: true,
                    renderChoices: entry.Branches is { Count: > 0 }, cancellationToken, decorate);
            }, "ShowEntry(decorated) " + entry?.Id);

        private async UniTask ShowEntryOnce(DialogEntry entry, CancellationToken cancellationToken = default) {
            if (!TryEnsureReady()) {
                return;
            }
            if (entry == null) {
                _logger.LogWarning("ShowEntry called with null entry — skipping");
                return;
            }
            // *** THE ENTRY'S OWN FLAG DECIDES, THE SAME RULE THE WALK USES. *** ShowById's loop
            // reads `isConfirm` off TextWithChoice (0x200) and renders the branch row from it; a
            // caller handing the same entry straight to ShowEntry used to get a bare
            // click-anywhere wait, so the buttons the record carries silently disappeared. That is
            // what made the combat assessment dismiss on any key where the original draws Accept
            // (TASK-598). Nothing else changes: an entry without the flag has no row to draw.
            await ShowEntryCore(entry, play: null, waitForInput: true,
                renderChoices: (entry.Flags & DialogEntryFlags.TextWithChoice) != 0,
                cancellationToken);
        }

        public UniTask ShowEntry(DialogPlay play, CancellationToken cancellationToken = default) => Playing(() => ShowEntryOnce(play, cancellationToken), "ShowEntry " + play?.Entry?.Id);

        private async UniTask ShowEntryOnce(DialogPlay play, CancellationToken cancellationToken = default) {
            if (!TryEnsureReady()) {
                return;
            }
            if (play?.Entry == null) {
                _logger.LogWarning("ShowEntry called with null play — skipping");
                return;
            }
            await ShowEntryCore(play.Entry, play, waitForInput: true, renderChoices: false, cancellationToken);
        }

        public async UniTask DisplayEntry(DialogEntry entry, CancellationToken cancellationToken = default) {
            if (!TryEnsureReady()) {
                return;
            }
            if (entry == null) {
                _logger.LogWarning("DisplayEntry called with null entry — skipping");
                return;
            }
            // Fire-and-forget render: the panel stays up after the await
            // completes; the caller disposes via ClearDialog() (or by issuing
            // a subsequent show call).
            await ShowEntryCore(entry, play: null, waitForInput: false, renderChoices: false, cancellationToken);
        }

        public async UniTask DisplayEntry(DialogPlay play, CancellationToken cancellationToken = default) {
            if (!TryEnsureReady()) {
                return;
            }
            if (play?.Entry == null) {
                _logger.LogWarning("DisplayEntry called with null play — skipping");
                return;
            }
            await ShowEntryCore(play.Entry, play, waitForInput: false, renderChoices: false, cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<bool> ShowAcceptOrCancelById(int id, CancellationToken cancellationToken = default) {
            if (!TryEnsureReady()) {
                return false;
            }
            DialogPlay play = await ResolveById(id, cancellationToken);
            if (play?.Entry == null) {
                return false;
            }
            await ShowEntryCore(play.Entry, play, waitForInput: false, renderChoices: false, cancellationToken);
            VisualElement scrim = EnsureModalScrim(_rootDocument.rootVisualElement, onTop: true);
            bool? answer = null;
            var layer = new BakAgain.UI.InputCore.ActionLayer(LayerIdPrefix + "accept-cancel",
                () => answer = true, () => answer = false, onMove: _ => { });
            void OnPointerUp(PointerUpEvent evt) => answer = evt.button == 0;
            _stack?.Push(layer);
            scrim.RegisterCallback<PointerUpEvent>(OnPointerUp);
            try {
                await UniTask.Yield(PlayerLoopTiming.Update); // not the click that opened it
                answer = null;
                while (answer == null && !cancellationToken.IsCancellationRequested && scrim.panel != null) {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            } finally {
                scrim.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                _stack?.Remove(layer);
                ClearDialog();
            }
            return answer == true;
        }

        public void ClearDialog() {
            Deactivate();
        }

        public void LetClicksThroughPanel() {
            if (_activePanel == null) {
                return;
            }
            _activePanel.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in _activePanel.Query<VisualElement>().ToList()) {
                child.pickingMode = PickingMode.Ignore;
            }
        }

        /// <summary>
        /// Build <paramref name="entry"/>'s chrome, at <paramref name="entry"/>'s area, as a child of
        /// <paramref name="host"/> — no text. See <see cref="IDialogManager.BuildStyledBoxAsync"/> for
        /// why a caller wanting a styled box it draws into must build it in its OWN tree rather than
        /// show it through this overlay.
        /// </summary>
        public async UniTask<VisualElement> BuildStyledBoxAsync(DialogEntry entry, VisualElement host) {
            if (entry == null || host == null) {
                return null;
            }
            // This entry point builds into the CALLER's tree, so it has no UIDocument requirement
            // and deliberately does not gate on TryEnsureReady — but it does need _resources and
            // _logger, which the lazy initializer creates.
            EnsureInitialized();
            // Same resolution chain a shown dialog goes through, so the box is indistinguishable
            // from the panel it stands in for.
            int effectiveStyleId = DialogTypeResolver.ResolveEffectiveStyleId(DialogContext.None, entry);
            // The table comes from the resource system (DIALSTYL.DAT), not from a static array —
            // that is what lets a mod override reach it. See DialogResourceLoader.GetStyleTableAsync.
            DialogStyleTable styleTable = await _resources.GetStyleTableAsync();
            DialogStyle style = styleTable.Get(effectiveStyleId);
            LayoutHint area = ResolveArea(entry, style.DefaultArea);

            Color[] palette = _activePalette;
            if (palette == null || palette.Length == 0) {
                palette = await _resources.GetDefaultPaletteAsync();
            }

            var box = new VisualElement {
                name = "BakStyledBox",
                pickingMode = PickingMode.Ignore, // decoration: never steals a click from the host
            };
            // Same single translator every other screen's layout goes through, so a styled box
            // and the panel it stands in for are placed by identical rules.
            LayoutApplier.Apply(box, area);
            // The intra-panel geometry (chrome edge widths here) rides on the same resource as the
            // rows, so a mod author's DIALSTYL.json reaches the styled box too.
            DialogPanelBuilder.BuildChrome(box, style, styleTable.Layout, palette, area: area);
            host.Add(box);
            _logger.LogDebug(
                "Built styled box from entry id={EntryId} styleId={StyleId} area=({L},{T},{W},{H})",
                entry.Id, effectiveStyleId, area.Left, area.Top, area.Width, area.Height);
            return box;
        }

        /// <summary>Name of the vine layer, so a re-render replaces it rather than stacking.</summary>
        private const string VineLayerName = "BakDialogVines";

        /// <summary>
        /// The two vine corner pieces the full-screen dialog is framed with.
        /// </summary>
        /// <remarks>
        /// One sprite drawn twice, the second turned 180° — see
        /// <see cref="GameData.Resources.Dialog.DialogVineCorners"/> for the placements and why the
        /// coordinates are screen-absolute. Only style 6 gets them; every other style returns
        /// before the branch in the original.
        /// </remarks>
        private void AddVineCorners(VisualElement stage, int effectiveStyleId) {
            stage.Q<VisualElement>(VineLayerName)?.RemoveFromHierarchy();
            if (!GameData.Resources.Dialog.DialogVineCorners.DecoratesStyle(effectiveStyleId)) {
                return;
            }

            var layer = new VisualElement {
                name = VineLayerName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            stage.Add(layer);
            AddVineSpritesAsync(layer).Forget();
        }

        private async UniTaskVoid AddVineSpritesAsync(VisualElement layer) {
            Sprite sprite = await _resources.GetSpriteAsync(
                GameData.Resources.Dialog.DialogVineCorners.IconSet + "#"
                + GameData.Resources.Dialog.DialogVineCorners.ImageIndex);
            if (sprite == null || layer.panel == null) {
                return;
            }

            foreach (GameData.Resources.Dialog.DialogVineCorners.Placement at
                     in GameData.Resources.Dialog.DialogVineCorners.Placements) {
                var piece = new VisualElement {
                    pickingMode = PickingMode.Ignore,
                    style = {
                        position = Position.Absolute,
                        left = at.X, top = at.Y,
                        width = sprite.rect.width, height = sprite.rect.height,
                        backgroundImage = new StyleBackground(sprite),
                    },
                };
                if (at.Rotated) {
                    // bitmapFlags 3 is both axes, which is a 180 degree turn — a single-axis
                    // mirror would put the vine's stem on the wrong side.
                    piece.style.rotate = new StyleRotate(new Rotate(180f));
                }
                layer.Add(piece);
            }
        }

        // Single merged dialog flow. <paramref name="waitForInput"/> false →
        // render and return immediately (panel stays up for the caller to
        // dispose). <paramref name="renderChoices"/> true → render one button
        // per branch (confirm path) and resolve with the selected index; false
        // → narrative dismiss-on-click and resolve with -1.
        private async UniTask<int> ShowEntryCore(
            DialogEntry entry, DialogPlay play, bool waitForInput, bool renderChoices,
            CancellationToken cancellationToken, System.Action<VisualElement, LayoutHint> decorate = null) {
            // Tear down any previous panel before showing the new one — the
            // cutscene path can fire a clear + show in the same frame, and the
            // in-game path expects modal one-at-a-time semantics.
            RemovePanel(mine: null);

            // The layer THIS call pushes, so its teardown removes its own and not a concurrent
            // show's — see RemovePanel. Declared out here so the catch below can reach it too.
            BakAgain.UI.InputCore.IInputLayer mine = null;

            // BEFORE anything reads a speaker, exactly as the original rewrites wSpeaker_id at the
            // top of its record loop. Everything downstream — portrait, name plate, camera turn —
            // uses this and never entry.ActorNumber.
            _speakerActor = _speakers.Resolve(entry?.ActorNumber ?? 0, ChapterSpeakerId,
                play?.Slots?.Kinds);
            // The one write the ladder makes: a 0xf0..0xfc speaker becomes the primary actor
            // (nEvtArgActor0, DIALOG.C:888), which is who a later bare '@' or kind-10 slot names.
            // Mirrored onto the play's context rather than global 30004, which this port exposes
            // read-only.
            if (_speakers.NewPrimaryActor is int primaryActor) {
                if (play?.Context != null) {
                    play.Context.PrimaryActorId = primaryActor;
                }
                // The original writes the GLOBAL, not a per-play copy:
                // `g_gameState.nEvtArgActor0 = g_speaker_kinds[idx]` (DIALOG.C:889). Mirroring it
                // onto the session is what lets a LATER dialog's slot 0 see who this one was about.
                if (_gameSession != null) {
                    _gameSession.EventActor = primaryActor;
                }
            }

            gameObject.SetActive(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // The dialog overlay is one of the few documents that goes up over whatever is already
            // showing, so this is where a same-tier collision can first exist.
            UiSortTierCheck.WarnOnConflicts("showing a dialog");
#endif
            try {
                // DialogContext flags (in-game / full-screen overrides) are not
                // currently wired through the manager — every callsite used
                // DialogContext.None. If/when those globals get mirrored, hold
                // them in a service the manager reads, not as a per-call arg.
                int effectiveStyleId = DialogTypeResolver.ResolveEffectiveStyleId(DialogContext.None, entry);
                // Style rows come from the DIALSTYL.DAT resource, so an override document placed
                // in the mod directory moves/restyles this panel without a recompile.
                DialogStyleTable styleTable = await _resources.GetStyleTableAsync();
                DialogStyle style = styleTable.Get(effectiveStyleId);
                LayoutHint area = ResolveArea(entry, style.DefaultArea);

                _logger.LogDebug(
                    "Showing dialog entry id={EntryId} sourceType={SourceType} effectiveStyleId={EffectiveStyleId} area=({L},{T},{W},{H})",
                    entry.Id, entry.DialogType, effectiveStyleId,
                    area.Left, area.Top, area.Width, area.Height);

                VisualElement root = _rootDocument.rootVisualElement;
                if (root == null) {
                    _logger.LogError("Root visual element is null on the DialogOverlay UIDocument; cannot render dialog.");
                    return -1;
                }

                // The default theme USS assigns the root container an *opaque
                // black* backgroundColor. Force it transparent so the dialog
                // panel composites onto the cutscene below.
                root.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
                // Host the dialog panel in the centered canonical stage so its
                // absolute 1600×1200-space coordinates land in the pillarboxed
                // 4:3 region rather than the panel's left edge (the panel is
                // wider than 1600 on non-4:3 windows). GetOrCreate also takes
                // the root out of the shared panel flow (absolute + full inset),
                // which is what used to prevent the "background pushed up" bug
                // when this document shares its panel with the MainMenu.
                // The frame comes from the SAME resource as the box geometry it resolves: the
                // style table's rows say where the box sits and its Layout says what sits inside
                // it, both in design-frame px, and styleTable.Frame is the space those px are in.
                // (This used to pass null, i.e. the canonical fallback, which produced the right
                // box only because the fallback happens to be the canonical frame.)
                //
                // Fit = Fill IS authorable as of phase 5 (2026-08-05). An override document that
                // says {"Frame":{"Fit":"Fill"}} and restates a style row's DefaultArea in
                // percentages makes the dialog surface span the whole window and reflow with the
                // aspect. Proven end-to-end against the running game, not just in tests: with row 2
                // authored as 4.0625%/5.5%/91.875%/50.5%, the panel resolved to 1960×606.67 logical
                // (1764 physical px = 91.875% of the window) at 1920×1080, against 1470 logical
                // (1323 px = 68.906%) for the shipped Contain data at the same window — and to
                // exactly the shipped 65/66/1470/606 at 4:3, so the document is a pixel no-op there.
                // The four things that used to block it are fixed: ResizeDialogAction now speaks
                // LayoutLength, the open wipe grows from the panel's resolved rect (see
                // WaitForResolvedRect below), CursorManager resolves the topmost stage by paint
                // order, and WorldViewport maps through CanonicalStage.ScreenRect.
                //
                // Contain is unchanged on screen throughout — with ONE deliberate correction, worth
                // stating rather than glossing: WorldViewport's world-picking rect now differs below
                // 4:3, where the deleted min() math had invented a vertical letterbox CanonicalStage
                // never draws (see WorldViewport.ToScreenRect). Nothing visible moves; a latent
                // hit-test error at sub-4:3 aspects is fixed.
                //
                // Two limits a mod author still hits, both measured 2026-08-05 (backlog task-64):
                //
                //   1. A ResizeDialog in the DDX entry still wins. It is an absolute replacement of
                //      the style's area, and although the action now HAS a percentage vocabulary the
                //      extractor only ever emits px (the binary holds raw VGA ushorts), so the 548
                //      of 5,932 text-bearing entries that carry one stay pinned to a fixed px rect
                //      no matter what DIALSTYL.json says. Reflowing those needs a DDX override too.
                //      Live example: DDX 111's confirm resolved to 185px/384px/1215px/432px on a
                //      Fill stage.
                //   2. The open wipe plays for a percentage area, but not un-scaled. PlayAsync
                //      reparents the panel into the growing clip mask, so a percentage width/height
                //      re-resolves against the mask and the box grows with it instead of being
                //      revealed at full size; the text re-wraps for the 0.18 s of the reveal and
                //      snaps to the right geometry at the end. A px area is unaffected (the panel
                //      holds 1470×725.6 throughout while the mask grows), so no shipped dialog moves.
                VisualElement stage = CanonicalStage.GetOrCreate(root, styleTable.Frame);
                EnsureOverlayIsClickThrough(root, stage);
                // *** BEFORE THE FACE AND THE PANEL. *** The parchment is a screen-filling blit in
                // the original (ExecuteDialog @0x499dc-0x49a08 reads Dialog.scr and copies the whole
                // 320x200), so everything the dialog draws goes on top of it. Added first so the
                // face, the vines and the panel all land above.
                // Resolve the palette text pens index into. Use whatever the
                // active environment set via SetActivePalette (cutscene path);
                // fall back to OPTIONS.PAL when none is set (in-game / menu).
                Color[] palette = _activePalette;
                if (palette == null || palette.Length == 0) {
                    palette = await _resources.GetDefaultPaletteAsync();
                }

                await AddFullScreenBackdropAsync(stage, entry, palette);
                TurnCameraForBackdrop(entry);
                ShowSpeakerFace(stage, entry);

                // Resolve @N text variables (RenderDialogText @0x48d7b) before rendering. Computed
                // into a local rather than mutated onto entry.Text — LoadDialogAsync caches the
                // Dialog (and its DialogEntry instances), so mutating the cached entry would corrupt
                // it for the next show (double-substitution / stale party names).
                string resolved = entry.Text;
                if (!string.IsNullOrEmpty(entry.Text)) {
                    // A resolved play already carries its slots, written by EVERY entry the branch
                    // walk touched — re-seeding here would drop the text-less routers' writes AND
                    // re-roll the random companions. An entry handed to us directly (the cutscene
                    // path, ShowEntry/DisplayEntry) has no walk behind it, so it seeds its own.
                    DialogSlotTable slots = play?.Slots;
                    DialogSlotContext slotContext = play?.Context;
                    if (slots == null) {
                        slotContext = BakAgain.Core.Services.DialogSlotContextFactory.FromSession(_gameSession);
                        slots = DialogSlotPopulator.CreateForPlay(slotContext);
                        DialogSlotPopulator.ApplyEntryActions(slots, entry, slotContext);
                    }
                    resolved = TextVariableResolver.Substitute(entry.Text, slots,
                        slotContext.NameOf(slotContext.CurrentActorId));
                }

                _activeArea = area;
                _activePanel = DialogPanelBuilder.BuildPanel(entry, style, styleTable.Layout, palette, resolved, area);

                // Confirm / choice mode: one button per branch, labelled from
                // KEYWORD.DAT via the branch's keyword index. These are the
                // only way to dismiss the dialog (no background-click escape).
                // *** The entry's OWN flag decides the shape of the menu. *** renderChoices is the
                // caller's request for "a menu"; ChoiceMenu (0x0400) is the data saying that menu is
                // an ask-about topic grid rather than a Yes/No row. Before this, an entry carrying
                // the flag rendered as ordinary text with no topic list at all.
                bool keywordMode = (entry.Flags & DialogEntryFlags.ChoiceMenu) != 0;
                KeywordList pendingKeywords = null;
                bool choiceMode = (renderChoices || keywordMode) && waitForInput;
                if (choiceMode) {
                    // Modal scrim BEHIND the buttons: swallows clicks that miss a
                    // button so they can't fall through to the screen beneath,
                    // while the Yes/No buttons stay clickable on top.
                    EnsureModalScrim(root, onTop: false);
                    KeywordList keywords = await _resources.GetKeywordsAsync();
                    if (keywordMode) {
                        // Deferred until after the panel is on the stage — see below.
                        pendingKeywords = keywords;
                    } else {
                        AddConfirmButtons(_activePanel, entry.Branches, keywords,
                            partyPicker: (entry.Flags & DialogEntryFlags.PartyMemberSelection) != 0,
                            slots: play?.Slots, slotContext: play?.Context);
                    }
                }

                // Vines BEFORE the panel, so the panel's text draws over them — the original's
                // order too (dialog_DrawChrome runs before RenderDialogText). They go on the stage
                // rather than in the panel because their coordinates are screen positions and one
                // of them is deliberately negative; inside the panel that overhang would clip.
                AddVineCorners(stage, effectiveStyleId);
                stage.Add(_activePanel);
                // *** The topic grid goes on the STAGE, and AFTER the panel. *** Its slot rects are
                // screen positions, not panel-relative — the same reason the vines are on the stage —
                // so putting it inside the panel would offset every topic by the panel's origin. And
                // it has to be added after the panel or the panel paints over the buttons.
                if (pendingKeywords != null) {
                    AddKeywordGrid(stage, entry.Branches, pendingKeywords);
                }
                // *** AFTER the panel, like the original. *** dialog_draw_speech_bubble is the last
                // thing ExecuteDialog draws, and the plate straddles the seam between the world
                // viewport and the panel — drawn any earlier the panel paints over its lower half.
                await ShowSpeakerNamePillAsync(stage, entry, palette, keywordGrid: pendingKeywords != null);
                // Place the panel at its absolute canonical rect and size the
                // label fonts. Static in canonical units — no resize tracking.
                ApplyCanonicalLayout();
                if (decorate != null && _activePanel != null) {
                    decorate(_activePanel, area);
                }

                // Faithful open-wipe: reveal the laid-out panel centre-out (the original's box-out,
                // anim_screenTransitionEffect @ 0x53ab5), unless the entry opts out via SkipOpenWipe.
                // The wipe wants the panel's RECT, not its LayoutHint — it only ever reached for the
                // hint because it ran before layout. Waiting one layout tick and reading the panel's
                // resolved rect instead means px and percent areas both wipe correctly (the
                // percent-refusal warning this used to log on ~5,896 of 5,932 dialogs is gone).
                // Cancel-safe: snaps to the final laid-out state on teardown. Close stays instant.
                // The `_activePanel != null` is the same hazard one await earlier:
                // ShowSpeakerNamePillAsync above awaits too, so the panel can already be gone
                // before the capture below ever reads it.
                if (DialogOpenWipe.ShouldPlay(entry.Flags) && _activePanel != null) {
                    // Hidden BEFORE the await, cleared inside DialogOpenWipe's own SetStep(0f).
                    //
                    // Reading the panel's resolved rect costs at least one UniTask.Yield for a
                    // freshly built panel (IsResolved(panel.layout) is false by construction), so
                    // frame N now lays the panel out, fires its GeometryChangedEvent AND REPAINTS
                    // IT AT FULL SIZE, with the mask only arriving in frame N+1. That is a one-frame
                    // full-size pop-in before the collapse-and-reveal, on the 5,896 of 5,932
                    // text-bearing entries that play the wipe — a rendered-game difference on the
                    // shipped path, which this phase does not get to make. (The old px-only code had
                    // no window: its whole prologue, mask included, ran synchronously in frame N.)
                    //
                    // visibility (not display) so the panel still lays out and still raises the
                    // GeometryChangedEvent this is waiting for — display:None would deadlock the
                    // wait it is meant to cover. Hidden here rather than "SetStep(0f) against the
                    // declared rect first" because the declared rect is exactly what this whole
                    // change stopped trusting: an override's area may be a percentage, which has no
                    // px value to build a mask from until layout resolves it. Hiding needs no
                    // geometry at all, so it is correct for both unit vocabularies.
                    //
                    // *** AND THE PANEL IS A MUTABLE FIELD ACROSS THESE AWAITS. *** `_activePanel`
                    // is nulled by RemovePanel, which a concurrent show or teardown calls while the
                    // geometry wait below is in flight — the shipped case the finally names, a
                    // town's fire-and-forget dialog against the location's own scene description.
                    // Re-reading the field after the await handed `null` to PlayAsync, which
                    // dereferences it unguarded and threw: 18 "threw while rendering" in one 20 h
                    // log, and the dialog never appeared (TASK-578).
                    //
                    // Captured here, per call, for the same reason `mine = _dialogLayer` is captured
                    // further down — and compared afterwards rather than merely null-checked,
                    // because the field can also be REPLACED. A panel that is no longer the active
                    // one has been superseded by a dialog that owns the screen now, so the right
                    // outcome is to skip the wipe, not to wipe a detached element.
                    VisualElement wipePanel = _activePanel;
                    wipePanel.style.visibility = Visibility.Hidden;
                    try {
                        Rect resolvedRect = await WaitForResolvedRect(wipePanel, cancellationToken);
                        if (resolvedRect.width > 0f && resolvedRect.height > 0f
                            && ReferenceEquals(wipePanel, _activePanel)) {
                            // A full-screen dialog irises open across the WHOLE screen, uncovering
                            // the parchment and its vines rather than appearing on top of them
                            // already-drawn: ExecuteDialog nulls the entry pointer at 0x49c58 for
                            // dialogType 6 so the wipe covers (0,0,320,200). Every other dialog
                            // wipes its own panel rect, which is the `null` case below.
                            Rect? whole = null;
                            System.Collections.Generic.List<VisualElement> alsoReveal = null;
                            if (DialogOpenWipe.WipesWholeScreen(entry.DialogType)
                                && CanonicalStage.TryGetFrame(stage, out DesignFrame wipeFrame, out _)
                                && wipeFrame.Width > 0 && wipeFrame.Height > 0) {
                                whole = new Rect(0f, 0f, wipeFrame.Width, wipeFrame.Height);
                                alsoReveal = new System.Collections.Generic.List<VisualElement>();
                                VisualElement backdrop = stage.Q<VisualElement>("BakDialogBackdrop");
                                if (backdrop != null) {
                                    alsoReveal.Add(backdrop);
                                }
                                VisualElement vines = stage.Q<VisualElement>(VineLayerName);
                                if (vines != null) {
                                    alsoReveal.Add(vines);
                                }
                            }
                            await DialogOpenWipe.PlayAsync(stage, wipePanel, _activeArea, resolvedRect,
                                OpenWipeDurationSeconds, cancellationToken, whole, alsoReveal);
                        }
                    } finally {
                        // The panel must never be left hidden by a path that skipped the wipe: the
                        // geometry wait timing out, cancellation, or a zero-size rect. PlayAsync
                        // already cleared it on the normal path; clearing again is a no-op.
                        //
                        // *** AND THE PANEL CAN BE GONE BY NOW. *** The open wipe awaits, and a
                        // concurrent show or teardown calls RemovePanel() — which nulls this field —
                        // while it does. Entering a town is the shipped case: the town dialog is
                        // started fire-and-forget and the location then plays its own scene
                        // description, whose ShowEntryCore begins by removing the panel in flight.
                        // Dereferencing here threw a NullReferenceException that reached nobody but
                        // the unobserved-task log (TASK-352).
                        //
                        // Un-hides THE PANEL THIS CALL HID, which is what the captured local is for.
                        // Reading the field here instead un-hid whatever is active NOW — a different
                        // panel on the replacement path, which was never hidden, while the one this
                        // call hid stayed hidden. Clearing visibility on a detached element is a
                        // no-op, so this needs no guard; leaving a live panel hidden is an invisible
                        // dialog, which is the failure it exists to prevent.
                        wipePanel.style.visibility = StyleKeyword.Null;
                    }
                }

                // Fire-and-forget path leaves the panel up — caller disposes
                // via ClearDialog() (or by issuing a subsequent show call).
                if (!waitForInput) {
                    return -1;
                }

                // The dialog now owns input structurally: AddConfirmButtons (choice) pushed an
                // Exclusive NavigableLayer; the narrative branch pushes an Exclusive ActionLayer
                // below. The menu beneath is blocked by the stack — no ModalActive flag.

                // Wait for either a button click (choice mode) or any-input
                // dismiss (narrative mode). Either way, tear down the panel
                // before returning.
                //
                // *** THIS SHOW REMOVES THIS SHOW'S LAYER. *** `_dialogLayer` is a single field
                // and shows overlap — the comment on the open wipe above names the shipped case,
                // a town's fire-and-forget dialog against the location's own scene description.
                // Reading the field at teardown time removed whichever layer the LAST show pushed
                // and nulled it, so the other one removed nothing and left an Exclusive layer on
                // the stack with no panel behind it: every Activate consumed, nothing popped, the
                // game unplayable. Captured here, per call, for the same reason a coroutine keeps
                // its own locals.
                mine = _dialogLayer;
                // *** A TORN-DOWN PANEL MUST END ITS OWN WAIT. *** RemovePanel detaches the panel
                // and the scrim and replaces the answer box, and neither wait below can observe any of
                // that: the choice loop waits for a field that was just set back to null, and
                // WaitForDismiss waits for a pointer event a detached scrim can never receive. So a
                // panel taken down by anything other than its own dismissal -- a navigator pop, the
                // camp screen opening -- leaves its awaiter suspended for the rest of the session.
                // Measured 2026-09-16: PartyUpkeepService._announcing stuck true with dialog 0x40
                // queued, which wedges camping because CampMenu.RestAsync's finally owns
                // LastRestTicks and RestQuality (TASK-563).
                //
                // Captured PER SHOW, exactly like `mine` above and for the same reason: shows
                // overlap, so a shared counter would let an inner show's teardown end the outer
                // show's wait. A detached VisualElement reports a null `panel`, which is the one
                // signal that survives the teardown without any new shared state.
                VisualElement shown = _activePanel;
                bool keepPanel = false;
                try {
                    if (choiceMode) {
                        // *** THE TEXT IS PAGED BEFORE THE MENU APPEARS. *** DIALOG.C:1343-1356 is
                        // the narrative paging loop with the choice menu after it, so a long
                        // ask-about or Yes/No record is read a page at a time and only the last one
                        // offers the branches.
                        await PageBeforeChoiceAsync(root, stage, cancellationToken);

                        // Resolution comes from the pushed dialog layer (Enter/Esc/first-letter via the
                        // InputAdapter) or a button click — both write this show's answer box. No
                        // per-frame keyboard poll; the overlay GameObject can stay inactive.
                        //
                        // *** CAPTURED AFTER THE PAGING, AND POLLED BY REFERENCE. *** The box is
                        // whichever one this show's own AddConfirmButtons/AddKeywordGrid installed;
                        // a later menu installs its own and leaves this one unreachable. That is
                        // what stops a wait which outlived its dialog from being completed by an
                        // unrelated answer — it used to poll a shared field, so the next dialog
                        // answered with its first button set it to 0 and finished this wait too,
                        // and CrossZoneAsync read that 0 as "cross" (TASK-556).
                        ChoiceAnswer answered = _choiceResult;
                        while (answered.Value == null && !cancellationToken.IsCancellationRequested
                            && (shown == null || shown.panel != null)) {
                            await UniTask.Yield();
                        }
                        return answered.Value ?? -1;
                    }
                    // Narrative / tooltip: a full-screen scrim ON TOP owns every
                    // click; clicking it dismisses (on release) without the click
                    // reaching the button beneath that opened the dialog. Keyboard/gamepad dismiss
                    // arrives as an Activate/Cancel intent on the pushed Exclusive ActionLayer.
                    VisualElement scrim = EnsureModalScrim(root, onTop: true);
                    bool dismissed = false;
                    // *** ANY KEY ADVANCES A DIALOG, EXCEPT THE FOUR ARROWS. ***
                    // `dialog_poll_arrow_or_button` (DIALOG.C:161-171) returns the scancode for
                    // every key it reads and only DISCARDS 0x48/0x50/0x4b/0x4d — so Y, A and the
                    // space bar all turn the page in the original, and the arrows do not. Measured
                    // 2026-09-13: Enter advanced here and every other key did nothing (TASK-390).
                    // `onMove` swallows the arrows, `anyIntentActivates` takes everything else.
                    var narrativeLayer = new BakAgain.UI.InputCore.ActionLayer(
                        "dialog-narrative", () => dismissed = true, () => dismissed = true,
                        onMove: _ => { }, anyIntentActivates: true);
                    _stack?.Push(narrativeLayer);
                    _dialogLayer = narrativeLayer;
                    mine = narrativeLayer;

                    // *** A LONG RECORD IS PAGED, NOT CLIPPED. *** DIALOG.C:1361-1384: the wait
                    // returns, `i += g_wTextWrapLinesDrawn`, the frame is redrawn and the body
                    // re-rendered from line `i`, and only the page with nothing remaining closes
                    // the panel. Same input either way — a page turn IS a dismiss that did not
                    // land. Without this the tail of a 467-character record simply vanished under
                    // the panel's `overflow: hidden` (TASK-389); the original shows it as page 2.
                    GameTextBlock body = _activePanel?.Q<GameTextBlock>("BakDialogBody");
                    if (body != null) {
                        body.Paginate = true;
                        // One frame, because the first page count comes from the block's RESOLVED
                        // geometry: turning pagination on re-flows immediately, but if the panel's
                        // layout has not settled that pass bails out and leaves LinesRemaining at
                        // the pre-pagination 0 — which reads as "one page" and clips exactly as
                        // before.
                        await UniTask.Yield(PlayerLoopTiming.Update);
                    }
                    while (true) {
                        // *** AND THE TIMEOUT IS SUPPRESSED WHILE LINES REMAIN. *** The wait is
                        // called with the record's flags replaced by 0 whenever
                        // g_wTextWrapLinesRemaining != 0, and `wFlags &= ~0x40` kills auto-dismiss
                        // for the whole record up front. A port that kept its text-speed deadline
                        // running would flick through the pages by itself.
                        bool more = body != null && body.LinesRemaining > 0;
                        // *** SkipWait (0x4000) SKIPS ONLY THE LAST PAGE'S WAIT. *** The record's flags
                        // reach dialog_wait_for_acknowledge only once no wrapped lines remain, and
                        // there `if (flags & 0x4000) return 1;` moves on with the page still drawn.
                        // Measured at LaMut's Blue Wheel Inn: "The room was cramped…" waited for a key,
                        // and its last page, "In moments, they were all fast asleep…", went straight
                        // into the night and stayed up over it (TASK-496).
                        if (!more && (entry.Flags & DialogEntryFlags.SkipWait) != 0) {
                            keepPanel = true;
                            break;
                        }
                        dismissed = false;
                        await WaitForDismiss(scrim, () => dismissed, cancellationToken,
                            more ? null : AutoDismissSecondsFor(entry));
                        if (!more) {
                            break;
                        }
                        body.AdvancePage();
                    }
                    return -1;
                } finally {
                    if (keepPanel) {
                        ReleaseInputKeepingPanel(mine);
                    } else {
                        Deactivate(mine);
                    }
                }
            } catch (System.OperationCanceledException) {
                // A skipped cutscene cancels the dialog it is showing. That is not a fault, and
                // logging it as one put a false error in every sweep of the cutscenes.
                Deactivate(mine);
                throw;
            } catch (System.Exception e) {
                // *** THE RETHROW ALONE LOSES THE ORIGIN. *** Most callers reach this through
                // .Forget() (HotspotService.PlayDialog, the cutscene paths), so the exception ends
                // up in UniTask's unobserved-task handler with the stack starting at THIS rethrow —
                // which points every reader at the catch instead of at the fault. Logging it here,
                // with the entry that was rendering, is the only place that still knows either.
                _logger.LogError(e, "DialogManager: entry {EntryId} threw while rendering.",
                    entry?.Id);
                Deactivate(mine);
                throw;
            }
        }

        /// <summary>
        /// Mirrors <c>dialog_getDialogArea</c> at 0x485bc: start with the style's default area,
        /// then let any ResizeDialog action on the entry override it. The original cutscene
        /// narrative path (<c>anim_show_dialog</c> cases 0/3) also goes through this — the
        /// (0, 115, 320, 85) strip it manages is purely a back-buffer save/restore region, not a
        /// dialog-area override; the actual text layout comes from the PlainWithoutBox entry's own
        /// resolved style.
        ///
        /// <para><b>A resize REPLACES the style's area; it does not merge with it.</b> That is the
        /// original's behaviour (the entry's rect is used in place of the style's, wholesale) and
        /// the port keeps it. The consequence is worth stating plainly rather than papering over:
        /// an override author who anchors, say, row 2's area — or restates it in percentages —
        /// gets all of that discarded for any DDX entry that carries a resize, which then places
        /// the panel by its own px insets from the top-left. This is faithful, not a defect. Both
        /// sides speaking <see cref="LayoutHint"/> is what makes the replacement clean: there is
        /// no component-by-component mixing, so a px inset can never end up measured from a
        /// percentage-valued anchor.</para>
        ///
        /// <para>Internal rather than private so the replacement semantics can be tested directly
        /// — they are RE-derived behaviour, not an implementation detail.</para>
        /// </summary>
        internal static LayoutHint ResolveArea(DialogEntry entry, LayoutHint fallback) {
            if (entry.TryGetResizeAction(out ResizeDialogAction resize) && resize != null) {
                return resize.ToLayoutHint();
            }
            // Cloned, not handed back as-is: fallback is DialogStyle.DefaultArea, a field on the
            // shared DialogStyleTable row (the table is a cached resource instance now, but still
            // one instance) that every dialog of that style resolves to. The
            // caller stores the result as its own live state (DialogManager._activeArea), so a
            // future mutation of that state (e.g. clamping/nudging the placed panel) must not be
            // able to reach — and permanently rewrite — the table row itself.
            return fallback.Clone();
        }

        // Bounded so a panel that never resolves a layout (detached, or a bug upstream) cannot
        // hang the dialog forever — a handful of frames is generously more than one UI Toolkit
        // layout pass ever needs.
        //
        // Internal rather than private so the timeout bound can be asserted directly from the
        // test that fences it (DialogManagerWaitForResolvedRectTests) instead of the test
        // hard-coding a duplicate "5" that could silently drift from this value.
        internal const int WipeGeometryTimeoutFrames = 5;

        /// <summary>
        /// Waits for <paramref name="panel"/>'s first resolved layout so the open-wipe can grow its
        /// clip mask from real geometry. This replaces the old px-only <c>TryGetWipeRect</c>: the
        /// wipe never actually wanted the <see cref="LayoutHint"/>, it wanted the panel's rect, and
        /// it only reached for the hint because it used to run before layout ever settled. Waiting
        /// one layout tick and reading the panel's resolved <see cref="VisualElement.layout"/>
        /// instead means px and percent areas both resolve to a real rect — the unit question this
        /// method exists to remove.
        ///
        /// <para>If the panel is already laid out (non-zero, non-NaN width/height), it returns
        /// immediately with no yield. Otherwise it awaits the panel's first
        /// <see cref="GeometryChangedEvent"/>, bounded by <see cref="WipeGeometryTimeoutFrames"/>.
        /// A timeout returns <see cref="Rect.zero"/> rather than throwing — the caller's zero-size
        /// guard then skips the wipe, but the dialog still has to appear, so this must never hang
        /// or fault the show.</para>
        ///
        /// <para>Internal rather than private so the timeout/no-throw/zero-result behaviour can be
        /// tested directly against a panel that is deliberately never added to any panel hierarchy
        /// (so it can never lay out) — see <c>DialogManagerWaitForResolvedRectTests</c>. That is
        /// RE-adjacent timing behaviour load-bearing enough to hang a dialog if it regresses, not
        /// an implementation detail.</para>
        /// </summary>
        internal static async UniTask<Rect> WaitForResolvedRect(VisualElement panel, CancellationToken cancellationToken) {
            if (panel == null) {
                return Rect.zero;
            }

            if (IsResolved(panel.layout)) {
                return panel.layout;
            }

            Rect? resolved = null;
            void OnGeometryChanged(GeometryChangedEvent evt) => resolved = evt.newRect;
            panel.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            try {
                for (int frame = 0; frame < WipeGeometryTimeoutFrames; frame++) {
                    if (resolved.HasValue || cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    await UniTask.Yield();
                }
                return resolved ?? Rect.zero;
            } finally {
                panel.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }
        }

        private static bool IsResolved(Rect rect) =>
            rect.width > 0f && rect.height > 0f && !float.IsNaN(rect.width) && !float.IsNaN(rect.height);

        // Open-wipe reveal duration (s). Approximates the original's centre-out box-out cadence
        // (anim_screenTransitionEffect @ 0x53ab5); tune in-Editor if it reads too fast/slow.
        private const float OpenWipeDurationSeconds = 0.18f;

        // Place the active panel at its resolved area. Every shipped style's area is canonical
        // 1600×1200 px — the panel's reference resolution — so those values land directly in panel
        // units and the ScaleWithScreenSize scaler (plus the Stage's pillarbox) handles every
        // window/aspect change; an override's percentages resolve against the stage instead, which
        // is UI Toolkit's own job. Called once when the panel is built; no per-resize re-fit is
        // needed because nothing here depends on the screen rect.
        private void ApplyCanonicalLayout() {
            if (_activePanel == null || _rootDocument == null) {
                return;
            }

            // The one translator from LayoutHint to UI Toolkit styles — see LayoutApplier's own
            // remarks on why no arithmetic belongs on this side of the boundary.
            LayoutApplier.Apply(_activePanel, _activeArea);

            // Text size is NOT applied here any more. It used to be walked onto every Label on
            // the panel after the fact, which meant the builder could not own the pairing of size
            // and aspect stretch that makes game text match the original — one of them would have
            // been overwritten here. DialogPanelBuilder now styles its labels through
            // GameFontText as it creates them (task-46).
        }


        // Dismiss-on-input for narrative dialogs (the tooltip / cutscene strip).
        // The mouse path is handled through the dialog root's own UI Toolkit
        // pointer events rather than by polling the global device: a press
        // captures the pointer to the root and stops propagation, and the dialog
        // is dismissed on RELEASE. That keeps the whole press→release gesture
        // owned by the (higher-sortingOrder) dialog so the click can't also fire
        // the button beneath that opened it — and the panel is never torn down
        // mid-gesture, which used to let the release fall through to the menu.
        // Keyboard/gamepad dismiss arrives as an Activate/Cancel intent on the pushed Exclusive
        // ActionLayer, flipping <paramref name="dismissed"/> — no device poll here. The
        // pagination half of WaitForDialogInput @0x483a8 is ported: the narrative branch above
        // loops on GameTextBlock.LinesRemaining and suppresses the deadline while any remain
        // (DIALOG.C:1361-1384). The CHOICE branch does not page yet — see TASK-389.
        /// <summary>
        /// How long this entry's panel should hold before dismissing itself, from the player's
        /// text-speed preference and the amount of text — null at Slow, which waits for input.
        /// See <see cref="GameData.Resources.Config.DialogTextSpeed"/> for the formula and where
        /// the engine's tick rate comes from.
        /// </summary>
        private double? AutoDismissSecondsFor(DialogEntry entry) {
            GameData.Resources.Config.Preferences prefs = _preferences?.Current;
            if (prefs == null) {
                return null; // no preferences yet: behave as Slow and wait for the player
            }
            return GameData.Resources.Config.DialogTextSpeed.AutoDismissSeconds(
                entry?.Text?.Length ?? 0, prefs.TextSpeed, entry?.Flags ?? 0);
        }

        /// <summary>
        /// Reads a long choice record out page by page, and only then lets its menu be answered.
        /// </summary>
        /// <remarks>
        /// <b>The original pages first and offers the branches last.</b> DIALOG.C:1343-1356 is the
        /// same loop the narrative branch runs, with
        /// <c>askabout_menu_page_run_selection(record)</c> after it — so every page but the last is
        /// an ordinary page turn, and the choice is presented once there is nothing left to read.
        ///
        /// <para><b>Why the menu is HIDDEN rather than merely ignored.</b> The choice layer is a
        /// <c>NavigableLayer</c> whose Activate presses the focused button, so a page turn has
        /// nowhere to go while that layer is on top. This pushes its own Exclusive layer above it
        /// for the keyboard and a click-catching scrim above everything for the mouse, and takes the
        /// buttons' visibility away so a stray click cannot answer a question the player has not
        /// finished reading. <c>visibility: hidden</c> is the right tool because it also makes them
        /// unpickable — the same property the REQ renderer relies on.</para>
        ///
        /// <para>Measured in the shipped data: 330 entries carry a choice flag and 67 of them are
        /// over 400 characters, where 467 already needed two pages in the row-2 box — and a choice
        /// panel has less room than that because the buttons take the bottom of it (TASK-428).</para>
        /// </remarks>
        private async UniTask PageBeforeChoiceAsync(VisualElement root, VisualElement stage,
            CancellationToken cancellationToken) {
            GameTextBlock body = _activePanel?.Q<GameTextBlock>("BakDialogBody");
            if (body == null) {
                return;
            }
            body.Paginate = true;
            // One frame, for the same reason the narrative branch waits one: the page count comes
            // from the block's RESOLVED geometry, and a pass taken before layout settles bails out
            // and leaves LinesRemaining at the pre-pagination zero.
            // *** WAIT FOR THE BLOCK TO SETTLE, NOT FOR ONE FRAME. *** The page count comes from
            // resolved geometry, and a choice record takes an extra layout pass: the menu row it
            // reserves and the overflow nudge are both applied from the flow itself
            // (GameTextBlock.ChoiceMenuReserve). Reading after exactly one yield caught the block
            // mid-settle, saw LinesRemaining == 0, and returned — leaving the buttons drawn over
            // the last line of page one. Bounded so a record that genuinely fits still costs only
            // a few frames.
            for (var settle = 0; settle < SettleFrames && body.LinesRemaining <= 0; settle++) {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (body.LinesRemaining <= 0) {
                return;
            }

            VisualElement row = _activePanel?.Q("BakDialogConfirmRow");
            VisualElement grid = stage?.Q("BakKeywordGrid");
            SetMenuVisible(row, grid, false);
            try {
                while (body.LinesRemaining > 0 && !cancellationToken.IsCancellationRequested) {
                    // Its own scrim rather than the choice mode's: that one is deliberately
                    // INSERTED BEHIND so the buttons stay clickable, which is the opposite of what
                    // a page turn needs.
                    var pageScrim = new VisualElement {
                        name = "BakDialogPageScrim",
                        pickingMode = PickingMode.Position,
                        style = {
                            position = Position.Absolute,
                            left = 0, top = 0, right = 0, bottom = 0,
                            backgroundColor = new StyleColor(Color.clear),
                        },
                    };
                    pageScrim.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    pageScrim.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                    root.Add(pageScrim);

                    bool turned = false;
                    // Same rule as the narrative layer above: any key but an arrow turns the page.
                    var pageLayer = new BakAgain.UI.InputCore.ActionLayer(
                        LayerIdPrefix + "page", () => turned = true, () => turned = true,
                        onMove: _ => { }, anyIntentActivates: true);
                    _stack?.Push(pageLayer);
                    try {
                        // No deadline: while lines remain the original passes 0 in place of the
                        // record's flags, so auto-dismiss cannot fire on a page that is not the last.
                        await WaitForDismiss(pageScrim, () => turned, cancellationToken);
                    } finally {
                        _stack?.Remove(pageLayer);
                        pageScrim.RemoveFromHierarchy();
                    }
                    body.AdvancePage();
                }
            } finally {
                SetMenuVisible(row, grid, true);
            }
        }

        /// <summary>How many frames a paged choice block is given to settle before it is read.</summary>
        private const int SettleFrames = 4;

        private static void SetMenuVisible(VisualElement row, VisualElement grid, bool visible) {
            StyleEnum<Visibility> value = visible ? Visibility.Visible : Visibility.Hidden;
            if (row != null) {
                row.style.visibility = value;
            }
            if (grid != null) {
                grid.style.visibility = value;
            }
        }

        private async UniTask WaitForDismiss(VisualElement scrim, System.Func<bool> dismissed,
            CancellationToken cancellationToken, double? autoDismissAfterSeconds = null) {
            bool pointerDismissed = false;
            int capturedPointer = PointerId.invalidPointerId;

            // The scrim is the full-screen top layer, so every click targets it
            // directly — the down captures the pointer (so the matching release is
            // guaranteed to come back here, not to the menu) and the release
            // dismisses. The scrim's own swallow callbacks StopPropagation, so the
            // gesture never reaches the screen behind. We dismiss on RELEASE, never
            // on press, so the panel is never torn down mid-gesture.
            void OnPointerDown(PointerDownEvent evt) {
                capturedPointer = evt.pointerId;
                scrim.CapturePointer(evt.pointerId);
            }

            void OnPointerUp(PointerUpEvent evt) {
                if (scrim.HasPointerCapture(evt.pointerId)) {
                    scrim.ReleasePointer(evt.pointerId);
                }
                pointerDismissed = true;
            }

            scrim.RegisterCallback<PointerDownEvent>(OnPointerDown);
            scrim.RegisterCallback<PointerUpEvent>(OnPointerUp);
            try {
                await UniTask.Yield(PlayerLoopTiming.Update); // skip the frame the call was issued on
                // Text speed: past Slow the panel dismisses itself once the reading time is up,
                // and input still cuts it short — the engine's `while (deadline > ticks)` loop
                // with its input poll (DIALOG.C:239-248). At Slow there is no deadline at all.
                double deadline = autoDismissAfterSeconds.HasValue
                    ? Time.unscaledTimeAsDouble + autoDismissAfterSeconds.Value
                    : double.PositiveInfinity;
                while (!pointerDismissed && !dismissed()
                    && scrim.panel != null
                    && Time.unscaledTimeAsDouble < deadline) {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            } finally {
                scrim.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                scrim.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                if (capturedPointer != PointerId.invalidPointerId && scrim.HasPointerCapture(capturedPointer)) {
                    scrim.ReleasePointer(capturedPointer);
                }
            }
        }
    }
}
