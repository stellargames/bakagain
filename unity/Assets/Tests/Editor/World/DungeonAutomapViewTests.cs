namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Which entities the dungeon automap shows — <c>renderDungeonAutomap</c>'s one filter.
    /// </summary>
    public class DungeonAutomapViewTests {
        private GameObject _zone;
        private GameObject _mapRoot;
        private GameObject _terrain;
        private GameObject _entities;
        private DungeonAutomapView _view;

        [SetUp]
        public void SetUp() {
            _zone = new GameObject("Zone");
            _mapRoot = new GameObject("Automap");
            _terrain = new GameObject("Terrain");
            _entities = new GameObject("Entities");
            _view = _zone.AddComponent<DungeonAutomapView>();
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in new[] { _zone, _mapRoot, _terrain, _entities }) {
                if (go != null) {
                    Object.DestroyImmediate(go);
                }
            }
        }

        private GameObject Placement(byte tileX, byte tileY, int index) {
            var go = new GameObject($"e{tileX}_{tileY}_{index}");
            go.transform.SetParent(_mapRoot.transform);
            _view.Add(tileX, tileY, index, go);
            return go;
        }

        [Test]
        public void TheMapStartsHiddenSoTravelPaysNothingForIt() {
            Placement(13, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);

            Assert.IsFalse(_mapRoot.activeSelf);
            Assert.IsTrue(_terrain.activeSelf);
            Assert.IsFalse(_view.IsShowing);
        }

        [Test]
        public void ShowRevealsOnlyTheVisitedPlacements() {
            GameObject walkedPast = Placement(13, 9, 0);
            GameObject neverSeen = Placement(13, 9, 1);
            GameObject otherTile = Placement(14, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);

            var visits = new EncounterVisitTable();
            visits.MarkSeen(11, 13, 9, 0);

            int shown = _view.Show(visits, zone: 11);

            Assert.AreEqual(1, shown);
            Assert.IsTrue(walkedPast.activeSelf);
            Assert.IsFalse(neverSeen.activeSelf, "an unexplored corridor is absent, not dimmed");
            Assert.IsFalse(otherTile.activeSelf, "the tile is part of the address, not just the index");
            Assert.AreEqual(1, _view.LastShownCount);
        }

        [Test]
        public void ShowSwapsTheWorldOutForTheMap() {
            Placement(13, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);
            var visits = new EncounterVisitTable();
            visits.MarkSeen(11, 13, 9, 0);

            _view.Show(visits, zone: 11);

            Assert.IsTrue(_mapRoot.activeSelf);
            Assert.IsFalse(_terrain.activeSelf, "the automap is not the world with things hidden");
            Assert.IsFalse(_entities.activeSelf);
            Assert.IsTrue(_view.IsShowing);
        }

        [Test]
        public void HidePutsTheWorldBack() {
            Placement(13, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);
            var visits = new EncounterVisitTable();
            visits.MarkSeen(11, 13, 9, 0);
            _view.Show(visits, zone: 11);

            _view.Hide();

            Assert.IsFalse(_mapRoot.activeSelf);
            Assert.IsTrue(_terrain.activeSelf);
            Assert.IsTrue(_entities.activeSelf);
            Assert.IsFalse(_view.IsShowing);
        }

        [Test]
        public void HideIsSafeWhenTheMapWasNeverShown() {
            _view.Initialize(_mapRoot, _terrain, _entities);
            Assert.DoesNotThrow(() => _view.Hide());
            Assert.IsTrue(_terrain.activeSelf);
        }

        [Test]
        public void AnUnexploredDungeonIsEmpty() {
            // The whole point: a fresh save has every slot free, so opening the map underground
            // before walking anywhere shows nothing at all.
            Placement(13, 9, 0);
            Placement(13, 9, 1);
            _view.Initialize(_mapRoot, _terrain, _entities);

            Assert.AreEqual(0, _view.Show(new EncounterVisitTable(), zone: 11));
        }

        [Test]
        public void AMarkForAnotherZoneDoesNotLeakIn() {
            // The table is shared across all three underground zones, so the zone is part of the
            // address. Without it, walking Z11 would light up Z12's map.
            GameObject go = Placement(13, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);
            var visits = new EncounterVisitTable();
            visits.MarkSeen(12, 13, 9, 0);

            Assert.AreEqual(0, _view.Show(visits, zone: 11));
            Assert.IsFalse(go.activeSelf);
        }

        [Test]
        public void WithNoTableNothingIsShownAndNothingThrows() {
            Placement(13, 9, 0);
            _view.Initialize(_mapRoot, _terrain, _entities);

            Assert.AreEqual(0, _view.Show(null, zone: 11));
            Assert.IsTrue(_mapRoot.activeSelf, "still a map, just an empty one");
        }
    }
}
