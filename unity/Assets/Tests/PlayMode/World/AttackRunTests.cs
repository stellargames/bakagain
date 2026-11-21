namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using NUnit.Framework;

    /// <summary>
    /// The thrust is its own run: anim slot 4, two frames per facing, starting at the facing
    /// (combat_actor_play_anim_sprite4, CACTOR.C:1947-1950; frame base :1873-1883), where the
    /// swing is slot 3, four frames from twice the facing (:1952-1955). The port played the swing
    /// for both, so every walking attack ran twice as long and showed other pictures (TASK-1095).
    /// </summary>
    public class AttackRunTests {
        [TestCase(0, 0, 4)]
        [TestCase(2, 4, 4)]
        [TestCase(4, 8, 4)]
        [TestCase(6, 4, 4)]
        public void TheSwingRunsFourFramesFromTwiceTheFacing(int octant, int start, int frames) =>
            Assert.AreEqual((start, frames), AttackSwing.RunFor(octant, thrust: false));

        [TestCase(0, 0, 2)]
        [TestCase(2, 2, 2)]
        [TestCase(3, 2, 2)]
        [TestCase(4, 4, 2)]
        [TestCase(6, 2, 2)]
        public void TheThrustRunsTwoFramesFromTheFacing(int octant, int start, int frames) =>
            Assert.AreEqual((start, frames), AttackSwing.RunFor(octant, thrust: true));

        /// <summary>
        /// A cast's pose is anim slot 8 (ranged kinds: two frames from twice the facing, mode 5, so an
        /// odd octant keeps its own run) or slot 9 (the rest: two frames from the even facing, mode 3):
        /// combat_actor_play_ranged_windup / play_melee_swing, CACTOR.C:1967-1977, frame base
        /// :1856-1883 (TASK-1107 D3).
        /// </summary>
        [TestCase(8, 3, 6, 2)]
        [TestCase(8, 6, 4, 2)]
        [TestCase(8, 0, 0, 2)]
        [TestCase(9, 3, 2, 2)]
        [TestCase(9, 4, 4, 2)]
        [TestCase(3, 2, 4, 4)]
        [TestCase(4, 3, 2, 2)]
        public void EachSlotRunsFromTheOriginalsFrameBase(int slot, int octant, int start, int frames) =>
            Assert.AreEqual((start, frames), AttackSwing.RunFor(slot, octant));

        /// <summary>
        /// The frame is a face of the slot's own mesh, not a bitmap index: COMBAT.TBL gives each anim
        /// slot a mesh (RuntimeFlagsIndex = the slot) whose faces name the pictures, and the thrust's
        /// mesh starts at picture 12, after the swing's twelve (CACTOR.C:828/873). Seen in the
        /// original: Owyn's thrust east is OWN2 pictures 14 then 15 (wind-back, lunge); the port drew
        /// the swing's 2 and 3 (TASK-1095, 2026-11-21).
        /// </summary>
        [TestCase(2, true, new[] { 14, 15 })]
        [TestCase(0, true, new[] { 12, 13 })]
        [TestCase(2, false, new[] { 4, 5, 6, 7 })]
        public void TheRunDrawsTheFacesOfItsSlotsMesh(int octant, bool thrust, int[] pictures) {
            var meshes = new System.Collections.Generic.List<GameData.Resources.World.MeshRecord> {
                Mesh(0, 0, 15), Mesh(1, 15, 12), Mesh(3, 0, 12), Mesh(4, 12, 6), Mesh(5, 18, 3),
            };

            var drawn = new System.Collections.Generic.List<int>();
            foreach (GameData.Resources.World.SpriteBMeshFace f in AttackSwing.RunFaces(meshes, octant, thrust)) {
                drawn.Add(f.BitmapIndex);
            }

            CollectionAssert.AreEqual(pictures, drawn);
        }

        /// <summary>
        /// A run facing past south-west draws a lower facing's pictures MIRRORED: the stored facing
        /// (mode 3 forced even) above 4 sets g_nActorSpriteFlip (CACTOR.C:1865-1867, 891-894). The port
        /// took the flip from the standing sprite, so Owyn casting west at James from an unflipped
        /// stance showed west's pictures facing east (TASK-1107 D3, compared with the original).
        /// </summary>
        [TestCase(9, 6, true)]
        [TestCase(9, 2, false)]
        [TestCase(9, 5, false)]
        [TestCase(8, 5, true)]
        [TestCase(3, 7, true)]
        [TestCase(4, 4, false)]
        public void ARunPastFourFacingsIsMirrored(int slot, int octant, bool flipped) =>
            Assert.AreEqual(flipped, AttackSwing.FlipsFor(slot, octant));

        /// <summary>
        /// Each slot draws from one of the creature's three bitmaps: 0-1 the first, 3-6 the second,
        /// 7-9 the third (combat_actor_rsrc_load_3values, CACTOR.C:577-596). The port drew every run
        /// from the second, so Owyn's wind-up showed OWN2's pictures, not OWN3's raised staff
        /// (TASK-1107 D3, compared with the original).
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(3, 1)]
        [TestCase(4, 1)]
        [TestCase(6, 1)]
        [TestCase(7, 2)]
        [TestCase(8, 2)]
        [TestCase(9, 2)]
        public void EachSlotDrawsFromItsOwnBitmap(int slot, int bitmap) =>
            Assert.AreEqual(bitmap, GameData.Resources.Combat.CombatAnimSlot.BitmapOf(slot));

        private static GameData.Resources.World.MeshRecord Mesh(byte slot, int first, int count) {
            var m = new GameData.Resources.World.MeshRecord { RuntimeFlagsIndex = slot };
            for (int i = 0; i < count; i++) {
                m.MeshFaces.Add(new GameData.Resources.World.SpriteBMeshFace { BitmapIndex = (ushort)(first + i) });
            }
            return m;
        }
    }
}
