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

        /// <summary>Ticks each frame holds — <c>frameDelay</c>.</summary>
        public const int FrameDelayTicks = 4;

        /// <summary>How long one frame is shown.</summary>
        public static float FrameSeconds => (float)(FrameDelayTicks / GameTick.TicksPerSecond);

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

        public void Play(WorldEntityRenderContext ctx, SpriteBMeshFace face, string setName,
            int octant, int extent) {
            if (ctx == null || face == null || string.IsNullOrEmpty(setName)) {
                return;
            }
            RunAsync(ctx, face, setName, RunStartFor(octant), extent).Forget();
        }

        private async UniTaskVoid RunAsync(WorldEntityRenderContext ctx, SpriteBMeshFace face,
            string setName, int start, int extent) {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) {
                return;
            }

            Mesh had = filter.sharedMesh;
            Material hadMaterial = renderer.sharedMaterial;
            Vector3 hadScale = transform.localScale;

            for (var i = 0; i < Frames; i++) {
                Texture2D tex = await ctx.LoadSpriteTextureAsync($"{setName}#{start + i}", gameObject);
                if (tex == null || this == null) {
                    break;
                }

                (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(
                    face, Mathf.Abs(extent), tex.width, tex.height);
                ctx.TrackMesh(mesh);
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = ctx.GetSpriteMaterial(tex);
                transform.localScale = new Vector3(
                    hadScale.x < 0 ? -scale.x : scale.x, scale.y, scale.z);
                CurrentFrame = start + i;

                await UniTask.Delay(System.TimeSpan.FromSeconds(FrameSeconds));
                if (this == null) {
                    return;
                }
            }

            CurrentFrame = -1;
            if (filter != null) {
                filter.sharedMesh = had;
                renderer.sharedMaterial = hadMaterial;
                transform.localScale = hadScale;
            }
            Destroy(this);
        }
    }
}
