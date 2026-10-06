namespace BakAgain.UI.Rest {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Config;
    using GameData.Resources.Inventory;
    using GameData.Resources.Text;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The party table both rest screens fill their panel with —
    /// <c>UI_show_actor_healthStatus</c> @0x70d2d.
    /// </summary>
    /// <remarks>
    /// <b>Shared because it is literally the same drawing call.</b> The original's function takes no
    /// arguments and writes at absolute screen positions, and both rest screens call it unchanged
    /// (0x7050a from camping, 0x5016e from the inn) — so there is one table, not two that happen to
    /// look alike. The rules are <see cref="CampPartyStats"/>; this only draws them.
    ///
    /// <para>The palette comes from the caller because the two screens load different ones — the
    /// inn's is INVENTOR.PAL (0x4ffd8) — and the pens here are palette indices.</para>
    /// </remarks>
    public sealed class RestPartyTableView {
        /// <summary>Width of a centring box. Wide enough for the longest heading, and only ever
        /// used to centre text within itself — see <see cref="Centred"/>.</summary>
        private const int CentreBoxWidth = 800;

        private readonly List<VisualElement> _elements = new();
        private GameData.Resources.Palette.PaletteResource _palette;

        /// <summary>Removes the table. Safe before anything has been drawn.</summary>
        public void Clear() {
            foreach (VisualElement e in _elements) {
                e.RemoveFromHierarchy();
            }
            _elements.Clear();
        }

        /// <summary>
        /// Draws two headings and a row per active member, replacing anything drawn before.
        /// </summary>
        public void Build(VisualElement stage, GameSession session,
            GameData.Resources.Palette.PaletteResource palette) {
            Clear();
            if (stage == null || session == null || !session.IsActive) {
                return;
            }
            _palette = palette;

            Label healthHeading = Centred(UiStrings.Get(CampPartyStats.HealthStaminaLabelKey),
                CampPartyStats.HeadingCentreX(0), CampPartyStats.HeadingY,
                CampPartyStats.HealthyTextColour);
            Label rationsHeading = Centred(UiStrings.Get(CampPartyStats.RationsLabelKey),
                CampPartyStats.HeadingCentreX(1), CampPartyStats.HeadingY,
                CampPartyStats.HealthyTextColour);
            Add(stage, healthHeading);
            Add(stage, rationsHeading);
            healthHeading.RegisterCallback<GeometryChangedEvent>(_ => FitHeadings(healthHeading, rationsHeading));

            byte[] roster = session.ActivePartyIndices;
            for (var slot = 0; slot < roster.Length; slot++) {
                BuildMemberRow(stage, session, slot, roster[slot]);
            }
        }

        private void BuildMemberRow(VisualElement stage, GameSession session, int slot,
            byte characterId) {
            int y = CampPartyStats.RowY(slot);
            ActorStat[] stats = session.StatsOf(characterId);
            ActorStat health = stats?[(int)ActorAttribute.Health];
            ActorStat stamina = stats?[(int)ActorAttribute.Stamina];
            // *** THE CURRENT FIGURE IS THE EFFECTIVE READ, NOT THE STORED PAIR. ***
            // ENCAMP.C:495-496 prints `stat_actor_get(char, 0x10, 0)` over
            // `stat_actor_get(char, 0x10, 1)`, and STAT.C:109 defines stat 0x10 as
            // `get(0, mode) + get(1, mode)` — so "current" is health and stamina each put through
            // the modifier and affliction pipeline, while "max" is the plain stored pair.
            //
            // Summing `Base` ignored every affliction. Measured at Joftaz's in Silden on 2026-09-13:
            // buying Keshian Ale applies Drunk 28 to the buyer (both games do this), and the
            // original's inn table then reads Locklear **92 of 100** where ours read 100 of 100.
            int current = session.EffectivePool(characterId);
            int maximum = session.EffectivePoolMax(characterId);

            // The name carries the affliction ink — and Healing is not an affliction, which is why
            // this asks CampPartyStats rather than testing the vector itself.
            ActorConditions conditions = session.ConditionsOf(characterId);
            string[] names = session.PartyActorNames;
            string name = characterId < names.Length ? names[characterId] : string.Empty;
            Add(stage, Positioned(name, CampPartyStats.NameX, y,
                CampPartyStats.NameColour(conditions)));

            Add(stage, Centred(CampPartyStats.HealthStaminaText(current, maximum),
                CampPartyStats.ValueCentreX(0), y, CampPartyStats.HealthyTextColour));

            // THE WOUNDED HIGHLIGHT OVERPRINTS THE CURRENT FIGURE ALONE, at the same origin as the
            // whole string — the number recolours and " of M" stays plain. A colour on the row would
            // tint the maximum too, which the original never does.
            if (CampPartyStats.IsWounded(current, maximum)) {
                Add(stage, Centred(CampPartyStats.HealthStaminaText(current, maximum),
                    CampPartyStats.ValueCentreX(0), y, CampPartyStats.WoundedTextColour,
                    onlyLeadingChars: current.ToString(CultureInfo.InvariantCulture).Length));
            }

            // InventoryQuery.CountByKind already carries the rule that a slot with Variable 0
            // counts as ONE rather than zero — its own doc names ration counts as a consumer.
            RuntimeContainer pack = session.GetActorInventory(characterId);
            Add(stage, Centred(
                CampPartyStats.RationsFor(id => InventoryQuery.CountByKind(pack, id))
                    .ToString(CultureInfo.InvariantCulture),
                CampPartyStats.ValueCentreX(1), y, CampPartyStats.HealthyTextColour));
        }

        private void Add(VisualElement stage, VisualElement element) {
            stage.Add(element);
            _elements.Add(element);
        }

        private Label Positioned(string text, int x, int y, int pen) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = x, top = y },
            };
            label.style.color = PaletteColors.ResolvePen(_palette, pen, Color.black);
            GameFontText.Apply(label);

            return label;
        }

        /// <summary>
        /// A line centred on <paramref name="centreX"/>, laid out by centring text inside a fixed
        /// box rather than by measuring it — MeasureTextSize reads 0 before layout.
        /// </summary>
        /// <param name="onlyLeadingChars">
        /// When set, everything past this many characters is drawn transparent, so the label paints
        /// only its leading run while still occupying the full width. That is what makes the wounded
        /// overprint land on exactly the characters the original recolours.
        /// </param>
        /// <summary>The least space kept between the two headings, canonical px.</summary>
        private const float HeadingGap = 20f;

        /// <summary>
        /// The two column headings shrink together when their text would run into each other (TASK-779).
        /// </summary>
        /// <remarks>
        /// <b>Not a box each.</b> In English "Health/Stamina" is already wider than the distance
        /// between the column centres and only clears "Rations" because that is short, so the
        /// space belongs to the pair. English never overlaps and is left at the font's size; a
        /// longer translation scales both by one factor, down to <see cref="GameFontText.MinFitScale"/>.
        /// </remarks>
        private static void FitHeadings(Label left, Label right) {
            float full = GameFontText.FontSizePx;
            float current = left.resolvedStyle.fontSize;
            if (current <= 0f) {
                return;
            }
            float Natural(Label l) => l.MeasureTextSize(l.text, 0f, VisualElement.MeasureMode.Undefined,
                0f, VisualElement.MeasureMode.Undefined).x * full / current;
            float halves = (Natural(left) + Natural(right)) / 2f;
            float room = CampPartyStats.HeadingCentreX(1) - CampPartyStats.HeadingCentreX(0) - HeadingGap;
            float want = halves <= room ? full : Mathf.Max(full * GameFontText.MinFitScale, full * room / halves);
            if (Mathf.Abs(want - current) > 0.5f) {
                left.style.fontSize = want;
                right.style.fontSize = want;
            }
        }

        private Label Centred(string text, int centreX, int y, int pen, int onlyLeadingChars = -1) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = centreX - (CentreBoxWidth / 2),
                    top = y,
                    width = CentreBoxWidth,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            };
            Color ink = PaletteColors.ResolvePen(_palette, pen, Color.black);
            if (onlyLeadingChars >= 0 && onlyLeadingChars < text.Length) {
                label.enableRichText = true;
                label.text = "<color=#" + ColorUtility.ToHtmlStringRGB(ink) + ">"
                    + text.Substring(0, onlyLeadingChars) + "</color><alpha=#00>"
                    + text.Substring(onlyLeadingChars);
            }
            label.style.color = ink;
            GameFontText.Apply(label);

            return label;
        }
    }
}
