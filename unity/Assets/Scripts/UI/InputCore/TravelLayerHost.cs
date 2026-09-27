namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using UnityEngine;

    // Bridges the in-game travel REQ (the UserInterfaceLoader on the InGameScreen prefab) into the
    // input-ownership stack — the travel counterpart of MenuLayerHost. Pushes a Passive TravelLayer
    // when the loader has built its widgets; pops it on teardown. OnDisable is ALSO the sibling-swap
    // path: opening a sibling screen (inventory/spells/rest) hides the travel screen, which pops this
    // layer, so movement stops with no special-casing. Exposes IsInputActive — the gate the movement
    // driver reads. Reconciled (pull) like MenuLayerHost for the Built-before-subscribe race; resolves
    // the stack via UiDriver.Stack (sibling components are not [Inject]-ed).
    [RequireComponent(typeof(UserInterfaceLoader))]
    public sealed class TravelLayerHost : MonoBehaviour {
        // Esc opens the game/options menu: ActionOptions (24) on REQ_MAIN. Hardcoded (not a reference
        // to InGameScreen) to keep InputCore independent of in-game screen types.
        private const int EscActionId = 24;

        private UserInterfaceLoader _loader;
        private IActionHandler _actionHandler;
        private InputLayerStack _stack;
        private TravelLayer _layer;

        // The travel surface owns input right now: it is the stack's resolved target (nothing above
        // wants input). False whenever any menu/modal is resolved above it, or it is not pushed.
        public bool IsInputActive =>
            _layer != null && _stack != null && ReferenceEquals(_stack.ResolveInputTarget(), _layer);

        private void Awake() {
            _loader = GetComponent<UserInterfaceLoader>();
            _actionHandler = GetComponent<IActionHandler>();
            _stack = UiDriver.Stack;
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

        private void ReconcileIfBuilt() {
            if (_layer == null && _loader.IsBuilt) {
                PushLayer(_loader.CurrentNavWidgets);
            }
        }

        private void OnBuilt(IReadOnlyList<NavWidget> widgets) => PushLayer(widgets);

        private void OnCleared() => PopLayer();

        private void PushLayer(IReadOnlyList<NavWidget> widgets) {
            PopLayer();
            _layer = new TravelLayer(name, widgets,
                () => _actionHandler?.PrimaryAction(EscActionId),
                letter => {
                    int action = GameData.Resources.World.TravelHotkeys.ActionFor(letter);
                    if (action != GameData.Resources.World.TravelHotkeys.NoAction) {
                        _actionHandler?.PrimaryAction(action);
                    }
                });
            _stack?.Push(_layer);
        }

        private void PopLayer() {
            if (_layer != null) {
                _stack?.Remove(_layer);
                _layer = null;
            }
        }
    }
}
