namespace BakAgain.World.Interaction {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using GameData.Resources.World;

    /// <summary>
    /// Clicking a chasm to swing across it on a rope — <c>handle_Pit</c> (ovr195 @0x79c63).
    /// </summary>
    /// <remarks>
    /// <b>This is the CROSSING, and it is not the falling.</b> Two code paths share one entity
    /// type: a <c>m_pit</c> polygon is walkable and dropping into it is delivered by the movement
    /// loop (<see cref="PitDescent"/>, already wired), while this is the world OBJECT you click on.
    /// Conflating them gives a pit that either cannot be crossed or cannot be fallen into.
    ///
    /// <para>Every rule lives in <see cref="PitRopeCrossing"/>; this handler is the plumbing that
    /// feeds it a rope count, a rotation and two coordinates.</para>
    /// </remarks>
    public sealed class PitInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;

        // Lazy for the reason HotspotService's accessors are: the world layer must not take a hard
        // dependency on an object built later, and PartyMovement is created during the world build
        // while the handler list is assembled with the HUD.
        private readonly Func<PartyMovement> _movement;

        public PitInteractionHandler(GameSession session, IDialogManager dialog,
            Func<PartyMovement> movement) {
            _session = session;
            _dialog = dialog;
            _movement = movement;
        }

        public string Behavior => "pit";

        /// <summary>
        /// Offer the swing, and take the party across if they accept.
        /// </summary>
        /// <remarks>
        /// <b>Two of the three refusals are silent, and the third is not.</b> The rope count is
        /// tested first and its failure SPEAKS — <see cref="PitRopeCrossing.NoRopeDialog"/>, "if we
        /// only had a rope". The GEOMETRY failures are the quiet ones: a pit at an unusable angle,
        /// or a party outside the lateral band, both fall to a flag test that returns without a
        /// word. (An earlier version of this remark said every refusal was silent, which the code
        /// beneath it already contradicted.)
        ///
        /// <para><b>The swing is animated</b> — <see cref="PartyMovement.SwingAcrossAsync"/> walks
        /// the party to the near lip and crosses in <see cref="PitRopeCrossing.StepUnits"/>
        /// increments, dipping through <see cref="PitRopeCrossing.SagHeightAt"/> and sounding
        /// <see cref="PitRopeCrossing.SwingSoundId"/> once at the exact centre. All of that lives on
        /// the movement side because the camera does; the handler's job is the geometry.</para>
        /// </remarks>
        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            if (entity == null) {
                return;
            }

            // The button is tested before the rope count is even read (@0x79c9c), so examining a pit
            // works with no rope and from any position — unlike crossing it.
            if (!isPrimary) {
                await _dialog.ShowById(PitRopeCrossing.ExamineDialog);

                return;
            }

            // *** THE ROPE IS CHECKED FIRST, AND ITS ABSENCE TALKS. *** @0x79cb1, ahead of every
            // geometry test — so a party with no rope hears "if we only had a rope" wherever they
            // are standing and whichever way the pit lies.
            if (RopesCarried() <= 0) {
                await _dialog.ShowById(PitRopeCrossing.NoRopeDialog);

                return;
            }

            PitRopeCrossing.PitAxis axis = PitRopeCrossing.AxisOf(entity.RotationZ);
            (int pitX, int pitY) = BakCoordinateConverter.ToBakXY(entity.transform.position);

            // Which coordinate lines the party up and which one the swing crosses depends on the
            // way the pit lies — see PitRopeCrossing.IsLinedUp, which tests the axis the pit RUNS
            // ALONG rather than the one the swing crosses.
            bool alongX = axis == PitRopeCrossing.PitAxis.AlongX;
            int partyAlong = alongX ? _session.PositionX : _session.PositionY;
            int pitAlong = alongX ? pitX : pitY;

            // The GEOMETRY failures are the silent ones — an unusable angle or a party outside the
            // band both fall to a flag test that returns without a word (@0x79e98).
            if (!PitRopeCrossing.CanOffer(RopesCarried(), entity.RotationZ, partyAlong, pitAlong)) {
                return;
            }

            if (!await _dialog.ShowConfirmById(PitRopeCrossing.OfferDialog)) {
                return;
            }

            int partyAcross = alongX ? _session.PositionY : _session.PositionX;
            int landing = PitRopeCrossing.LandingPosition(partyAcross, alongX ? pitY : pitX);

            PartyMovement movement = _movement?.Invoke();
            if (movement == null) {
                return;
            }

            // The near lip: the pit's crossing coordinate on the side the party is standing.
            int start = PitRopeCrossing.LandingPosition(landing, alongX ? pitY : pitX);
            await movement.SwingAcrossAsync(crossingAxisIsY: alongX, pitX, pitY, start, landing);

            // *** THE LANDING SPENDS A ROPE, AND ONLY THEN IS THE COUNT RE-READ. *** @0x7a11e calls
            // useItem(Rope) — party_consumeOneOfKindFromAnyMember — and the "that's it for our good
            // rope" line fires only if nothing is left. Crossing with two ropes spends one and says
            // nothing; omitting the spend gives unlimited crossings from one rope.
            SpendARope();
            if (RopesCarried() <= 0) {
                await _dialog.ShowById(PitRopeCrossing.OutOfRopeDialog);
            }
        }

        /// <summary>Take one rope from whoever is carrying one.</summary>
        /// <remarks>
        /// <c>party_consumeOneOfKindFromAnyMember</c>: the first member holding one loses it, so the
        /// rope is a party resource rather than a particular character's.
        /// </remarks>
        private void SpendARope() {
            if (_session?.ActivePartyIndices == null) {
                return;
            }

            foreach (byte member in _session.ActivePartyIndices) {
                RuntimeContainer pack = _session.GetActorInventory(member);
                if (pack != null && InventoryConsume.TryConsumeOne(
                        pack, PitRopeCrossing.RopeObjectId, _session.ObjectInfo.GetById)) {
                    return;
                }
            }
        }

        /// <summary>
        /// Ropes across the WHOLE party, not one member's pack.
        /// </summary>
        /// <remarks>
        /// <see cref="PitRopeCrossing.CanOffer"/> says so explicitly: the original counts the
        /// party's ropes, so a rope in anyone's pack lets everyone swing.
        /// </remarks>
        private int RopesCarried() {
            if (_session?.ActivePartyIndices == null) {
                return 0;
            }

            var carried = 0;
            foreach (byte member in _session.ActivePartyIndices) {
                RuntimeContainer pack = _session.GetActorInventory(member);
                if (pack != null) {
                    carried += InventoryQuery.CountByKind(pack, PitRopeCrossing.RopeObjectId);
                }
            }
            return carried;
        }
    }
}
