namespace BakAgain.UI.InputCore {
    using System;
    using System.Collections.Generic;

    // The single input-ownership stack. Discrete intents route to exactly one resolved layer;
    // an Exclusive layer captures input and blocks every layer beneath it. This replaces the
    // old ModalActive/_choiceArmed/_awaitKeyReleaseAfterModal gating flags with structural truth.
    public sealed class InputLayerStack {
        private readonly List<IInputLayer> _layers = new List<IInputLayer>();

        public IReadOnlyList<IInputLayer> Layers => _layers;
        public IInputLayer Top => _layers.Count > 0 ? _layers[_layers.Count - 1] : null;
        public bool IsModal => TopExclusive() != null;
        public event Action Changed;

        public void Push(IInputLayer layer) {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (_layers.Contains(layer)) throw new InvalidOperationException("Layer already pushed: " + layer.Id);
            _layers.Add(layer);
            layer.OnPushed();
            Recompute();
        }

        public IInputLayer Pop() {
            if (_layers.Count == 0) return null;
            IInputLayer top = _layers[_layers.Count - 1];
            _layers.RemoveAt(_layers.Count - 1);
            top.OnPopped();
            Recompute();
            return top;
        }

        public bool Remove(IInputLayer layer) {
            int i = _layers.IndexOf(layer);
            if (i < 0) return false;
            _layers.RemoveAt(i);
            layer.OnPopped();
            Recompute();
            return true;
        }

        /// <summary>
        /// Drop every layer whose <see cref="IInputLayer.Id"/> starts with <paramref name="prefix"/>.
        /// Returns how many went.
        /// </summary>
        /// <remarks>
        /// <b>For an owner that is going away and must not leave input captured behind it.</b> An
        /// Exclusive layer stranded with nothing behind it consumes every intent and pops for
        /// nobody — the compass goes dead with no panel on screen to dismiss, and only a debugger
        /// can clear it. That has happened twice (2026-09-09 after a combat interrupted a narrative
        /// dialog, 2026-09-10 at Romney's bridge with two overlapping shows), which is why the
        /// recovery lives here rather than in each owner's teardown path: a teardown that can be
        /// skipped is exactly the failure.
        ///
        /// <para>Swept by id prefix rather than by a tracked list so it also catches a layer whose
        /// owner has lost the reference — the overlapping-show case, where one field held one of
        /// two live layers.</para>
        /// </remarks>
        public int RemoveWithIdPrefix(string prefix) {
            if (string.IsNullOrEmpty(prefix)) return 0;
            var removed = 0;
            for (int j = _layers.Count - 1; j >= 0; j--) {
                IInputLayer l = _layers[j];
                if (l.Id == null || !l.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                _layers.RemoveAt(j);
                l.OnPopped();
                removed++;
            }
            if (removed > 0) Recompute();
            return removed;
        }

        // True if no Exclusive layer sits above `layer` in the stack.
        public bool IsInteractable(IInputLayer layer) {
            int i = _layers.IndexOf(layer);
            if (i < 0) return false;
            for (int j = i + 1; j < _layers.Count; j++) {
                if (_layers[j].CaptureMode == CaptureMode.Exclusive) return false;
            }
            return true;
        }

        // Discrete-intent recipient: topmost Exclusive, else topmost passive that wants focus,
        // else topmost passive, else null.
        public IInputLayer ResolveInputTarget() {
            IInputLayer exclusive = TopExclusive();
            if (exclusive != null) return exclusive;
            IInputLayer fallback = null;
            for (int j = _layers.Count - 1; j >= 0; j--) {
                IInputLayer l = _layers[j];
                if (l.CaptureMode != CaptureMode.Passive) continue;
                if (fallback == null) fallback = l;
                if (l.WantsFocus) return l;
            }
            return fallback;
        }

        public bool DispatchIntent(UiIntent intent) {
            IInputLayer target = ResolveInputTarget();
            return target != null && target.HandleIntent(intent);
        }

        private IInputLayer TopExclusive() {
            for (int j = _layers.Count - 1; j >= 0; j--) {
                if (_layers[j].CaptureMode == CaptureMode.Exclusive) return _layers[j];
            }
            return null;
        }

        private void Recompute() {
            for (int j = 0; j < _layers.Count; j++) {
                _layers[j].OnActiveChanged(IsInteractable(_layers[j]));
            }
            Changed?.Invoke();
        }
    }
}
