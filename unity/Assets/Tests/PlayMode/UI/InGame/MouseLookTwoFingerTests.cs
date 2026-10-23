namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// TASK-967: a finger held on a movement pad and a finger dragging the world are independent, in
    /// either order. The look used to follow the touchscreen's primary touch alone, so a pad finger
    /// that went down first was "the pointer" and a second finger's drag was never seen.
    /// Real Touchscreen device, real touch ids, through SystemInputSource.
    /// </summary>
    public class MouseLookTwoFingerTests : InputTestFixture {
        private static readonly Vector2 Pad = new Vector2(100, 100);     // the pads sit bottom-left
        private static readonly Vector2 World = new Vector2(800, 400);
        private static bool OnWorld(Vector2 screen) => screen.x > 500;

        private SystemInputSource _source;
        private MouseLookDriver _look;

        public override void Setup() {
            base.Setup();
            InputSystem.AddDevice<Touchscreen>();
            _source = new SystemInputSource();
            _look = new MouseLookDriver(_source, new EnhancedTravelLayout());
        }

        public override void TearDown() {
            _source.Dispose();
            base.TearDown();
        }

        private Vector2 Tick() => _look.Tick(true, OnWorld);

        [Test]
        public void APadHeldFirstDoesNotBlockALookByASecondFinger() {
            BeginTouch(1, Pad);
            Tick();
            BeginTouch(2, World);
            Tick();
            MoveTouch(2, World + new Vector2(40, 0), new Vector2(40, 0));
            Vector2 look = Tick();

            Assert.IsTrue(_look.Dragging, "the second finger's drag was not seen");
            Assert.Greater(Mathf.Abs(look.x), 0f);

            EndTouch(2, World + new Vector2(40, 0));
            Tick();
            Assert.IsFalse(_look.Dragging, "lifting the look finger ends the look while the pad stays held");
        }

        [Test]
        public void ALookFirstKeepsLookingWhileAPadIsPressed_AndThePadFingerNeverLooks() {
            BeginTouch(1, World);
            Tick();
            MoveTouch(1, World + new Vector2(40, 0), new Vector2(40, 0));
            Tick();
            BeginTouch(2, Pad);
            Tick();
            MoveTouch(1, World + new Vector2(80, 0), new Vector2(40, 0));
            Assert.Greater(Mathf.Abs(Tick().x), 0f, "the look stopped when the pad finger came down");

            EndTouch(1, World + new Vector2(80, 0));
            Tick();
            MoveTouch(2, Pad + new Vector2(40, 0), new Vector2(40, 0));
            Assert.AreEqual(Vector2.zero, Tick(), "the pad finger is not a look finger");
            Assert.IsFalse(_look.Dragging);
        }

        [Test]
        public void AFingerThatWentDownOnAPadNeverLooks_EvenSlidOntoTheWorld() {
            BeginTouch(1, Pad);
            Tick();
            MoveTouch(1, World, World - Pad);
            Assert.AreEqual(Vector2.zero, Tick());
            Assert.IsFalse(_look.Dragging);
        }
    }
}
