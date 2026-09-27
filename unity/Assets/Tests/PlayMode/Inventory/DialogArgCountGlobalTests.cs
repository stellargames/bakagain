namespace BakAgain.Tests.PlayMode.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Character;
    using GameData.Resources.GameState;
    using NUnit.Framework;

    /// <summary>
    /// The global the inventory screen publishes a dialog's mode through.
    /// </summary>
    /// <remarks>
    /// <b>It was 30016, and that is the party-down byte.</b> Every lock prompt wrote its
    /// <see cref="LockPicking.LockContext"/> there — 1 for a door, 2 for a container, 3 for a
    /// traversal — so opening any of them told the game the party was down and
    /// <c>InGameScreen.LeaveTheWorldPartyDownAsync</c> left the world for the main menu. Measured on
    /// the Mac Mordain Cadal's tunnel: <c>PartyDeathState</c> 0 → 3. TASK-397.
    ///
    /// <para>The arg count is index 0, and the shipped prompt agrees: dialog 79's four branches are
    /// <c>VarCondition Var 0</c> with Min=Max=0..3, one per context.</para>
    /// </remarks>
    public class DialogArgCountGlobalTests {
        [Test]
        public void TheDialogArgGlobalIsTheArgCount_NotAFieldWithItsOwnMeaning() {
            Assert.That(GameStateEventFields.FieldFor(InventoryMenu.DialogArgCountGlobal),
                Is.EqualTo(GameStateEventFields.Field.EventArgCount));
        }

        [Test]
        public void EveryLockContextWouldHaveFlaggedThePartyDown_AtTheOldKey() {
            // The regression this pins, stated as the arithmetic that caused it: the old constant
            // names a field the session ACTS on, so any non-zero context ended the session. Only
            // Person, which is zero, was harmless — which is why it went unnoticed.
            const int oldKey = 30016;
            Assert.That(GameStateEventFields.FieldFor(oldKey),
                Is.EqualTo(GameStateEventFields.Field.PartyDeathState));

            var session = new GameSession();
            foreach (LockPicking.LockContext context in
                     System.Enum.GetValues(typeof(LockPicking.LockContext))) {
                session.PartyDeathState = 0;
                session.SetGlobalValue(InventoryMenu.DialogArgCountGlobal, (int)context);
                Assert.That(session.PartyDeathState, Is.EqualTo(0), context.ToString());
            }
        }
    }
}
