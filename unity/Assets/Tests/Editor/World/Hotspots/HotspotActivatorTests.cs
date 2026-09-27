namespace BakAgain.Tests.Editor.World.Hotspots {
    using System.Collections.Generic;
    using BakAgain.World.Hotspots;
    using GameData.Resources.GameState;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// The hotspot activate pass — docs/specs/collision-system.md §3.2-§3.4.1 and acceptance
    /// criteria 7, 8, 21b, 21c. Faithful to canassa <c>hotspotevt_activate_at_player</c>.
    /// </summary>
    public class HotspotActivatorTests {
        private sealed class FakeHost : IHotspotHost {
            public readonly Dictionary<int, int> Globals = new();
            public readonly List<uint> Dialogs = new();
            public int ScoutRolls;
            public bool ScoutSucceeds;
            public readonly HashSet<TileEventTrigger> Ambushes = new();
            public readonly HashSet<TileEventTrigger> Fought = new();
            public uint BlocDialog = 1900047;

            public int ReadGlobal(int key) => Globals.TryGetValue(key, out int v) ? v : 0;
            public void WriteGlobal(int key, int value) => Globals[key] = value;
            public int DoneFlagKey(int index) => 100000 + index;
            public int ScoutTriedFlagKey(int index) => 200000 + index;
            public int ScoutedFlagKey(int index) => 210000 + index;
            public bool EncounterIsDueThisStep(TileEventTrigger t, int index) => true;

            public bool RollEncounterAvoidance(TileEventTrigger t, bool scouted) => false;
            public void PlayDialog(uint dialogId, bool modal) => Dialogs.Add(dialogId);
            public uint BlockDialogId(TileEventTrigger t) => BlocDialog;
            public bool IsAmbush(TileEventTrigger t) => Ambushes.Contains(t);
            public bool EncounterFought(TileEventTrigger t) => Fought.Contains(t);
            public bool StartCombat(TileEventTrigger t, int hotspotIndex = -1) => false;   // activate pass never fights
            public bool EnoughGroundToFight(TileEventTrigger t) => true;   // the activate pass never asks

            public bool RollScouting(TileEventTrigger t) {
                ScoutRolls++;
                return ScoutSucceeds;
            }

            public bool ApplyChanceFlagWrite(TileEventTrigger t) => false;
            public uint SpeakDialogId(TileEventTrigger t) => 0;
            public int TownSceneNumber(TileEventTrigger t) => 0;
            public uint TownDialogId(TileEventTrigger t) => 0;
            public void EnterLocation(int scene) { }
            public void OfferTownEntry(TileEventTrigger trigger, int scene, int hotspotIndex) { }
            public void ApproachBeforeLocation(TileEventTrigger t) { }
            public void MarkActedThisChunk(int index) => WriteGlobal(ScoutTriedFlagKey(index), 1);

            // The activate pass never reaches the Zone arm; present to satisfy the interface.
            public bool ZoneCrossingIsOffered(TileEventTrigger t) => false;
            public void OfferZoneCrossing(TileEventTrigger t, int index) { }
        }

        private static TileEventTrigger Trigger(TileEventType type, int x0 = 0, int y0 = 0, int x1 = 39, int y1 = 39) {
            return new TileEventTrigger {
                Type = type,
                StartX = (byte)x0, StartY = (byte)y0, EndX = (byte)x1, EndY = (byte)y1,
            };
        }

        [Test]
        public void MatchAt_UsesAnInclusiveSubTileBoundingBox() {
            var t = Trigger(TileEventType.Dial, 20, 4, 33, 7);
            var list = new List<TileEventTrigger> { t };

            CollectionAssert.AreEqual(new[] { 0 }, HotspotActivator.MatchAt(list, 20, 4));
            CollectionAssert.AreEqual(new[] { 0 }, HotspotActivator.MatchAt(list, 33, 7));
            CollectionAssert.IsEmpty(HotspotActivator.MatchAt(list, 19, 5));
            CollectionAssert.IsEmpty(HotspotActivator.MatchAt(list, 25, 8));
        }

        [Test]
        public void MatchAt_ReturnsEveryOverlappingHotspotInTableOrder() {
            var list = new List<TileEventTrigger> {
                Trigger(TileEventType.Dial, 0, 0, 10, 10),
                Trigger(TileEventType.Trap, 20, 20, 30, 30),
                Trigger(TileEventType.Comb, 5, 5, 15, 15),
            };
            CollectionAssert.AreEqual(new[] { 0, 2 }, HotspotActivator.MatchAt(list, 7, 7));
        }

        [Test]
        public void ActivateAt_NoMatchingHotspot_KeepsTheStep() {
            var host = new FakeHost();
            var list = new List<TileEventTrigger> { Trigger(TileEventType.Bloc, 20, 20, 30, 30) };
            Assert.IsTrue(new HotspotActivator(host).ActivateAt(list, 0, 0, new List<int>()));
        }

        [Test]
        public void ActivateAt_BlocVolume_PlaysItsDialogAndRevertsTheStep() {
            // Acceptance #21b.
            var host = new FakeHost();
            var list = new List<TileEventTrigger> { Trigger(TileEventType.Bloc) };

            Assert.IsFalse(new HotspotActivator(host).ActivateAt(list, 5, 5, new List<int>()),
                "an interaction reverts the step");
            CollectionAssert.AreEqual(new uint[] { 1900047 }, host.Dialogs);
        }

        [Test]
        public void ActivateAt_BlocVolumeWithNoDialog_StillRevertsTheStep() {
            var host = new FakeHost { BlocDialog = 0 };
            var list = new List<TileEventTrigger> { Trigger(TileEventType.Bloc) };

            Assert.IsFalse(new HotspotActivator(host).ActivateAt(list, 5, 5, new List<int>()));
            CollectionAssert.IsEmpty(host.Dialogs);
        }

        [Test]
        public void ActivateAt_BlocVolumeWhoseForbidsFlagIsSet_IsInert() {
            // Acceptance #21c.
            var host = new FakeHost();
            host.Globals[7445] = 1;
            var bloc = Trigger(TileEventType.Bloc);
            bloc.Forbids = new FlagCondition { Flag = 7445, Set = true };

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { bloc }, 5, 5, new List<int>()));
            CollectionAssert.IsEmpty(host.Dialogs);
        }

        [Test]
        public void ActivateAt_BlocVolumeWhoseRequiresFlagIsClear_IsInert() {
            var host = new FakeHost();
            var bloc = Trigger(TileEventType.Bloc);
            bloc.Requires = new FlagCondition { Flag = 7444, Set = true };

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { bloc }, 5, 5, new List<int>()));

            host.Globals[7444] = 1;
            Assert.IsFalse(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { bloc }, 5, 5, new List<int>()));
        }

        [Test]
        public void ActivateAt_BlocVolume_WritesItsOnFireFlagAndFireOnceMarker() {
            var host = new FakeHost();
            var bloc = Trigger(TileEventType.Bloc);
            bloc.OnFire = new SetFlagEffect { Flag = 8000, Set = true };
            bloc.FireOnce = 1;

            new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { bloc }, 5, 5, new List<int>());

            Assert.AreEqual(1, host.ReadGlobal(8000));
            Assert.AreEqual(1, host.ReadGlobal(host.DoneFlagKey(0)), "FireOnce marks the done flag");

            // Second entry: the done flag now gates it out.
            host.Dialogs.Clear();
            Assert.IsTrue(new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { bloc }, 5, 5, new List<int>()));
            CollectionAssert.IsEmpty(host.Dialogs);
        }

        [Test]
        public void ActivateAt_CombatHotspot_QueuesTheEncounterAndKeepsTheStep() {
            // A plain (non-ambush) encounter does not revert; the world loop runs it where you landed.
            var host = new FakeHost();
            var pending = new List<int>();

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { Trigger(TileEventType.Comb) }, 5, 5, pending));
            CollectionAssert.AreEqual(new[] { 0 }, pending);
        }

        [Test]
        public void ActivateAt_AlreadyFoughtEncounter_QueuesNothing() {
            var host = new FakeHost();
            var comb = Trigger(TileEventType.Comb);
            host.Fought.Add(comb);
            var pending = new List<int>();

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { comb }, 5, 5, pending));
            CollectionAssert.IsEmpty(pending);
        }

        [Test]
        public void ActivateAt_ScoutedAmbush_RevertsTheStepAndQueuesNothing() {
            // Acceptance #7 read the other way round: spotting the ambush stops you short of it.
            var host = new FakeHost { ScoutSucceeds = true };
            var comb = Trigger(TileEventType.Comb);
            host.Ambushes.Add(comb);
            var pending = new List<int>();

            Assert.IsFalse(new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { comb }, 5, 5, pending));
            CollectionAssert.IsEmpty(pending);
            Assert.AreEqual(1, host.ScoutRolls);
        }

        [Test]
        public void ActivateAt_TwoAmbushHotspotsOnOneTile_RollScoutingOnlyOnce() {
            // Acceptance #8: the first kind-1/kind-7 arms the latch; the rest run in auto mode.
            var host = new FakeHost { ScoutSucceeds = false };
            var first = Trigger(TileEventType.Comb);
            var second = Trigger(TileEventType.Comb);
            host.Ambushes.Add(first);
            host.Ambushes.Add(second);
            var pending = new List<int>();

            new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { first, second }, 5, 5, pending);

            Assert.AreEqual(1, host.ScoutRolls, "exactly one skill roll for the whole pass");
            CollectionAssert.AreEqual(new[] { 0 }, pending, "the auto-mode second hotspot queues nothing");
        }

        [Test]
        public void ActivateAt_ScoutTriedFlagAlreadySet_DoesNotRollAgain() {
            var host = new FakeHost { ScoutSucceeds = true };
            var comb = Trigger(TileEventType.Comb);
            host.Ambushes.Add(comb);
            host.Globals[host.ScoutTriedFlagKey(0)] = 1;
            var pending = new List<int>();

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(new List<TileEventTrigger> { comb }, 5, 5, pending));
            Assert.AreEqual(0, host.ScoutRolls);
            CollectionAssert.AreEqual(new[] { 0 }, pending);
        }

        [Test]
        public void ActivateAt_DialAndSounHotspots_QueueThemselvesAndNeverRevert() {
            // The `default:` arm of the activate switch: noInteraction = pending = 1.
            var host = new FakeHost();
            var pending = new List<int>();

            Assert.IsTrue(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { Trigger(TileEventType.Dial), Trigger(TileEventType.Soun) },
                5, 5, pending));
            CollectionAssert.AreEqual(new[] { 0, 1 }, pending);
        }

        [Test]
        public void ActivateAt_ZoneBorder_RevertsTheStepSoTheTransitionRunsWhereYouStood() {
            var host = new FakeHost();
            Assert.IsFalse(new HotspotActivator(host).ActivateAt(
                new List<TileEventTrigger> { Trigger(TileEventType.Zone) }, 5, 5, new List<int>()));
        }

        [Test]
        public void ActivateAt_TownZoneAndBackground_AreQueuedForTheDispatchPass() {
            // `HotspotDispatcher` reaches EnterTown / OfferZoneCrossing ONLY through this list, so an
            // arm that reverts the step without queueing makes those handlers dead code — which is
            // what it did until 2026-09-09, and it left every GDS location unreachable from the
            // world. Measured at LaMut's gate before the fix: matched=1, keep=False, pending=0.
            foreach (TileEventType kind in new[] {
                         TileEventType.Town, TileEventType.Bkgr, TileEventType.Zone }) {
                var pending = new List<int>();
                Assert.IsFalse(new HotspotActivator(new FakeHost()).ActivateAt(
                    new List<TileEventTrigger> { Trigger(kind) }, 5, 5, pending), kind.ToString());
                CollectionAssert.AreEqual(new[] { 0 }, pending, kind.ToString());
            }
        }

        [Test]
        public void ActivateAt_OneInteractionAmongSeveral_StillRevertsTheStep() {
            // allNoInteraction is an AND across every matching hotspot.
            var host = new FakeHost();
            var list = new List<TileEventTrigger> { Trigger(TileEventType.Dial), Trigger(TileEventType.Bloc) };
            Assert.IsFalse(new HotspotActivator(host).ActivateAt(list, 5, 5, new List<int>()));
        }

        // hotspotevt_dispatch_at_point filters on wKind INSIDE the table walk (HOTSPOT.C:228), so the
        // done/tried flags stay keyed by the tile table's index. The tunnel path used to pass a
        // Zone-only list: the Diviner's Halls stairs (Zone 38, table index 4) read index 0's done
        // flag -- a Comb already fought -- and never offered the way out.
        [Test]
        public void AKindFilteredPassKeepsTheTableIndex() {
            var host = new FakeHost();
            var list = new List<TileEventTrigger> {
                Trigger(TileEventType.Comb),
                Trigger(TileEventType.Zone, 37, 37, 39, 39),
            };
            host.WriteGlobal(host.DoneFlagKey(0), 1);   // the Comb was fought
            var pending = new List<int>();

            new HotspotActivator(host).ActivateAt(list, 38, 38, pending, TileEventType.Zone);

            CollectionAssert.AreEqual(new[] { 1 }, pending, "the Zone, by its own table index");

            host.WriteGlobal(host.DoneFlagKey(1), 1);
            new HotspotActivator(host).ActivateAt(list, 38, 38, pending, TileEventType.Zone);
            CollectionAssert.IsEmpty(pending, "control: its own done flag still gates it");
        }
    }
}
