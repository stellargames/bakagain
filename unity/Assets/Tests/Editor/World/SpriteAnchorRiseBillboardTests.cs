namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A billboard whose anchor vertex is raised hangs from that height (renderSprite2 0x23031):
    /// COMBAT.TBL's whirlwind has AnchorY 0 and its vertex at Z 462, so without the rise the whole
    /// image sat under the arena floor and Winds of Eortis showed nothing (TASK-117).
    /// </summary>
    public class SpriteAnchorRiseBillboardTests {
        private static readonly SpriteBMeshFace TopAnchored = new() { AnchorX = 18, AnchorY = 0, SizeScale = 0 };

        private static float WorldBottom(int riseBak) {
            (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(TopAnchored, 200, 200, 258, riseBak);
            return mesh.bounds.min.y * scale.y;
        }

        [Test]
        public void ATopAnchoredSpriteWithoutARiseHangsBelowItsOrigin() {
            Assert.Less(WorldBottom(0), -1f);
        }

        [Test]
        public void TheWhirlwindsRiseStandsItOnTheFloor() {
            float bottom = WorldBottom(462);
            Assert.That(bottom, Is.InRange(-0.5f, 0.5f),
                "the image's top hangs from 4.62 units up and it is ~4.8 tall, so its foot is at the floor");
        }
    }
}
