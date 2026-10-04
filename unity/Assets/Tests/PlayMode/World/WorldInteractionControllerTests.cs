namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Graphics;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.World;
    using BakAgain.World.Interaction;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public class WorldInteractionControllerTests {
        private sealed class FakeHandler : IWorldInteractionHandler {
            private readonly string _behavior;
            public int Calls;
            public bool LastPrimary;
            public FakeHandler(string behavior) { _behavior = behavior; }
            public string Behavior => _behavior;
            public UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
                Calls++; LastPrimary = isPrimary; return UniTask.CompletedTask;
            }
        }

        private static WorldEntity Entity(string behavior) {
            var go = new GameObject("e");
            var e = go.AddComponent<WorldEntity>();
            e.Behavior = behavior;
            e.Interaction = behavior == null ? null : new InteractionProfile();
            return e;
        }

        [UnityTest]
        public IEnumerator Dispatch_RoutesToHandlerMatchingBehavior() => UniTask.ToCoroutine(async () => {
            var container = new FakeHandler("container");
            var door = new FakeHandler("door");
            var c = new WorldInteractionController(null, null, null, null,
                new List<IWorldInteractionHandler> { container, door });
            WorldEntity e = Entity("container");
            try {
                await c.DispatchAsync(e, isPrimary: true);
                Assert.AreEqual(1, container.Calls);
                Assert.AreEqual(0, door.Calls);
                Assert.IsTrue(container.LastPrimary);
            } finally { Object.DestroyImmediate(e.gameObject); }
        });

        [UnityTest]
        public IEnumerator Dispatch_UnknownOrNullBehavior_NoOp() => UniTask.ToCoroutine(async () => {
            var container = new FakeHandler("container");
            var c = new WorldInteractionController(null, null, null, null,
                new List<IWorldInteractionHandler> { container });
            WorldEntity unknown = Entity("door");   // no handler registered for "door"
            WorldEntity none = Entity(null);
            try {
                await c.DispatchAsync(unknown, isPrimary: true);
                await c.DispatchAsync(none, isPrimary: false);
                await c.DispatchAsync(null, isPrimary: true);
                Assert.AreEqual(0, container.Calls);
            } finally {
                Object.DestroyImmediate(unknown.gameObject);
                Object.DestroyImmediate(none.gameObject);
            }
        });

        // --- HandleClick (pick + proximity gate) coverage ---------------------------------
        // Restores the coverage lost when the old static InRange test (proximity thresholds)
        // and WorldInteractionPointerTests (pointer-driven pick) were deleted without
        // replacement during the WorldInteractionController rewrite (see task-9 review finding).

        private sealed class FullViewport : IWorldViewport {
            public Area CanonicalRect => new Area(0, 0, 1600, 1200);
            public float ViewportAspect => 1600f / 1200f;
            public int FocalLength => 2560;
            // The "viewport" is the whole stage rect it's handed — with a null stage (below),
            // WorldInteractionController.HandleClick's CanonicalStage.ScreenRect fallback supplies
            // the full-screen rect, matching this suite's pre-refactor fixed-Vector2(Screen.*) stub.
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            public Vector2Int RenderTextureSize(Rect stageScreenRect) =>
                new Vector2Int((int)stageScreenRect.width, (int)stageScreenRect.height);
        }

        // Places a pickable "container" WorldEntity dead-center in front of an origin camera,
        // distanceUnity units along +Z (matches WorldPickerTests' raycast setup). Range is
        // 7000/2500 DOS fine units == 70/25 Unity units (BakCoordinateConverter.WorldScale=100).
        private static (Camera camera, GameObject target) BuildPickableScene(float distanceUnity) {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity; // looks +Z

            int layer = LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);
            var target = new GameObject("target");
            target.layer = layer;
            target.transform.position = new Vector3(0, 0, distanceUnity);
            target.AddComponent<BoxCollider>().size = new Vector3(4, 4, 1);
            var we = target.AddComponent<WorldEntity>();
            we.Behavior = "container";
            we.Interaction = new InteractionProfile { Range = new InteractionRange(7000, 2500) };
            Physics.SyncTransforms();
            return (cam, target);
        }

        [UnityTest]
        public IEnumerator HandleClick_PointerNotPresent_NoOp() => UniTask.ToCoroutine(async () => {
            (Camera cam, GameObject target) = BuildPickableScene(distanceUnity: 50); // well within range
            var container = new FakeHandler("container");
            var screen = new Vector2(Screen.width, Screen.height);
            var pointer = new FakePointer { IsPresent = false, ScreenPosition = screen * 0.5f };
            var c = new WorldInteractionController(cam, new FullViewport(), pointer, null,
                new List<IWorldInteractionHandler> { container });
            try {
                await c.HandleClick(isPrimary: true);
                Assert.AreEqual(0, container.Calls);
            } finally {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
            }
        });

        [UnityTest]
        public IEnumerator HandleClick_InRange_DispatchesToHandler() => UniTask.ToCoroutine(async () => {
            (Camera cam, GameObject target) = BuildPickableScene(distanceUnity: 50); // 50 < 70 overground threshold
            var container = new FakeHandler("container");
            var screen = new Vector2(Screen.width, Screen.height);
            var pointer = new FakePointer { IsPresent = true, ScreenPosition = screen * 0.5f };
            var c = new WorldInteractionController(cam, new FullViewport(), pointer, null,
                new List<IWorldInteractionHandler> { container });
            try {
                await c.HandleClick(isPrimary: true);
                Assert.AreEqual(1, container.Calls);
                Assert.IsTrue(container.LastPrimary);
            } finally {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
            }
        });

        [UnityTest]
        public IEnumerator HandleClick_OutOfRange_NoOp() => UniTask.ToCoroutine(async () => {
            (Camera cam, GameObject target) = BuildPickableScene(distanceUnity: 90); // 90 > 70 overground threshold
            var container = new FakeHandler("container");
            var screen = new Vector2(Screen.width, Screen.height);
            var pointer = new FakePointer { IsPresent = true, ScreenPosition = screen * 0.5f };
            var c = new WorldInteractionController(cam, new FullViewport(), pointer, null,
                new List<IWorldInteractionHandler> { container });
            try {
                await c.HandleClick(isPrimary: true);
                Assert.AreEqual(0, container.Calls);
            } finally {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
            }
        });

        /// <summary>
        /// An entity with no behavior does nothing, which is correct for a type the profile table
        /// has no row for — and indistinguishable, on screen, from an entity that was built WITH a
        /// profile and lost it. <c>Behavior</c>/<c>Interaction</c> are <c>[NonSerialized]</c>, so a
        /// zone left in the edit-mode scene comes back from the play-mode serialization round-trip
        /// rendering and picking correctly while being permanently inert — a world where nothing
        /// is clickable, and no message saying why (TASK-82).
        ///
        /// <para>So the two cases must report differently: a WARNING when the type has a row (this
        /// entity lost something it was given), a plain log when it does not (nothing is wrong).</para>
        /// </summary>
        [Test]
        public void MissingBehavior_OnAProfiledType_WarnsThatTheEntityLostIt() {
            var c = new WorldInteractionController(null, null, null, null,
                new List<IWorldInteractionHandler>());
            WorldEntity e = Entity(null);
            e.EntityType = GameData.Resources.World.WorldEntityType.Corn;
            Assert.IsTrue(
                ResourceExtraction.InteractionProfileTable.TryGet(e.EntityType, out _, out _),
                "fixture precondition: Corn must HAVE a profile row, or this test proves nothing");
            try {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "HAS an interaction profile, so this entity was built without one"));
                c.ReportMissingBehavior(e);
            } finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void MissingBehavior_OnAnUnprofiledType_IsJustANote() {
            var c = new WorldInteractionController(null, null, null, null,
                new List<IWorldInteractionHandler>());
            WorldEntity e = Entity(null);
            // *** WATER — AND THE STAND-IN HAS RUN OUT OF OBJECTS. *** Ladder held the role until it
            // gained a "traversal" profile, then Pit, then Grave, then Catapult, then RiftMachine,
            // the last three inside one day (TASK-136). With RiftMachine mapped, the ONLY types left
            // without a row are Ground, Road and Water — terrain, not objects.
            //
            // The old note here said this fixture running out of candidates would mean the table is
            // finished. For objects it now is, which is worth knowing: a future move would have to
            // come from a NEW entity type, not from the backlog of unhandled ones.
            //
            // Standing in for: a world entity type the profile table has no row for at all, so the
            // controller's "no behavior" report is a note rather than a warning. Per the standing
            // advice — do not write a reason why THIS one never will gain a row; just move it and
            // say what it stands for.
            e.EntityType = GameData.Resources.World.WorldEntityType.Water;
            Assert.IsFalse(
                ResourceExtraction.InteractionProfileTable.TryGet(e.EntityType, out _, out _),
                "fixture precondition: the stand-in type must have NO profile row, or this test is "
                + "asserting the other branch");
            try {
                // Together with the sibling test this is discriminating: an implementation that
                // always warned would fail the expected-Log assertion here, and one that never
                // warned would fail the expected-Warning assertion there.
                LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(
                    "no interaction handler for this type yet"));
                c.ReportMissingBehavior(e);
            } finally { Object.DestroyImmediate(e.gameObject); }
        }
    }
}
