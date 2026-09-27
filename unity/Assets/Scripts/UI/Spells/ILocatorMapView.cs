namespace BakAgain.UI.Spells {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;

    /// <summary>
    /// The map inset the three locator spells share — <c>CastLocatorSpell</c> (IDA 0x6d062).
    /// </summary>
    /// <remarks>
    /// <b>Not a navigator screen — an overlay on the travel HUD</b>, for the same reason the camp
    /// panel is one: the navigator shows one screen at a time by design, so pushing this hid the
    /// HUD and took its compass, portraits and world view with it. The three spells differ only in
    /// the <see cref="FieldSpells.LocatorTarget"/> they pass — see <see cref="LocatorMap"/>.
    /// </remarks>
    public interface ILocatorMapView {
        /// <summary>Shows the inset for one search and returns when the player closes it.</summary>
        UniTask RunAsync(FieldSpells.LocatorTarget target);
    }
}
