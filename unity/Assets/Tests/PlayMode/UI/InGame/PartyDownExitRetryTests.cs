namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System;
    using BakAgain.UI.InGame;
    using NUnit.Framework;

    /// <summary>
    /// A failed party-down exit must not cost the game-over permanently.
    /// </summary>
    /// <remarks>
    /// <c>EndTheLoopIfThePartyIsDown</c> starts <c>LeaveTheWorldPartyDownAsync</c> fire-and-forget
    /// behind a one-shot latch, and that latch only clears when the party reads
    /// <see cref="GameData.Resources.GameState.PartyDownState.Standing"/> again — which a downed
    /// party never does. So a fault anywhere inside the exit used to strand the player in a world
    /// they had already lost: able to walk, camp, heal to full and save, with no way back
    /// (TASK-617).
    ///
    /// <para>It is not reachable by a player today. It is guarded because <b>TASK-578</b> is an open
    /// defect where <c>DialogOpenWipe.PlayAsync</c> throws while rendering certain dialog entries,
    /// and an exception inside a <c>.Forget()</c> is swallowed — if that ever hits entry 0x145 this
    /// is exactly what happens.</para>
    ///
    /// <para><b>Cancellation is deliberately excluded.</b> A cancelled exit means the screen is
    /// being torn down under the transition, so re-arming would spin against a dying object.</para>
    /// </remarks>
    [TestFixture]
    public class PartyDownExitRetryTests {
        [Test]
        public void AnOrdinaryFaultRetries() {
            // The TASK-578 shape: a render that throws inside the fire-and-forget exit.
            Assert.IsTrue(InGameScreen.RetriesAfter(new NullReferenceException()),
                "a swallowed fault must re-arm the exit, or the game-over is lost for good");
            Assert.IsTrue(InGameScreen.RetriesAfter(new InvalidOperationException()));
        }

        /// <summary>
        /// The control. Without it "always retry" would pass the assertions above, and the
        /// distinction this method exists to make would go untested.
        /// </summary>
        [Test]
        public void CancellationDoesNOTRetry() {
            Assert.IsFalse(InGameScreen.RetriesAfter(new OperationCanceledException()),
                "a torn-down screen must not be re-armed against");
            Assert.IsFalse(InGameScreen.RetriesAfter(new TaskCanceledExceptionStandIn()));
        }

        // OperationCanceledException's own subclass, to prove the test keys on the TYPE HIERARCHY
        // rather than on an exact type match — UniTask cancellation surfaces as a subclass.
        private sealed class TaskCanceledExceptionStandIn : OperationCanceledException { }
    }
}
