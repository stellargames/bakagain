namespace BakAgain.UI.Cheats {
    using GameData.Resources.World;

    /// <summary>The travel screen's menu, REQ_KNOC — <c>townscene_cheat_menu_screen</c> (TOWNSCN.C:684).</summary>
    public sealed class KnockKnockCheatScreen : CheatCentralScreen {
        protected override CheatCentral.Effect EffectFor(int actionId) => CheatCentral.KnockKnockEffect(actionId);

        protected override GameData.Resources.Inventory.RuntimeContainer ItemChest() =>
            Session.GetRuntimeContainerAt(0, CheatCentral.KnockKnockChestX, 0);

        /// <summary>Opened as chapter 9 (TOWNSCN.C:715-720) — the shelf hides what a chapter has not reached.</summary>
        protected override int? ChestChapter => CheatCentral.KnockKnockChestChapter;
    }
}
