namespace BakAgain.UI.Rest {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The stone dial both rest screens draw — <c>encamp_drawDialStones</c> @0x70a4a.
    /// </summary>
    /// <remarks>
    /// <b>Shared because the two screens differ only in what they ask for.</b> Camping marks the
    /// hours it has slept so far and puts gold under the cursor; an inn marks the current hour and
    /// puts gold on the hour it will wake you. Both are the same loop over ENCAMP.DAT's 24 clock
    /// entries, and the icon each stone gets is <see cref="EncampDial.IconFor"/>.
    ///
    /// <para>The gold stones in ENCAMP.SCX's artwork are backdrop, and the original covers every one
    /// of them. Drawing nothing leaves the artwork's gold showing, which reads as a dial with no
    /// time marker at all.</para>
    /// </remarks>
    public static class RestDialView {
        /// <summary>Name of the stone layer, so a rebuild replaces it rather than stacking.</summary>
        public const string LayerName = "BakRestDialStones";

        /// <summary>
        /// Draws the 24 hour-stones onto <paramref name="stage"/>, replacing any previous set.
        /// </summary>
        /// <param name="markedHour">The hour drawn red whatever else is happening.</param>
        /// <param name="highlightedStone">
        /// The one stone drawn gold, or -1 for none — the cursor's stone while camping, the waking
        /// hour at an inn.
        /// </param>
        /// <param name="spanStartHour">First hour of a red range, or -1 for none.</param>
        /// <param name="spanEndHour">Last hour of that range.</param>
        /// <remarks>
        /// The icon goes at the clock entry's own position: the original passes the entry's x,y
        /// straight to the blit. <c>IconAnchorX/Y</c> is the hit box's offset (see
        /// <c>encamp_getClockEntryAtMouse</c>), not a draw offset — subtracting it here would shift
        /// every stone off its rim.
        /// </remarks>
        /// <param name="shadowTicksOfDay">
        /// Time of day for the sundial's shadow, or -1 to leave it off. Separate from
        /// <paramref name="markedHour"/> because the shadow moves every HALF hour and the stones
        /// only every hour — and because it is absent at night, where a marked stone still is not.
        /// </param>
        public static async UniTask BuildAsync(VisualElement stage, IResourceCache resources,
            EncampData encamp, int markedHour, int highlightedStone = -1,
            int spanStartHour = -1, int spanEndHour = -1, int shadowTicksOfDay = -1,
            GameData.Resources.Palette.PaletteResource palette = null,
            Texture2D shadedArt = null) {
            if (stage == null || resources == null || encamp?.ClockEntries == null) {
                return;
            }

            var layer = new VisualElement {
                name = LayerName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };

            for (var stone = 0; stone < encamp.ClockEntries.Count; stone++) {
                EncampPoint at = encamp.ClockEntries[stone];
                int icon = EncampDial.IconFor(stone, markedHour, highlightedStone,
                    spanStartHour, spanEndHour);
                var sprite = await resources.GetOrLoadAsync<Sprite>(
                    $"{EncampDial.StoneIconSet}#{icon}");
                if (sprite == null) {
                    continue;
                }

                layer.Add(new VisualElement {
                    pickingMode = PickingMode.Ignore,
                    style = {
                        position = Position.Absolute,
                        left = at.X, top = at.Y,
                        width = sprite.rect.width, height = sprite.rect.height,
                        backgroundImage = new StyleBackground(sprite),
                    },
                });
            }

            // Swapped in only once the whole ring is built. Removing the old layer first would show
            // a dial with no stones at all for however long the sprite loads take, which on a
            // per-hour redraw is a visible flicker every hour of a rest.
            if (layer.panel == null && stage.panel == null) {
                return; // the screen went away while the sprites were loading
            }
            stage.Q<VisualElement>(LayerName)?.RemoveFromHierarchy();
            stage.Add(layer);

            // Under the stones, over the artwork: the original draws the shadow first and blits the
            // stones after it (0x50287 before 0x502a5), so a stone the shadow crosses stays visible.
            stage.Q<VisualElement>(RestDialShadow.LayerName)?.RemoveFromHierarchy();
            if (shadowTicksOfDay >= 0) {
                var shadow = new RestDialShadow();
                stage.Insert(stage.IndexOf(layer), shadow);
                shadow.SetTime(encamp, shadowTicksOfDay, palette, shadedArt);
            }
        }
    }
}
