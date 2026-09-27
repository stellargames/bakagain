namespace BakAgain.UI.Inventory {
    using System.Collections.Generic;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Layout;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The item-inspect layout drawn by <c>UI_showItem</c> @0x5A778 while an item's description is
    /// up: the inspected item's icon centred over the left panel, with its name, type line and
    /// status line beneath. The description prose itself is a DDX dialog and is not this class's
    /// business — see <see cref="InventoryMenu"/>.
    ///
    /// <para>Replaces the grid for the duration: the original does not call <c>UI_DrawInventory</c>
    /// in this state, so the caller clears the cells first and re-renders them on close.</para>
    ///
    /// <para><b>This class owns no coordinates.</b> The icon's resting point, the four text-line
    /// positions, the drop-shadow offset and the icon flight's step granularity all come from
    /// <see cref="InventoryLayout"/> — the engine-independent geometry hung off the screen's REQ
    /// resource. What stays here is behaviour: which line a name is drawn on, the easing, and the
    /// pens. All four text lines are drawn with <c>align == 1</c>, which
    /// <c>invui_draw_text_aligned_shadow</c> implements as <c>x -= width/2</c> — i.e. each hint's
    /// Left is the line's CENTRE, not its left edge, which is why the labels carry a -50%
    /// translate.</para>
    /// </summary>
    internal sealed class ItemInspectPanel {
        // invui_draw_text_aligned_shadow defaults: fg = pen 0x9F when the caller passes -1 (the
        // inspect view does), and a shadow drawn one pixel down-right in colour 0 (how far "one
        // pixel" is, is InventoryLayout.TextShadowOffsetX/Y). Shared with the grid's quantity
        // labels and the money readout — see InventoryTextStyle for why they are one colour.
        private static readonly Color TextColor = InventoryTextStyle.DefaultTextColor;
        private static readonly Color ShadowColor = InventoryTextStyle.ShadowColor;

        private readonly List<VisualElement> _elements = new();
        private IResourceProviderService _resources;
        private GameData.Resources.Palette.PaletteResource _palette;
        private VisualElement _icon;
        // The model's own defaults ARE the faithful geometry, so this is meaningful before the
        // screen's REQ resource has landed (and for a REQ that carries no inventory block).
        private InventoryLayout _layout = new();
        private Vector2 _iconFrom;     // where the flight starts, in the screen's own space
        private Vector2 _iconPosition; // where the icon currently sits, so a fly-back knows its start
        // True once _iconPosition holds a real position in this panel's own space — i.e. the flight
        // actually ran. It does not when the icon's resting point is authored in a unit this class
        // cannot resolve (see TryResolveIconRestingPoint), and a fly-back with no known start would
        // animate from a stale or made-up place, so it is skipped too.
        private bool _iconPositionKnown;
        private UniTaskCompletionSource _flightDone = new();
        private int _generation;

        /// <summary>Remove everything this panel added and release any loaded icon handle. Safe to
        /// call repeatedly and before anything has been rendered.</summary>
        internal void Clear() {
            _generation++; // invalidate any in-flight icon load
            foreach (VisualElement e in _elements) {
                e.RemoveFromHierarchy();
            }
            _elements.Clear();
            _icon = null;
            _iconPositionKnown = false;
            // Release any awaiter — a cleared panel has no flight to finish, and leaving the source
            // unresolved would hang the caller that awaits it.
            _flightDone.TrySetResult();
            _resources?.ReleaseAssets(this);
            _resources = null;
        }

        /// <summary>
        /// Completes when the icon has finished flying to its inspect position — or immediately if
        /// there is no icon to fly. The flight itself is started by <see cref="AddIconAsync"/> as
        /// soon as the sprite lands, so nothing here has to poll for it.
        /// </summary>
        internal UniTask WaitForIconFlightAsync() => _flightDone.Task;

        /// <summary>
        /// Fly the icon from <paramref name="from"/> to <paramref name="to"/>, one
        /// <see cref="StepToward"/> per frame — <c>invinspect_animate_item_move</c> @0x5A0CB.
        ///
        /// <para><b>The flight moves on a lattice, and that is deliberate.</b> The easing is
        /// "advance a twelfth of the remaining distance, plus one step" — the <c>+1</c> puts a
        /// FLOOR on the speed and is what guarantees arrival once integer division stops
        /// contributing. So the step size is not a rounding detail: halve it and the tail of the
        /// flight takes twice as many frames at half the speed, which is visible. The size comes
        /// from <see cref="InventoryLayout.IconFlightStepX"/>/<c>Y</c>, so this method works in
        /// whole steps and converts back only to place the element. With the shipped data one step
        /// is one of the original's pixels and the pacing is identical to the original's; an
        /// override that wants a genuinely continuous flight sets the step to 1.</para>
        ///
        /// <para><b>Both endpoints and the step are design-frame px</b> — the space
        /// <paramref name="from"/> arrives in and the space the icon is placed in. Mixing units
        /// here would fly the icon to the wrong place, so the resting point is resolved into this
        /// space (and rejected if it cannot be) before the flight starts — see
        /// <see cref="TryResolveIconRestingPoint"/>.</para>
        ///
        /// <para>Driven by the element's own <c>schedule</c> rather than an external frame loop:
        /// that is UI Toolkit's native mechanism for per-frame work, and the scheduled item belongs
        /// to the element, so it stops on its own when the icon leaves the panel instead of needing
        /// a generation guard to notice. A USS <c>transition</c> or <c>experimental.animation</c>
        /// would be more declarative still, but both impose their own interpolation and can't
        /// reproduce the original's integer delta/12 +/- 1 easing.</para>
        /// </summary>
        private void StartIconFlight(VisualElement icon, Vector2 from, Vector2 to) {
            float stepX = StepSize(_layout.IconFlightStepX);
            float stepY = StepSize(_layout.IconFlightStepY);
            int x = Mathf.RoundToInt(from.x / stepX);
            int y = Mathf.RoundToInt(from.y / stepY);
            int toX = Mathf.RoundToInt(to.x / stepX);
            int toY = Mathf.RoundToInt(to.y / stepY);
            Place(icon, x, y, stepX, stepY);
            if (x == toX && y == toY) {
                _flightDone.TrySetResult();
                return;
            }
            IVisualElementScheduledItem flight = null;
            flight = icon.schedule.Execute(() => {
                x = StepToward(x, toX);
                y = StepToward(y, toY);
                Place(icon, x, y, stepX, stepY);
                if (x == toX && y == toY) {
                    flight?.Pause();
                    _flightDone.TrySetResult();
                }
            }).Every(0); // every panel update — the original steps once per presented frame
        }

        // A zero step would make the lattice degenerate and the flight never terminate, so it
        // degrades to the finest step there is. Negative is read as its magnitude — a step is a
        // distance, and the direction is StepToward's business.
        private static float StepSize(float step) {
            float size = Mathf.Abs(step);
            return size > 0.0001f ? size : 1f;
        }

        /// <summary>
        /// The icon's resting point, in this panel's own space (design-frame px), or false when it
        /// is not expressible there.
        ///
        /// <para>A percentage is a fraction of a parent whose size only a completed layout pass
        /// knows. This class deliberately measures nothing — same rule as
        /// <see cref="LayoutApplier"/>, which is a translator and not a layout engine — so it
        /// cannot resolve one, and the flight's start point, step size and placement are all plain
        /// px. Rather than mix the two and animate to the wrong place, say so loudly and let the
        /// caller skip the flight: the icon then simply appears at the resting position
        /// <see cref="AddIconAsync"/> already wrote, which is still correct, and its parked
        /// position cannot disagree with its resting style.</para>
        /// </summary>
        private static bool TryResolveIconRestingPoint(LayoutHint at, out Vector2 point) =>
            LayoutApplier.TryResolvePoint(at, "InventoryLayout.InspectIcon",
                "the inspect icon's fly-in runs in design-frame px and cannot resolve a percentage "
                + "without measuring the parent, so the flight is skipped and the icon is placed "
                + "directly at its resting point",
                out point);

        /// <summary>
        /// Fly the icon back out to <paramref name="to"/> — the grid cell it came from — and
        /// complete when it lands. This is the tail of <c>invinspect_item_flow</c>
        /// (INVINSP.C:430 / <c>LAB_5a82_0d2c</c>): once the description (or the stat block) has been
        /// dismissed, the SAME animation runs with its endpoints swapped, so the item visibly
        /// returns to the grid before the inventory screen is redrawn.
        ///
        /// <para>Completes immediately when there is no icon — a caller can always await it.</para>
        /// </summary>
        internal UniTask FlyIconBackAsync(Vector2 to) {
            // _iconPositionKnown false means the fly-IN never ran (an unresolvable resting point),
            // so there is no start for the fly-back either — and inventing one would be worse than
            // not animating. AddIconAsync has already logged why.
            if (_icon == null || _icon.panel == null || !_iconPositionKnown) {
                return UniTask.CompletedTask;
            }
            _flightDone = new UniTaskCompletionSource();
            StartIconFlight(_icon, _iconPosition, to);
            return _flightDone.Task;
        }

        // Position on the flight lattice -> the element's inset. Design-frame px, always: the
        // lattice, both endpoints and the panel's own drawing space are px, and a length computed
        // from three px quantities is a px length whatever unit the resting hint happens to use.
        // (TryResolveIconRestingPoint has already refused any hint that is not px-expressible, so
        // this can never disagree with the resting style AddIconAsync wrote.)
        private void Place(VisualElement icon, int xSteps, int ySteps, float stepX, float stepY) {
            _iconPosition = new Vector2(xSteps * stepX, ySteps * stepY);
            icon.style.left = LayoutApplier.ToStyleLength(LayoutLength.Px(_iconPosition.x));
            icon.style.top = LayoutApplier.ToStyleLength(LayoutLength.Px(_iconPosition.y));
            _iconPositionKnown = true;
        }

        /// <summary>
        /// Draw the inspect layout for one item. <paramref name="affecting"/> gates the "Using"
        /// prefix on the status line (set for a party member's own inventory, clear for loot/shop).
        /// <paramref name="layout"/> is the screen resource's <see cref="InventoryLayout"/>
        /// geometry — null is not an error, the model's own defaults are the faithful values.
        /// <paramref name="from"/> is where the icon flies in from, in the same space this panel
        /// draws in (the item's grid cell centre).
        /// </summary>
        internal void Render(VisualElement root, RuntimeItem item, ObjectInfo obj, bool affecting,
            IResourceProviderService resources, InventoryLayout layout, Vector2 from) {
            Clear();
            _flightDone = new UniTaskCompletionSource(); // Clear() completed the previous one
            _layout = layout ?? new InventoryLayout();
            _shadowInsetWarned = false;
            _iconFrom = from;
            if (root == null || item == null || obj == null) {
                _flightDone.TrySetResult();
                return;
            }

            // The conditional is CONTENT, not geometry: both line positions are data, and what is
            // decided here is only which of them this particular name needs. A name that fits on
            // one line is faithfully drawn on the SECOND line, not the first — so a short name
            // sits lower than the first line of a long one, which is the original's behaviour and
            // not something the layout vocabulary should try to express.
            IReadOnlyList<string> nameLines = ItemInspectText.NameLines(obj);
            if (nameLines.Count > 1) {
                AddText(root, nameLines[0], _layout.InspectNameFirstLine);
                AddText(root, nameLines[1], _layout.InspectNameSecondLine);
            } else {
                AddText(root, nameLines[0], _layout.InspectNameSecondLine);
            }

            string typeLine = ItemInspectText.TypeLine(item, obj);
            if (typeLine != null) {
                AddText(root, typeLine, _layout.InspectTypeLine);
            }

            string status = ItemInspectText.StatusLine(item, obj, affecting);
            if (!string.IsNullOrEmpty(status)) {
                AddText(root, status, _layout.InspectStatusLine);
            }

            _resources = resources;
            AddIconAsync(root, obj, item.ItemFlags, _generation).Forget();
            LoadPaletteAsync(_generation).Forget();
        }

        /// <summary>
        /// Draw the "More Info" stat lines produced by <see cref="ItemStatsText"/>
        /// (<c>UI_showItemStats</c> @0x5A1DA). Labels and values invert each other's pens: a label is
        /// pen 0 (black) text under a pen 0x0B shadow, a value is pen 0x9F text under a black shadow
        /// — that's <c>invui_draw_text_aligned_shadow</c>'s fg/shadow arguments, where -1 means
        /// "default" (0x9F for the text, 0 for the shadow).
        /// </summary>
        internal void RenderStats(VisualElement root, IReadOnlyList<ItemStatsText.Line> lines) {
            if (root == null || lines == null) {
                return;
            }
            foreach (ItemStatsText.Line line in lines) {
                Color fg = line.IsLabel ? Pen(0, ShadowColor) : Pen(0x9F, TextColor);
                Color shadow = line.IsLabel ? Pen(0x0B, TextColor) : ShadowColor;
                // Stat lines are a cursor walk, not named boxes (see ItemStatsText), so their
                // positions arrive as plain design-frame numbers and are wrapped into a hint here.
                AddText(root, line.Text,
                    new LayoutHint { Left = LayoutLength.Px(line.X), Top = LayoutLength.Px(line.Y) },
                    fg, shadow, line.Centred);
            }
        }

        /// <summary>
        /// One frame of the icon's travel between its grid cell and the inspect position, from
        /// <c>invinspect_animate_item_move</c> @0x5A0CB: move a twelfth of the remaining distance,
        /// plus one step in the direction of travel. The <c>+/-1</c> both sets a floor on the speed
        /// and guarantees termination — integer division alone would stall once the gap dropped
        /// below 12. Positions are counted in whole STEPS, not lengths: how long a step is comes
        /// from <see cref="InventoryLayout.IconFlightStepX"/>/<c>Y</c>, so this stays the
        /// original's integer easing at any granularity.
        /// </summary>
        internal static int StepToward(int current, int target) {
            int delta = target - current;
            if (delta == 0) {
                return current;
            }
            int next = current + (delta > 0 ? delta / StepDivisor + 1 : delta / StepDivisor - 1);
            // The +/-1 can overshoot on the last frame; the original's loop ends on delta == 0, so
            // clamp rather than oscillate around the target.
            return delta > 0 ? System.Math.Min(next, target) : System.Math.Max(next, target);
        }

        private const int StepDivisor = 12; // 0xC

        /// <summary>Resolve an INVENTOR.PAL pen index, falling back when the palette isn't loaded.</summary>
        private Color Pen(int index, Color fallback) =>
            BakAgain.Graphics.PaletteColors.ResolvePen(_palette, index, fallback);

        // A line plus its drop shadow. Centring uses translate(-50%) so it holds whatever the
        // measured text width turns out to be — MeasureTextSize reads 0 before layout.
        private void AddText(VisualElement root, string text, LayoutHint at) =>
            AddText(root, text, at, TextColor, ShadowColor, centred: true);

        private void AddText(VisualElement root, string text, LayoutHint at,
            Color fg, Color shadowColor, bool centred) {
            AddLabel(root, text, at, shadowColor, shadow: true, centred);
            AddLabel(root, text, at, fg, shadow: false, centred);
        }

        /// <summary>
        /// A shadow label's inset: the line's own inset plus the drop-shadow offset. These lines are
        /// pinned from the left/top, so displacing the shadow down-right is a positive offset on
        /// both axes. The arithmetic, and the refusal when the line is positioned in percent, are
        /// <see cref="LayoutApplier.ShadowInset"/>'s — shared with the money readout.
        /// </summary>
        private StyleLength ShadowInset(LayoutLength inset, float offset, string property) =>
            LayoutApplier.ShadowInset(inset, offset, "InventoryLayout." + property,
                "this inspect line's text shadow is drawn without its offset",
                ref _shadowInsetWarned);

        // One complaint per render, not one per label: four lines x two axes would be eight
        // identical errors for a single authoring mistake.
        private bool _shadowInsetWarned;

        private void AddLabel(VisualElement root, string text, LayoutHint at, Color color,
            bool shadow, bool centred = true) {
            LayoutLength left = at?.Left ?? LayoutLength.Auto;
            LayoutLength top = at?.Top ?? LayoutLength.Auto;
            StyleLength x = shadow
                ? ShadowInset(left, _layout.TextShadowOffsetX, "TextShadowOffsetX")
                : LayoutApplier.Derived(left.Value, left);
            StyleLength y = shadow
                ? ShadowInset(top, _layout.TextShadowOffsetY, "TextShadowOffsetY")
                : LayoutApplier.Derived(top.Value, top);
            var label = new Label(text) {
                name = shadow ? "inspect_text_shadow" : "inspect_text",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                    color = color,
                    // align == 1 in the original == x -= width/2; align == 0 draws from x.
                    translate = centred
                        ? new StyleTranslate(new Translate(Length.Percent(-50f), 0f))
                        : new StyleTranslate(new Translate(0f, 0f)),
                },
            };
            // Size + aspect correction live in one place for all game-font text (task-45). The text
            // is positioned by the top of its glyph cell, so the stretch anchors there.
            GameFontText.Apply(label,
                centred ? GameFontText.AnchorX.Centre : GameFontText.AnchorX.Left,
                GameFontText.AnchorY.Top);
            root.Add(label);
            _elements.Add(label);
        }

        // INVENTOR.PAL backs the pens the panel draws with. Loaded lazily alongside the icon so the
        // stat labels can use the real pen 0/0x0B pair instead of hardcoded RGB; until it lands (or
        // if it fails) Pen() falls back to the known-good body colours.
        private async UniTaskVoid LoadPaletteAsync(int generation) {
            if (_resources == null) {
                return;
            }
            var pal = await _resources.LoadAssetAsync<GameData.Resources.Palette.PaletteResource>(
                "INVENTOR.PAL", this);
            if (generation == _generation) {
                _palette = pal;
            }
        }

        // The icon is centred on InventoryLayout.InspectIcon at its native size — the same "native
        // size, centred" treatment the grid gives item icons, just anchored to a point instead of
        // a cell.
        private async UniTaskVoid AddIconAsync(VisualElement root, ObjectInfo obj, ushort itemFlags,
            int generation) {
            // Same key resolution the grid uses — ItemIconResolver is the single owner of the
            // ObjectInfo -> "INVSHP?.BMX#n" mapping, INCLUDING the lit-torch override, so the
            // inspect view cannot disagree with the cell it was opened from (TASK-583).
            string key = ItemIconResolver.ResolveBmxSubResource(obj, itemFlags);
            if (key == null || _resources == null) {
                _flightDone.TrySetResult(); // no icon to fly
                return;
            }
            Sprite sprite = await _resources.LoadAssetAsync<Sprite>(key, this);
            if (sprite == null || generation != _generation || root.panel == null) {
                _flightDone.TrySetResult(); // superseded/cleared while awaiting, or torn down
                return;
            }
            LayoutHint at = _layout.InspectIcon;
            var icon = new VisualElement {
                name = "inspect_icon",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    // Resting place; StartIconFlight immediately re-places it at the flight's
                    // start, and every frame after that.
                    left = LayoutApplier.ToStyleLength(at?.Left ?? LayoutLength.Auto),
                    top = LayoutApplier.ToStyleLength(at?.Top ?? LayoutLength.Auto),
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                    // The hint's Left/Top is the icon's CENTRE, as it is for the text lines.
                    translate = new StyleTranslate(new Translate(Length.Percent(-50f), Length.Percent(-50f))),
                    backgroundImage = new StyleBackground(sprite),
                },
            };
            _icon = icon;
            root.Add(icon);
            _elements.Add(icon);
            if (!TryResolveIconRestingPoint(at, out Vector2 to)) {
                // No flight: the icon keeps the resting style written above, which is where it
                // would have ended up anyway. Release the awaiter so the screen still proceeds.
                _flightDone.TrySetResult();
                return;
            }
            // The icon exists now, so the flight can start immediately — no polling for it.
            StartIconFlight(icon, _iconFrom, to);
        }
    }
}
