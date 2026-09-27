namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using GameData.Resources.World;
    using NUnit.Framework;

    public class WorldEntityClassifierTests {
        [Test]
        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)3)]
        public void Classify_TerrainTypesWithPolygons_ReturnsTerrain(byte type) {
            var kind = WorldEntityClassifier.Classify(false, false, (WorldEntityType)type, hasPolygons: true, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.Terrain, kind);
        }

        [Test]
        public void Classify_TerrainTypeWithoutPolygons_ReturnsNone() {
            // Terrain classification requires polygon geometry; without it nothing renders.
            var kind = WorldEntityClassifier.Classify(false, false, (WorldEntityType)0, hasPolygons: false, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.None, kind);
        }

        [Test]
        public void Classify_NonTerrainType_WithFlags_NotTreatedAsTerrain() {
            // entityType 2 is not a terrain type, even with no flags and polygons.
            var kind = WorldEntityClassifier.Classify(false, false, (WorldEntityType)2, hasPolygons: true, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.PolygonEntity, kind);
        }

        [Test]
        public void Classify_UnboundedDepthSortedSprite_NoPolygons_ReturnsSprite() {
            var kind = WorldEntityClassifier.Classify(true, true, (WorldEntityType)5, hasPolygons: false, hasSprite: true);
            Assert.AreEqual(WorldEntityKind.Sprite, kind);
        }

        [Test]
        public void Classify_SpriteFlags_MissingOneFlag_NotSprite() {
            // Only unbounded set, depth-sorted missing -> not a sprite.
            var kind = WorldEntityClassifier.Classify(true, false, (WorldEntityType)5, hasPolygons: false, hasSprite: true);
            Assert.AreEqual(WorldEntityKind.None, kind);
        }

        [Test]
        public void Classify_SpriteFlags_ButHasPolygons_PrefersPolygonEntity() {
            var kind = WorldEntityClassifier.Classify(true, true, (WorldEntityType)5, hasPolygons: true, hasSprite: true);
            Assert.AreEqual(WorldEntityKind.PolygonEntity, kind);
        }

        [Test]
        public void Classify_SpriteFlags_ButNoSpriteBitmap_NotSprite() {
            var kind = WorldEntityClassifier.Classify(true, true, (WorldEntityType)5, hasPolygons: false, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.None, kind);
        }

        [Test]
        public void Classify_PolygonEntity_NonTerrainFlags_ReturnsPolygonEntity() {
            // Has polygons but neither terrain nor sprite criteria met.
            var kind = WorldEntityClassifier.Classify(false, false, (WorldEntityType)5, hasPolygons: true, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.PolygonEntity, kind);
        }

        [Test]
        public void Classify_EmptyEntity_ReturnsNone() {
            var kind = WorldEntityClassifier.Classify(false, false, (WorldEntityType)5, hasPolygons: false, hasSprite: false);
            Assert.AreEqual(WorldEntityKind.None, kind);
        }
    }
}
