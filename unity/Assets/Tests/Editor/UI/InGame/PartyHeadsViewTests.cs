namespace BakAgain.Tests.UI.InGame {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI.InGame;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class PartyHeadsViewTests {
        // The travel HUD calls SetVisible(true) from Update, i.e. every frame out of combat. Each
        // call used to re-render, and every render is three HEADS.BMX loads whose handles the
        // provider keeps until Dispose -- ~180 loads a second, 574,101 handles on one view (TASK-576).
        [Test]
        public void SetVisibleTrue_EveryFrameWhileShown_LoadsNothingMore() {
            var (view, resources, _, _) = Build();
            int afterFirstRender = resources.LoadCalls;

            for (int frame = 0; frame < 60; frame++) {
                view.SetVisible(true);
            }

            Assert.That(afterFirstRender, Is.EqualTo(3));
            Assert.That(resources.LoadCalls, Is.EqualTo(afterFirstRender));
        }

        [Test]
        public void ShowingAgainAfterAFight_RepaintsTheHeads() {
            var (view, resources, root, _) = Build();

            view.SetVisible(false);
            AssertHeads(root, DisplayStyle.None);
            view.SetVisible(true);

            Assert.That(resources.LoadCalls, Is.EqualTo(6));
            AssertHeads(root, DisplayStyle.Flex);
        }

        [Test]
        public void ARosterChangeDuringAFight_StaysHidden_ThenShowsTheNewParty() {
            var (view, resources, root, session) = Build();

            view.SetVisible(false);
            session.SetActiveParty(3, new byte[] { 0, 2, 1 });
            AssertHeads(root, DisplayStyle.None);
            view.SetVisible(true);

            AssertHeads(root, DisplayStyle.Flex);
            Assert.That(resources.LoadCalls, Is.EqualTo(9));
        }

        private static (PartyHeadsView, FakeResourceProviderService, VisualElement, GameSession) Build() {
            var session = new GameSession();
            session.SetActiveParty(3, new byte[] { 4, 2, 1 });
            var resources = new FakeResourceProviderService();
            foreach (int id in new[] { 0, 1, 2, 4 }) {
                resources.Register($"HEADS.BMX#{id}",
                    Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.zero));
            }
            var root = new VisualElement();
            for (int action = 2; action <= 4; action++) {
                root.Add(new VisualElement { name = $"hotspot_{action}" });
            }
            var view = new PartyHeadsView(session, resources);
            view.Attach(root);
            view.RenderAsync().Forget();
            return (view, resources, root, session);
        }

        private static void AssertHeads(VisualElement root, DisplayStyle expected) {
            for (int slot = 0; slot < 3; slot++) {
                Assert.That(root.Q($"partyhead_{slot}").style.display.value, Is.EqualTo(expected), $"slot {slot}");
            }
        }

        [Test]
        public void ActiveSlot_ReturnsCharacterId() {
            byte[] idx = { 3, 1, 4 };
            Assert.That(PartyHeadsView.ResolveHeadId(0, 3, idx, -1), Is.EqualTo(3));
            Assert.That(PartyHeadsView.ResolveHeadId(1, 3, idx, -1), Is.EqualTo(1));
            Assert.That(PartyHeadsView.ResolveHeadId(2, 3, idx, -1), Is.EqualTo(4));
        }

        [Test]
        public void EmptySlot_ReturnsPlaceholder() {
            byte[] idx = { 3, 1 };
            Assert.That(PartyHeadsView.ResolveHeadId(2, 2, idx, -1), Is.EqualTo(-1));
            Assert.That(PartyHeadsView.ResolveHeadId(2, 2, idx, 99), Is.EqualTo(99));
        }

        [Test]
        public void ActiveCountExceedsIndices_Clamps() {
            byte[] idx = { 3 };
            Assert.That(PartyHeadsView.ResolveHeadId(1, 3, idx, -1), Is.EqualTo(-1));
        }

        [Test]
        public void NegativeSlot_ReturnsPlaceholder() {
            byte[] idx = { 3 };
            Assert.That(PartyHeadsView.ResolveHeadId(-1, 3, idx, 7), Is.EqualTo(7));
        }
    }
}
