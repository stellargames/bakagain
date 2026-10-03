namespace BakAgain.UI.Character {
    using System.Collections.Generic;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Image;
    using PaletteResource = GameData.Resources.Palette.PaletteResource;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Draws a party member's stat sheet — <c>charscreen_info_draw</c> @0x580fe in its compact form:
    /// the portrait, the bordered ratings panel with its four rows, and the condition column.
    /// </summary>
    /// <remarks>
    /// <b>Embeddable rather than a screen.</b> The temple healer redraws the sheet for whichever
    /// member it is showing on every pass rather than holding a display, so this renders into a host
    /// element and owns no place, no input and no lifetime of its own. The standalone character
    /// screen — which does own those things — is a caller of this, not a rival to it.
    ///
    /// <para><b>It is not a pure draw: reading the sheet clears the change marks.</b> A rating that
    /// improved is drawn highlighted exactly once, and looking at it is the acknowledgement
    /// (<c>UI_show_attribute_x_of_y</c> fetches the flag and writes zero back in the same breath).
    /// That is why this takes the session rather than a snapshot of values.</para>
    ///
    /// <para><b>The lower half is not drawn.</b> The full sheet adds twelve more rows with their
    /// sword bars below the panel; the compact form the healer asks for skips them entirely, and
    /// that is the form this renders. See <see cref="CharacterSheetLayout.DrawsLowerHalf"/>.</para>
    ///
    /// <para><b>No coordinates live here.</b> Every position, size and pen comes from
    /// <see cref="CharacterSheetLayout"/> and <see cref="CharacterSheetPanelRow"/>, in canonical
    /// space, so the sheet scales with the stage rather than with anything this class believes about
    /// the window.</para>
    /// </remarks>
    internal sealed class CharacterSheetView {
        private readonly List<VisualElement> _elements = new();
        private int _generation;

        /// <summary>Remove everything the last render added. Safe before anything was drawn.</summary>
        internal void Clear() {
            _generation++; // abandon any sprite still loading for the previous render
            foreach (VisualElement element in _elements) {
                element.RemoveFromHierarchy();
            }
            _elements.Clear();
        }

        /// <summary>
        /// Drop everything this view drew behind the REQ's own widgets.
        /// </summary>
        /// <remarks>
        /// <b>The sheet is chrome; the REQ's entries are the controls.</b> The loader builds the
        /// entries first and this renders over them, so anything the REQ shows in the same space
        /// ends up buried — REQ_INFO's "Spells" sits across the ratings panel's lower edge and came
        /// up with only a sliver visible, and unclickable with it, because UI Toolkit picks the
        /// topmost element.
        ///
        /// <para>Ordering cannot be fixed by revealing the button later instead: <c>SetEntryState</c>
        /// rebuilds, a rebuild raises <c>Built</c>, and the handler redraws this — so the sheet lands
        /// on top whichever order the caller uses. Pushing this view down is the end of that loop.</para>
        ///
        /// <para>Walked in reverse because <see cref="VisualElement.SendToBack"/> moves one element
        /// to index 0: front-to-back would reverse this view's own draw order.</para>
        /// </remarks>
        internal void SendBehindPanelWidgets() {
            for (int i = _elements.Count - 1; i >= 0; i--) {
                _elements[i].SendToBack();
            }
        }

        /// <summary>
        /// Draw <paramref name="characterIndex"/>'s sheet into <paramref name="host"/>, replacing
        /// whatever the last call drew.
        /// </summary>
        /// <param name="characterIndex">
        /// The member's POSITION in the party roster — what <see cref="Core.GameSession.StatsOf"/>
        /// and <see cref="Core.GameSession.ConditionsOf"/> take. Not an actor number, which is this
        /// plus one; the original's own portrait call does that increment on the way out.
        /// </param>
        /// <param name="palette">
        /// The palette the sheet's pens resolve against — the screen's own, since the sheet is drawn
        /// over whatever is already on it. Null falls back to legible colours rather than black.
        /// </param>
        internal async UniTask RenderAsync(VisualElement host, int characterIndex,
            Core.GameSession session, IResourceCache sprites, PaletteResource palette,
            ILogger logger = null, bool fullSheet = false) {
            Clear();
            if (host == null || session == null) {
                return;
            }
            int generation = _generation;

            await DrawParchmentAsync(host, sprites, generation, logger);
            DrawPortrait(host, characterIndex, sprites, logger);
            DrawPanel(host, palette);
            DrawHeadings(host, palette);
            DrawRatings(host, characterIndex, session, palette);
            DrawConditions(host, characterIndex, session, palette);
            await DrawFrameAsync(host, sprites, fullSheet, generation, logger);
            if (fullSheet) {
                await DrawLowerHalfAsync(host, characterIndex, session, sprites, palette, generation,
                    logger);
            }
        }

        // ---- the parchment --------------------------------------------------------------------

        /// <summary>The full-frame parchment, at the very bottom of the host.</summary>
        /// <remarks>
        /// Inserted rather than added: the REQ's buttons are already in the host, and the original
        /// blits this before it draws anything, so nothing else the screen shows may sit under it.
        /// </remarks>
        private async UniTask DrawParchmentAsync(VisualElement host, IResourceCache sprites,
            int generation, ILogger logger) {
            if (sprites == null) {
                return;
            }
            Sprite parchment = await sprites.GetOrLoadAsync<Sprite>(CharacterSheetLayout.Parchment);
            if (generation != _generation) {
                return;
            }
            if (parchment == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                    "CharacterSheetView: {Key} did not load; the sheet is drawn without it.",
                    CharacterSheetLayout.Parchment);

                return;
            }
            var element = new VisualElement {
                name = "BakCharacterSheetParchment",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                    backgroundImage = Background.FromSprite(parchment),
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            };
            host.Insert(0, element);
            _elements.Insert(0, element);
        }

        // ---- the portrait ---------------------------------------------------------------------

        private void DrawPortrait(VisualElement host, int characterIndex, IResourceCache sprites,
            ILogger logger) {
            if (sprites == null) {
                return;
            }
            VisualElement face = Add(host, "BakCharacterSheetFace",
                CharacterSheetLayout.PortraitX, CharacterSheetLayout.PortraitY);
            // The actor number is the roster position plus one — the increment the original does at
            // its own call site (0x5815b) rather than inside the face loader.
            // INVENTOR.PAL is the sheet's own screen palette (CHARSCRN.C:281), and the portrait's
            // surround is painted from it — see ActorFaceView's hostPalette.
            ActorFaceView.ApplyAsync(face, characterIndex + 1, sprites,
                    CharacterSheetLayout.PortraitIsAlternate, logger, sizeToSprite: true,
                    hostPalette: CharacterSheetLayout.ScreenPalette)
                .Forget();
        }

        // ---- the panel ------------------------------------------------------------------------

        private void DrawPanel(VisualElement host, PaletteResource palette) {
            VisualElement panel = Add(host, "BakCharacterSheetPanel",
                CharacterSheetLayout.PanelX, CharacterSheetLayout.PanelY);
            panel.style.width = CharacterSheetLayout.PanelWidth;
            panel.style.height = CharacterSheetLayout.PanelHeight;
            panel.style.backgroundColor =
                PaletteColors.ResolvePen(palette, CharacterSheetLayout.PanelFillPen, PanelFallback);
        }

        /// <summary>
        /// The four dotted rules that frame the panel, each opposite edge the same image turned
        /// round.
        /// </summary>
        /// <remarks>
        /// Awaited, unlike the portrait: these are the sheet's own chrome and a caller that waits
        /// for the sheet wants it finished, whereas a missing face is an ordinary outcome the layout
        /// does not depend on.
        /// </remarks>
        private async UniTask DrawFrameAsync(VisualElement host, IResourceCache sprites,
            bool fullSheet, int generation, ILogger logger) {
            if (sprites == null) {
                return;
            }
            foreach (CharacterSheetLayout.Piece piece in CharacterSheetLayout.PanelFrame) {
                await DrawPieceAsync(host, piece, sprites, generation, logger);
            }
            foreach (CharacterSheetLayout.Piece piece in
                     CharacterSheetLayout.Vines(fullSheet ? FullSheet : CompactSheet)) {
                await DrawPieceAsync(host, piece, sprites, generation, logger);
            }
        }

        private async UniTask DrawPieceAsync(VisualElement host, CharacterSheetLayout.Piece piece,
            IResourceCache sprites, int generation, ILogger logger) {
            string key = CharacterSheetLayout.FrameIconSet + SubImageSeparator + piece.IconIndex;
            Sprite sprite = await sprites.GetOrLoadAsync<Sprite>(key);
            if (generation != _generation) {
                return; // a newer render replaced this one while the sprite was loading
            }
            if (sprite == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                    "CharacterSheetView: {Key} did not load; that edge is drawn without it.", key);

                return;
            }
            VisualElement element = Add(host, "BakCharacterSheetFramePiece", piece.X, piece.Y);
            element.style.width = sprite.rect.width;
            element.style.height = sprite.rect.height;
            element.SetBackgroundSpriteNativeSizeTopLeft(sprite);
            // A flip is a mirror about the piece's own middle, so the element keeps its position.
            float x = piece.Flags.HasFlag(ImageFlags.HorizontalFlip) ? -1f : 1f;
            float y = piece.Flags.HasFlag(ImageFlags.VerticalFlip) ? -1f : 1f;
            element.style.scale = new StyleScale(new Scale(new Vector2(x, y)));
        }

        // ---- the ratings ----------------------------------------------------------------------

        private void DrawHeadings(VisualElement host, PaletteResource palette) {
            AddText(host, GameData.Resources.Text.UiStrings.Get(CharacterSheetLayout.RatingsHeadingKey),
                CharacterSheetLayout.RatingsHeadingX, CharacterSheetLayout.HeadingY,
                CharacterSheetPanelRow.Pen, CharacterSheetPanelRow.ShadowPen, palette, TextAnchor.UpperLeft);
            AddText(host, GameData.Resources.Text.UiStrings.Get(CharacterSheetLayout.ConditionHeadingKey),
                CharacterSheetLayout.ConditionHeadingX, CharacterSheetLayout.HeadingY,
                CharacterSheetPanelRow.Pen, CharacterSheetPanelRow.ShadowPen, palette, TextAnchor.UpperLeft);
        }

        private void DrawRatings(VisualElement host, int characterIndex, Core.GameSession session,
            PaletteResource palette) {
            ActorStat[] stats = session.StatsOf(characterIndex);
            if (stats == null) {
                return;
            }
            ActorStat health = stats[(int)ActorAttribute.Health];
            ActorConditions conditions = session.ConditionsOf(characterIndex);
            for (var attribute = 0; attribute < CharacterSheetPanelRow.Count; attribute++) {
                var which = (ActorAttribute)attribute;
                // The sheet is where a player sees what an affliction costs them, so it reads the
                // value the way the original's stat read does — with the condition penalties folded
                // in between the equipment modifier and the health scaling, not after.
                // The timed modifiers belong here too — the sheet is where a player sees what a
                // buff or an affliction is doing, and reading only the conditions showed half of it.
                int value = StatEngine.Get(stats[attribute], which, health, StatReadMode.Effective,
                    session.PartyEffectsFor(characterIndex, which, inCombat: false));
                int maximum = StatEngine.Get(stats[attribute], which, health, StatReadMode.Maximum);
                DrawRating(host, attribute, value, maximum,
                    TakeChangeMark(session, characterIndex, attribute), palette);
            }
        }

        /// <summary>
        /// Whether this rating changed since it was last looked at — <b>and clears the mark</b>.
        /// </summary>
        /// <remarks>
        /// Read-and-clear in one step because that is what the original does (0x58031 then
        /// 0x58060), and splitting it is how a port ends up either highlighting a rating forever or
        /// losing the highlight before the player saw it.
        /// </remarks>
        /// <para><b>Remembered for the rest of the opening</b> (TASK-753): the screen redraws on
        /// every REQ rebuild, and a second take found the flag already cleared and drew the row plain.
        /// The original draws once and the red stays up; <see cref="ForgetMarks"/> ends the opening.</para>
        internal bool TakeChangeMark(Core.GameSession session, int characterIndex, int attribute) {
            int key = CharacterSheetPanelRow.ChangedFlagFor(characterIndex, attribute);
            if (_takenMarks.TryGetValue(key, out bool taken)) {
                return taken;
            }
            bool changed = (session.GetGlobalValue(key) ?? 0) != 0;
            if (changed) {
                session.SetGlobalValue(key, 0);
            }
            _takenMarks[key] = changed;

            return changed;
        }

        private readonly Dictionary<int, bool> _takenMarks = new();

        /// <summary>The sheet closed: the next opening reads the flags afresh.</summary>
        internal void ForgetMarks() => _takenMarks.Clear();

        private void DrawRating(VisualElement host, int attribute, int value, int maximum,
            bool changed, PaletteResource palette) {
            int pen = CharacterSheetPanelRow.RowPen(changed);
            int shadow = CharacterSheetPanelRow.RowShadowPen(changed);
            int y = CharacterSheetPanelRow.RowY(attribute);

            AddText(host, ActorLabels.AttributeName(attribute), CharacterSheetPanelRow.NameX, y,
                pen, shadow, palette, TextAnchor.UpperLeft);
            AddText(host, Number(value), CharacterSheetPanelRow.ValueRightX, y,
                pen, shadow, palette, TextAnchor.UpperRight);
            if (!CharacterSheetPanelRow.ShowsMaximum(attribute)) {
                return;
            }
            AddText(host, GameData.Resources.Text.UiStrings.Get(CharacterSheetPanelRow.SeparatorKey),
                CharacterSheetPanelRow.SeparatorX, y, pen, shadow, palette, TextAnchor.UpperLeft);
            AddText(host, Number(maximum), CharacterSheetPanelRow.MaximumRightX, y,
                pen, shadow, palette, TextAnchor.UpperRight);
        }

        // ---- the lower half -------------------------------------------------------------------

        /// <summary>
        /// The full sheet's twelve rating rows, below the panel.
        /// </summary>
        /// <remarks>
        /// <b>Two columns, and both of them carry a bar.</b> The column decides which side of it
        /// the bar hangs off and which way round it points, not whether there is one — see
        /// <see cref="CharacterSheetRow.ShowsBar"/>, which is where an earlier reading of this had
        /// bars belonging to the skills alone.
        /// </remarks>
        private async UniTask DrawLowerHalfAsync(VisualElement host, int characterIndex,
            Core.GameSession session, IResourceCache sprites, PaletteResource palette,
            int generation, ILogger logger) {
            ActorStat[] stats = session.StatsOf(characterIndex);
            if (stats == null) {
                return;
            }
            ActorStat health = stats[(int)ActorAttribute.Health];
            ActorConditions conditions = session.ConditionsOf(characterIndex);

            for (var row = 0; row < CharacterSheetLayout.LowerHalfAttributeCount; row++) {
                int attribute = CharacterSheetLayout.LowerHalfFirstAttribute + row;
                var which = (ActorAttribute)attribute;
                // The timed modifiers belong here too — the sheet is where a player sees what a
                // buff or an affliction is doing, and reading only the conditions showed half of it.
                int value = StatEngine.Get(stats[attribute], which, health, StatReadMode.Effective,
                    session.PartyEffectsFor(characterIndex, which, inCombat: false));
                int maximum = StatEngine.Get(stats[attribute], which, health, StatReadMode.Maximum);
                bool changed = TakeChangeMark(session, characterIndex, attribute);

                bool emphasised = SkillEmphasis.IsEmphasised(
                    session.GetGlobalValue(SkillEmphasis.FlagFor(characterIndex, attribute)) ?? 0);

                DrawLowerRow(host, attribute, value, maximum, changed, palette);
                await DrawBarAsync(host, attribute, value, emphasised, sprites, generation, logger);
            }
        }

        /// <summary>One rating: its name and its value, which share their ink.</summary>
        private void DrawLowerRow(VisualElement host, int attribute, int value, int maximum,
            bool changed, PaletteResource palette) {
            int pen = CharacterSheetRow.NamePen(changed);
            int shadow = CharacterSheetRow.NameShadowPen(changed);
            int column = CharacterSheetRow.ColumnX(attribute);
            int y = CharacterSheetRow.RowY(attribute) + CharacterSheetRow.TextOffsetY;

            AddText(host, ActorLabels.AttributeName(attribute),
                column + CharacterSheetRow.NameOffsetX, y, pen, shadow, palette, TextAnchor.UpperLeft);
            // "N/A" is tested on the MAXIMUM: never-had reads differently from lost-it-all.
            AddText(host, CharacterSheetRow.ValueText(maximum, value),
                column + CharacterSheetRow.ValueOffsetX, y, pen, shadow, palette,
                TextAnchor.UpperRight);
        }

        /// <summary>
        /// The rating's bar: the empty one whole, then the fill clipped to how much of it there is.
        /// </summary>
        /// <remarks>
        /// <b>The fill is a second bitmap behind a window, not a shortened first one.</b> The
        /// original draws the empty bar, narrows the render view to the filled fraction, and draws a
        /// different image inside it — so the window is reproduced as a clipping element and the
        /// fill keeps its own size. Cropping or scaling the empty bar would leave a half-full rating
        /// looking like a short sword rather than a full one.
        /// </remarks>
        private async UniTask DrawBarAsync(VisualElement host, int attribute, int percentage,
            bool emphasised, IResourceCache sprites, int generation, ILogger logger) {
            if (sprites == null || !CharacterSheetRow.ShowsBar(attribute)) {
                return;
            }
            bool mirrored = CharacterSheetRow.BarIsMirrored(attribute);

            await DrawBarPieceAsync(host, CharacterSheetRow.BarEmptyIcon,
                CharacterSheetRow.BarX(attribute), CharacterSheetRow.BarY(attribute), mirrored,
                sprites, generation, logger);

            // The mark for a rating the character is concentrating on — the player's own choice,
            // made by clicking the row. Drawn at the bar's far end, and NOT a mirrored copy of the
            // near one: the two columns' marker offsets are unrelated numbers.
            if (emphasised) {
                await DrawBarPieceAsync(host, CharacterSheetRow.BarEndMarkerIcon,
                    CharacterSheetRow.BarEndMarkerX(attribute),
                    CharacterSheetRow.BarFillY(attribute), mirrored, sprites, generation, logger);
            }

            if (!CharacterSheetRow.BarHasFill(percentage)) {
                return;   // an empty rating draws no fill at all, not a zero-width one
            }

            (int left, int right) = CharacterSheetRow.BarFillClip(attribute, percentage);
            VisualElement window = Add(host, "BakCharacterSheetBarWindow", left,
                CharacterSheetRow.BarFillY(attribute));
            window.style.width = right - left;
            window.style.overflow = Overflow.Hidden;

            Sprite fill = await sprites.GetOrLoadAsync<Sprite>(
                CharacterSheetRow.BarIconSet + SubImageSeparator + CharacterSheetRow.BarFillIcon);
            if (generation != _generation || fill == null) {
                return;
            }
            window.style.height = fill.rect.height;
            // Positioned inside the window by where it would have been drawn on the sheet, so the
            // window is what shortens it.
            var image = new VisualElement {
                name = "BakCharacterSheetBarFill",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = CharacterSheetRow.BarFillX(attribute) - left,
                    top = 0,
                    width = fill.rect.width,
                    height = fill.rect.height,
                },
            };
            image.SetBackgroundSpriteNativeSizeTopLeft(fill);
            Mirror(image, mirrored);
            window.Add(image);
        }

        private async UniTask DrawBarPieceAsync(VisualElement host, int icon, float x, float y,
            bool mirrored, IResourceCache sprites, int generation, ILogger logger) {
            Sprite sprite = await sprites.GetOrLoadAsync<Sprite>(
                CharacterSheetRow.BarIconSet + SubImageSeparator + icon);
            if (generation != _generation) {
                return;
            }
            if (sprite == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                    "CharacterSheetView: bar image {Icon} did not load; the row is drawn without it.",
                    icon);

                return;
            }
            VisualElement element = Add(host, "BakCharacterSheetBar", x, y);
            element.style.width = sprite.rect.width;
            element.style.height = sprite.rect.height;
            element.SetBackgroundSpriteNativeSizeTopLeft(sprite);
            Mirror(element, mirrored);
        }

        /// <summary>A mirror about the element's own middle, so it keeps its position.</summary>
        private static void Mirror(VisualElement element, bool mirrored) {
            if (mirrored) {
                element.style.scale = new StyleScale(new Scale(new Vector2(-1f, 1f)));
            }
        }

        // ---- the conditions -------------------------------------------------------------------

        /// <summary>
        /// The afflictions the member has, closed up rather than indexed.
        /// </summary>
        /// <remarks>
        /// The line counter advances only for a condition that is actually listed, so three
        /// afflictions always fill the first three lines whichever three they are — indexing by
        /// condition would scatter them down the column with gaps.
        /// </remarks>
        private void DrawConditions(VisualElement host, int characterIndex, Core.GameSession session,
            PaletteResource palette) {
            ActorConditions conditions = session.ConditionsOf(characterIndex);
            var line = 0;
            for (var condition = 0; condition < CharacterSheetLayout.ConditionCount; condition++) {
                int rank = conditions?[(ActorCondition)condition] ?? 0;
                if (!CharacterSheetLayout.IsListed(rank)) {
                    continue;
                }
                line++;
                AddText(host,
                    string.Format(CharacterSheetLayout.ConditionFormat,
                        ActorLabels.ConditionName(condition), rank),
                    CharacterSheetLayout.ConditionX, CharacterSheetLayout.ConditionLineY(line),
                    CharacterSheetLayout.ConditionPen, CharacterSheetLayout.ConditionShadowPen,
                    palette, TextAnchor.UpperLeft);
            }
            if (line > 0) {
                return;
            }
            AddText(host, GameData.Resources.Text.UiStrings.Get(CharacterSheetLayout.NormalKey),
                CharacterSheetLayout.ConditionX, CharacterSheetLayout.NormalY,
                CharacterSheetPanelRow.Pen, CharacterSheetPanelRow.ShadowPen, palette,
                TextAnchor.UpperLeft);
        }

        // ---- building blocks ------------------------------------------------------------------

        private VisualElement Add(VisualElement host, string name, float x, float y) {
            var element = new VisualElement {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = x, top = y },
            };
            host.Add(element);
            _elements.Add(element);

            return element;
        }

        /// <summary>A line of text and the drop shadow the original draws under it.</summary>
        private void AddText(VisualElement host, string text, float x, float y, int pen,
            int shadowPen, PaletteResource palette, TextAnchor align) {
            AddLabel(host, text, x + CharacterSheetLayout.TextShadowOffsetX,
                y + CharacterSheetLayout.TextShadowOffsetY, shadowPen, ShadowFallback, palette, align);
            AddLabel(host, text, x, y, pen, TextFallback, palette, align);
        }

        private void AddLabel(VisualElement host, string text, float x, float y, int pen,
            Color fallback, PaletteResource palette, TextAnchor align) {
            var label = new Label(text) {
                name = "BakCharacterSheetText",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                    color = PaletteColors.ResolvePen(palette, pen, fallback),
                    // Right-aligned text is positioned by its RIGHT edge in the original
                    // (DisplayText's alignment 2 subtracts the measured width), so the label is
                    // shifted by its own width rather than by a number measured here.
                    translate = align == TextAnchor.UpperRight
                        ? new StyleTranslate(new Translate(Length.Percent(-100f), 0f))
                        : new StyleTranslate(new Translate(0f, 0f)),
                },
            };
            GameFontText.Apply(label, GameFontText.HorizontalAnchor(align), GameFontText.AnchorY.Top);
            host.Add(label);
            _elements.Add(label);
        }

        private static string Number(int value) =>
            value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>What the sheet's two sizes are called at the one place that picks one.</summary>
        private const int CompactSheet = 0;

        /// <inheritdoc cref="CompactSheet"/>
        private const int FullSheet = 1;

        private const string SubImageSeparator = "#";

        // Legible stand-ins for the pens until the screen's palette lands — the same reason
        // PaletteColors.ResolvePen takes a fallback at all.
        private static readonly Color PanelFallback = new(0.11f, 0.09f, 0.07f);
        private static readonly Color TextFallback = new(244f / 255f, 196f / 255f, 164f / 255f);
        private static readonly Color ShadowFallback = Color.black;
    }
}
