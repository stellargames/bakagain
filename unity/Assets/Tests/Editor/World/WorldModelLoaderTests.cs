namespace BakAgain.Tests.Editor.World {
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.World;
    using BakAgain.World.Rendering;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine;

    public class WorldModelLoaderTests {
        private WorldModelLoader _loader;

        [TearDown]
        public void TearDown() => _loader?.Dispose();

        [Test]
        public async Task GetAsync_CachesPerTypeId() {
            var tbl = new ZoneTable("Z01") { Entries = new List<ZoneTableEntry> { SyntheticEntity.DepthSortedQuad("a"), SyntheticEntity.DepthSortedQuad("b") } };
            var ctx = WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());
            _loader = new WorldModelLoader(tbl, ctx);

            var first = await _loader.GetAsync(0);
            var again = await _loader.GetAsync(0);
            var other = await _loader.GetAsync(1);

            Assert.AreSame(first, again, "same TypeId must return the cached WorldModel");
            Assert.AreNotSame(first, other);
        }
    }
}
