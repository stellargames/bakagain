namespace BakAgain.UI.Layout {
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Translates the engine-independent <see cref="LayoutHint"/> onto UI Toolkit styles.
    ///
    /// <para>This is a translator, not a layout engine: it contains no arithmetic, no solver and
    /// no branching on screen size. UI Toolkit already does the layout; the data only has to say
    /// what it wants. If this file ever needs a calculation or a <c>Screen.width</c> check, the
    /// vocabulary in GameData was insufficient — extend the vocabulary rather than this file.</para>
    /// </summary>
    public static class LayoutApplier {
        /// <summary>Converts a model length into a UI Toolkit style length. Auto maps to the
        /// Auto keyword rather than a zero value, so intrinsic sizing still applies.</summary>
        public static StyleLength ToStyleLength(LayoutLength length) {
            switch (length.Unit) {
                case LayoutLengthUnit.Px:
                    return new StyleLength(new UnityEngine.UIElements.Length(length.Value, LengthUnit.Pixel));
                case LayoutLengthUnit.Percent:
                    return new StyleLength(new UnityEngine.UIElements.Length(length.Value, LengthUnit.Percent));
                default:
                    return new StyleLength(StyleKeyword.Auto);
            }
        }

        /// <summary>
        /// A length a view COMPUTED (a sum, an offset, a snapped animation position), carried in
        /// the same unit as the datum it was derived from — so a percentage-authored override
        /// stays a percentage instead of being silently flattened to px.
        ///
        /// <para><see cref="LayoutLengthUnit.Auto"/> is not a unit an offset can be expressed in
        /// ("intrinsic" says nothing about where something sits), so it degrades to the design
        /// frame's own px, which is the space every extracted value is already in.</para>
        /// </summary>
        public static StyleLength Derived(float value, LayoutLength source) =>
            ToStyleLength(new LayoutLength(value,
                source.Unit == LayoutLengthUnit.Auto ? LayoutLengthUnit.Px : source.Unit));

        /// <summary>
        /// Can this length take part in design-frame px arithmetic? Auto can: it carries no
        /// opinion and <see cref="Derived"/> already degrades it to the frame's own px, which is
        /// the space every extracted value is in. A percentage cannot — it is a fraction of a
        /// parent the caller has not measured, and "18.125% + 5px" is not a length any single unit
        /// can carry.
        ///
        /// <para>The predicate lives here, next to <see cref="Derived"/>, because it is the guard
        /// that decides whether <see cref="Derived"/> may be called at all — a view that sums two
        /// lengths asks this first and refuses when it answers false
        /// (<see cref="RefuseUnresolvable"/>).</para>
        /// </summary>
        public static bool IsDesignPx(LayoutLength length) =>
            length.Unit != LayoutLengthUnit.Percent;

        /// <summary>
        /// Say NO to an authored value this view cannot resolve, and say which field and why. One
        /// place, so every refusal across the UI reads the same and always names the datum a mod
        /// author would have to edit.
        ///
        /// <para>An error, not a warning, and the caller degrades to something the data really
        /// does state rather than stamping two incompatible units together — silently adding a
        /// percentage's bare number to a px value is the one outcome ruled out, because it puts
        /// the element somewhere neither the author nor the original asked for, with no
        /// diagnostic.</para>
        /// </summary>
        public static void RefuseUnresolvable(string field, string authored, string consequence) =>
            Debug.LogError(field + " cannot be resolved as authored (" + authored + "): "
                + consequence + ". Author it in design-frame px to get the original behaviour back.");

        /// <summary>
        /// An inset displaced by a design-frame px offset — the shape every drop shadow needs, since
        /// the original draws shadowed text as the SAME string twice, the second copy one original
        /// pixel down-right (<c>invui_draw_text_aligned_shadow</c>, INVENTOR.C:161). How far "one
        /// pixel" is, is data (<c>InventoryLayout.TextShadowOffsetX</c>/<c>Y</c>).
        ///
        /// <para>The sum only has a value when the inset is design-frame px: a percentage is a
        /// fraction of a parent the caller has not measured, and "18.125% + 5px" is not a length any
        /// single unit can carry. When it isn't, this returns the inset unchanged — the text's own
        /// position — and refuses once via <paramref name="warned"/>, rather than stamping the px
        /// number onto the percentage and producing a 5%-wide displacement nobody asked for.</para>
        ///
        /// <para><paramref name="offset"/> is signed by the CALLER, not by this method: displacing
        /// a <c>Left</c> inset one pixel right means +1, while displacing a <c>Right</c> inset the
        /// same direction means -1. Only the caller knows which edge its element is pinned from.
        /// </para>
        ///
        /// <para><paramref name="warned"/> is a caller-owned once-flag so a screen drawing several
        /// shadowed lines reports one authoring mistake, not one per line per axis.</para>
        /// </summary>
        public static StyleLength ShadowInset(LayoutLength inset, float offset, string field,
            string consequence, ref bool warned) {
            if (!IsDesignPx(inset)) {
                if (!warned) {
                    warned = true;
                    RefuseUnresolvable(field, inset.ToString(), consequence);
                }
                return Derived(inset.Value, inset);
            }
            return Derived(inset.Value + offset, inset);
        }

        /// <summary>
        /// Resolve a hint's <c>Left</c>/<c>Top</c> to a point in design-frame px, or refuse.
        ///
        /// <para>For the callers that do px ARITHMETIC on a position — an animation walking toward
        /// it, a cursor adding column offsets to it — rather than handing it to UI Toolkit. Those
        /// callers measure nothing, so a percentage is unresolvable for them even though
        /// <see cref="Apply"/> would pass it straight through. Each supplies its own
        /// <paramref name="consequence"/> and picks its own degradation on false; what is shared is
        /// the predicate, the Auto-reads-as-zero rule, and the one refusal wording.</para>
        /// </summary>
        public static bool TryResolvePoint(LayoutHint hint, string field, string consequence,
            out Vector2 point) {
            LayoutLength left = hint?.Left ?? LayoutLength.Auto;
            LayoutLength top = hint?.Top ?? LayoutLength.Auto;
            if (!IsDesignPx(left) || !IsDesignPx(top)) {
                point = Vector2.zero;
                RefuseUnresolvable(field, left + ", " + top, consequence);
                return false;
            }
            // Auto reads as 0 — a point at the origin, not an error.
            point = new Vector2(left.Value, top.Value);
            return true;
        }

        /// <summary>Applies a hint to an element. A null hint leaves the element untouched.</summary>
        public static void Apply(VisualElement element, LayoutHint layout) {
            if (element == null || layout == null) {
                return;
            }

            // Auto is "no opinion", not "reset to auto": Apply must be layerable onto an
            // element another system already sized, so it writes only explicit lengths.
            if (layout.Width.Unit != LayoutLengthUnit.Auto) {
                element.style.width = ToStyleLength(layout.Width);
            }

            if (layout.Height.Unit != LayoutLengthUnit.Auto) {
                element.style.height = ToStyleLength(layout.Height);
            }

            if (layout.AspectRatio.HasValue) {
                ApplyAspectRatio(element, layout.AspectRatio.Value, layout.Width, layout.Height);
            }

            // Position (how THIS element is placed in its parent) and Flow (how it lays out
            // ITS OWN children) are independent concerns — an absolutely-placed element can
            // still be a flex container for its children (the credits row: pinned at fixed
            // insets in the scroller, and a flex row for role/leader/name). So there is no
            // either/or branch here: Position always runs, Flow always runs when present.
            if (layout.Position == LayoutPosition.Absolute) {
                element.style.position = Position.Absolute;
                // Order is load-bearing: ApplyInsets must run after ApplyAnchor so an explicit
                // inset overrides whatever the anchor pinned, not the other way around.
                ApplyAnchor(element, layout.Anchor);
                ApplyInsets(element, layout);
            } else {
                element.style.position = Position.Relative;
                WarnIfAnchorOrInsetsSetWhileInFlow(element, layout);
            }

            if (layout.Flow != null) {
                ApplyFlow(element, layout.Flow);
            }

            if (layout.Padding != null) {
                ApplyPadding(element, layout.Padding);
            }

            RefuseSlice(layout);
        }

        /// <summary>
        /// <see cref="LayoutHint.Slice"/> has no implementation, so an author who sets one is told
        /// rather than left wondering.
        /// </summary>
        /// <remarks>
        /// Nine-slice is in the authoring vocabulary and nowhere else: no extractor writes it and
        /// nothing renders it. The field is on every <see cref="LayoutHint"/>, so it serializes
        /// into 315 keys across <c>generated/</c> — but <b>every one of them is the default</b>,
        /// which is why refusing costs nothing on shipped data. An earlier note held this guard
        /// back believing the shipped values were non-default and the refusal would fire on every
        /// screen; they are all (0,0,0,0), so it fires only for the author who sets one.
        ///
        /// <para>Same <see cref="RefuseUnresolvable"/> convention as the percentage insets and
        /// <see cref="RefuseGap"/>: say which field, and what the picture will look like instead.
        /// A refused slice still leaves the element sized and placed — only the border artwork is
        /// missing, which degrades to a plain stretched background.</para>
        /// </remarks>
        private static void RefuseSlice(LayoutHint layout) {
            NineSlice slice = layout.Slice;
            if (slice.Left == 0 && slice.Top == 0 && slice.Right == 0 && slice.Bottom == 0) {
                return;
            }

            RefuseUnresolvable("LayoutHint.Slice",
                $"({slice.Left}, {slice.Top}, {slice.Right}, {slice.Bottom})",
                "nine-slice borders are not implemented, so the element's background is drawn "
                + "stretched rather than sliced — the element is still sized and placed");
        }

        // Position.InFlow means the parent's layout places this element — an anchor or an
        // explicit inset would be contradictory input (both are "how I place myself against my
        // parent's edges", which is exactly what InFlow hands off to the parent). Per the same
        // over-constrained-input convention as ApplyAspectRatio: ignore the anchor/insets
        // (already skipped — this method only runs when the Absolute branch above did not) and
        // warn once, rather than throw.
        private static void WarnIfAnchorOrInsetsSetWhileInFlow(VisualElement element, LayoutHint layout) {
            bool explicitInset = layout.Left.Unit != LayoutLengthUnit.Auto
                || layout.Top.Unit != LayoutLengthUnit.Auto
                || layout.Right.Unit != LayoutLengthUnit.Auto
                || layout.Bottom.Unit != LayoutLengthUnit.Auto;
            bool nonDefaultAnchor = layout.Anchor != LayoutAnchor.TopLeft;

            if (explicitInset || nonDefaultAnchor) {
                Debug.LogWarning(
                    $"LayoutApplier: ignoring Anchor/insets on '{element.name}' because Position " +
                    "is InFlow — an in-flow element is placed by its parent's layout, not an " +
                    "anchor or inset, so that over-constrains the element.");
            }
        }

        // UI Toolkit's style.aspectRatio (UnityEngine.UIElements.Ratio/StyleRatio) is a real,
        // verified native property — not the guess an earlier spec draft carried — so honouring
        // AspectRatio is a straight translation, same as Width/Height. Over-constraining (both
        // Width and Height explicit) is ignored with a single warning per the spec's error table.
        private static void ApplyAspectRatio(VisualElement element, LayoutAspectRatio aspectRatio, LayoutLength width, LayoutLength height) {
            bool bothExplicit = width.Unit != LayoutLengthUnit.Auto && height.Unit != LayoutLengthUnit.Auto;
            if (bothExplicit) {
                Debug.LogWarning(
                    $"LayoutApplier: ignoring AspectRatio ({aspectRatio}) on '{element.name}' because " +
                    "both Width and Height are explicit — that over-constrains the element.");
                return;
            }

            // aspectRatio.Ratio throws if Height <= 0 — the case a default(LayoutAspectRatio)
            // produces (LayoutHint.AspectRatio = default gives HasValue == true but no real
            // ratio). Treat that the same as the over-constrained case: warn and skip, don't
            // let a malformed override crash element construction. The type itself keeps the
            // throw (Parse/the constructor already reject this) — only this call site guards it.
            if (aspectRatio.Height <= 0f) {
                Debug.LogWarning(
                    $"LayoutApplier: ignoring AspectRatio on '{element.name}' because it is an " +
                    "uninitialised (default) value with no usable ratio.");
                return;
            }

            element.style.aspectRatio = new StyleRatio(new Ratio(aspectRatio.Ratio));
        }

        // Anchoring is expressed as which edges the element pins to, plus which axes are
        // centred. Only the pinned/centred edges are written; the others are left Null so an
        // absolute position set elsewhere still wins. UI Toolkit's absolute positioning has no
        // implicit centering, so a centred axis needs both a 50% inset AND a -50% translate on
        // that axis (verified against the live Unity 6 API: IStyle.translate/StyleTranslate
        // accepts percentage Length values) — inset alone would only move the element's edge,
        // not its center, to the midpoint.
        private static void ApplyAnchor(VisualElement element, LayoutAnchor anchor) {
            bool left = anchor == LayoutAnchor.TopLeft || anchor == LayoutAnchor.MiddleLeft || anchor == LayoutAnchor.BottomLeft;
            bool right = anchor == LayoutAnchor.TopRight || anchor == LayoutAnchor.MiddleRight || anchor == LayoutAnchor.BottomRight;
            bool top = anchor == LayoutAnchor.TopLeft || anchor == LayoutAnchor.TopCenter || anchor == LayoutAnchor.TopRight;
            bool bottom = anchor == LayoutAnchor.BottomLeft || anchor == LayoutAnchor.BottomCenter || anchor == LayoutAnchor.BottomRight;
            bool hCenter = anchor == LayoutAnchor.TopCenter || anchor == LayoutAnchor.Center || anchor == LayoutAnchor.BottomCenter;
            bool vCenter = anchor == LayoutAnchor.MiddleLeft || anchor == LayoutAnchor.Center || anchor == LayoutAnchor.MiddleRight;

            element.style.left = left ? new StyleLength(0f)
                : hCenter ? new StyleLength(new Length(50f, LengthUnit.Percent))
                : new StyleLength(StyleKeyword.Null);
            element.style.right = right ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
            element.style.top = top ? new StyleLength(0f)
                : vCenter ? new StyleLength(new Length(50f, LengthUnit.Percent))
                : new StyleLength(StyleKeyword.Null);
            element.style.bottom = bottom ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);

            if (hCenter || vCenter) {
                element.style.translate = new StyleTranslate(new Translate(
                    new Length(hCenter ? -50f : 0f, LengthUnit.Percent),
                    new Length(vCenter ? -50f : 0f, LengthUnit.Percent)));
            } else {
                element.style.translate = new StyleTranslate(StyleKeyword.Null);
            }
        }

        // Explicit insets override whatever the anchor pinned. Auto leaves the anchor's
        // decision intact — that is the whole point of Auto meaning "no opinion".
        private static void ApplyInsets(VisualElement element, LayoutHint layout) {
            bool explicitLeft = layout.Left.Unit != LayoutLengthUnit.Auto;
            bool explicitTop = layout.Top.Unit != LayoutLengthUnit.Auto;
            bool explicitRight = layout.Right.Unit != LayoutLengthUnit.Auto;
            bool explicitBottom = layout.Bottom.Unit != LayoutLengthUnit.Auto;

            if (explicitLeft) {
                element.style.left = ToStyleLength(layout.Left);
            }

            if (explicitTop) {
                element.style.top = ToStyleLength(layout.Top);
            }

            if (explicitRight) {
                element.style.right = ToStyleLength(layout.Right);
            }

            if (explicitBottom) {
                element.style.bottom = ToStyleLength(layout.Bottom);
            }

            // A centering anchor's -50% translate is part of the mechanism by which it pins
            // that edge, not a separate concern — so an explicit inset on an axis overrides the
            // anchor's translate on that same axis too, or the edge ends up pinned correctly
            // while still shifted by the stale centering offset (a "half-centred" element).
            // Only the overridden axis is touched: an explicit Left must not disturb a Center
            // anchor's still-in-effect vertical centering, and vice versa.
            if (explicitLeft || explicitRight) {
                ClearTranslateAxis(element, horizontal: true);
            }

            if (explicitTop || explicitBottom) {
                ClearTranslateAxis(element, horizontal: false);
            }
        }

        // No-ops unless the anchor actually set a translate (StyleTranslate with a real Translate
        // value, keyword Undefined) — an anchor with no centering on either axis leaves
        // style.translate as StyleKeyword.Null, and there is nothing to clear. When there IS a
        // translate, only the requested axis is zeroed; Translate's x/y/z are independently
        // settable, so the other axis's centering offset (if any) survives untouched.
        private static void ClearTranslateAxis(VisualElement element, bool horizontal) {
            StyleTranslate current = element.style.translate;
            if (current.keyword != StyleKeyword.Undefined) {
                return;
            }

            Translate translate = current.value;
            if (horizontal) {
                translate.x = new Length(0f);
            } else {
                translate.y = new Length(0f);
            }

            element.style.translate = new StyleTranslate(translate);
        }

        // Auto is "no opinion" here too, same as every other field Apply writes: only the
        // explicit sides are set, so this stays layerable onto an element another system (USS,
        // a base style) already padded — mirrors ApplyInsets's Left/Top/Right/Bottom pattern,
        // just against style.padding* instead of style.left/top/right/bottom.
        private static void ApplyPadding(VisualElement element, LayoutPadding padding) {
            if (padding.Left.Unit != LayoutLengthUnit.Auto) {
                element.style.paddingLeft = ToStyleLength(padding.Left);
            }

            if (padding.Top.Unit != LayoutLengthUnit.Auto) {
                element.style.paddingTop = ToStyleLength(padding.Top);
            }

            if (padding.Right.Unit != LayoutLengthUnit.Auto) {
                element.style.paddingRight = ToStyleLength(padding.Right);
            }

            if (padding.Bottom.Unit != LayoutLengthUnit.Auto) {
                element.style.paddingBottom = ToStyleLength(padding.Bottom);
            }
        }

        private static void ApplyFlow(VisualElement element, LayoutFlow flow) {
            element.style.flexDirection = flow.Direction == LayoutFlowDirection.Column
                ? FlexDirection.Column
                : FlexDirection.Row;
            element.style.flexWrap = flow.Wrap ? Wrap.Wrap : Wrap.NoWrap;
            element.style.justifyContent = ToJustify(flow.Justify);
            element.style.alignItems = ToAlign(flow.Align);
            RefuseGap(flow);
        }

        /// <summary>
        /// <see cref="LayoutFlow.Gap"/> cannot be honoured by this translator, so an author who
        /// sets it is told rather than ignored.
        /// </summary>
        /// <remarks>
        /// <b>This Unity's UI Toolkit has no flex gap at all.</b> Checked against the live API
        /// rather than remembered: <c>IStyle</c> exposes 90 properties and none of <c>rowGap</c>,
        /// <c>columnGap</c> or <c>gap</c>, and the only member in <c>UIElementsModule</c> matching
        /// "gap" is <c>UIPainter2D.SetDashGapPattern</c>. The one way to honour it would be to
        /// synthesise margins onto the children — arithmetic and child mutation inside a file whose
        /// contract is "translator: no arithmetic, no measurement, no solver".
        ///
        /// <para>So this refuses instead, following the same
        /// <see cref="RefuseUnresolvable"/> convention the percentage insets use. Nothing shipped
        /// is affected: every emitted Gap in the corpus is 0px. The point is the mod author who
        /// sets one and currently gets silence.</para>
        /// </remarks>
        private static void RefuseGap(LayoutFlow flow) {
            if (flow.Gap.Value == 0f) {
                return;
            }

            RefuseUnresolvable("LayoutFlow.Gap", flow.Gap.ToString(),
                "this Unity's UI Toolkit has no flex-gap property, so the children are laid out "
                + "touching and the spacing is lost");
        }

        private static Justify ToJustify(LayoutFlowJustify justify) {
            switch (justify) {
                case LayoutFlowJustify.Center: return Justify.Center;
                case LayoutFlowJustify.End: return Justify.FlexEnd;
                case LayoutFlowJustify.SpaceBetween: return Justify.SpaceBetween;
                case LayoutFlowJustify.SpaceAround: return Justify.SpaceAround;
                default: return Justify.FlexStart;
            }
        }

        private static Align ToAlign(LayoutFlowAlign align) {
            switch (align) {
                case LayoutFlowAlign.Center: return Align.Center;
                case LayoutFlowAlign.End: return Align.FlexEnd;
                case LayoutFlowAlign.Stretch: return Align.Stretch;
                default: return Align.FlexStart;
            }
        }

        /// <summary>
        /// Positions <paramref name="child"/> absolutely at its declared cell(s) within
        /// <paramref name="grid"/>: left = Column x CellWidth, top = Row x CellHeight,
        /// width = ColumnSpan x CellWidth, height = RowSpan x CellHeight.
        ///
        /// <para>This is arithmetic, which the rest of this file deliberately avoids — but it is
        /// admissible here because it is a pure, closed-form expansion of numbers the DATA
        /// already declares (the grid's cell size, and the child's own column/row/span), not a
        /// layout decision made in code. There is no solver, no measurement, and no branching on
        /// screen size or anything else; the only thing this method branches on is which UNIT the
        /// cell length carries (px vs percent), which is bookkeeping for the StyleLength type, not
        /// a policy choice. If a future change ever needs this method to branch on anything else,
        /// that means the vocabulary (<see cref="LayoutGrid"/> / <see cref="LayoutGridPlacement"/>)
        /// is insufficient again — extend it there, not here.</para>
        ///
        /// <para>Percentage cells are the reason this exists, not an edge case: a grid whose
        /// CellWidth/CellHeight is a percentage must place children at percentage offsets and
        /// sizes too (Column x cellPercent, etc.) so the whole grid genuinely reflows when the
        /// container resizes — a version of this method that only handled px would defeat the
        /// point of having a grid vocabulary at all.</para>
        ///
        /// <para>Deciding WHICH placement a child gets (e.g. a two-handed weapon needing a 2x2
        /// footprint) is a game rule and lives in the view that builds the
        /// <see cref="LayoutGridPlacement"/> — never here. This method only turns an
        /// already-decided placement into geometry.</para>
        ///
        /// <para><b>Trust boundary:</b> this method does not read <see cref="LayoutGrid.Columns"/>
        /// / <see cref="LayoutGrid.Rows"/>, and does not validate that <paramref name="placement"/>
        /// fits inside them, nor that ColumnSpan/RowSpan are positive. Columns/Rows are the
        /// packer's own bookkeeping (how it decided placements don't collide), not applier input —
        /// bounds-checking them here would pull grid-packing semantics into the translator, which
        /// is exactly the layering this file exists to avoid. A <see cref="LayoutGridPlacement"/>
        /// is trusted, already-decided input, same as every other <c>LayoutHint</c> value Apply()
        /// writes without re-deriving; an out-of-range or non-positive span is the packer's bug to
        /// catch, not this method's to guard against.</para>
        ///
        /// <para><b>The cell UNIT is not inside that boundary.</b> Columns/Rows/spans are counts
        /// this method never reads, but <see cref="LayoutGrid.CellWidth"/>/<c>CellHeight</c> are
        /// lengths it multiplies, and <see cref="LayoutLengthUnit.Auto"/> — their default, which
        /// <c>"Grid": {"Columns": 4, "Rows": 4}</c> in an override produces verbatim — is not a
        /// length that can be multiplied at all. Writing <c>auto</c> into left/top/width/height
        /// would collapse every cell of the grid silently. That is exactly the case this project's
        /// rule says to refuse loudly, so it is refused: the error names the field and the child is
        /// left untouched, rather than styled into nothing.</para>
        /// </summary>
        /// <summary>
        /// Something identifying to put in an error, for an element that may have no name.
        /// </summary>
        /// <remarks>
        /// <b>A loud refusal that cannot say WHAT it refused is just noise.</b> The grid-placement
        /// error below prints the child's name, and every occurrence of it in a real run printed
        /// <c>''</c> — a UI Toolkit element only has a name if something set one, and the ones this
        /// path refuses are built from layout data that names few of them. Falling back to the type,
        /// the first USS class and the parent's name gives the reader somewhere to start.
        /// </remarks>
        private static string Describe(VisualElement element) {
            if (element == null) {
                return "<null>";
            }
            if (!string.IsNullOrEmpty(element.name)) {
                return element.name;
            }

            string kind = element.GetType().Name;
            string cls = null;
            foreach (string c in element.GetClasses()) {
                cls = c;
                break;
            }
            // *** WALK UP TO THE FIRST NAMED ANCESTOR, not just the parent. *** The elements this
            // path refuses sit inside equally anonymous wrappers, so stopping at the immediate
            // parent reported nothing useful — the nearest named one is a screen or a panel, which
            // is somewhere a reader can actually look.
            string parent = null;
            for (VisualElement up = element.parent; up != null; up = up.parent) {
                if (!string.IsNullOrEmpty(up.name)) {
                    parent = up.name;
                    break;
                }
            }
            string self = cls != null ? $"{kind}.{cls}" : kind;
            return parent != null ? $"{self} in {parent}" : self;
        }

        public static void ApplyGridPlacement(VisualElement child, LayoutGrid grid, LayoutGridPlacement placement) {
            if (child == null || grid == null || placement == null) {
                return;
            }

            if (grid.CellWidth.Unit == LayoutLengthUnit.Auto || grid.CellHeight.Unit == LayoutLengthUnit.Auto) {
                Debug.LogError(
                    $"LayoutApplier: cannot place '{Describe(child)}' in a grid whose cell size is Auto " +
                    $"({grid.CellWidth} x {grid.CellHeight}). A cell offset is Column x CellWidth, " +
                    "and 'intrinsic' is not a length that can be multiplied — every cell would " +
                    "collapse. Give the grid an explicit CellWidth/CellHeight (px or percent).");
                return;
            }

            child.style.position = Position.Absolute;
            child.style.left = ToStyleLength(new LayoutLength(grid.CellWidth.Value * placement.Column, grid.CellWidth.Unit));
            child.style.top = ToStyleLength(new LayoutLength(grid.CellHeight.Value * placement.Row, grid.CellHeight.Unit));
            child.style.width = ToStyleLength(new LayoutLength(grid.CellWidth.Value * placement.ColumnSpan, grid.CellWidth.Unit));
            child.style.height = ToStyleLength(new LayoutLength(grid.CellHeight.Value * placement.RowSpan, grid.CellHeight.Unit));
        }
    }
}
