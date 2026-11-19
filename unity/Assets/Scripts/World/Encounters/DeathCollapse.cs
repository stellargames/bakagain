namespace BakAgain.World.Encounters {
    using Cysharp.Threading.Tasks;
    using BakAgain.World.Converters;
    using GameData.Resources.Animation;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Plays a creature's four-frame collapse on the body that has just appeared.
    /// </summary>
    /// <remarks>
    /// <b>It runs on the CORPSE, not on the living sprite, and that is what makes it cheap.</b> The
    /// picture the arena already draws for a body is the death run's LAST frame — the downed mesh's
    /// columns for a mordel are bitmaps 18, 22 and 26, exactly the fourth frame of each four-frame
    /// run (15..18, 19..22, 23..26). So the animation counts back three from the frame this
    /// GameObject was built with and ends on it, which means the hand-off is a no-op: the last frame
    /// drawn is the frame that would have been there anyway.
    ///
    /// <para><b>The mesh is rebuilt per frame rather than swapped.</b> The images differ in size —
    /// a creature standing and a creature collapsed are not the same rectangle — and
    /// <c>TblSpriteConverter.BuildBillboard</c> takes the texture's own dimensions, so a shared quad
    /// would stretch them.</para>
    ///
    /// <para><b>Timing is the original's.</b> <c>combat_actor_play_anim_sprite1</c> @0x5ef0c passes
    /// <c>frameDelay 4</c> on the animation clock, which is the ARENA FRAME (<see cref="GameData.Resources.Combat.ArenaFrame"/>):
    /// each picture holds four drawn frames, ~0.25 s, ~1 s for the run.</para>
    ///
    /// <para><b>Whether it has played lives on the COMBATANT</b>, because a corpse is rebuilt on
    /// every combat redraw and a flag here would make the body fall over again each time.</para>
    /// </remarks>
    public sealed class DeathCollapse : MonoBehaviour {
        /// <summary>
        /// Whether a blow is still on its way, so a body must not start falling yet: a melee swing,
        /// a projectile in flight, or a spell's effect still playing (TASK-1073, TASK-1107).
        /// </summary>
        public static bool HeldBack =>
            AttackSwing.Playing > 0 || ProjectileFlight.Flying > 0 || SpellVfx.Draining > 0;

        /// <summary>Frames in the run — <c>framesPerDir</c>.</summary>
        public const int Frames = 4;

        /// <summary>Arena frames each picture holds — <c>frameDelay</c>.</summary>
        public const int FrameDelayTicks = 4;

        /// <summary>How long one picture is shown: <see cref="FrameDelayTicks"/> ARENA frames
        /// (<see cref="GameData.Resources.Combat.ArenaFrame"/>), not timer ticks (TASK-768).</summary>
        public static float FrameSeconds => (float)(FrameDelayTicks / GameData.Resources.Combat.ArenaFrame.PerSecond);

        /// <summary>The bitmap index currently on screen, or -1 before the run starts.</summary>
        /// <remarks>
        /// Exposed for the reason <c>DirectionalSprite.GaitFrame</c> is: which picture a sprite is
        /// showing is otherwise unobservable, and "the component was attached" is not evidence that
        /// the body actually fell.
        /// </remarks>
        public int CurrentFrame { get; private set; } = -1;

        private WorldEntityRenderContext _ctx;
        private SpriteBMeshFace _face;
        private string _setName;
        private int _lastFrame;
        private int _extent;

        /// <summary>Start the collapse. Does nothing without a creature set to read frames from.</summary>
        /// <param name="standIn">The victim's living sprite, kept from the previous redraw: shown instead of the
        /// body until the swing that killed it has ended, then destroyed. Null when there is none.</param>
        public UniTask Play(WorldEntityRenderContext ctx,
            SpriteBMeshFace face,
            string setName, int lastFrame, int extent, GameObject standIn = null) {
            _standIn = standIn;
            if (ctx == null || face == null || string.IsNullOrEmpty(setName)
                || lastFrame < Frames - 1) {
                DropStandIn();
                return UniTask.CompletedTask;
            }
            _ctx = ctx;
            _face = face;
            _setName = setName;
            _lastFrame = lastFrame;
            _extent = extent;
            return RunAsync();
        }

        /// <summary>
        /// The pictures a run shows, in order: the four death frames, ending on the last, which stays
        /// (sprite1 is four frames, CACTOR.C:1957-1960; a non-0 slot holds end_frame, :862-864).
        /// </summary>
        public static int[] FramesFor(int lastFrame) {
            var frames = new int[Frames];
            for (int i = 0; i < Frames; i++) {
                frames[i] = lastFrame - (Frames - 1) + i;
            }
            return frames;
        }

        /// <summary>Falls still playing; the arena counts them as busy (the original waits for
        /// <c>combat_actor_play_anim_sprite1</c>, COMBAT.C:226 / CACTOR.C:1895).</summary>
        public static int Playing { get; private set; }

        private async UniTask RunAsync() {
            Playing++;
            try {
                await RunCoreAsync();
            } finally {
                Playing--;
            }
        }

        private GameObject _standIn;

        private void DropStandIn() {
            if (_standIn != null) {
                Destroy(_standIn);
            }
            _standIn = null;
        }

        private void OnDestroy() => DropStandIn();

        private async UniTask RunCoreAsync() {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) {
                DropStandIn();
                return;
            }

            // *** THE BODY FALLS AFTER THE BLOW. *** The original's swing blocks, then apply_damage plays this
            // run (COMBAT.C:487 then 226). Until the swing has ended the victim is drawn alive, by its own sprite
            // from the last redraw; the body stays hidden (TASK-1073, forum p=54). The swing starts on this same
            // redraw, so give it a frame to register before asking.
            renderer.enabled = false;
            await UniTask.Yield();
            await UniTask.WaitWhile(() => HeldBack);
            if (this == null) {
                return;
            }

            // All four, ending on the last picture: the body is hidden until the run starts (above), so
            // the corpse the rebuild left is not on screen to finish it (TASK-1100).
            foreach (int index in FramesFor(_lastFrame)) {
                Texture2D tex = await _ctx.LoadSpriteTextureAsync($"{_setName}#{index}", gameObject);
                if (this == null) {
                    return;
                }
                if (tex == null || _ctx == null) {
                    renderer.enabled = true;   // no run to play: show the body as built
                    DropStandIn();
                    return;
                }

                (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(
                    _face, Mathf.Abs(_extent), tex.width, tex.height);
                _ctx.TrackMesh(mesh);
                filter.sharedMesh = mesh;
                CurrentFrame = index;
                renderer.sharedMaterial = _ctx.GetSpriteMaterial(tex);
                if (!renderer.enabled) {
                    renderer.enabled = true;   // the run's first picture replaces the stand-in, no gap between
                    DropStandIn();
                }
                // Keep the mirroring the builder chose; only the magnitude comes from the frame.
                Vector3 had = transform.localScale;
                transform.localScale = new Vector3(
                    had.x < 0 ? -scale.x : scale.x, scale.y, scale.z);

                await UniTask.Delay(System.TimeSpan.FromSeconds(FrameSeconds),
                    ignoreTimeScale: false);
                if (this == null) {
                    return;
                }
            }
        }
    }
}
