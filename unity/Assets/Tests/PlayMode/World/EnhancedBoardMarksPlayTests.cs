namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using System.Linq;
    using BakAgain.Tests.Editor.World.Hotspots;
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>The enhanced board's marks and foe plates (TASK-972): drawn in Enhanced combat, absent in Classic.</summary>
    public class EnhancedBoardMarksPlayTests {
        private bool _enhanced, _combatOn;
        private GameObject _root;

        [SetUp]
        public void Remember() {
            _enhanced = BakAgain.Core.GameOptions.Enhanced;
            _combatOn = BakAgain.Core.GameOptions.GetFeature(BakAgain.Core.EnhancedFeature.Combat);
            BakAgain.Core.GameOptions.Enhanced = true;
        }

        [TearDown]
        public void Restore() {
            BakAgain.Core.GameOptions.Enhanced = _enhanced;
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, _combatOn);
            if (_root != null) { Object.DestroyImmediate(_root); }
        }

        private BakAgain.World.WorldRuntime NewWorld(bool combat) {
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, combat);
            return new BakAgain.World.WorldRuntime(null,
                new BakAgain.Core.GameSession { PositionX = 50000, PositionY = 60000 }, null, null, null, null, null);
        }

        [Test]
        public void ClassicCombatDrawsNoMarksAndNoPlates() {
            _root = new GameObject("arena");
            Assert.IsNull(NewWorld(combat: false).AttachBoardMarks(_root));
            Assert.IsNull(_root.GetComponent<ArenaBoardMarks>());
        }

        [UnityTest]
        public IEnumerator EnhancedCombatDrawsTheReachThrustMarksAndTheFoePlate() {
            var (service, acting) = EnhancedBoardMarksTests.PartyTurn();
            service.Combat.Grid.SetOccupied(acting.X, acting.Y, false);
            acting.X = 4; acting.Y = 4; acting.Speed = 5;
            service.Combat.Grid.SetOccupied(4, 4, true);
            var foe = service.Combat.Encounter.Enemies[0];
            service.Combat.Grid.SetOccupied(foe.X, foe.Y, false);
            foe.X = 5; foe.Y = 4;
            service.Combat.Grid.SetOccupied(5, 4, true);
            service.CursorCell = (5, 4);

            _root = new GameObject("arena");
            var marker = new GameObject("foe");
            marker.transform.SetParent(_root.transform, false);
            marker.AddComponent<ArenaCombatant>().RosterSlot = 0;
            var quad = new Mesh();
            quad.SetVertices(new[] { new Vector3(-1, 0, 0), new Vector3(-1, 2, 0), new Vector3(1, 2, 0), new Vector3(1, 0, 0) });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            marker.AddComponent<MeshFilter>().sharedMesh = quad;

            var marks = _root.AddComponent<ArenaBoardMarks>();
            marks.Bind(() => service.EnhancedBoardShapes(128f / 300f), service.EnhancedFoePlates, service.EnhancedBoardInputs,
                new GameData.Resources.Layout.EnhancedCombatLayout(), 3f);
            yield return null;

            int rebuilds = marks.Rebuilds;
            for (int frame = 0; frame < 5; frame++) {
                yield return null;
            }
            Assert.AreEqual(rebuilds, marks.Rebuilds, "unchanged inputs: no rebuild frame after frame (I-2)");

            var kinds = marks.DrawnKinds().ToList();
            CollectionAssert.IsSubsetOf(new[] { "ReachFill", "Actor", "ThrustMark", "SwingMark", "Target", "Path" }.Take(5), kinds);
            GameObject plate = marks.PlateOf(0);
            Assert.IsNotNull(plate, "the living foe has a plate");
            Assert.AreEqual("A", plate.transform.Find("Letter").GetComponent<TextMesh>().text);
            Assert.AreEqual("⚔⚔", plate.transform.Find("Swords").GetComponent<TextMesh>().text, "thrust and swing both open");
            Assert.AreEqual("", plate.transform.Find("Seen").GetComponent<TextMesh>().text, "nothing seen yet, nothing shown");

            service.Combat.Knowledge.AddSeen(0, 0);    // a miss
            service.Combat.Knowledge.AddSeen(0, -3);   // a heal
            yield return null;
            Assert.AreEqual("", marks.PlateOf(0).transform.Find("Seen").GetComponent<TextMesh>().text, "a miss or a heal shows nothing");

            service.Combat.Knowledge.AddSeen(0, 6);
            yield return null;
            Assert.AreEqual(GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.EnhancedCombatSeen, ("count", 6)),
                marks.PlateOf(0).transform.Find("Seen").GetComponent<TextMesh>().text);
            StringAssert.Contains("6", marks.PlateOf(0).transform.Find("Seen").GetComponent<TextMesh>().text);
        }

        [UnityTest]
        public IEnumerator APlateFacesTheCameraItIsGiven_NotCameraMain() {
            var (service, _) = EnhancedBoardMarksTests.PartyTurn();
            _root = new GameObject("arena");
            var marker = new GameObject("foe");
            marker.transform.SetParent(_root.transform, false);
            marker.AddComponent<ArenaCombatant>().RosterSlot = 0;
            var quad = new Mesh();
            quad.SetVertices(new[] { new Vector3(-1, 0, 0), new Vector3(-1, 2, 0), new Vector3(1, 2, 0), new Vector3(1, 0, 0) });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            marker.AddComponent<MeshFilter>().sharedMesh = quad;
            var camGo = new GameObject("WorldCamera");   // untagged, as the game's own: Camera.main never finds it
            try {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.rotation = Quaternion.Euler(40f, 180f, 0f);
                var marks = _root.AddComponent<ArenaBoardMarks>();
                marks.Bind(() => service.EnhancedBoardShapes(128f / 300f), service.EnhancedFoePlates, service.EnhancedBoardInputs,
                    new GameData.Resources.Layout.EnhancedCombatLayout(), 3f, () => cam);
                yield return null;
                yield return null;
                GameObject plate = marks.PlateOf(0);
                Assert.IsNotNull(plate);
                Assert.AreEqual(1f, Vector3.Dot(plate.transform.forward, cam.transform.forward), 1e-3f,
                    "the plate turns with the world camera, so its letter reads the right way round");
            } finally {
                Object.Destroy(camGo);
            }
        }

        [UnityTest]
        public IEnumerator APathWithTheSameLengthAndStartButAnotherEndRedraws_AndRefreshesDoNotLeak() {
            var paths = new System.Collections.Generic.List<(long X, long Y)[]> {
                new[] { (0L, 0L), (300L, 0L), (600L, 0L) },
            };
            _root = new GameObject("arena");
            var marks = _root.AddComponent<ArenaBoardMarks>();
            for (int round = 0; round < 3; round++) {
                marks.Bind(() => new System.Collections.Generic.List<BakAgain.World.Hotspots.HotspotService.BoardShape> {
                    new BakAgain.World.Hotspots.HotspotService.BoardShape(ArenaOverlayKind.Path, paths[0]) },
                    () => null, () => paths[0].GetHashCode(), new GameData.Resources.Layout.EnhancedCombatLayout(), 3f);
                yield return null;
            }
            yield return null;
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            Mesh first = _root.GetComponentInChildren<MeshFilter>().sharedMesh;

            for (int round = 0; round < 6; round++) {
                paths[0] = new[] { (0L, 0L), (300L, 0L), (600L, round % 2 == 0 ? 300L : 0L) };
                yield return null;
                yield return null;
                Mesh now = _root.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.AreNotSame(first, now, "a different end redraws");
                first = now;
            }
            for (int round = 0; round < 3; round++) {
                marks.Bind(() => null, () => null, () => 0, new GameData.Resources.Layout.EnhancedCombatLayout(), 3f);
                yield return null;
            }
            yield return null;
            Assert.LessOrEqual(Resources.FindObjectsOfTypeAll<Mesh>().Length, meshes, "no meshes accumulate");
            Assert.LessOrEqual(Resources.FindObjectsOfTypeAll<Material>().Length, materials, "no materials accumulate");
        }
    }
}
