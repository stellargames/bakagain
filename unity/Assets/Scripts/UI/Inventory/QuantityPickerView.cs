namespace BakAgain.UI.Inventory {
    using BakAgain.ResourceManagement;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using GameData.Resources.Menu;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The modal stack-quantity picker (<c>quantityPickerDialog @0x59EA1</c>, REQ_INV2.DAT;
    /// spec docs/specs/inventory-item-handling.md §14). Shown when a transfer needs an amount
    /// (<c>InventoryTransfer.TransferPlan</c>): resolves to <b>0</b> (cancelled — Esc, or
    /// accepting at "None"), <b>N</b> (move N), or <b>-1</b> (Share with party).
    ///
    /// <para>Value rules live in <see cref="QuantityPickerModel"/> (wrap-around single steps,
    /// clamp-then-wrap five-steps, the Give/None label). Shift+step = five-step, like the
    /// original's Shift+arrow (INVINSP.C:113-134).</para>
    ///
    /// <para>Layout is <b>loaded</b>, not baked: the panel rect, title and every button rect and
    /// caption come from REQ_INV2.DAT through the normal resource path, so a mod's replacement REQ
    /// moves and re-labels this screen like any other (<see cref="PickerLayout"/>). The compiled-in
    /// <see cref="PickerLayout.Shipped"/> is the fallback for a harness with no resource provider.
    /// Look comes from the shared theme: buttons are <c>.text-button</c> — the same class as every
    /// REQ button — and the panel/title are the <c>.qty-picker-*</c> classes (ClassicTheme.tss),
    /// scoped by the REQ's own <c>.colorset-*</c>. Rendering through a reusable REQ widget renderer
    /// is still a follow-up: the loader is married to its prefab composition, and these entries are
    /// ClickAreas that this screen draws as text buttons.</para>
    ///
    /// <para>Modality is two-part, mirroring DialogManager's pattern: an Exclusive
    /// <see cref="ActionLayer"/> owns keyboard/gamepad intents (Activate=accept, Cancel=cancel,
    /// MoveFocus=step, PageUp/PageDown=±5, Accelerator=S/G) — with <c>skipActivates:false</c>,
    /// because InputAdapter turns every pointer release into a Skip intent and the picker's clicks
    /// must mean only what the clicked button says — and a full-stage scrim swallows pointer events
    /// so the REQ chrome's Clickables beneath can't fire. The host must also gate its own
    /// stage-mounted gesture recogniser while the picker is up (TrickleDown sees through
    /// same-stage overlays).</para>
    /// </summary>
    internal static class QuantityPickerView {
        // REQ_INV2's action ids ARE the original's scancodes (INVINSP.C reads the same numbers off
        // the keyboard), which is why the keyboard shortcuts and the buttons line up one-to-one.
        internal const int ActionGive = 0x22;   // 'G'
        internal const int ActionDown5 = 0x51;  // PgDn
        internal const int ActionDown1 = 0x4A;  // numpad '-'
        internal const int ActionUp1 = 0x4E;    // numpad '+'
        internal const int ActionUp5 = 0x49;    // PgUp
        internal const int ActionShare = 0x1F;  // 'S'

        // Dropping the Share entry shrinks the panel: INVINSP.C:74-76 nudges it down 7 and takes 14
        // off its height (VGA px -> canonical x6). Code constants, not REQ data — they stay here.
        private const float NoShareYShift = 42f, NoShareHShrink = 84f;

        private static readonly object ResourceOwner = new object();

        /// <summary>Show the picker over <paramref name="stage"/> and await the choice.</summary>
        /// <param name="resources">Loads REQ_INV2.DAT (and so honours a mod override of it). Null
        /// falls back to the shipped layout — for harnesses with no provider.</param>
        /// <param name="abandoned">
        /// Cancelled when the screen under the picker goes away. <b>Without it an unanswered picker
        /// never finishes</b>: the only things that resolved the wait were this panel's own buttons
        /// and its input layer, so tearing the screen down left the continuation parked for ever —
        /// and with it BOTH finallys, this one and the caller's. The scrim and panel stayed
        /// allocated and the caller's <c>_pickerOpen</c> stayed true, which deadened the inventory's
        /// gesture handlers for the rest of the session while the rebuilt screen looked perfectly
        /// normal (TASK-568).
        ///
        /// <para>Cancelling resolves the wait as <b>zero</b>, which is not a special case: zero IS
        /// the cancel answer here — <c>onCancel</c> and accepting at "None" both resolve to it, and
        /// every caller already treats <c>&lt;= 0</c> as "no transfer".</para>
        /// </param>
        public static async UniTask<int> ShowAsync(VisualElement stage, InputLayerStack stack,
            int max, bool allowShare, IResourceProviderService resources = null,
            System.Threading.CancellationToken abandoned = default) {
            PickerLayout layout = await PickerLayout.LoadAsync(resources);
            var model = new QuantityPickerModel(max);
            var done = new UniTaskCompletionSource<int>();
            bool resolved = false;
            void Resolve(int v) {
                if (!resolved) {
                    resolved = true;
                    done.TrySetResult(v);
                }
            }

            // Pointer modality: a full-stage transparent scrim above everything already on the
            // stage. Clicks that miss the picker land here and die (the original's picker loop
            // simply ignores them).
            var scrim = new VisualElement {
                name = "qty_picker_scrim",
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            stage.Add(scrim);

            var panel = new VisualElement { name = "quantity_picker" };
            panel.AddToClassList("qty-picker-panel");
            panel.AddToClassList("colorset-" + layout.Colorset);
            panel.style.left = layout.Panel.x;
            panel.style.top = layout.Panel.y + (allowShare ? 0f : NoShareYShift);
            panel.style.width = layout.Panel.width;
            panel.style.height = layout.Panel.height - (allowShare ? 0f : NoShareHShrink);
            stage.Add(panel);

            var title = new Label(layout.Title) {
                name = "qty_title", pickingMode = PickingMode.Ignore,
            };
            title.AddToClassList("qty-picker-title");
            // The anchor is the CENTRE of the title (align 1 in invui_draw_text_aligned_shadow);
            // upper-center text alignment over a full-width strip achieves the same.
            title.style.left = 0f;
            title.style.right = 0f;
            title.style.top = layout.TitleY;
            panel.Add(title);

            Label give = AddButton(panel, layout, ActionGive, model.Label,
                () => Resolve(model.Value)); // accepting at 0 ("None") IS the cancel
            void Refresh() => give.text = model.Label;
            bool Shift() {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && kb.shiftKey.isPressed;
            }
            void Step(bool up, bool five) {
                if (five) { if (up) { model.StepUp5(); } else { model.StepDown5(); } }
                else { if (up) { model.StepUp(); } else { model.StepDown(); } }
                Refresh();
            }
            AddButton(panel, layout, ActionDown5, null, () => Step(up: false, five: true));
            AddButton(panel, layout, ActionDown1, null, () => Step(up: false, five: Shift()));
            AddButton(panel, layout, ActionUp1, null, () => Step(up: true, five: Shift()));
            AddButton(panel, layout, ActionUp5, null, () => Step(up: true, five: true));
            if (allowShare) {
                AddButton(panel, layout, ActionShare, null, () => Resolve(-1));
            }

            // Keyboard/gamepad, matching the original's scancode reads (INVINSP.C:112-166): arrows
            // step (left/down -, right/up +; Shift = 5), PgUp/PgDn are ±5, Enter accepts, Esc
            // cancels, G accepts and S shares. skipActivates:false — the clicks belong to the
            // buttons; without it every mouse release also accepted.
            var layer = new ActionLayer("quantity-picker",
                onActivate: () => Resolve(model.Value),
                onCancel: () => Resolve(0),
                onMove: dir => {
                    switch (dir) {
                        case NavDirection.PageUp: Step(up: true, five: true); break;
                        case NavDirection.PageDown: Step(up: false, five: true); break;
                        case NavDirection.Left:
                        case NavDirection.Down: Step(up: false, five: Shift()); break;
                        default: Step(up: true, five: Shift()); break;
                    }
                },
                skipActivates: false,
                onAccelerator: c => {
                    switch (char.ToLowerInvariant(c)) {
                        case 'g': Resolve(model.Value); return true;
                        // INVINSP.C:111-169: Space ends the loop with the value (0x39); ',' and '.'
                        // step like the arrows (0x33 / 0x34), Shift for five.
                        case ' ': Resolve(model.Value); return true;
                        case ',': Step(up: false, five: Shift()); return true;
                        case '.': Step(up: true, five: Shift()); return true;
                        case 's':
                            if (!allowShare) { return false; }
                            Resolve(-1);
                            return true;
                        default: return false;
                    }
                });
            stack?.Push(layer);

            // Registered AFTER everything above so the teardown below always has a panel, a scrim
            // and a layer to take down -- and disposed by the `using` before the finally runs, so a
            // cancel arriving during teardown cannot re-enter Resolve.
            using (abandoned.Register(() => Resolve(0))) {
                try {
                    return await done.Task;
                } finally {
                    stack?.Remove(layer);
                    panel.RemoveFromHierarchy();
                    scrim.RemoveFromHierarchy();
                    resources?.ReleaseAssets(ResourceOwner);
                }
            }
        }

        // A picker button IS a REQ text button (.text-button + .req-element) so the raised tan
        // bevel, font and shadow match every other REQ screen for free. `caption` overrides the
        // REQ's own label — only the value button does that, since its text is the model's.
        //
        // Returns the CAPTION child rather than the chrome: that is where the text lives, and it is
        // what the caller rewrites on every step.
        private static Label AddButton(VisualElement panel, PickerLayout layout, int actionId,
            string caption, System.Action onClick) {
            var button = new Label {
                name = $"qty_btn_{actionId}", pickingMode = PickingMode.Position,
            };
            button.AddToClassList("text-button");
            // The caption is a child so the stretch does not take the bevel with it. This element
            // stays a Label because the value button's text is read back from it — through the
            // caption now, not off the chrome.
            Label text = GameFontText.Caption(button, caption ?? layout.LabelOf(actionId));
            button.AddToClassList("req-element");
            Rect rect = layout.RectOf(actionId);
            button.style.left = rect.x;
            button.style.top = rect.y;
            button.style.width = rect.width;
            button.style.height = rect.height;
            button.AddManipulator(new Clickable(onClick));
            panel.Add(button);
            // *** The CAPTION, not the chrome. *** The value button's text is rewritten every step
            // (Refresh), and the chrome stopped carrying text when the caption moved into a child —
            // returning the chrome left the picker showing its opening value for ever.
            return text;
        }

        /// <summary>
        /// REQ_INV2.DAT reduced to what the picker draws. Loaded through
        /// <see cref="IResourceProviderService"/>, so the override provider gets first refusal and
        /// a mod's REQ_INV2 reshapes the picker with no code change. Anything the loaded REQ does
        /// not supply falls back to <see cref="Shipped"/>, so a truncated override degrades to the
        /// original geometry rather than collapsing to zero-sized buttons.
        /// </summary>
        internal sealed class PickerLayout {
            private readonly UserInterface _req;

            private PickerLayout(UserInterface req) {
                _req = req;
            }

            /// <summary>The compiled-in fallback — generated/REQ/REQ_INV2.json, canonical px.</summary>
            internal static PickerLayout Shipped { get; } = new PickerLayout(null);

            internal static async UniTask<PickerLayout> LoadAsync(IResourceProviderService resources) {
                if (resources == null) {
                    return Shipped;
                }
                UserInterface req = await resources.LoadAssetAsync<UserInterface>(
                    "REQ_INV2.DAT", ResourceOwner);
                return req == null ? Shipped : new PickerLayout(req);
            }

            internal Rect Panel => _req == null
                ? new Rect(530f, 210f, 530f, 408f)
                : new Rect(_req.XPosition, _req.YPosition, _req.Width, _req.Height);

            /// <summary>Title anchor: the page's own offset (the menupage title draw origin).</summary>
            internal float TitleY => _req?.YOffset ?? 18f;

            internal string Title => string.IsNullOrEmpty(_req?.Title) ? "Select amount:" : _req.Title;

            internal int Colorset => (int)(_req?.Colorset ?? GameData.Resources.Menu.Colorset.Picker);

            internal Rect RectOf(int actionId) {
                UiElement e = Entry(actionId);
                return e == null
                    ? ShippedRect(actionId)
                    : new Rect(e.XPosition, e.YPosition, e.Width, e.Height);
            }

            internal string LabelOf(int actionId) {
                UiElement e = Entry(actionId);
                return string.IsNullOrEmpty(e?.Label) ? ShippedLabel(actionId) : e.Label;
            }

            private UiElement Entry(int actionId) {
                UiElement[] entries = _req?.MenuEntries;
                if (entries == null) {
                    return null;
                }
                foreach (UiElement e in entries) {
                    if (e != null && e.ActionId == actionId) {
                        return e;
                    }
                }
                return null;
            }

            // generated/REQ/REQ_INV2.json — page-relative, canonical px.
            private static Rect ShippedRect(int actionId) => actionId switch {
                ActionGive => new Rect(55f, 96f, 400f, 78f),
                ActionDown5 => new Rect(55f, 192f, 80f, 78f),
                ActionDown1 => new Rect(155f, 192f, 80f, 78f),
                ActionUp1 => new Rect(275f, 192f, 80f, 78f),
                ActionUp5 => new Rect(375f, 192f, 80f, 78f),
                ActionShare => new Rect(55f, 288f, 400f, 78f),
                _ => Rect.zero,
            };

            private static string ShippedLabel(int actionId) => actionId switch {
                ActionDown5 => "<<",
                ActionDown1 => "<",
                ActionUp1 => ">",
                ActionUp5 => ">>",
                ActionShare => "Share with party",
                _ => string.Empty, // the value button's caption is the model's, never the REQ's
            };
        }
    }
}
