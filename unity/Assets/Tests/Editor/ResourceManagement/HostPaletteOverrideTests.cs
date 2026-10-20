namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;

    /// <summary>
    /// A portrait is requested as <c>ACT001A.BMX#0@INVENTOR.PAL</c> (the host palette it is drawn
    /// against, <see cref="GameData.PaletteMapping.WithHostPalette"/>). A mod's replacement is one
    /// PNG whatever the host, so the suffix must not stop the lookup.
    /// </summary>
    public class HostPaletteOverrideTests {
        private TempOverrideDirectory _overrides;

        [SetUp]
        public void SetUp() => _overrides = new TempOverrideDirectory();

        [TearDown]
        public void TearDown() => _overrides.Dispose();

        [Test]
        public void Locate_HostPaletteKey_FindsOverride() {
            string file = _overrides.Write(Path.Combine("BMX", "ACTOR1"), "0.png", "png");
            string key = GameData.PaletteMapping.WithHostPalette("ACTOR1.BMX#0", "OPTIONS");

            bool found = new OverrideResourceLocator().Locate(key, typeof(Sprite), out IList<IResourceLocation> locations);

            Assert.IsTrue(found, key);
            Assert.AreEqual(file, locations[0].InternalId);
            Assert.AreEqual(key, locations[0].PrimaryKey, "the location keeps the key that was asked for");
        }
    }
}
