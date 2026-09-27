namespace BakAgain.Tests.Editor.World {
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.World;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;

    public class TblModelConverterTests {
        private GameObject _visual;

        [TearDown]
        public void TearDown() {
            if (_visual != null) Object.Destroy(_visual);
            _visual = null;
        }

        [Test]
        public void BuildVisual_PerPenPolygon_ProducesDisabledTemplateWithMesh() {
            var entry = SyntheticEntity.PerPenPolygon("pp");

            // Same context as the depth-sorted test — the per-pen branch (EntityFlags 0x00) exercises
            // TblMeshConverter.ConvertTerrainEntity → WorldMeshObjects.BuildTerrain.
            var ctx = WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());

            _visual = TblModelConverter.BuildVisualAsync(entry, ctx).GetAwaiter().GetResult();

            Assert.IsNotNull(_visual);
            Assert.IsFalse(_visual.activeSelf, "template must be disabled");
            var mf = _visual.GetComponentInChildren<MeshFilter>();
            Assert.IsNotNull(mf);
            Assert.Greater(mf.sharedMesh.vertexCount, 0);
        }

        [Test]
        public void BuildVisual_DepthSortedEntity_ProducesDisabledTemplateWithMesh() {
            var entry = SyntheticEntity.DepthSortedQuad("test");

            // Minimal render context: a vertex-color polygon material is enough for a depth-sorted model.
            var ctx = WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());

            _visual = TblModelConverter.BuildVisualAsync(entry, ctx).GetAwaiter().GetResult();

            Assert.IsNotNull(_visual);
            Assert.IsFalse(_visual.activeSelf, "template must be disabled");
            var mf = _visual.GetComponentInChildren<MeshFilter>();
            Assert.IsNotNull(mf);
            Assert.Greater(mf.sharedMesh.vertexCount, 0);
        }
    }
}
