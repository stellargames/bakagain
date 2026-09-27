namespace BakAgain.Tests.Editor.World.Collision {
    using System.Collections.Generic;
    using BakAgain.World.Collision;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// The proximity scan itself — candidate selection (FILTER.DAT, docs/specs/collision-system.md
    /// §2.5), the per-record hit test (§2.3), the ground height it yields (§2.4) and the
    /// walkable-kind set (§2.2/§2.6).
    /// </summary>
    public class ProximityWorldTests {
        private static GidRegion Square(int half, short baseElevation = 0) {
            var region = new GidRegion { BaseElevation = baseElevation };
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            return region;
        }

        private static ZoneTableEntry Entry(byte kind, GidRegion region, short radius,
            byte vertexScale = 0, byte drawPriority = 0, byte gidFlags = 0) {
            var entry = new ZoneTableEntry {
                Dat = new TableDatInfo {
                    EntityType = (WorldEntityType)kind,
                    VertexScale = vertexScale,
                    DrawPriority = drawPriority,
                },
                Gid = new TableGidInfo { XRadius = radius, YRadius = radius, Flags = gidFlags },
            };
            if (region != null) {
                entry.Gid.Regions.Add(region);
            }
            return entry;
        }

        // FILTER.DAT-shaped table: terrain kinds always drawn, everything else at `others`.
        private static FilterData Filter(int others = 18750, int level0Others = 18750) {
            var filter = new FilterData("FILTER.DAT");
            for (int level = 0; level < FilterData.DetailLevelCount; level++) {
                var block = new DetailLevelFilter { Level = level };
                for (int k = 0; k < FilterData.EntityTypeCount; k++) {
                    block.DrawDistances[k] = k <= 3 ? 1 : (level == 0 ? level0Others : others);
                }
                block.DrawDistances[7] = -1;
                filter.DetailLevels.Add(block);
            }
            return filter;
        }

        private static ProximityWorld World(FilterData filter, params ProximityRecord[] records) {
            var world = new ProximityWorld(new List<ProximityRecord>(records), filter);
            world.BuildCandidates(0, 0, detailLevel: 3);
            return world;
        }

        [Test]
        public void TryScan_PointInsideAPolygon_ReturnsThatRecordsKindAndGroundHeight() {
            var ground = Entry(kind: 0, Square(1000, baseElevation: 40), radius: 1000);
            var world = World(Filter(), new ProximityRecord(ground, x: 5000, y: 5000, zBase: 7, angle: 0));

            Assert.IsTrue(world.TryScan(5100, 4900, out int kind, out int groundZ));
            Assert.AreEqual(0, kind);
            Assert.AreEqual(47, groundZ, "BaseElevation << VertexScale + the record's own z");
        }

        [Test]
        public void TryScan_PointOutsideEveryPolygon_ReturnsFalse() {
            // Acceptance #21: falling off the edge of the authored polygon soup is a miss, and a
            // miss is what blocks the move.
            var ground = Entry(kind: 0, Square(1000), radius: 1000);
            var world = World(Filter(), new ProximityRecord(ground, x: 5000, y: 5000, zBase: 0, angle: 0));

            Assert.IsFalse(world.TryScan(9000, 9000, out _, out _));
        }

        [Test]
        public void TryScan_OverlappingRecords_TheNearestOneWins() {
            // proximity_scan_list walks the visible list back to front, and every proxscan_full call
            // site sorts that list with vislist_sort first (far -> near). Back-to-front over a
            // far-to-near list therefore means "nearest first", regardless of table order.
            var near = Entry(kind: 0, Square(20000), radius: 20000);
            var far = Entry(kind: 1, Square(20000), radius: 20000);
            var world = World(Filter(),
                new ProximityRecord(near, 0, 0, 0, 0),      // party is standing on this one
                new ProximityRecord(far, 5000, 0, 0, 0));   // added later, but further away

            Assert.IsTrue(world.TryScan(100, 100, out int kind, out _));
            Assert.AreEqual(0, kind);
        }

        [Test]
        public void TryScan_PriorityModelsAreOnlyConsideredAfterEverythingElse() {
            // vislist_sort partitions priority != 0 to the FRONT of the list, so the back-to-front
            // scan reaches them last — a proud model never steals a hit from the ground under it,
            // even when it is nearer.
            var proud = Entry(kind: 3, Square(20000), radius: 20000, drawPriority: 7);
            var ground = Entry(kind: 0, Square(20000), radius: 20000);
            var world = World(Filter(),
                new ProximityRecord(ground, 5000, 0, 0, 0),
                new ProximityRecord(proud, 0, 0, 0, 0));       // nearest, and added last

            Assert.IsTrue(world.TryScan(100, 100, out int kind, out _));
            Assert.AreEqual(0, kind);
        }

        [Test]
        public void TryScan_RotatedRecord_TestsThePointInModelSpace() {
            // A 2000x400 slab rotated a quarter turn is only hit along the rotated long axis.
            var slab = new ZoneTableEntry {
                Dat = new TableDatInfo { EntityType = 0 },
                Gid = new TableGidInfo { XRadius = 1000, YRadius = 200 },
            };
            slab.Gid.Regions.Add(Rect(1000, 200));
            var world = World(Filter(), new ProximityRecord(slab, 0, 0, 0, angle: 0x4000));

            Assert.IsTrue(world.TryScan(0, 900, out _, out _), "long axis now runs along world Y");
            Assert.IsFalse(world.TryScan(900, 0, out _, out _));
        }

        private static GidRegion Rect(int halfX, int halfY) {
            var region = new GidRegion();
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-halfX, AnchorY = (short)halfY });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)halfX, AnchorY = (short)halfY });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)halfX, AnchorY = (short)-halfY });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-halfX, AnchorY = (short)-halfY });
            return region;
        }

        [Test]
        public void TryScan_FlatFlaggedModel_ReportsZeroGroundHeight() {
            // ProximityZoneSettings.bFlat_flag is the DAT DrawPriority byte; non-zero forces nZ_delta = 0
            // (no shift, no record z).
            var billboardish = Entry(kind: 0, Square(1000, baseElevation: 250), radius: 1000, drawPriority: 7);
            var world = World(Filter(), new ProximityRecord(billboardish, 0, 0, zBase: 900, angle: 0));

            Assert.IsTrue(world.TryScan(10, 10, out _, out int groundZ));
            Assert.AreEqual(0, groundZ);
        }

        [Test]
        public void TryScan_AabbShortcutFlag_ContainsEveryPointInsideTheBox() {
            // Gid.Flags bit 0 skips the polygon walk entirely — region 0 covers the whole AABB.
            var box = Entry(kind: 0, Square(10, baseElevation: 5), radius: 1000, gidFlags: 0x01);
            var world = World(Filter(), new ProximityRecord(box, 0, 0, 0, 0));

            Assert.IsTrue(world.TryScan(900, -900, out _, out int groundZ), "outside the polygon but inside the AABB");
            Assert.AreEqual(5, groundZ);
        }

        [Test]
        public void TryScan_RecordWithNoGidRegions_IsNeverAHit() {
            // proximity_check_index bails on !zone->bVertex_count before any other test.
            var propWithoutRegions = Entry(kind: 0, region: null, radius: 1000);
            var world = World(Filter(), new ProximityRecord(propWithoutRegions, 0, 0, 0, 0));

            Assert.IsFalse(world.TryScan(0, 0, out _, out _));
        }

        [Test]
        public void BuildCandidates_SkipsKindSeven() {
            var db = Entry(kind: 7, Square(1000), radius: 1000);
            var world = World(Filter(), new ProximityRecord(db, 0, 0, 0, 0));
            Assert.AreEqual(0, world.CandidateCount);
        }

        [Test]
        public void BuildCandidates_ExcludesTypesWhoseFilterThresholdIsMinusOne() {
            var filter = Filter();
            filter.DetailLevels[3].DrawDistances[5] = -1;
            var tree = Entry(kind: 5, Square(1000), radius: 1000);
            var world = World(filter, new ProximityRecord(tree, 0, 0, 0, 0));
            Assert.AreEqual(0, world.CandidateCount);
        }

        [Test]
        public void BuildCandidates_ThresholdOfOneIncludesTheRecordAtAnyDistance() {
            var ground = Entry(kind: 0, Square(1000), radius: 1000); // FILTER[0] == 1
            var world = new ProximityWorld(
                new List<ProximityRecord> { new ProximityRecord(ground, 5_000_000, 5_000_000, 0, 0) }, Filter());
            world.BuildCandidates(0, 0, detailLevel: 3);
            Assert.AreEqual(1, world.CandidateCount);
        }

        [Test]
        public void BuildCandidates_DistanceGatedTypeDropsOutBeyondItsThreshold() {
            var filter = Filter();
            filter.DetailLevels[3].DrawDistances[14] = 10000;
            var corridor = Entry(kind: 14, Square(1000), radius: 1000);
            var world = new ProximityWorld(
                new List<ProximityRecord> { new ProximityRecord(corridor, 40000, 0, 0, 0) }, filter);

            world.BuildCandidates(0, 0, detailLevel: 3);
            Assert.AreEqual(0, world.CandidateCount, "40000 units away, threshold 10000");

            world.BuildCandidates(35000, 0, detailLevel: 3);
            Assert.AreEqual(1, world.CandidateCount);
        }

        [Test]
        public void BuildCandidates_DetailLevelDoesNotChangeWhatTerrainYouCanWalkOn() {
            // Acceptance #22: FILTER == 1 for kinds 0-3 at every detail level.
            var ground = Entry(kind: 0, Square(1000), radius: 1000);
            var records = new List<ProximityRecord> { new ProximityRecord(ground, 100000, 0, 0, 0) };
            var world = new ProximityWorld(records, Filter(others: 18750, level0Others: 5000));

            for (int level = 0; level < FilterData.DetailLevelCount; level++) {
                world.BuildCandidates(100000, 0, level);
                Assert.AreEqual(1, world.CandidateCount, $"detail level {level}");
            }
        }

        [Test]
        public void IsWalkableKind_MatchesTheOriginalJumptable() {
            foreach (int kind in new[] { 0, 1, 2, 14, 15, 23 }) {
                Assert.IsTrue(ProximityWorld.IsWalkableKind(kind), $"kind {kind} should be walkable");
            }
            foreach (int kind in new[] { 3, 4, 5, 6, 7, 10, 13, 16, 20, 22, 24, 42 }) {
                Assert.IsFalse(ProximityWorld.IsWalkableKind(kind), $"kind {kind} should block");
            }
        }

        [Test]
        public void ProbeWalkable_LandingOnWalkableGround_Succeeds() {
            var ground = Entry(kind: 0, Square(1000, baseElevation: 12), radius: 1000);
            var world = World(Filter(), new ProximityRecord(ground, 0, 0, 0, 0));

            // Heading 0 steps +Y; 800 units from (0,-500) lands at (0,300), still inside the square.
            Assert.IsTrue(world.ProbeWalkable(0, -500, heading: 0, step: 800, out int kind, out int z));
            Assert.AreEqual(0, kind);
            Assert.AreEqual(12, z);
        }

        [Test]
        public void ProbeWalkable_LandingOnANonWalkableKind_Fails() {
            var rock = Entry(kind: 3, Square(1000), radius: 1000);
            var world = World(Filter(), new ProximityRecord(rock, 0, 0, 0, 0));

            Assert.IsFalse(world.ProbeWalkable(0, -500, heading: 0, step: 800, out _, out _));
        }
    
        // --- dungeon automap recording ----------------------------------------------------------

        private const byte MappableKind = 0x0e;   // the one automap kind present in Z10-Z12
        private const byte DoorKind = (byte)WorldEntityType.Door;   // 23 — a walkable kind
        private const int Underground = ZoneDefinition.UndergroundZoneLocation;

        [Test]
        public void Automap_RecordsAnEntityTheScanPassesCloseTo() {
            var table = new EncounterVisitTable();
            var door = Entry(MappableKind, Square(500), radius: 500);
            var world = new ProximityWorld(
                new List<ProximityRecord> {
                    new ProximityRecord(door, x: 100, y: 100, zBase: 0, angle: 0,
                        tileX: 13, tileY: 9, indexInTile: 7),
                },
                Filter());
            world.EnableAutomapRecording(table, zone: 11, zoneKind: Underground);

            world.BuildCandidates(0, 0, detailLevel: 3);

            Assert.IsTrue(table.HasSeen(11, 13, 9, 7), "the mark is written from the scan itself");
            Assert.AreEqual(1, table.UsedSlots);
        }

        [Test]
        public void Automap_IgnoresAnEntityBeyondTheRecordingRange() {
            var table = new EncounterVisitTable();
            var door = Entry(MappableKind, Square(500), radius: 500);
            // Comfortably past 0x640 on the octagonal metric, but still a collision candidate.
            var world = new ProximityWorld(
                new List<ProximityRecord> {
                    new ProximityRecord(door, x: 5000, y: 0, zBase: 0, angle: 0,
                        tileX: 13, tileY: 9, indexInTile: 7),
                },
                Filter());
            world.EnableAutomapRecording(table, zone: 11, zoneKind: Underground);

            world.BuildCandidates(0, 0, detailLevel: 3);

            Assert.AreEqual(0, table.UsedSlots);
        }

        [Test]
        public void Automap_RecordsNothingAboveGround() {
            var table = new EncounterVisitTable();
            var door = Entry(MappableKind, Square(500), radius: 500);
            var world = new ProximityWorld(
                new List<ProximityRecord> {
                    new ProximityRecord(door, x: 100, y: 100, zBase: 0, angle: 0,
                        tileX: 13, tileY: 9, indexInTile: 7),
                },
                Filter());
            world.EnableAutomapRecording(table, zone: 1, zoneKind: 0);

            world.BuildCandidates(0, 0, detailLevel: 3);

            Assert.AreEqual(0, table.UsedSlots, "the original allocates no table outside game mode 2");
        }

        [Test]
        public void Automap_RecordsAnEntityThatCarriesNoCollisionPolygons() {
            // A couple of kind-14 entries per underground zone own no GID regions, so they are
            // dropped from the collision record list. They must still be mappable — this is the
            // reason automap eligibility is decided before that filter.
            var table = new EncounterVisitTable();
            var door = Entry(MappableKind, region: null, radius: 500);
            var world = new ProximityWorld(
                new List<ProximityRecord> {
                    new ProximityRecord(door, x: 100, y: 100, zBase: 0, angle: 0,
                        tileX: 13, tileY: 9, indexInTile: 3),
                },
                Filter());
            world.EnableAutomapRecording(table, zone: 11, zoneKind: Underground);

            world.BuildCandidates(0, 0, detailLevel: 3);

            Assert.AreEqual(0, world.RecordCount, "it is not a collision record");
            Assert.IsTrue(table.HasSeen(11, 13, 9, 3), "but it is still recorded on the map");
        }

        [Test]
        public void Automap_IgnoresAKindThatIsNotDrawnOnTheMap() {
            var table = new EncounterVisitTable();
            var tree = Entry(kind: 5, Square(500), radius: 500);
            var world = new ProximityWorld(
                new List<ProximityRecord> {
                    new ProximityRecord(tree, x: 100, y: 100, zBase: 0, angle: 0,
                        tileX: 13, tileY: 9, indexInTile: 7),
                },
                Filter());
            world.EnableAutomapRecording(table, zone: 11, zoneKind: Underground);

            world.BuildCandidates(0, 0, detailLevel: 3);

            Assert.AreEqual(0, table.UsedSlots);
        }

        [Test]
        public void Automap_WithoutATableTheScanIsUnchanged() {
            var door = Entry(MappableKind, Square(500), radius: 500);
            var world = World(Filter(), new ProximityRecord(door, x: 100, y: 100, zBase: 0, angle: 0,
                tileX: 13, tileY: 9, indexInTile: 7));

            Assert.AreEqual(1, world.CandidateCount, "recording must not disturb candidate building");
        }
        /// <summary>
        /// A door that swings open must become walkable ground WITHOUT a zone reload.
        /// </summary>
        /// <remarks>
        /// The two door shapes are the same picture with and without a GID region (0x5c none,
        /// 0x5d XRadius 80 / YRadius 800 — <see cref="DoorMechanics.ClosedShapeId"/>), and that
        /// region is the doorway's only floor: measured over all 22 door placements in Z10, not one
        /// has ground from any other record at its centre. A shut door is therefore dropped at
        /// construction and the collision world does not know the doorway exists, so opening one
        /// has to ADD the record, not update it.
        /// </remarks>
        [Test]
        public void SetEntryAt_OpeningADoor_MakesTheDoorwayWalkable() {
            var shut = Entry(DoorKind, region: null, radius: 0);
            var open = Entry(DoorKind, Square(800), radius: 800);
            var world = World(Filter(), new ProximityRecord(shut, x: 5000, y: 5000, zBase: 0, angle: 0));

            Assert.IsFalse(world.TryScan(5000, 5000, out _, out _), "shut: no ground in the doorway");

            world.SetEntryAt(5000, 5000, angle: 0, zBase: 0, entry: open);
            world.BuildCandidates(5000, 5000, detailLevel: 3);

            Assert.IsTrue(world.TryScan(5000, 5000, out int kind, out _), "open: the doorway is floored");
            Assert.AreEqual(DoorKind, kind);
            Assert.IsTrue(ProximityWorld.IsWalkableKind(kind));
        }

        /// <summary>Shutting it takes the floor away again — the same swap, run backwards.</summary>
        [Test]
        public void SetEntryAt_ShuttingADoor_TakesTheDoorwayBack() {
            var shut = Entry(DoorKind, region: null, radius: 0);
            var open = Entry(DoorKind, Square(800), radius: 800);
            var world = World(Filter(), new ProximityRecord(open, x: 5000, y: 5000, zBase: 0, angle: 0));

            Assert.IsTrue(world.TryScan(5000, 5000, out _, out _));

            world.SetEntryAt(5000, 5000, angle: 0, zBase: 0, entry: shut);
            world.BuildCandidates(5000, 5000, detailLevel: 3);

            Assert.IsFalse(world.TryScan(5000, 5000, out _, out _));
        }

        /// <summary>
        /// The swap must not disturb a record sharing the door's exact point.
        /// </summary>
        /// <remarks>
        /// <b>Not a hypothetical: 21 of the 79 door placements in Z10-Z12 sit on another
        /// placement's exact coordinates</b>, and several of those neighbours are the room models
        /// that floor the room. A swap matching on position alone deleted one of them the first
        /// time the door swung — the door opened and took a piece of the floor with it.
        /// </remarks>
        [Test]
        public void SetEntryAt_LeavesARecordSharingTheDoorsPointAlone() {
            var floor = Entry(kind: 0, Square(1000), radius: 1000);
            var shut = Entry(DoorKind, region: null, radius: 0);
            var open = Entry(DoorKind, Square(800), radius: 800);
            var world = World(Filter(),
                new ProximityRecord(floor, x: 5000, y: 5000, zBase: 0, angle: 0),
                new ProximityRecord(shut, x: 5000, y: 5000, zBase: 0, angle: 0));

            world.SetEntryAt(5000, 5000, angle: 0, zBase: 0, entry: open);
            world.BuildCandidates(5000, 5000, detailLevel: 3);

            Assert.IsTrue(world.TryScan(5900, 5000, out int kind, out _),
                "the co-located room floor is still there, out past the door's own 800");
            Assert.AreEqual(0, kind);
        }
}
}
