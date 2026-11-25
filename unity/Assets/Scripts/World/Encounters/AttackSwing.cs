namespace BakAgain.World.Encounters {
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Plays a creature's four-frame swing, then puts its walking sprite back.
    /// </summary>
    /// <remarks>
    /// <b>Slot 3, which is bitmap 1 — <c>SpriteKeys[1]</c>, a set the arena does not otherwise
    /// load.</b> <c>combat_actor_play_anim_sprite3</c> @0x5eeeb asks for <c>framesPerDir 4</c> in
    /// mode 3, so three facings of four frames occupy frames 0..11 of that bitmap.
    ///
    /// <para><b>The facing comes from the sprite's own octant</b>, not a second computation.
    /// <see cref="DirectionalSprite.CurrentOctant"/> already resolves which way the creature is seen;
    /// mode 3 then forces it even and mirrors it above 4, and the frame base is that halved and
    /// scaled — <c>0, 4, 8</c> for the three runs. Deriving it here from scratch would be a second
    /// answer that could disagree with the one being drawn.</para>
    ///
    /// <para><b>Unlike the death collapse this RESTORES what it replaced.</b> A body ends on the
    /// frame the rebuild would draw anyway; a swinging creature is still alive and has to go back to
    /// standing, and the next redraw may be a while away.</para>
    /// </remarks>
    public sealed class AttackSwing : MonoBehaviour {
        /// <summary>Frames in the run — <c>framesPerDir</c>.</summary>
        public const int Frames = 4;

        /// <summary>Arena frames each picture holds — <c>frameDelay</c>.</summary>
        public const int FrameDelayTicks = 4;

        /// <summary>How long one picture is shown: <see cref="FrameDelayTicks"/> ARENA frames
        /// (<see cref="GameData.Resources.Combat.ArenaFrame"/>), not timer ticks (TASK-768).</summary>
        public static float FrameSeconds => (float)(FrameDelayTicks / GameData.Resources.Combat.ArenaFrame.PerSecond);

        /// <summary>The bitmap index currently on screen, or -1 outside a run.</summary>
        public int CurrentFrame { get; private set; } = -1;

        /// <summary>
        /// The first frame of the run for an octant — mode 3's even-and-mirrored rule.
        /// </summary>
        /// <remarks>
        /// <c>startCreatureBitmapAnimation</c> forces the octant even (<c>si -= si % 2</c>), mirrors
        /// anything above 4 to <c>8 - si</c>, and takes <c>facing * framesPerDir / 2</c>. For the
        /// eight octants that is 0, 0, 4, 4, 8, 8, 4, 4 — three distinct runs, which is why this
        /// animation has three facings where the walk has five.
        /// </remarks>
        /// <summary>Frames in the thrust's run — anim slot 4 plays two per facing (CACTOR.C:1947-1950).</summary>
        public const int ThrustFrames = 2;

        /// <summary>The run's first frame and length: the swing four from twice the facing, the
        /// thrust two from the facing (frame base CACTOR.C:1873-1883; TASK-1095).</summary>
        public static (int Start, int Frames) RunFor(int octant, bool thrust) =>
            thrust ? (RunStartFor(octant) / 2, ThrustFrames) : (RunStartFor(octant), Frames);

        /// <summary>Anim slot of the swing and of the thrust (combat_actor_play_anim_sprite3/4).</summary>
        public const int SwingSlot = GameData.Resources.Combat.CombatAnimSlot.Swing,
            ThrustSlot = GameData.Resources.Combat.CombatAnimSlot.Thrust;

        /// <summary>
        /// The run of a slot: its first frame and length, by <c>combat_actor_anim_play</c>'s frame base
        /// (CACTOR.C:1856-1883) with each slot's frames-per-facing and direction mode
        /// (CACTOR.C:1947-1977). Mode 3 forces the facing even; above 4 it mirrors to 8 - facing.
        /// </summary>
        public static (int Start, int Frames) RunFor(int slot, int octant) {
            (int perFacing, int mode) = slot switch {
                GameData.Resources.Combat.CombatAnimSlot.Swing => (4, 3),
                GameData.Resources.Combat.CombatAnimSlot.Thrust => (2, 3),
                GameData.Resources.Combat.CombatAnimSlot.Crossbow => (4, 5),
                GameData.Resources.Combat.CombatAnimSlot.Windup => (2, 5),
                GameData.Resources.Combat.CombatAnimSlot.CastSwing => (2, 3),
                _ => (0, 0),
            };
            if (perFacing == 0) {
                return (0, 0);
            }
            int facing = System.Math.Max(0, octant);
            if (mode == 3) {
                facing -= facing % 2;
            }
            if (facing > 4) {
                facing = 8 - facing;
            }
            int start = perFacing > 1
                ? (mode == 3 ? facing * (perFacing >> 1) : facing * perFacing)
                : facing >> 1;
            return (start, perFacing);
        }

        /// <summary>
        /// The run's pictures: faces of the mesh COMBAT.TBL gives the anim slot
        /// (<see cref="MeshRecord.RuntimeFlagsIndex"/> = slot), from the run's first frame. The frame
        /// is an index into that mesh, not into the bitmap (CACTOR.C:828, 873): the thrust's mesh
        /// starts at picture 12, after the swing's twelve (TASK-1095).
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<SpriteBMeshFace> RunFaces(
            System.Collections.Generic.IReadOnlyList<MeshRecord> meshes, int octant, bool thrust) =>
            RunFaces(meshes, octant, thrust ? ThrustSlot : SwingSlot);

        /// <summary>
        /// Whether the run draws mirrored: the facing it stores (forced even in mode 3) is past 4
        /// (CACTOR.C:1865-1867 store it, 891-894 set g_nActorSpriteFlip from it), whatever the
        /// standing sprite was.
        /// </summary>
        public static bool FlipsFor(int slot, int octant) {
            int facing = System.Math.Max(0, octant);
            bool mode3 = slot != GameData.Resources.Combat.CombatAnimSlot.Windup
                && slot != GameData.Resources.Combat.CombatAnimSlot.Crossbow;
            if (mode3) {
                facing -= facing % 2;
            }
            return facing > 4;
        }

        /// <summary>The faces of <paramref name="slot"/>'s mesh that its run shows.</summary>
        public static System.Collections.Generic.IReadOnlyList<SpriteBMeshFace> RunFaces(
            System.Collections.Generic.IReadOnlyList<MeshRecord> meshes, int octant, int slot) {
            var run = new System.Collections.Generic.List<SpriteBMeshFace>();
            MeshRecord mesh = null;
            for (int i = 0; meshes != null && i < meshes.Count; i++) {
                if (meshes[i]?.RuntimeFlagsIndex == slot) {
                    mesh = meshes[i];
                    break;
                }
            }
            if (mesh?.MeshFaces == null) {
                return run;
            }
            (int start, int frames) = RunFor(slot, octant);
            for (int i = start; i < start + frames && i < mesh.MeshFaces.Count; i++) {
                if (mesh.MeshFaces[i] is SpriteBMeshFace f) {
                    run.Add(f);
                }
            }
            return run;
        }

        public static int RunStartFor(int octant) {
            if (octant < 0) {
                return 0;
            }
            int even = octant - (octant % 2);
            if (even > 4) {
                even = 8 - even;
            }
            return even * Frames / 2;
        }

        /// <summary>Plays the run; the task ends when the walking sprite is back (the original waits for it:
        /// <c>combat_actor_anim_play(..., wait 1, ...)</c>, CACTOR.C:1828-1898).</summary>
        public UniTask Play(WorldEntityRenderContext ctx,
            System.Collections.Generic.IReadOnlyList<MeshRecord> meshes, string setName,
            int octant, int extent, int slot = GameData.Resources.Combat.CombatAnimSlot.Swing, int runs = 1) {
            // A later run on a sprite still holding an earlier one replaces it.
            foreach (AttackSwing earlier in GetComponents<AttackSwing>()) {
                if (earlier != this) {
                    Destroy(earlier);
                }
            }
            System.Collections.Generic.IReadOnlyList<SpriteBMeshFace> run = RunFaces(meshes, octant, slot);
            if (ctx == null || run.Count == 0 || string.IsNullOrEmpty(setName)) {
                return UniTask.CompletedTask;
            }
            return RunAsync(ctx, run, setName, extent, FlipsFor(slot, octant), runs);
        }

        /// <summary>Swings still playing. The arena counts them as busy, so the turn waits for them as the
        /// original's <c>combat_actor_anim_play(..., wait 1, ...)</c> does (CACTOR.C:1895); kept here rather than
        /// at the call site so a caller cannot forget it (TASK-972 round 4).</summary>
        public static int Playing { get; private set; }

        /// <summary>This swing is still playing: the idle gait leaves the combatant alone (TASK-1100).</summary>
        public bool Running { get; internal set; }

        /// <summary>
        /// The swing has ended on its last picture and holds it: a non-0 slot keeps its end frame
        /// (CACTOR.C:862-864) until the next turn redraws the attacker (COMBAT.C:1434-1439), which
        /// here is the arena redraw that replaces this sprite (TASK-1100).
        /// </summary>
        public bool Holding { get; internal set; }

        private async UniTask RunAsync(WorldEntityRenderContext ctx,
            System.Collections.Generic.IReadOnlyList<SpriteBMeshFace> run, string setName, int extent, bool flip,
            int runs) {
            Playing++;
            Running = true;
            try {
                await RunCoreAsync(ctx, run, setName, extent, flip, runs);
            } finally {
                Playing--;
                Running = false;
            }
        }

        private async UniTask RunCoreAsync(WorldEntityRenderContext ctx,
            System.Collections.Generic.IReadOnlyList<SpriteBMeshFace> run, string setName, int extent, bool flip,
            int runs) {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) {
                return;
            }

            Mesh had = filter.sharedMesh;
            Material hadMaterial = renderer.sharedMaterial;
            Vector3 hadScale = transform.localScale;

            // *** RUNS > 1: THE SLING THROWER POSES TWICE AND THEN STANDS. *** Creature 0x1a plays its slot-7
            // pose, slot 0 (not blocking, so never drawn), the pose again, then slot 0 before its flight
            // (CBTAITRN.C:121-126; CACTOR.C:1912-1918), where every other shooter holds the pose's last
            // picture through the flight (TASK-1124).
            for (var pass = 0; pass < runs; pass++) {
                foreach (SpriteBMeshFace face in run) {
                    Texture2D tex = await ctx.LoadSpriteTextureAsync($"{setName}#{face.BitmapIndex}", gameObject);
                    if (tex == null || this == null) {
                        break;
                    }

                    (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(
                        face, Mathf.Abs(extent), tex.width, tex.height);
                    ctx.TrackMesh(mesh);
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterial = ctx.GetSpriteMaterial(tex);
                    // Mirrored by the run's own facing, not the standing sprite's (FlipsFor).
                    transform.localScale = new Vector3(
                        flip ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x), scale.y, scale.z);
                    CurrentFrame = face.BitmapIndex;

                    await UniTask.Delay(System.TimeSpan.FromSeconds(FrameSeconds));
                    if (this == null) {
                        return;
                    }
                }
            }

            // The last picture stays: the redraw that hands the turn on rebuilds this sprite standing.
            // A run that drew nothing has nothing to hold, so the standing picture is put back.
            bool drew = CurrentFrame >= 0;
            CurrentFrame = -1;   // the swing is over (the harness reads this); its picture holds
            // A held run stays on the sprite: the idle gait looks for it (ArenaIdleAnimator), and the
            // redraw that hands the turn on replaces the whole sprite (TASK-1107 D3).
            if (drew && runs <= 1) {
                Holding = true;
                return;
            }
            if (filter != null) {
                filter.sharedMesh = had;
                renderer.sharedMaterial = hadMaterial;
                transform.localScale = hadScale;
            }
            Destroy(this);
        }
    }
}
