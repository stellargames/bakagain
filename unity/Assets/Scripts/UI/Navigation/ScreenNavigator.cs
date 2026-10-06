namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Default <see cref="IScreenNavigator"/>. One-screen-at-a-time: every operation mutates the
    /// back-stack, then <see cref="EnsureTopShownAsync"/> ensures exactly the stack top is enabled —
    /// hiding the currently-shown screen (fade out + disable) FIRST, then showing the new top
    /// (enable + fade in), the sequential dip. The back-stack only decides return order; only the top
    /// is ever enabled, so a transition between two screens never touches anything beneath them.
    ///
    /// Operations are serialized through a FIFO queue drained by a single pump, so concurrent
    /// fire-and-forget calls (a button's <c>Push(...).Forget()</c>) can't interleave a half-finished
    /// transition. Invariant: a screen's <c>ShowAsync</c>/<c>HideAsync</c> must not call back into
    /// the navigator (its op would be queued behind the one awaiting it → deadlock); screens render,
    /// flows navigate.
    /// </summary>
    public sealed class ScreenNavigator : IScreenNavigator {
        private sealed class Entry {
            public IScreen Screen;
            /// <summary>Installed by <see cref="ResetTo"/>: a base screen nobody pushed.</summary>
            public bool IsBase;
        }

        private readonly List<Entry> _stack = new List<Entry>(); // [0] = bottom
        private readonly Queue<Func<UniTask>> _queue = new Queue<Func<UniTask>>();
        private bool _pumping;
        private IScreen _shown; // the single currently-enabled screen (null = none)

        /// <summary>
        /// Covers the screen while a transition swaps what is under it.
        /// </summary>
        /// <remarks>
        /// Settable rather than constructor-injected, matching the other late-bound collaborators in
        /// this project, and defaulted to <see cref="NullScreenFade" /> so a navigator built without
        /// one behaves exactly as it always did. See <see cref="IScreenFade" /> for why the fade
        /// belongs here and not at the two places that compute "the screen changed".
        /// </remarks>
        public IScreenFade Fade { get; set; } = new NullScreenFade();

        public IScreen Current => _stack.Count > 0 ? _stack[_stack.Count - 1].Screen : null;

        public UniTask ResetTo(IScreen root) => Serialize(async () => {
            await Fade.FadeOutAsync();
            _stack.Clear();
            AddTop(root);
            _stack[_stack.Count - 1].IsBase = true;
            await EnsureTopShownAsync();
            await Fade.FadeInAsync();
        });

        public UniTask Push(IScreen screen) => PushCore(screen, fade: true);

        /// <inheritdoc />
        public UniTask PushUnfaded(IScreen screen) => PushCore(screen, fade: false);

        private UniTask PushCore(IScreen screen, bool fade) => Serialize(async () => {
            if (fade) {
                await Fade.FadeOutAsync();
            }
            AddTop(screen);
            await EnsureTopShownAsync();
            if (fade) {
                await Fade.FadeInAsync();
            }
        });

        /// <inheritdoc />
        /// <remarks>
        /// Polls rather than raising an event: the stack is the only state that says whether a
        /// screen is still up, screens close by several routes (their own Exit, a Pop from the
        /// action that opened them, a Clear on a flow change), and a per-frame identity check
        /// cannot miss one of them the way a subscription can.
        /// </remarks>
        public async UniTask PushAndWaitAsync(IScreen screen) {
            await Push(screen);
            // *** AND WAIT FOR THE TRANSITION, NOT ONLY FOR THE STACK. *** Every op mutates the
            // stack BEFORE it calls EnsureTopShownAsync, so `Current` stops being this screen while
            // the closing transition is still running — the outgoing screen has not hidden and the
            // revealed one has not shown. A caller that resumes there is looking at a half-swapped
            // UI, and anything it does to those screens is undone a frame later by the transition
            // that was still in flight.
            //
            // Measured at Romney 2026-09-12 (TASK-407): LocationScreen.RaiseScreenAsync hid the
            // travel HUD the moment the shop's Exit popped it, read IsVisible == false because the
            // pop had not reached it yet, skipped the hide — and EnsureTopShownAsync then showed the
            // HUD over the location.
            await UniTask.WaitUntil(() => !ReferenceEquals(Current, screen) && IsIdle);
        }

        /// <summary>No operation is running or queued, so the stack and what is shown agree.</summary>
        private bool IsIdle => !_pumping && _queue.Count == 0;

        public UniTask Pop() => PopCore(fade: true);

        /// <inheritdoc />
        public UniTask PopUnfaded() => PopCore(fade: false);

        private UniTask PopCore(bool fade) => Serialize(async () => {
            if (fade) {
                await Fade.FadeOutAsync();
            }
            Entry popped = null;
            if (_stack.Count > 0) {
                popped = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);
            }
            // *** POPPING THE LAST SCREEN IS NEVER LEGITIMATE, AND IT IS SILENT. *** There is always
            // a base screen under whatever opened -- the travel HUD in the world, the main menu
            // outside it -- so a pop that empties the stack means somebody popped what they did not
            // push. The result is a game with NO screen shown at all while the world keeps running:
            // TASK-569 records it twice, and both times it was noticed much later, by `camp`
            // refusing because it could not find the (disabled) InGameScreen.
            //
            // A stack trace HERE is the whole diagnosis, and the reason it is worth a line: the
            // defect leaves no trace of its own, the state afterwards names only the symptom, and
            // three separate candidates have been raised by reading the code and two of them killed
            // the same way. Rare enough to cost nothing -- a pop is a screen transition, and this
            // fires only on the pathological one.
            //
            // *** ONLY WHEN THE POP TOOK A BASE. *** A screen that was PUSHED onto an empty stack
            // belongs to the caller that pushed it, and popping it back to empty is that caller
            // finishing: ChapterScenesPlayer pushes its cutscene after GoToChapter has cleared the
            // travel HUD, and warned on every chapter change for it (TASK-635). The defect is
            // removing a screen installed by ResetTo -- the travel HUD, the main menu.
            if (_stack.Count == 0 && popped != null && popped.IsBase) {
                Debug.LogWarning("ScreenNavigator: Pop emptied the stack -- nothing will be shown. "
                    + "Whoever called this popped a screen it did not push (TASK-569).\n"
                    + new System.Diagnostics.StackTrace(true));
            }
            await EnsureTopShownAsync();
            if (fade) {
                await Fade.FadeInAsync();
            }
        });

        public UniTask Replace(IScreen screen) => Serialize(async () => {
            await Fade.FadeOutAsync();
            bool wasBase = false;
            if (_stack.Count > 0) {
                wasBase = _stack[_stack.Count - 1].IsBase;
                _stack.RemoveAt(_stack.Count - 1);
            }
            AddTop(screen);
            _stack[_stack.Count - 1].IsBase = wasBase; // replacing the base installs a new base
            await EnsureTopShownAsync();
            await Fade.FadeInAsync();
        });

        // *** Clear DOES NOT FADE, and that is deliberate. *** It empties the stack on the way OUT
        // of a configuration and is followed by a ResetTo into the next one, which fades. Fading here
        // as well would darken twice for one transition, with a flash of the cleared screen between.
        public UniTask Clear() => Serialize(async () => {
            _stack.Clear();
            await EnsureTopShownAsync();
        });

        // --- stack mutation helpers (only run inside the serialized tail) ---

        private void AddTop(IScreen screen) {
            if (screen == null) {
                throw new ArgumentNullException(nameof(screen));
            }
            // Screens are singletons; re-pushing one already on the stack is a caller bug we make
            // survivable by treating it as move-to-top.
            int existing = _stack.FindIndex(e => ReferenceEquals(e.Screen, screen));
            if (existing >= 0) {
                Debug.LogWarning($"ScreenNavigator: {screen.GetType().Name} pushed while already on the stack; moving to top.");
                _stack.RemoveAt(existing);
            }
            _stack.Add(new Entry { Screen = screen });
        }

        // Exactly one screen enabled: the stack top. Hide the currently-shown one (fade out + disable)
        // FIRST, then show the new top (enable + fade in) — the sequential dip. Because only the top is
        // ever enabled, a transition between two menu-family screens never touches the travel HUD
        // beneath them (it was already disabled when the menu opened), so no transition routes through it.
        private async UniTask EnsureTopShownAsync() {
            IScreen top = _stack.Count > 0 ? _stack[_stack.Count - 1].Screen : null;
            if (ReferenceEquals(_shown, top)) {
                return;
            }
            if (_shown != null) {
                IScreen outgoing = _shown;
                _shown = null;
                await outgoing.HideAsync();
            }
            if (top != null) {
                await top.ShowAsync();
                _shown = top;
            }
        }

        // FIFO serialization: enqueue the op, then (if not already draining) drain the queue to
        // completion. Each op runs start-to-finish before the next begins. A faulted op is logged
        // and its own awaiter observes the fault, without wedging the queue — safe for both `await`
        // and `.Forget()` callers.
        private async UniTask Serialize(Func<UniTask> op) {
            var done = new UniTaskCompletionSource();
            _queue.Enqueue(async () => {
                try {
                    await op();
                    done.TrySetResult();
                } catch (Exception e) {
                    Debug.LogException(e);
                    done.TrySetException(e);
                }
            });
            if (!_pumping) {
                _pumping = true;
                try {
                    while (_queue.Count > 0) {
                        await _queue.Dequeue()();
                    }
                } finally {
                    _pumping = false;
                }
            }
            await done.Task;
        }
    }
}
