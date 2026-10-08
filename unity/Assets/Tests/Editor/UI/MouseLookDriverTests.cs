namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;

    public class MouseLookDriverTests {
        private static readonly System.Func<Vector2, bool> World = _ => true;
        private FakePointer _p;
        private MouseLookDriver _d;

        [SetUp]
        public void SetUp() {
            _p = new FakePointer { IsPresent = true };
            _d = new MouseLookDriver(_p, new EnhancedTravelLayout());
        }

        private Vector2 Frame(bool down, Vector2 delta, bool pressed = false, bool released = false) {
            _p.Secondary.SetDown(down, pressed, released);
            _p.Delta = delta;
            _p.ScreenPosition += delta;
            return _d.Tick(enabled: true, World);
        }

        [Test]
        public void AShortRightClickIsStillAClick() {
            Frame(true, Vector2.zero, pressed: true);
            Frame(true, new Vector2(3, 0));
            Frame(false, Vector2.zero, released: true);
            Assert.IsFalse(_d.ConsumeClick(), "under the threshold the click goes through");
        }

        [Test]
        public void ADragLooksAndSwallowsTheClick() {
            Frame(true, Vector2.zero, pressed: true);
            Vector2 look = Frame(true, new Vector2(40, 0));
            Assert.IsTrue(_d.Dragging);
            Assert.Greater(Mathf.Abs(look.x), 0f);
            Frame(false, Vector2.zero, released: true);
            Assert.IsTrue(_d.ConsumeClick());
            Assert.IsFalse(_d.ConsumeClick(), "only once");
        }

        [Test]
        public void TheReleaseClickIsSwallowedEvenWhenUiToolkitDispatchesItBeforeTheTick() {
            Frame(true, Vector2.zero, pressed: true);
            Frame(true, new Vector2(40, 0));
            Assert.IsTrue(_d.ConsumeClick(), "the click arrived before this frame's Tick saw the release");
            Frame(false, Vector2.zero, released: true);
            Frame(false, Vector2.zero);
            Frame(false, Vector2.zero);
            Assert.IsFalse(_d.ConsumeClick(), "nothing left over to eat the next real click");
        }

        [Test]
        public void ADragWhoseClickNeverArrivesDoesNotEatTheNextClick() {
            Frame(true, Vector2.zero, pressed: true);
            Frame(true, new Vector2(40, 0));
            Frame(false, Vector2.zero, released: true);   // released over the HUD: no world click
            Frame(false, Vector2.zero);
            Frame(false, Vector2.zero);
            Assert.IsFalse(_d.ConsumeClick());
        }

        [Test]
        public void APressThatStartsOnTheHudNeverLooks() {
            _p.Secondary.SetDown(true, pressedThisFrame: true);
            _d.Tick(true, _ => false);
            _p.Secondary.SetDown(true);
            _p.Delta = new Vector2(80, 0);
            Assert.AreEqual(Vector2.zero, _d.Tick(true, _ => false));
            Assert.IsFalse(_d.Dragging);
        }

        [Test]
        public void DisabledItDoesNothing() {
            _p.Secondary.SetDown(true, pressedThisFrame: true);
            _p.Delta = new Vector2(80, 80);
            Assert.AreEqual(Vector2.zero, _d.Tick(false, World));
            Assert.IsFalse(_d.ConsumeClick());
        }

        [Test]
        public void OnTouchTheFingerIsThePrimaryButton() {
            _p.IsPresent = false; _p.CanPointOverride = true;
            _p.Primary.SetDown(true, pressedThisFrame: true);
            _d.Tick(true, World);
            _p.Primary.SetDown(true); _p.Delta = new Vector2(0, 40);
            Vector2 look = _d.Tick(true, World);
            Assert.Greater(Mathf.Abs(look.y), 0f);
        }
    }
}
