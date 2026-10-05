namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using BakAgain.UI.Cursor;
    using UnityEngine;

    // Bridges a UserInterfaceLoader-built REQ screen into the input-ownership stack: when the loader
    // has built its widgets, push a Passive NavigableLayer; on teardown, pop it. Lives only on the
    // in-scope menu prefabs (main menu, preferences, load, contents) — NOT on the in-game travel REQ,
    // which keeps its own movement poller. The single push/pop site, replacing per-screen keyboard
    // Update().
    //
    // Push is RECONCILED (pull), not purely event-driven: the loader's async build can fire Built
    // before this sibling's Awake subscribes (VContainer prefab-instantiation lifecycle ordering), so
    // we also reconcile against loader.IsBuilt in Awake/OnEnable. The Built/Cleared events cover the
    // genuinely-async build that completes after we're listening.
    //
    // Resolves the stack via the UiDriver static handle (set at container build) rather than [Inject]:
    // VContainer's RegisterComponentInNewPrefab injects only the registered controller, not sibling
    // components, so Construct() would never run on this host (same constraint the loader documents).
    [RequireComponent(typeof(UserInterfaceLoader))]
    public sealed class MenuLayerHost : MonoBehaviour {
        // Esc maps to the Cancel/Return widget's actionId, which is 1 on every REQ screen (the original
        // dispatcher returned scancode 1 for Esc).
        private const int CancelActionId = 1;

        /// <summary>
        /// This screen captures input outright, blocking whatever is still live beneath it.
        /// </summary>
        /// <remarks>
        /// <b>Off for a screen that replaces what came before, on for one that overlays it.</b>
        /// Most REQ screens deactivate the surface underneath, so Passive and Exclusive are
        /// indistinguishable for them; the camp panel is the exception, drawn over a travel HUD that
        /// stays live and clickable.
        ///
        /// <para>The original has no flag for this — <c>UI_Encamp</c> @0x703d0 runs its OWN loop over
        /// its OWN menu data and simply never hit-tests the HUD's widgets while it is up, so the
        /// exclusivity is in the control flow. REQ_CAMP's own <c>IsModal</c> is false, so the data
        /// cannot be read for it either; it has to be declared here.</para>
        ///
        /// <para>Without it the HUD underneath kept taking clicks: with camp open, clicking the map
        /// button opened the map, which the original ignores.</para>
        /// </remarks>
        [SerializeField]
        private bool capturesExclusively;

        private UserInterfaceLoader _loader;
        private IActionHandler _actionHandler;
        private IScreenInput _screenInput;
        private InputLayerStack _stack;
        private ICursorManager _cursor;
        private IInputLayer _layer;

        /// <summary>
        /// This screen owns input right now: its layer is the stack's resolved target. The gate a
        /// screen that drives the world underneath it (the overhead map's movement) reads, so a
        /// dialog opened over it stops the party — the same structural stop
        /// <see cref="TravelLayerHost.IsInputActive"/> gives the travel HUD.
        /// </summary>
        public bool IsInputActive =>
            _layer != null && _stack != null && ReferenceEquals(_stack.ResolveInputTarget(), _layer);

        private void Awake() {
            _loader = GetComponent<UserInterfaceLoader>();
            _actionHandler = GetComponent<IActionHandler>();
            _screenInput = GetComponent<IScreenInput>();
            _stack = UiDriver.Stack;
            _cursor = UnityEngine.Object.FindAnyObjectByType<CursorManager>();
            _loader.Built += OnBuilt;
            _loader.Cleared += OnCleared;
            ReconcileIfBuilt(); // catch a build that already fired Built before we subscribed
        }

        private void OnDestroy() {
            _loader.Built -= OnBuilt;
            _loader.Cleared -= OnCleared;
        }

        private void OnEnable() => ReconcileIfBuilt();

        private void OnDisable() => PopLayer();

        // Push if the loader has built and we have no layer yet (handles the Built-before-subscribe
        // race). The async-build case is covered by the Built event; this is the pull half.
        private void ReconcileIfBuilt() {
            if (_layer == null && _loader.IsBuilt) {
                PushLayer(_loader.CurrentNavWidgets);
            }
        }

        /// <summary>
        /// The loader rebuilt or re-stated its widgets. Update the layer we already have —
        /// <b>never</b> re-push it, which would move this screen above whatever is on top of it.
        /// </summary>
        /// <remarks>
        /// <b>A REBUILD IS NOT AN OPEN, AND `Built` FIRES FOR BOTH.</b>
        /// <see cref="BakAgain.ResourceManagement.Loaders.UserInterfaceLoader.SetEntryState"/> ends
        /// with <c>Built?.Invoke(_navWidgets)</c> — so every "grey that button out" raises it, not
        /// only a first build. Routing that to <see cref="PushLayer"/> pops and pushes, which lands
        /// the screen on the TOP of the stack.
        ///
        /// <para>Measured live on 2026-09-21, camp screen with an announcement dialog up:
        /// <c>[InGameScreen][CampScreen][dialog-narrative]</c> became
        /// <c>[InGameScreen][dialog-narrative][CampScreen]</c> after one
        /// <c>CampMenu.ApplyButtonState()</c>. The dialog's layer was buried, so no Activate could
        /// ever reach it, <c>_announcing</c> stayed true and the rest's <c>finally</c> never ran —
        /// TASK-563's stranded <c>IsResting</c>, whose recorded symptom is exactly
        /// <c>top=CampScreen(Clone)</c> with the dialog text still on screen.</para>
        ///
        /// <para>A genuine rebuild still re-pushes correctly: the loader raises <c>Cleared</c>
        /// first, <see cref="OnCleared"/> pops, and the next <c>Built</c> finds no layer.</para>
        /// </remarks>
        private void OnBuilt(IReadOnlyList<NavWidget> widgets) {
            if (_layer != null) {
                // No-op for a ScreenInputLayer, which owns the keys rather than a widget list —
                // and it equally must not jump the stack.
                SetWidgets(widgets);

                return;
            }
            PushLayer(widgets);
        }

        private void PushLayer(IReadOnlyList<NavWidget> widgets) {
            PopLayer();
            // Type-2 (InteractiveScreen) screens whose controller implements IScreenInput own
            // arrows/Tab/Enter/Esc outright (SAVE/LOAD) — give them a ScreenInputLayer instead of the
            // ordinary focus-warp NavigableLayer. Every other screen (type-0, and a type-2 screen whose
            // controller doesn't implement IScreenInput yet — Tasks 5/6 land it later) keeps the
            // NavigableLayer, so nothing is ever left input-dead.
            if (_loader.UserInterfaceType == GameData.Resources.Menu.UserInterfaceType.InteractiveScreen
                && _screenInput != null) {
                _layer = new ScreenInputLayer(name, _screenInput);
            } else {
                // Pass the loader's picker-selection hook so cursor keys drive the file
                // picker on screens that have one (no-op — returns false — elsewhere).
                _layer = new NavigableLayer(name,
                    capturesExclusively ? CaptureMode.Exclusive : CaptureMode.Passive, widgets,
                    () => _actionHandler?.PrimaryAction(CancelActionId), _cursor, _loader.TryMovePickerSelection,
                    onUnmatchedScancode: sc => _actionHandler is IUnmatchedScancodeHandler h && h.OnUnmatchedScancode(sc));
            }
            _stack?.Push(_layer);
            // No initial focus on open: focusing a widget warps the software cursor onto it (and
            // highlights it), but the cursor should only move when the user navigates by keyboard.
            // The first Tab/arrow focuses the first widget (Cyclic from the unfocused -1 state lands
            // on index 0) and warps+highlights it then; until then the cursor stays at the mouse.
        }

        /// <summary>
        /// Replace the pushed layer's widget list. A screen that builds focusable content of its
        /// own — the inventory's item cells — composes chrome + its own widgets and hands the whole
        /// list over on every rebuild. Mirrors the original, where the screen code (not the menu
        /// system) builds and trims page->pEntries: cmbinv_combat_encounter_begin sets
        /// wEntry_count = 7 and appends one entry per item (canassa CMBINV.C:60-145).
        ///
        /// <para>Returns false when there is nothing to set — the loader has not built yet, or this
        /// is a ScreenInputLayer screen — so a caller can invoke it unconditionally.</para>
        /// </summary>
        public bool SetWidgets(IReadOnlyList<NavWidget> widgets) {
            if (_layer is NavigableLayer navigable) {
                navigable.SetWidgets(widgets);
                return true;
            }
            return false;
        }

        private void OnCleared() => PopLayer();

        private void PopLayer() {
            if (_layer != null) {
                _stack?.Remove(_layer);
                _layer = null;
            }
        }
    }
}
