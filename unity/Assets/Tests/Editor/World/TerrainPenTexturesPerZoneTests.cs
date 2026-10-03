namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using NUnit.Framework;

    /// <summary>
    /// The ground strips come from the zone's own Z##L.SCX (ZONE.C:355, zone_load_scx_image). The port
    /// baked Z01L.SCX once per session for every zone, so zone 9's brown dirt was drawn as zone 1's grass
    /// (measured at noon on walk SAVE846, both games).
    /// </summary>
    public class TerrainPenTexturesPerZoneTests {
        [Test]
        public void EachZoneBakesFromItsOwnStripImageAndPalette() {
            Assert.AreEqual(("Z09L.SCX", "Z09.PAL"), TerrainPenTextures.SourceKeys(9));
            Assert.AreEqual(("Z01L.SCX", "Z01.PAL"), TerrainPenTextures.SourceKeys(1));
        }

        [Test]
        public void TheBakeIsCachedPerZone_NotOncePerSession() {
            Assert.AreSame(TerrainPenTextures.LoadAll(9), TerrainPenTextures.LoadAll(9));
            Assert.AreNotSame(TerrainPenTextures.LoadAll(1), TerrainPenTextures.LoadAll(9));
        }
    }
}
