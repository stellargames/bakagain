namespace BakAgain.World.Encounters {
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Re-picks a creature sprite's directional frame as the camera moves around it.
    /// </summary>
    /// <remarks>
    /// <b><see cref="BillboardSprite"/> turns the QUAD; this turns the PICTURE ON it.</b> The two
    /// are easy to conflate, and having only the first is what made a mordel show its face while
    /// the party walked behind it: the billboard kept the quad square to the camera, but the
    /// texture stayed whichever of the eight facings was resolved once at build time.
    ///
    /// <para><b>Only the column changes.</b> The creature's mesh set is fixed — walk or downed, and
    /// which one is a property of the actor, not the view (see
    /// <c>EncounterActorSpriteBuilder.FaceFor</c>). This re-reads
    /// <see cref="EncounterActorPose.SpriteColumn"/> for the current octant and swaps to that
    /// column's face, which is a different bitmap, a differently-sized quad and possibly a mirror.
    /// </para>
    ///
    /// <para><b>Frames are built lazily and kept.</b> A creature has at most five walk columns and
    /// the party rarely sees them all, so building every one at spawn would cost five texture loads
    /// per actor for frames most of them never show. Each column is built the first time it is
    /// needed and cached on the component; the underlying texture load is cached by key in the
    /// render context, so two mordels facing the same way share one.</para>
    ///
    /// <para><b>An in-flight build does not block the current frame.</b> Until a column's art is
    /// ready the actor keeps the one it has, which reads as a slightly late turn rather than a
    /// flicker to nothing.</para>
    /// </remarks>
    public sealed class DirectionalSprite : MonoBehaviour {
        private readonly Dictionary<int, Built> _built = new Dictionary<int, Built>();

        private WorldEntityRenderContext _ctx;
        private Camera _camera;
        private MeshRecord _mesh;
        private string _setName;
        private int _extent;
        private int _kind;
        private long _worldX;
        private long _worldY;
        private short _facing;
        private int _currentColumn = -1;

        /// <summary>Whether the column now applied is the mirrored one.</summary>
        /// <remarks>
        /// <b>The column alone does not identify a facing.</b> WalkingColumns is
        /// { 0, 3, 6, 9, 12, 9, 6, 3 } and <c>SpriteColumn</c> mirrors at octant >= 5, so octants
        /// 7/1, 6/2 and 5/3 are the SAME column drawn with opposite handedness — five sheets
        /// covering eight facings. Tracking only the number meant turning from right-facing to
        /// left-facing left the number unchanged, the "already showing it" test passed, and the
        /// sprite never turned (TASK-595).
        /// </remarks>
        private bool _currentMirrored;

        private bool _building;

        // The gait, for WalkingKind only. Stepped by RoamingActor.Step so an actor animates exactly
        // when it moves — a stationary pattern never steps, so it never walks on the spot, and no
        // clock has to be invented for it.
        private int _frame;
        private bool _advancing = true;

        private struct Built {
            public Mesh Mesh;
            public Material Material;
            public Vector3 Scale;
        }

        /// <param name="mesh">The creature's chosen mesh set — walk or downed, already resolved.</param>
        /// <param name="column">The column the builder already drew, so it is not rebuilt.</param>
        public void Bind(WorldEntityRenderContext ctx, Camera camera, MeshRecord mesh,
            string setName, int extent, int kind, long worldX, long worldY, short facing,
            int column, Mesh builtMesh, Material builtMaterial, Vector3 builtScale) {
            _ctx = ctx;
            _camera = camera;
            _mesh = mesh;
            _setName = setName;
            _extent = extent;
            _kind = kind;
            _worldX = worldX;
            _worldY = worldY;
            _facing = facing;
            _currentColumn = column;
            // The builder already applied the mirror, so the scale's sign IS the flag — reading it
            // back keeps the two from disagreeing without widening this signature.
            _currentMirrored = builtScale.x < 0f;
            _built[Key(column, _currentMirrored)] =
                new Built { Mesh = builtMesh, Material = builtMaterial, Scale = builtScale };
        }

        private void LateUpdate() {
            if (_ctx == null || _mesh == null) {
                return;
            }

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null) {
                return;
            }

            int column = ColumnFacing(cam, out bool mirrored);
            if (column < 0 || (column == _currentColumn && mirrored == _currentMirrored)) {
                return;
            }

            if (_built.TryGetValue(Key(column, mirrored), out Built ready)) {
                Apply(ready);
                _currentColumn = column;
                _currentMirrored = mirrored;
                return;
            }
            if (!_building) {
                BuildColumnAsync(column, mirrored).Forget();
            }
        }

        /// <summary>
        /// Tells the sprite where the actor is and which way it is looking now.
        /// </summary>
        /// <remarks>
        /// <b>Without this a walking actor picks its frame from where it SPAWNED.</b> These three
        /// were set once by <see cref="Bind"/> and never again, so a roaming actor's octant was
        /// computed against its spawn point for the rest of the session — an error that grows with
        /// every step it takes, and that hides a road-follower's turn at a bend completely.
        ///
        /// <para>A note on <c>TASK-103</c>'s claim that computing from the recorded position
        /// rather than the transform "is what keeps it right for a roaming actor": the coordinate
        /// space is the real reason (the transform is a LOCAL offset under the zone root, so
        /// scaling it back up is only correct while that root sits at the origin). Keeping the
        /// fields is right; leaving them frozen was not.</para>
        /// </remarks>
        /// <summary>The pose the frame choice is computed against — what <see cref="SetPose"/> sets.</summary>
        public (long X, long Y, short Facing) RecordedPose => (_worldX, _worldY, _facing);

        public void SetPose(long worldX, long worldY, short facing) {
            _worldX = worldX;
            _worldY = worldY;
            _facing = facing;
        }

        /// <summary>Which column the actor presents to the camera right now.</summary>
        /// <remarks>
        /// The inverse of <c>BakCoordinateConverter.ConvertPosition</c>, which maps
        /// <c>(x, y)</c> to <c>(x, _, y)</c> over the shared world scale — so the camera's x and z
        /// scale back up to BaK's x and y. The transform is a local offset under the zone root, so
        /// reading it back would only be correct while that root sits at the origin; the recorded
        /// world position is unambiguous. <see cref="SetPose"/> keeps it current.
        /// </remarks>
        private int ColumnFacing(Camera cam, out bool mirrored) {
            mirrored = false;
            Vector3 p = cam.transform.position;
            long dx = (long)(p.x * BakCoordinateConverter.WorldScale) - _worldX;
            long dy = (long)(p.z * BakCoordinateConverter.WorldScale) - _worldY;
            ushort angle = GameData.Resources.Combat.ArenaFacing.HeadingTo(dx, dy);
            int octant = EncounterActorPose.Octant(angle, _facing);
            int column = EncounterActorPose.SpriteColumn(_kind, octant, out mirrored);

            // *** THE COLUMN IS THE BASE OF THE FACING'S THREE-FRAME GROUP, NOT A SINGLE SPRITE. ***
            // WalkingColumns is { 0, 3, 6, 9, 12, 9, 6, 3 } — stride 3, which is exactly WalkFrames.
            // So the gait selects within the group and the facing selects the group. Until this was
            // added the frame was always the group's first, and every roaming actor slid along with
            // its legs frozen mid-stride.
            //
            // DownedKind is deliberately excluded: its table has stride 4 and one pose per quadrant,
            // so adding a frame there would index a neighbouring facing's art.
            return _kind == EncounterActorPose.WalkingKind ? column + _frame : column;
        }

        /// <summary>
        /// Take one step of the walk cycle.
        /// </summary>
        /// <remarks>
        /// <b>Called from the actor's own step, not from a timer.</b> Roaming actors advance only
        /// when the party moves (<c>WorldRuntime</c> accumulates party distance and issues whole
        /// ticks), so tying the gait to the step reproduces the original's coupling for free: an
        /// actor that is not moving is not animating, and the walk speed follows the world's own
        /// pace rather than a frame rate.
        ///
        /// <para>The cycle is <see cref="EncounterActorPose.Advance"/>'s — 0, 1, 2, 1, 0 …, a
        /// ping-pong rather than a loop, so the leg swings back instead of snapping. Its rule is
        /// NOT the combat creature's; see that method's remarks.</para>
        ///
        /// <para><b>It advances for every kind and is CONSUMED only by the walking one.</b> Keeping
        /// the kind test in one place — <see cref="ColumnFacing"/>, where the frame is actually
        /// used — means the two cannot disagree about which kinds have a gait. The cost is a
        /// counter ticking unread on a downed actor, which is nothing.</para>
        /// </remarks>
        public void AdvanceGait() => EncounterActorPose.Advance(ref _frame, ref _advancing);

        /// <summary>
        /// Starts this sprite mid-stride instead of at frame 0.
        /// </summary>
        /// <remarks>
        /// <b>For a sprite that replaces one that was already walking.</b> A combat redraw destroys
        /// and rebuilds the arena, so without this every step ends by snapping the legs back to the
        /// start of the cycle. The frame it resumes from is kept on the combatant, which outlives
        /// the GameObject — see <c>Combatant.GaitFrame</c>.
        ///
        /// <para>No invalidation is needed: the frame is part of what <see cref="ColumnFacing"/>
        /// computes, so the next <c>LateUpdate</c> sees a different column and rebuilds it.</para>
        /// </remarks>
        public void SeedGait(int frame, bool advancing) {
            _frame = frame;
            _advancing = advancing;
        }

        /// <summary>The gait frame the walking columns are offset by — 0..2.</summary>
        /// <remarks>
        /// Exposed for the same reason <see cref="RecordedPose"/> is: the frame the sprite will
        /// draw is otherwise unobservable, and "the actor moved" is not evidence that its legs did.
        /// </remarks>
        public int GaitFrame => _frame;

        /// <summary>Which way the ping-pong is currently running — the other half of the frame.</summary>
        /// <remarks>
        /// Frame 1 appears twice per cycle, once going up and once coming back, so the frame alone
        /// does not say where the gait is. Both are needed to resume it — see <see cref="SeedGait"/>.
        /// </remarks>
        public bool GaitAdvancing => _advancing;

        /// <summary>
        /// The octant this sprite is currently drawn from, 0..7, or -1 with no camera.
        /// </summary>
        /// <remarks>
        /// Exposed so an animation that indexes its frames BY FACING can reuse the one computation
        /// that already gets this right, rather than deriving a second answer that could disagree.
        /// The creature-bitmap animations do exactly that: mode 3 forces the octant even and then
        /// mirrors it, and their frame base is a function of the result.
        /// </remarks>
        public int CurrentOctant {
            get {
                Camera cam = _camera != null ? _camera : Camera.main;
                if (cam == null) {
                    return -1;
                }
                Vector3 p = cam.transform.position;
                long dx = (long)(p.x * BakCoordinateConverter.WorldScale) - _worldX;
                long dy = (long)(p.z * BakCoordinateConverter.WorldScale) - _worldY;
                return EncounterActorPose.Octant(
                    GameData.Resources.Combat.ArenaFacing.HeadingTo(dx, dy), _facing);
            }
        }

        private int _tintRung;

        /// <summary>Which fade rung this sprite is currently drawn through — 0 is untinted.</summary>
        public int TintRung => _tintRung;

        /// <summary>
        /// Redraw this sprite through fade rung <paramref name="rung"/> of the zone's RMP.
        /// </summary>
        /// <remarks>
        /// <b>This is what a hit reaction looks like.</b> The original re-tints a struck actor for
        /// two redraws by selecting a remap block with <c>spriteHitDir</c>, so the creature is drawn
        /// one step further into its own zone's fog rather than being moved — see <c>HitReaction</c>
        /// for why a displacement would be inventing the effect.
        ///
        /// <para>The built-column cache is keyed by rung as well as column, so switching back costs
        /// nothing after the first flash and the two variants cannot be confused for each other.</para>
        /// </remarks>
        public void SetTintRung(int rung) {
            if (rung == _tintRung) {
                return;
            }
            _tintRung = rung;
            // Force LateUpdate to resolve again: the column number has not changed, only which
            // texture it should be drawn from.
            _currentColumn = -1;
        }

        /// <summary>Cache key for a built column — the rung is part of the identity, not a mode.</summary>
        /// <remarks>
        /// <b>And so is the mirror.</b> The handedness lives in the entry's <c>Scale</c>, so two
        /// facings that share a column — octants 7/1, 6/2, 5/3 — differ only in the sign of x. With
        /// the flag out of the key they collided, and whichever was built first was handed to both:
        /// walk past an actor and it faced the same way from either side (TASK-595).
        /// </remarks>
        private int Key(int column, bool mirrored) =>
            (mirrored ? 1 << 16 : 0) | (_tintRung << 8) | (column & 0xFF);

        private async UniTaskVoid BuildColumnAsync(int column, bool mirrored) {
            _building = true;
            try {
                IReadOnlyList<MeshFaceRecord> faces = _mesh.MeshFaces;
                if (faces == null || column < 0 || column >= faces.Count
                    || !(faces[column] is SpriteBMeshFace face)) {
                    return;
                }

                Texture2D tex = await _ctx.LoadSpriteTextureAsync(
                    $"{_setName}#{face.BitmapIndex}", gameObject, _tintRung);
                // The component can be torn down mid-load — leaving a zone during a turn is the
                // ordinary way this happens, not an edge case.
                if (tex == null || this == null || _ctx == null) {
                    return;
                }

                (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(
                    face, Mathf.Abs(_extent), tex.width, tex.height);
                _ctx.TrackMesh(mesh);

                var built = new Built {
                    Mesh = mesh,
                    Material = _ctx.GetSpriteMaterial(tex),
                    Scale = mirrored ? new Vector3(-scale.x, scale.y, scale.z) : scale,
                };
                _built[Key(column, mirrored)] = built;

                // Re-check before applying: the camera may have moved on while this loaded, and
                // painting a stale column would be a visible step backwards.
                Camera cam = _camera != null ? _camera : Camera.main;
                if (cam != null && ColumnFacing(cam, out bool stillMirrored) == column
                    && stillMirrored == mirrored) {
                    Apply(built);
                    _currentColumn = column;
                    _currentMirrored = mirrored;
                }
            } finally {
                _building = false;
            }
        }

        private void Apply(Built built) {
            var filter = GetComponent<MeshFilter>();
            if (filter != null) {
                filter.sharedMesh = built.Mesh;
            }
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null) {
                renderer.sharedMaterial = built.Material;
            }
            transform.localScale = built.Scale;
        }
    }
}
