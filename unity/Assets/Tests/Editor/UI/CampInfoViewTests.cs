namespace BakAgain.Tests.Editor.UI {
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI.Rest;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>The Enhanced camp screen's "?" and its explanation (TASK-980).</summary>
    public class CampInfoViewTests {
        private EnhancedPrefsStash _stash;
        private GameSession _session;

        [SetUp]
        public void SetUp() {
            _stash = EnhancedPrefsStash.Take();
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "Locklear", "Owyn", "Gorath" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            SetPool(0, 41, 55, 0, 45);    // TASK-979's Locklear
            SetPool(1, 40, 40, 41, 45);   // and Owyn, above four fifths
            SetPool(2, 60, 60, 65, 65);   // Gorath full
        }

        [TearDown]
        public void TearDown() => _stash.Restore();

        private void SetPool(int character, int health, int healthMax, int stamina, int staminaMax) {
            ActorStat[] stats = _session.StatsOf(character);
            stats[(int)ActorAttribute.Health].Base = (byte)health;
            stats[(int)ActorAttribute.Health].Max = (byte)healthMax;
            stats[(int)ActorAttribute.Stamina].Base = (byte)stamina;
            stats[(int)ActorAttribute.Stamina].Max = (byte)staminaMax;
        }

        private static readonly Rect CampPanel = new(65, 66, 1470, 606);

        [Test]
        public void InEnhancedTheQuestionMarkOpensTheRulesAndALinePerMember() {
            GameOptions.Enhanced = true;
            var stage = new VisualElement();
            var view = new CampInfoView();
            view.Build(stage, _session, CampPanel);

            VisualElement panel = stage.Q(CampInfoView.PanelName);
            Assert.IsNotNull(stage.Q(CampInfoView.ButtonName));
            Assert.AreEqual(DisplayStyle.None, panel.style.display.value, "closed until asked");
            Assert.AreEqual(CampPanel.yMax, panel.style.top.value.value, "under the camp panel");

            view.Toggle();
            Assert.AreEqual(DisplayStyle.Flex, panel.style.display.value);
            CollectionAssert.AreEqual(new[] {
                "Locklear's health comes back first, then stamina.",
                "Camping can't help Owyn further. An inn can.",
                "Gorath is fully rested.",
            }, panel.Query<Label>(className: CampInfoView.MemberLineClass).ToList().Select(l => l.text));

            view.Build(stage, _session, CampPanel);
            Assert.AreEqual(DisplayStyle.Flex, stage.Q(CampInfoView.PanelName).style.display.value,
                "a rebuild after a rest hour keeps it open");
            Assert.AreEqual(1, stage.Query(CampInfoView.PanelName).ToList().Count, "and does not stack");
        }

        [Test]
        public void ClassicHasNoQuestionMark() {
            GameOptions.Enhanced = false;
            var stage = new VisualElement();
            new CampInfoView().Build(stage, _session, CampPanel);
            Assert.IsNull(stage.Q(CampInfoView.ButtonName));
            Assert.IsNull(stage.Q(CampInfoView.PanelName));
        }
    }
}
