namespace BakAgain.Tests.Editor.Core {
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using NUnit.Framework;

    /// <summary>
    /// When the world loop may act on a dialog's exit request.
    /// </summary>
    /// <remarks>
    /// The original reads <c>nWorldLoopExitRequest</c> at the bottom of a loop pass (WORLDLP.C:402),
    /// after the dialog that wrote it has returned. The port's pump ran every frame the travel
    /// screen was visible — which it is under a dialog — so Finn's chapter-4 conversation, whose
    /// first page writes Var 17, had chapter 5's scenes start under its remaining pages.
    /// </remarks>
    public class GameFlowWorldLoopTests {
        // Request 2 is the exit that is not a chapter advance: the pump only clears it, so the test
        // reaches the decision without a world, a navigator or a scene player.
        private const byte NotAnAdvance = 2;

        private static (GameFlow Flow, GameSession Session) Build() {
            var session = new GameSession();
            var flow = new GameFlow(
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<GameFlow>(),
                null, session, null, null, null, null, null, null, null, null, null, null, null, null);
            return (flow, session);
        }

        private static void Pump(GameFlow flow) =>
            typeof(GameFlow).GetMethod("PumpWorldLoop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(flow, null);

        [Test]
        public void AnExitRequestWaitsForTheConversationToEnd() {
            var (flow, session) = Build();
            session.ChapterTransitionPending = NotAnAdvance;
            session.DialogsPlaying = 1;

            Pump(flow);
            Assert.AreEqual(NotAnAdvance, session.ChapterTransitionPending, "acted on under a dialog");

            session.DialogsPlaying = 0;
            Pump(flow);
            Assert.AreEqual(0, session.ChapterTransitionPending, "control: taken once it has ended");
        }

        // The wait above makes a stale count fatal: a play counted and never released froze every
        // later teleport and chapter change. Measured in chapter 6: after a load the count read 1
        // with nothing on screen, and the palace ladder's queued move never ran.
        [Test]
        public void NoPlaySurvivesAClearedSession() {
            var (flow, session) = Build();
            session.DialogsPlaying = 1;
            session.ChapterTransitionPending = NotAnAdvance;

            session.Clear();
            session.ChapterTransitionPending = NotAnAdvance;
            Pump(flow);

            Assert.AreEqual(0, session.DialogsPlaying);
            Assert.AreEqual(0, session.ChapterTransitionPending, "the world loop runs again");
        }
    }
}
