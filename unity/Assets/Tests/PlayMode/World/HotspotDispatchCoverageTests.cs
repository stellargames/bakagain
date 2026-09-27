namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.World.Hotspots;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// **Dispatch coverage, per trigger TYPE.** The measurable half of TASK-297's numerator.
    ///
    /// <para>The point is to have a number at all. "All nine placed trigger types have a handler"
    /// reads as 100% and hides that nothing counts the 1,394 individual placements; conversely
    /// walking every placement in the emulator was costed at ~46 hours and is not worth building.
    /// The dispatcher already reports <c>UnhandledKinds</c> — "no runtime exists for this kind" —
    /// so asking it directly, once per type, measures the routing layer exhaustively in
    /// milliseconds.</para>
    ///
    /// <para><b>What this does NOT measure.</b> That a handler was reached, not that it did the
    /// right thing. It is one rung above "a handler exists" and well below "the encounter plays
    /// correctly". Weighted against `docs/CONTENT-CENSUS.md` it answers "how many placements route
    /// somewhere", and nothing more — a coverage number that overclaims is worse than none.</para>
    /// </summary>
    public class HotspotDispatchCoverageTests {
        /// <summary>
        /// A MAXIMALLY COOPERATIVE host: every gate says yes. That is deliberate, and the first run
        /// of this test is why.
        ///
        /// <para><c>UnhandledKinds</c> is NOT purely "no runtime exists for this kind", despite
        /// being the natural reading and despite <c>Refused</c>'s own remark drawing exactly that
        /// line. <c>Unhandled()</c> is called from the <c>default:</c> arm AND from inside the Zone,
        /// Bkgr and Town arms when their host call declines. With a stub that refused the zone
        /// crossing, Zone reported unhandled although it has a dispatch arm — 86 placements
        /// mis-scored as unrouted.</para>
        ///
        /// <para>So "routes" here means <b>reaches a handler when nothing refuses it</b>, which is
        /// the question this instrument is for. A host that declines measures the rules, not the
        /// routing.</para>
        /// </summary>
        private sealed class StubHost : IHotspotHost {
            public int ReadGlobal(int key) => 0;
            public void WriteGlobal(int key, int value) { }
            public int DoneFlagKey(int hotspotIndex) => 0;
            public int ScoutTriedFlagKey(int hotspotIndex) => 0;
            public int ScoutedFlagKey(int hotspotIndex) => 0;
            public bool EncounterIsDueThisStep(TileEventTrigger trigger, int index) => true;

            public bool RollEncounterAvoidance(TileEventTrigger trigger, bool scouted) => false; // avoidance NOT rolled, so the fight proceeds
            public void PlayDialog(uint dialogId, bool modal) { }
            public uint BlockDialogId(TileEventTrigger trigger) => 0u;
            public bool IsAmbush(TileEventTrigger trigger) => false;
            public bool EncounterFought(TileEventTrigger trigger) => false;
            public bool StartCombat(TileEventTrigger trigger, int hotspotIndex = -1) => true;
            public bool EnoughGroundToFight(TileEventTrigger trigger) => true;
            public bool RollScouting(TileEventTrigger trigger) => false;
            public bool ApplyChanceFlagWrite(TileEventTrigger trigger) => true;
            public uint SpeakDialogId(TileEventTrigger trigger) => 1u;
            public int TownSceneNumber(TileEventTrigger trigger) => 1;
            public uint TownDialogId(TileEventTrigger trigger) => 1u;
            public void ApproachBeforeLocation(TileEventTrigger trigger) { }
            public void EnterLocation(int gdsSceneNumber) { }
            public void OfferTownEntry(TileEventTrigger trigger, int gdsSceneNumber, int hotspotIndex) { }
            public bool ZoneCrossingIsOffered(TileEventTrigger trigger) => true;
            public void OfferZoneCrossing(TileEventTrigger trigger, int hotspotIndex) { }
            public void MarkActedThisChunk(int hotspotIndex) { }
        }

        /// <summary>Types with at least one placement in the shipped data, and how many — from
        /// <c>make census</c> on 2026-09-03. Kept here so the assertion is instance-weighted rather
        /// than a count of enum members.</summary>
        private static readonly (TileEventType Type, int Placements)[] Shipped = {
            (TileEventType.Comb, 506),
            (TileEventType.Trap, 413),
            (TileEventType.Dial, 194),
            (TileEventType.Zone, 86),
            (TileEventType.Bloc, 81),
            (TileEventType.Town, 41),
            (TileEventType.Disa, 36),
            (TileEventType.Enab, 28),
            (TileEventType.Bkgr, 9),
        };

        private static bool Routes(TileEventType type) {
            var dispatcher = new HotspotDispatcher(new StubHost());
            var trigger = new TileEventTrigger { Type = type };
            HotspotDispatchResult result = dispatcher.Dispatch(new[] { trigger }, new[] { 0 });

            return !result.UnhandledKinds.Contains(type);
        }

        [Test]
        public void EveryShippedTriggerTypeExceptBlocRoutesSomewhere() {
            var strays = Shipped.Where(s => s.Type != TileEventType.Bloc && !Routes(s.Type))
                .Select(s => s.Type.ToString())
                .ToArray();

            Assert.IsEmpty(strays,
                "these types have shipped placements but no dispatch arm: " + string.Join(", ", strays));
        }

        [Test]
        public void BlocIsNotDispatched_BecauseItIsAnACTIVATION_Concern() {
            // Bloc falls to the dispatcher's default arm on purpose: HotspotActivator handles it at
            // activation, a different stage. Pinned so a future reader does not "fix" the gap by
            // adding a dispatch arm and end up with it handled twice.
            Assert.IsFalse(Routes(TileEventType.Bloc));
        }

        [Test]
        public void TypesWithNoShippedPlacementsAreNotDispatched_AndThatCostsNothing() {
            // Comm, Heal and Soun are in the enum but have ZERO placements in the shipped data, so
            // their absence from the dispatcher is worth nothing to a player. Recorded so the raw
            // "4 of 12 enum members unhandled" never reads as a gap.
            foreach (TileEventType type in new[] { TileEventType.Comm, TileEventType.Heal, TileEventType.Soun }) {
                Assert.IsFalse(Shipped.Any(s => s.Type == type), $"{type} now has placements — re-measure");
                Assert.IsFalse(Routes(type));
            }
        }

        [Test]
        public void RoutedPlacementsAreTheOverwhelmingMajorityOfShippedTriggers() {
            int routed = Shipped.Where(s => Routes(s.Type)).Sum(s => s.Placements);
            int total = Shipped.Sum(s => s.Placements);

            // 1,313 of 1,394 route through the dispatcher; the other 81 are Bloc, handled at
            // activation. This pins the measurement so a regression that silently drops a whole
            // type shows up as a number, not as a bug report months later.
            Assert.AreEqual(1394, total, "census total changed — re-run make census and update Shipped");
            Assert.AreEqual(1313, routed);
        }
    }
}
