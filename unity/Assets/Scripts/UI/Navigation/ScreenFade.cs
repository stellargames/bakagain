namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The world's screen fade — a black cover that darkens before a transition and clears after it.
    /// </summary>
    /// <remarks>
    /// <b>Alpha, not a palette ramp, and that is forced by the port's own architecture.</b> The
    /// original fades by rewriting the VGA DAC (<c>palette_fade_out</c>, PALETTE.C:83); we resolve
    /// indexed colour to RGBA at load time, so there is no live palette to scale. A black overlay is
    /// the equivalent that reaches every pixel — faithful-LOOKING, which is the standing decision for
    /// this port.
    ///
    /// <para><b>The shape comes from the original, the duration from the project's one knob.</b>
    /// <see cref="FadeRamp.WorldFadePaletteWrites" /> is the recovered write count (intensity 63 to
    /// 0 in steps of 2, plus the final black) and <c>CutsceneTiming.FadeDurationSeconds</c> maps
    /// writes onto seconds. That factor is a calibration, not a fact — the original's loop presents
    /// no frame per step, so nothing in the data says how long a fade took. Reusing the existing knob
    /// keeps world and cutscene fades commensurable instead of drifting apart.</para>
    ///
    /// <para><b>It borrows a PanelSettings rather than shipping one.</b> The overlay has to sit above
    /// every panel including the software cursor, so it needs its own document at a higher
    /// sortingOrder — but authoring a new asset for one black rectangle is not worth it. If no
    /// UIDocument exists yet to borrow from, the fade degrades to a CUT: a transition that does not
    /// darken is a cosmetic loss, where throwing would take the screen change with it.</para>
    /// </remarks>
    public sealed class ScreenFade : IScreenFade {
        /// <summary>Above every other document, the cursor included.</summary>
        private const int OverlaySortingOrder = 10000;

        private UIDocument _document;
        private VisualElement _cover;
        private bool _warned;
        private float _opacity;

        /// <summary>
        /// The play session this fade belongs to, captured when the singleton is built.
        /// </summary>
        /// <remarks>
        /// *** CAPTURED, NOT READ LIVE. *** <c>Application.exitCancellationToken</c> is cancelled
        /// on leaving Play Mode and then REPLACED with a fresh one, so a continuation reading it
        /// after the stop sees an uncancelled token. Every world transition awaits this fade, so
        /// refusing here is what stops a pending swap from building the arena and this overlay in
        /// the EDIT scene after Play Mode ends (TASK-858: <c>CombatScreen(Clone)</c> and
        /// <c>BakScreenFade</c> were left behind, and the next boot failed its combat icons).
        /// In a build the token fires only on quit.
        /// </remarks>
        private readonly System.Threading.CancellationToken _session = Application.exitCancellationToken;

        /// <summary>How long one direction of the fade takes.</summary>
        public static float DurationSeconds =>
            CutScenes.CutsceneTiming.FadeDurationSeconds(FadeRamp.WorldFadePaletteWrites);

        public UniTask FadeOutAsync() => RampAsync(1f);

        public UniTask FadeInAsync() => RampAsync(0f);

        /// <summary>
        /// Ramps to <paramref name="to"/> from wherever the cover currently is.
        /// </summary>
        /// <remarks>
        /// *** IT RAMPS FROM THE CURRENT OPACITY, NOT FROM ZERO. *** Fades nest: entering the world
        /// darkens, and the <c>ResetTo</c> at the end of that flow darkens again through the
        /// navigator. A version that always started at 0 would snap the cover transparent and ramp
        /// back up — **a flash of the game in the middle of a transition**, which is the one thing a
        /// fade exists to prevent. Already at the target is a no-op, so nesting costs nothing.
        /// </remarks>
        private async UniTask RampAsync(float to) {
            _session.ThrowIfCancellationRequested();   // play has ended: abort the awaiting transition
            float from = _opacity;
            if (Mathf.Approximately(from, to)) {
                VisualElement settled = Cover();
                if (settled != null) {
                    settled.style.display = to <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
                }

                return;
            }

            VisualElement cover = Cover();
            if (cover == null) {
                return;   // no surface to draw on — the transition cuts
            }

            // Stepped rather than continuous, and the step count is the original's write count: the
            // fade is 33 discrete palette writes there, so it is 33 discrete opacities here.
            const int steps = FadeRamp.WorldFadePaletteWrites;
            float perStep = DurationSeconds / steps;
            for (var step = 1; step <= steps; step++) {
                _opacity = Mathf.Lerp(from, to, step / (float)steps);
                cover.style.opacity = _opacity;
                await UniTask.Delay(System.TimeSpan.FromSeconds(perStep), ignoreTimeScale: true,
                    cancellationToken: _session);
            }

            _opacity = to;
            cover.style.opacity = to;
            // Fully clear means out of the way entirely, so nothing of ours is ever composited over
            // the game for the sake of an invisible rectangle.
            cover.style.display = to <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// The cover element, creating or re-attaching it as needed.
        /// </summary>
        /// <remarks>
        /// <b>Nothing here latches a failure.</b> An earlier version set an "unavailable" flag the
        /// first time no PanelSettings could be borrowed — and since the fade can be asked for before
        /// any panel exists, one early call disabled the fade for the entire session. A transient
        /// absence must not be permanent: the warning is once, the retry is every time.
        ///
        /// <para>It also re-attaches rather than trusting a stored element, because a UIDocument
        /// replaces its <c>rootVisualElement</c> when it initialises.</para>
        /// </remarks>
        private VisualElement Cover() {
            if (_document == null) {
                PanelSettings borrowed = FindPanelSettings();
                if (borrowed == null) {
                    WarnOnce("no UIDocument to borrow PanelSettings from");
                    return null;   // retried next time; a transition cuts meanwhile
                }

                // Configure BEFORE the document enables: UIDocument builds its root in OnEnable from
                // the panelSettings it has at that moment, so assigning them to an already-live
                // component leaves the root null.
                var host = new GameObject("BakScreenFade");
                host.SetActive(false);
                Object.DontDestroyOnLoad(host);
                _document = host.AddComponent<UIDocument>();
                _document.panelSettings = borrowed;
                _document.sortingOrder = OverlaySortingOrder;
                host.SetActive(true);
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null) {
                WarnOnce("the fade document has no root yet");
                return null;
            }

            if (_cover == null || _cover.parent != root) {
                root.pickingMode = PickingMode.Ignore;
                Stretch(root);
                _cover = BuildCover();
                root.Add(_cover);
            }

            _cover.style.display = DisplayStyle.Flex;
            return _cover;
        }

        private void WarnOnce(string why) {
            if (_warned) {
                return;
            }

            _warned = true;
            Debug.LogWarning($"ScreenFade: {why}; transitions cut until it is. See TASK-214.");
        }

        /// <summary>
        /// Makes a code-built document's root fill the panel.
        /// </summary>
        /// <remarks>
        /// *** NOT OPTIONAL. *** A UIDocument with no source UXML lays its root out to its content,
        /// which for one absolutely-positioned child is nothing. Measured before this existed: the
        /// cover resolved to 2133 x <b>0</b> at opacity 1 — an overlay reporting itself fully opaque
        /// while covering no pixels, which a screenshot would not obviously explain.
        /// </remarks>
        private static void Stretch(VisualElement root) {
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
        }

        private static VisualElement BuildCover() =>
            new VisualElement {
                name = "screen-fade",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                    width = Length.Percent(100),
                    height = Length.Percent(100),
                    backgroundColor = Color.black,
                    opacity = 0f,
                },
            };

        private static PanelSettings FindPanelSettings() {
            UIDocument[] documents = Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (UIDocument document in documents) {
                if (document.panelSettings != null) {
                    return document.panelSettings;
                }
            }

            return null;
        }
    }
}
