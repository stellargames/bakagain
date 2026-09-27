namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using NUnit.Framework;

    /// <summary>
    /// Staged ENEMY edits belong to the save they were staged against, and a load discards them.
    /// </summary>
    /// <remarks>
    /// Unlike the party's, which <c>CollectDirtyActorEdits</c> recomputes from <c>_actorStats</c>
    /// every time a save is written, roster edits ACCUMULATE in a dictionary. Nothing emptied it on
    /// load, so an enemy killed before a load was still staged after it and the next save wrote that
    /// kill into a file whose own timeline had never fought them (TASK-619).
    ///
    /// <para><b>Why that is worse than a stale number:</b> the victims come back at zero health with
    /// no <c>CAF_DEAD</c> flag, and <c>CombatEncounter.EnemiesAlive</c> counts the FLAG, not the
    /// health — so they are counted as living, <c>IsOver()</c> never becomes true, and the party is
    /// trapped in a fight it can neither win nor leave. Found while playing: <c>walk/SAVE346</c> was
    /// written that way and its road to Silden is impassable (TASK-618 records the symptom).</para>
    /// </remarks>
    public class GameSessionRosterEditResetTests {
        private static DirtyRosterActorEdit KilledEnemy(int actorSlot) {
            var stats = new ActorStat[17];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 0, Max = 25 };
            }
            return new DirtyRosterActorEdit(actorSlot, stats);
        }

        [Test]
        public void ALoadDiscardsEnemyEditsStagedAgainstThePreviousSave() {
            var session = new GameSession();
            session.Initialize(new SaveGameBuilder().Build(), GameSessionSource.LoadSave);

            session.StageRosterActorEdits(new[] { KilledEnemy(466), KilledEnemy(467) });
            Assert.AreEqual(2, session.DirtyRosterActorEdits.Count,
                "sanity: the kills must actually be staged, or this test proves nothing");

            // The load that starts a different timeline.
            session.Initialize(new SaveGameBuilder().Build(), GameSessionSource.LoadSave);

            Assert.IsEmpty(session.DirtyRosterActorEdits,
                "kills staged before a load must not be written into the loaded timeline's saves");
        }

        /// <summary>
        /// The control: the reset must not break staging itself.
        /// </summary>
        /// <remarks>
        /// Without this, clearing the dictionary unconditionally on every access — or never
        /// accepting edits at all — would pass the test above while silently losing every enemy's
        /// wounds, which is the bug TASK-226 fixed and this must not reintroduce.
        /// </remarks>
        [Test]
        public void EditsStagedAFTERTheLoadAreStillKept() {
            var session = new GameSession();
            session.Initialize(new SaveGameBuilder().Build(), GameSessionSource.LoadSave);

            session.StageRosterActorEdits(new[] { KilledEnemy(466) });

            Assert.AreEqual(1, session.DirtyRosterActorEdits.Count);
            Assert.AreEqual(466, session.DirtyRosterActorEdits[0].ActorSlot);
        }
    }
}
