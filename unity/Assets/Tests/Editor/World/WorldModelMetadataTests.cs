namespace BakAgain.Tests.Editor.World {
    using BakAgain.ResourceManagement.Models;
    using NUnit.Framework;

    public class WorldModelMetadataTests {
        [Test]
        public void FromEntry_PopulatesAllFieldsFromEntry() {
            var entry = SyntheticEntity.DepthSortedQuad("test");

            var meta = WorldModelMetadata.FromEntry(entry, 7);

            Assert.AreEqual(7, meta.TypeId);
            Assert.AreEqual("test", meta.Name);
            Assert.IsTrue(meta.IsDepthSorted);   // DepthSortedQuad = EntityFlags 0x40
            Assert.IsFalse(meta.IsUnbounded);
            Assert.AreEqual(2, (byte)meta.EntityType);
            Assert.AreEqual(0, meta.DrawPriority);
        }

        [Test]
        public void FromEntry_MapsNonZeroDrawPriority() {
            // DepthSortedQuad has DrawPriority=0 — a never-assigned bug would pass that test.
            // PerPenPolygon uses DrawPriority=7 so a missing assignment is caught here.
            // (VertexScale is no longer carried on the metadata: the extractor bakes it into the
            // vertex pools, bbox and Extent, so the engine-independent model has no exponent.)
            var entry = SyntheticEntity.PerPenPolygon("pp");

            var meta = WorldModelMetadata.FromEntry(entry, 3);

            Assert.AreEqual(7, meta.DrawPriority);
        }
    }
}
