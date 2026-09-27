namespace BakAgain.World.Encounters {
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Identity for one drawn encounter actor that walks a route — what pairs a GameObject in the
    /// scene with the <see cref="RoamingMovement"/> pose and waypoints it steps along.
    /// </summary>
    /// <remarks>
    /// <b>The draw alone cannot carry this.</b> <c>EncounterActorSpriteBuilder.BuildAsync</c> returns
    /// a count, so once it has run there is nothing left connecting a transform to the
    /// <see cref="EncounterActorPlacement.Placed"/> it came from — the same gap the arena corpses had
    /// before <c>ArenaCorpse</c>, and the reason the ported tick had no way to move anything.
    ///
    /// <para><b>The pose is the whole state.</b> <see cref="RoamingMovement.Tick"/> keeps no
    /// waypoint index: an actor steps and then compares its new position against every waypoint of
    /// its route, turning on the first match. So nothing has to be remembered between ticks beyond
    /// x, y and heading, and this component holds exactly that plus the immutable route.</para>
    /// </remarks>
    public sealed class RoamingActor : MonoBehaviour {
        /// <summary>Where the actor is now, in world coordinates.</summary>
        public RoamingMovement.Pose Pose { get; private set; }

        /// <summary>Which route it walks.</summary>
        public RoamingMovement.Pattern Pattern { get; private set; }

        /// <summary>The route's waypoints, in world coordinates.</summary>
        public System.Collections.Generic.IReadOnlyList<long> WaypointX { get; private set; }

        /// <inheritdoc cref="WaypointX"/>
        public System.Collections.Generic.IReadOnlyList<long> WaypointY { get; private set; }

        private DirectionalSprite _sprite;

        private void Awake() => _sprite = GetComponent<DirectionalSprite>();

        public void Bind(EncounterActorPlacement.Placed placed) {
            Pose = new RoamingMovement.Pose(placed.WorldX, placed.WorldY,
                unchecked((ushort)placed.Facing));
            Pattern = placed.Pattern;
            WaypointX = placed.WaypointX;
            WaypointY = placed.WaypointY;
        }

        /// <summary>
        /// Take one tick and move the transform to match.
        /// </summary>
        /// <param name="isRoadAt">
        /// Reports whether a world position stands on road or bridge. <b>Only pattern 4 consults
        /// it</b>, and a null one refuses every road step — which turns such an actor on the spot,
        /// the behaviour every road-follower had before anything supplied this.
        /// </param>
        /// <remarks>
        /// <b>Patterns 1-3 take their step unconditionally</b> — no walkability test of any kind in
        /// the original — and that is left faithful here rather than quietly given the party's
        /// collision, which would strand a patrol wherever its authored route clips scenery.
        /// </remarks>
        public void Step(System.Func<int, int, bool> isRoadAt = null) {
            if (!RoamingMovement.Moves(Pattern)) {
                return;
            }
            Pose = RoamingMovement.Tick(Pose, Pattern, WaypointX, WaypointY,
                isRoadAt == null || Pattern != RoamingMovement.Pattern.RoadFollowing
                    ? null
                    : pose => RoamingMovement.RoadStepFor(pose, isRoadAt));
            transform.localPosition = BakCoordinateConverter.ConvertPosition(
                (int)Pose.X, (int)Pose.Y, 0);

            // Moving the transform is only half of it: the sprite picks its frame from the actor's
            // RECORDED position and facing, which Bind set once. Left alone they stay at the spawn
            // pose for the session, so a walking actor shows a frame computed for somewhere it no
            // longer is, and a road-follower's turn at a bend never appears at all.
            if (_sprite != null) {
                _sprite.SetPose(Pose.X, Pose.Y, unchecked((short)Pose.Heading));
                // One gait frame per step taken. EncounterActorPose has modelled this cycle since
                // the pose work and nothing had ever called it, so roaming actors turned to face
                // the camera correctly and walked with their legs frozen.
                _sprite.AdvanceGait();
            }
        }
    }
}
