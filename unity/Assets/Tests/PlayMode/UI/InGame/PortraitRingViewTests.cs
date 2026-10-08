namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI.InGame;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced HUD's rings read stats the faithful HUD never reads, so the read must change
    /// nothing a save carries. <c>EffectiveStat</c> is <c>stat_actor_get</c>, which writes the slot's
    /// cached-effective byte and frees lapsed modifiers — statediff caught the ring changing
    /// Stamina's cached byte (41 vs 40) through it.
    /// </summary>
    public class PortraitRingViewTests {
        [Test]
        public void RefreshLeavesEveryStatRecordAndModifierSlotUntouched() {
            var session = new GameSession();
            session.Initialize(new SaveGameBuilder().WithBackingBody().WithPartyActors().Build(),
                GameSessionSource.NewGame);
            byte[] roster = session.ActivePartyIndices;
            Assert.IsNotEmpty(roster, "the fixture has a party");

            // A sentinel no read would compute, in every cached-effective byte, and a LAPSED
            // stamina modifier on the first member: the two things a mutating read would change.
            foreach (byte id in roster) {
                foreach (ActorStat stat in session.StatsOf(id)) {
                    if (stat != null) stat.Effective = 0xEE;
                }
            }
            session.GameTimeIn2Seconds = 1000;
            ActorStatModifiers.Slot[] slots = session.StatModifierSlotsFor(roster[0]);
            Assert.IsNotNull(slots, "the fixture has a modifier table");
            slots[0] = new ActorStatModifiers.Slot(
                (int)ActorStatModifiers.ModifierFlags.Expires | 1, 1 << (int)ActorAttribute.Stamina,
                5, appliedAt: 0, expiresAt: 1);
            session.CommitStatModifierSlots(roster[0], slots);

            List<string> before = Snapshot(session, roster);
            var head = new VisualElement();
            new PortraitRingView(session, head, 0).Refresh();

            CollectionAssert.AreEqual(before, Snapshot(session, roster));
        }

        private static List<string> Snapshot(GameSession session, byte[] roster) {
            var s = new List<string>();
            foreach (byte id in roster) {
                ActorStat[] stats = session.StatsOf(id);
                for (int i = 0; i < stats.Length; i++) {
                    ActorStat a = stats[i];
                    s.Add(a == null ? $"{id}/{i}: null"
                        : $"{id}/{i}: {a.Base} {a.Max} {a.Experience} {a.Modifier} {a.Effective}");
                }
                foreach (ActorStatModifiers.Slot m in session.StatModifierSlotsFor(id) ?? new ActorStatModifiers.Slot[0]) {
                    s.Add($"{id} mod {m.Flags} {m.StatMask} {m.Value} {m.ExpiresAt}");
                }
            }
            return s;
        }
    }
}
