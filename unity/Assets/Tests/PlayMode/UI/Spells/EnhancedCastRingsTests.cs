namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Spells;
    using GameData.Resources.Character;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced cast screen's portraits carry the health and stamina rings the travel HUD and the
    /// inventory have, under the same Health rings switch (owner, 2026-10-19: "the stamina/health bars on
    /// the portraits are missing in the cast screen").
    /// </summary>
    public class EnhancedCastRingsTests {
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() => _stash.Restore();

        private static GameSession Party() {
            var session = new GameSession();
            session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            for (int id = 0; id < 3; id++) {
                var stats = new ActorStat[16];
                for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 30, Max = 40 };
                session.SetActorStatsForTest(id, stats);
            }
            return session;
        }

        /// <summary>The REQ_CAST elements the layout moves: Exit, the six schools, the three portraits.</summary>
        private static (VisualElement Root, VisualElement Stage) Req() {
            var root = new VisualElement();
            var stage = new VisualElement();
            root.Add(stage);
            var names = new List<string> { "imagebutton_1" };
            for (int i = 0; i < CastRingLayout.CategoryCount; i++) names.Add($"imagebutton_{CastMenuSelection.FirstSchoolActionId + i}");
            for (int i = 0; i < 3; i++) names.Add(Head(i));
            foreach (string n in names) stage.Add(new VisualElement { name = n });
            return (root, stage);
        }

        private static string Head(int slot) => $"hotspot_{CastMenuSelection.FirstPartySlotActionId + slot}";

        private static IReadOnlyList<RingPosition> Ring() =>
            Enumerable.Range(0, 30).Select(i => new RingPosition {
                X = 400 + (int)(300 * System.Math.Cos(i * 0.21)), Y = 600 + (int)(300 * System.Math.Sin(i * 0.21)),
            }).ToList();

        private static int RingsOn(VisualElement head) => head.Query(className: "enhanced-hud__ring").ToList().Count;

        [Test]
        public void EveryPortraitHasItsRing_AndRevertTakesThemAway() {
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.PortraitRings, true);
            (VisualElement root, VisualElement stage) = Req();
            var layout = new EnhancedCastLayout(root, Party());

            Assert.IsTrue(layout.Apply(stage, Ring(), new HashSet<int>(), combat: false));
            for (int i = 0; i < 3; i++) {
                Assert.Greater(RingsOn(root.Q(Head(i))), 0, $"portrait {i} has its ring");
            }

            layout.Revert();
            for (int i = 0; i < 3; i++) {
                Assert.AreEqual(0, RingsOn(root.Q(Head(i))), $"portrait {i} is back as the REQ drew it");
            }
        }

        [Test]
        public void TheRingsFollowTheHealthRingsSwitch() {
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.PortraitRings, false);
            (VisualElement root, VisualElement stage) = Req();
            var layout = new EnhancedCastLayout(root, Party());

            Assert.IsTrue(layout.Apply(stage, Ring(), new HashSet<int>(), combat: false));

            Assert.AreEqual(0, RingsOn(root.Q(Head(0))));
            layout.Revert();
        }
    }
}
