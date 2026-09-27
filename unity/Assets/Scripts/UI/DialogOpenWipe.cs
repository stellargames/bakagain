namespace BakAgain.UI {
    using System.Threading;
    using BakAgain.UI.Layout;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    // Faithful approximation of the original dialog open-wipe (anim_screenTransitionEffect @ 0x53ab5,
    // box-out style 0xA094): the already-laid-out panel is revealed UN-SCALED through a centred
    // rectangle that grows from 0x0 to its full canonical rect. UI Toolkit has no pixel framebuffer,
    // so the centre-out reveal is reproduced with a growing overflow:hidden clip mask: the panel is
    // reparented into the mask and pinned at its screen rect while the mask grows from the centre.
    public static class DialogOpenWipe {
        // The original plays the open-wipe unless the entry opts out via SkipOpenWipe (0x2000).
        public static bool ShouldPlay(DialogEntryFlags flags) =>
            !flags.HasFlag(DialogEntryFlags.SkipOpenWipe);

        /// <summary>
        /// Whether the wipe covers the WHOLE SCREEN rather than just the dialog's own area.
        /// </summary>
        /// <remarks>
        /// <b>ExecuteDialog throws the entry away for type 6.</b> At 0x49c4d it reads
        /// <c>dialogType</c>, and on <c>dialogType == 6</c> (<see cref="DialogType.PlainFullScreen"/>)
        /// it zeroes the pointer — <c>xor dx,dx; xor ax,ax</c> at 0x49c58 — before calling
        /// <c>dialog_WipeOpenFromCenter</c>. That wrapper wipes
        /// <c>dialog_getDialogArea(entry)</c> for a non-null entry and the full
        /// <c>(0,0,320,200)</c> for a null one, so the full-screen dialog irises open across the
        /// entire screen while every other dialog irises open inside its own panel rect.
        ///
        /// <para>This is what lets the parchment be the thing that opens. It is loaded full-screen
        /// into the offscreen buffer, and a full-screen wipe is what brings it to the visible one —
        /// there is no second path doing it. A port that wipes only the panel rect leaves the
        /// parchment already on screen and irises the text alone.</para>
        /// </remarks>
        public static bool WipesWholeScreen(DialogType type) => type == DialogType.PlainFullScreen;

        // Centre-out step: the clip-mask rect at progress t (a centred sub-rect of `canonicalRect`),
        // plus the panel's offset INSIDE that mask so it stays pinned at `canonicalRect` on screen.
        public static (Rect mask, Vector2 panelOffset) StepGeometry(Rect canonicalRect, float t) {
            t = Mathf.Clamp01(t);
            float cx = canonicalRect.x + canonicalRect.width / 2f;
            float cy = canonicalRect.y + canonicalRect.height / 2f;
            float w = canonicalRect.width * t;
            float h = canonicalRect.height * t;
            float mx = cx - w / 2f;
            float my = cy - h / 2f;
            return (new Rect(mx, my, w, h), new Vector2(canonicalRect.x - mx, canonicalRect.y - my));
        }

        /// <summary>
        /// Returns the panel to the stage at its authored position. Deliberately re-applies the
        /// <see cref="LayoutHint"/> rather than writing back the pixel rect the mask animated
        /// through: the rect is resolved geometry, and writing it back as px would freeze a
        /// percentage-authored dialog at whatever size the window happened to be — silently
        /// undoing the reflow the author asked for.
        /// </summary>
        /// <param name="index">
        /// The panel's sibling index before the wipe took it, or -1 to append.
        /// <para><b>Appending is not a neutral default.</b> Anything the caller put on the stage
        /// ABOVE the panel — the ask-about topic grid is added after it, deliberately, "or the
        /// panel paints over the buttons" — ends up below it once the panel is re-appended at the
        /// end. That is invisible, because the panel's own background is transparent over the
        /// backdrop: the topics stay perfectly readable and simply stop taking clicks, while
        /// keyboard activation keeps working because it never goes through picking. Restoring the
        /// index the panel actually had costs one integer and cannot invert anything.</para>
        /// </param>
        public static void RestorePanel(VisualElement stage, VisualElement panel, LayoutHint area,
            int index = -1) {
            panel.RemoveFromHierarchy();
            panel.style.position = Position.Absolute;
            ClearWipePins(panel);
            LayoutApplier.Apply(panel, area);
            if (index < 0 || index >= stage.childCount) {
                stage.Add(panel);
            } else {
                stage.Insert(index, panel);
            }
        }

        /// <summary>
        /// Drops the inline size and insets the wipe pinned, so the authored hint decides again.
        /// </summary>
        /// <remarks>
        /// <b>This cannot be left to <see cref="LayoutApplier.Apply"/>.</b> Apply is deliberately
        /// layerable — it writes only the lengths the hint states explicitly and never resets the
        /// others — so a hint that sizes itself with percentage INSETS rather than an explicit
        /// width would leave the wipe's px width in place, freezing the panel at whatever the
        /// window happened to be. That is precisely the failure
        /// <see cref="RestorePanel"/> exists to avoid, arriving by the other door.
        /// </remarks>
        private static void ClearWipePins(VisualElement panel) {
            panel.style.width = StyleKeyword.Null;
            panel.style.height = StyleKeyword.Null;
            panel.style.right = StyleKeyword.Null;
            panel.style.bottom = StyleKeyword.Null;
        }

        /// <param name="wholeScreenRect">
        /// When set, the mask grows over THIS rect instead of the panel's, and
        /// <paramref name="alsoReveal"/> is uncovered along with the panel. Used for
        /// <see cref="WipesWholeScreen"/> dialogs, where the original wipes the full screen.
        /// </param>
        /// <param name="alsoReveal">
        /// Extra layers to reveal through the same mask — the parchment backdrop and the vine
        /// corners. <b>Each must be a STRETCH-anchored full-frame layer</b> (all four insets 0),
        /// because that is the anchoring restored to them afterwards; passing a positioned element
        /// would put it back at the wrong place.
        /// </param>
        public static async UniTask PlayAsync(VisualElement stage, VisualElement panel, LayoutHint area,
            Rect resolvedRect, float duration, CancellationToken cancellationToken,
            Rect? wholeScreenRect = null,
            System.Collections.Generic.IReadOnlyList<VisualElement> alsoReveal = null) {

            // *** A WIPE WITH NOTHING TO REVEAL IS A NO-OP, NOT A CRASH -- AND IT SAYS SO. ***
            // The caller reads _activePanel, a field RemovePanel nulls, and awaits twice before
            // getting here; the null then hit the first unguarded dereference below and the
            // exception escaped through ShowById to whoever awaited it, so the dialog never
            // appeared at all (TASK-578: 18 "threw while rendering" in one 20 h log).
            //
            // The CALL SITE is where that was actually fixed -- it captures the panel once and
            // wipes it only while it is still the active one. This guard is the backstop for the
            // next caller, and it LOGS rather than returning quietly on purpose: a silent return
            // would turn the same class of stale-field bug into a dialog that renders without its
            // wipe and never tells anyone, which is harder to find than the crash was.
            if (stage == null || panel == null) {
                Debug.LogWarning("DialogOpenWipe: asked to wipe with no "
                    + (stage == null ? "stage" : "panel")
                    + " -- the dialog was torn down while its geometry wait was in flight. "
                    + "Skipping the wipe; the dialog itself is unaffected.");

                return;
            }

            // The rect the iris opens across: the whole screen for a full-screen dialog, the
            // panel's own rect otherwise. The panel is pinned at resolvedRect either way.
            Rect wipeRect = wholeScreenRect ?? resolvedRect;

            // Captured BEFORE the mask joins the stage, so it is the panel's real place among the
            // caller's own layers — see RestorePanel's `index`.
            int panelIndex = stage.IndexOf(panel);

            var mask = new VisualElement { name = "DialogOpenWipeMask" };
            mask.style.position = Position.Absolute;
            mask.style.overflow = Overflow.Hidden;
            stage.Add(mask);

            // Capture where each extra layer sits BEFORE reparenting: inside the growing mask a
            // stretch-anchored layer would resolve its insets against the MASK and grow with it,
            // which is the same trap the panel's own pinning exists to avoid.
            var extras = new System.Collections.Generic.List<(VisualElement element, Rect rect)>();
            if (alsoReveal != null) {
                foreach (VisualElement layer in alsoReveal) {
                    if (layer == null || layer.panel == null) {
                        continue;
                    }
                    extras.Add((layer, layer.layout));
                }
            }

            // Reparent the extras FIRST so they keep their painting order behind the panel.
            foreach ((VisualElement layer, Rect rect) in extras) {
                layer.RemoveFromHierarchy();
                mask.Add(layer);
                layer.style.position = Position.Absolute;
                layer.style.right = StyleKeyword.Null;
                layer.style.bottom = StyleKeyword.Null;
                layer.style.width = rect.width;
                layer.style.height = rect.height;
            }

            // Move the built panel into the mask; it keeps its content and is absolutely placed.
            panel.RemoveFromHierarchy();
            mask.Add(panel);
            panel.style.position = Position.Absolute;

            // PIN THE PANEL AT ITS RESOLVED SIZE FOR THE DURATION OF THE MASK. The panel still
            // carries its authored hint, and inside the mask a PERCENTAGE width or inset re-resolves
            // against the MASK — which is growing. So a percentage-authored dialog grew with the
            // reveal and re-wrapped its text for the whole 0.18 s, then snapped to the right
            // geometry; measured at 1920x1080 the panel tracked maskWidth x 0.91875 across 28
            // frames. The original's box-out (anim_screenTransitionEffect @0x53ab5) reveals the
            // finished box un-scaled, so the panel must hold still while the window onto it opens.
            //
            // The insets are cleared as well as the size: a hint that pins itself with percentage
            // right/bottom would otherwise keep re-resolving those against the mask even with the
            // width pinned. left/top are set every step by SetStep.
            panel.style.width = resolvedRect.width;
            panel.style.height = resolvedRect.height;
            panel.style.right = StyleKeyword.Null;
            panel.style.bottom = StyleKeyword.Null;

            void SetStep(float t) {
                Rect m = StepGeometry(wipeRect, t).mask;
                mask.style.left = m.x;
                mask.style.top = m.y;
                mask.style.width = m.width;
                mask.style.height = m.height;
                panel.style.left = resolvedRect.x - m.x;
                panel.style.top = resolvedRect.y - m.y;
                // Each extra holds still at its own screen rect while the window opens over it —
                // the same pinning as the panel, just against a different origin.
                foreach ((VisualElement layer, Rect rect) in extras) {
                    layer.style.left = rect.x - m.x;
                    layer.style.top = rect.y - m.y;
                }
                // Reveal the panel only now that a mask is clipping it. DialogManager hides it
                // before awaiting the panel's first resolved layout, because that await costs a
                // frame and an un-hidden panel would be repainted at FULL SIZE in it — a one-frame
                // pop-in before the wipe, on every shipped dialog. Clearing the inline value (rather
                // than writing Visible) restores whatever the cascade says, so this cannot start
                // forcing a panel visible that something else deliberately hid. Done inside SetStep
                // so the reveal and the mask geometry are one indivisible step: there is no ordering
                // in which the panel is showing and the mask has not been sized.
                panel.style.visibility = StyleKeyword.Null;
            }

            try {
                SetStep(0f);
                float elapsed = 0f;
                while (elapsed < duration && !cancellationToken.IsCancellationRequested) {
                    await UniTask.Yield(); // unscaled per-frame tick; token checked in the condition
                    elapsed += Time.unscaledDeltaTime;
                    SetStep(elapsed / duration);
                }
            } finally {
                // Snap to final and restore the panel to the stage at its authored area; drop the mask.
                // The extras go back UNDER the panel (index 0, in their original order) and get
                // their stretch anchoring back rather than the px the mask pinned them at — a full
                // -frame layer frozen at px would stop following the frame on the next resize.
                for (int i = 0; i < extras.Count; i++) {
                    VisualElement layer = extras[i].element;
                    layer.RemoveFromHierarchy();
                    layer.style.left = 0;
                    layer.style.top = 0;
                    layer.style.right = 0;
                    layer.style.bottom = 0;
                    layer.style.width = StyleKeyword.Null;
                    layer.style.height = StyleKeyword.Null;
                    stage.Insert(i, layer);
                }
                RestorePanel(stage, panel, area, panelIndex);
                mask.RemoveFromHierarchy();
            }
        }
    }
}
