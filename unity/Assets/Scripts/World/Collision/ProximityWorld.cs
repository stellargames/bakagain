namespace BakAgain.World.Collision {
    using System.Collections.Generic;
    using GameData.Resources.Config;
    using GameData.Resources.World;

    /// <summary>
    /// One placed world entity as the proximity scan sees it — the original's
    /// <c>ProximityRecord</c> (a <c>WorldObject</c> read through a collision-shaped lens). The TBL
    /// entry is resolved once at build time instead of being re-looked-up by index per probe.
    /// </summary>
    public readonly struct ProximityRecord {
        public readonly ZoneTableEntry Entry;
        public readonly int X;
        public readonly int Y;
        public readonly int ZBase;
        public readonly ushort Angle;

        /// <summary>The tile this placement was read from, and its index within that tile's file.</summary>
        /// <remarks>
        /// <b>Carried because the dungeon automap's marks are addressed this way</b> — a mark is a
        /// tile plus a bit at the entity's index in that tile's list
        /// (<see cref="GameData.Resources.World.EncounterVisitTable"/>). Collision never looks at
        /// them; they exist so a mark we write means the same entity a mark the ORIGINAL wrote does,
        /// which matters because saves interoperate.
        ///
        /// <para><b>The index is the WLD file order, and that is only the original's order at high
        /// detail.</b> <c>czone_load_actors</c> reads the file in order but DROPS kind-5 shapes on a
        /// throttle sized by the detail preference (&gt;1 keeps 300, 1 keeps 5, 0 keeps 2 between
        /// drops), and every drop shifts the indices after it. At detail &gt; 1 the throttle is 300
        /// and the file is capped at 300 records, so nothing is ever dropped and index == file
        /// index. We render every placement — no throttle anywhere — so we are always that case, and
        /// this is the convention we match. A port that added a detail cull would silently start
        /// writing marks the original reads as different entities.</para>
        /// </remarks>
        public readonly byte TileX;

        /// <inheritdoc cref="TileX"/>
        public readonly byte TileY;

        /// <inheritdoc cref="TileX"/>
        public readonly int IndexInTile;

        public ProximityRecord(ZoneTableEntry entry, int x, int y, int zBase, ushort angle,
            byte tileX = 0, byte tileY = 0, int indexInTile = -1) {
            Entry = entry;
            X = x;
            Y = y;
            ZBase = zBase;
            Angle = angle;
            TileX = tileX;
            TileY = tileY;
            IndexInTile = indexInTile;
        }
    }

    /// <summary>
    /// The zone's collision geometry: every placed entity that owns GID polygons, plus the
    /// FILTER.DAT candidate gate. Answers "what is the ground at this point, and may the party
    /// stand on it".
    ///
    /// <para>Faithful to <c>proxscan_run</c> + <c>proximity_scan_list</c> +
    /// <c>worldmove_probe_walkable_at</c> (IDA <c>CheckMoveDestination</c> @0x72333). See
    /// docs/specs/collision-system.md §2. Deliberately free of UnityEngine so the whole gate is
    /// unit-testable.</para>
    ///
    /// <para>Non-requirements preserved (spec §6): the party is a point, only the destination point
    /// is sampled, actors and props are not obstacles, and nothing here tests z.</para>
    /// </summary>
    public sealed class ProximityWorld {
        /// <summary>The walkable-kind jumptable of <c>CheckMoveDestination</c>: cases 0-2, 14, 15, 23.</summary>
        private static readonly bool[] Walkable = BuildWalkableSet();

        private readonly List<ProximityRecord> _records;
        private readonly List<ProximityRecord> _automapRecords = new();
        private EncounterVisitTable _automapTable;
        private byte _automapZone;
        private int _automapZoneKind = -1;
        private readonly List<int> _candidates = new();
        private readonly List<long> _distances = new();
        private readonly FilterData _filter;

        /// <summary>Conservative world-space half-extent per record, for a cheap pre-reject.</summary>
        private readonly List<int> _reach = new();

        public ProximityWorld(IReadOnlyList<ProximityRecord> records, FilterData filter) {
            _filter = filter;
            Placements = records;
            _records = new List<ProximityRecord>(records.Count);
            foreach (ProximityRecord record in records) {
                // proximity_check_index bails on `!zone->bVertex_count` before anything else, so a
                // record whose entity owns no GID regions can never be a hit. Dropping them here is
                // what makes chests, trees, signposts and corpses non-obstacles (spec §2.6) and keeps
                // the per-probe scan to the ~15% of entries that carry collision polygons.
                // Automap eligibility is decided BEFORE the collision filter below, because the
                // two sets are not the same: a handful of kind-14 entries per underground zone own
                // no GID regions, and dropping them here would make them permanently unmappable
                // while the original still records them. Cheap — a few dozen entries a zone.
                if (record.Entry != null && record.IndexInTile >= 0
                    && ProximityScan.AppearsOnAutomap((byte)record.Entry.Dat.EntityType)) {
                    _automapRecords.Add(record);
                }

                if (record.Entry?.Gid == null || record.Entry.Gid.Regions.Count == 0) {
                    continue;
                }
                _records.Add(record);
            }

            foreach (ProximityRecord record in _records) {
                _reach.Add(ReachOf(record.Entry));
            }
        }

        /// <summary>Rotation preserves length, so |v| &lt;= XRadius + YRadius bounds every in-AABB point.</summary>
        private static int ReachOf(ZoneTableEntry entry) =>
            (System.Math.Abs(entry.Gid.XRadius) + System.Math.Abs(entry.Gid.YRadius))
            << entry.Dat.VertexScale;

        /// <summary>
        /// Re-shape the placement standing at <paramref name="x"/>/<paramref name="y"/> — a door
        /// swinging open or shut.
        /// </summary>
        /// <remarks>
        /// <b>A door is the one placement whose collision changes without the zone being rebuilt,
        /// and it changes by APPEARING rather than by being edited.</b> The two door shapes are the
        /// same geometry with and without a GID region (<see cref="DoorMechanics.ClosedShapeId"/>
        /// 0x5c has none, <see cref="DoorMechanics.OpenShapeId"/> 0x5d has one), and since a region
        /// is GROUND here, the open shape's region is the floor of the doorway. Measured over all 22
        /// door placements in Z10: not one has ground from any other record at its centre, so a shut
        /// door is not an obstacle in the doorway — it is the ABSENCE of a doorway.
        ///
        /// <para>That is why this adds and removes rather than editing in place: the shut shape owns
        /// no regions, so the constructor drops it and there is nothing to edit. Without this, the
        /// collision world stayed the zone-load snapshot and an opened door was impassable until the
        /// party left the zone and came back — the flag, the model and the swing all changed, and
        /// the one thing the swap exists for did not.</para>
        ///
        /// <para><b>The KIND is part of the match, and dropping it deletes floor.</b> A door's
        /// coordinates are not unique: 21 of the 79 door placements in Z10-Z12 sit on the exact
        /// point of another placement, and a fifth of those neighbours are room models — the very
        /// records that floor the room the door opens onto. Matching on position alone removed one
        /// of them on the first swing and left a hole no zone reload could explain. No two doors
        /// ever share a point (measured: zero, all zones), so kind plus position is exact.</para>
        ///
        /// <para>The candidate list is dropped because it holds indices into <c>_records</c>.
        /// <see cref="BuildCandidates"/> runs at the head of every move step, so nothing has to
        /// rebuild it here.</para>
        /// </remarks>
        /// <param name="entry">The shape the placement now has; one with no GID regions removes it.</param>
        public void SetEntryAt(int x, int y, ushort angle, int zBase, ZoneTableEntry entry) {
            if (entry?.Dat == null) {
                return;
            }

            WorldEntityType kind = entry.Dat.EntityType;
            for (int i = _records.Count - 1; i >= 0; i--) {
                if (_records[i].X == x && _records[i].Y == y
                    && _records[i].Entry.Dat.EntityType == kind) {
                    _records.RemoveAt(i);
                    _reach.RemoveAt(i);
                }
            }

            if (entry?.Gid != null && entry.Gid.Regions.Count > 0) {
                _records.Add(new ProximityRecord(entry, x, y, zBase, angle));
                _reach.Add(ReachOf(entry));
            }

            _candidates.Clear();
            _distances.Clear();
        }

        /// <summary>
        /// Every placement, props included, in each tile's file order — what the arena's scenery
        /// pass walks (<see cref="GameData.Resources.Combat.ArenaScenery"/>).
        /// </summary>
        public IReadOnlyList<ProximityRecord> Placements { get; }

        /// <summary>Records that own collision polygons (props are dropped at construction).</summary>
        public int RecordCount => _records.Count;

        /// <summary>
        /// Every placement in the loaded zone, for a caller that needs to sweep them all rather
        /// than query a point.
        /// </summary>
        /// <remarks>
        /// The stash-exposure decay walks all of them once a day accumulating cover and traffic
        /// weights (<c>StashExposure.AccumulateWeights</c>), which is the original's shape too — it
        /// sweeps every zone entity list and lets the distance bands do the filtering rather than
        /// culling to a neighbourhood first.
        /// </remarks>
        public IReadOnlyList<ProximityRecord> Records => _records;

        /// <summary>Size of the last candidate list built by <see cref="BuildCandidates"/>.</summary>
        public int CandidateCount => _candidates.Count;

        public static bool IsWalkableKind(int kind) => (uint)kind < Walkable.Length && Walkable[kind];

        /// <summary>
        /// Rebuild the candidate list for a party standing at <paramref name="partyX"/>/<paramref
        /// name="partyY"/> — the original does this once per frame in <c>proxscan_full</c>, so every
        /// probe of one move step sees the same list. FILTER.DAT thresholds are read for
        /// <paramref name="detailLevel"/>; <c>-1</c> excludes the type outright and <c>1</c> includes
        /// it at any distance (which is why terrain kinds 0-3 are always collidable and the graphics
        /// detail slider cannot change where you may walk).
        /// </summary>
        public void BuildCandidates(int partyX, int partyY, int detailLevel) {
            _candidates.Clear();
            _distances.Clear();
            int[] thresholds = ThresholdsFor(detailLevel);

            for (int i = 0; i < _records.Count; i++) {
                ProximityRecord record = _records[i];
                int kind = (byte)record.Entry.Dat.EntityType;
                if (kind == 7) {
                    continue; // the db1..db8 records are never rendered and never collided
                }

                // No FILTER.DAT (or an out-of-range kind): keep the record rather than lose collision.
                long threshold = thresholds != null && kind < thresholds.Length ? thresholds[kind] : 1;
                if (threshold == -1) {
                    continue;
                }

                long distance = ProximityMath.OctagonalDistance(
                    record.X - (long)partyX, record.Y - (long)partyY);
                long metric = threshold != 1 ? distance - record.Entry.Dat.Extent : 0;

                if (metric < threshold) {
                    _candidates.Add(i);
                    _distances.Add(distance);
                }
            }

            SortLikeTheRenderer();

            // The original writes automap marks from inside this same per-frame sweep, so it is
            // called from here rather than from the movement code — see RecordAutomapVisits.
            RecordAutomapVisits(partyX, partyY, detailLevel);

            // ponytail: the original caps the visible list at 600 entries because it is a fixed DOS
            // scratch buffer covering a 9-chunk window. Unity keeps the whole zone resident, so the
            // same cap would truncate a list that is a different shape entirely — applying it would
            // be less faithful, not more. Revisit if per-chunk streaming is ever added.
        }

        /// <summary>
        /// The entity a pit drops the party onto — <c>proxscan_paged_find_next_type0f</c>.
        /// </summary>
        /// <remarks>
        /// <b>The LAST pit in range, not the nearest and not the first.</b> The original scans its
        /// visible-entity list backwards and takes the first match; that list is appended in the
        /// zone's own entity order and never sorted, so this walks the records in the same order and
        /// asks <see cref="PitDescent.SelectTarget"/> which one wins. The rule lives there rather
        /// than being spelled again here.
        ///
        /// <para><b>It reads <c>_automapRecords</c>, not the collision list.</b> A pit is walkable
        /// and may own no GID regions at all, so the collision list — which drops exactly those —
        /// can be missing every pit in the zone. Both sets are built from the same supplied order,
        /// which is what keeps "last" meaning the same thing.</para>
        ///
        /// <para>The distance gate is the visibility pass's, not the automap's: the FILTER.DAT
        /// threshold with the entity's own extent subtracted, the same metric
        /// <see cref="BuildCandidates"/> applies.</para>
        /// </summary>
        /// <returns>False when no pit is in range — an ordinary outcome, see
        /// <see cref="PitDescent.NoTarget"/>.</returns>
        public bool TryFindDescentTarget(int partyX, int partyY, int detailLevel,
            out int targetX, out int targetY) {
            targetX = 0;
            targetY = 0;
            if (_automapRecords.Count == 0) {
                return false;
            }

            int[] thresholds = ThresholdsFor(detailLevel);
            var kinds = new List<int>(_automapRecords.Count);
            var inRange = new List<ProximityRecord>(_automapRecords.Count);

            foreach (ProximityRecord record in _automapRecords) {
                int kind = (byte)record.Entry.Dat.EntityType;
                long threshold = thresholds != null && kind < thresholds.Length ? thresholds[kind] : 1;
                if (threshold == ProximityScan.DisabledThreshold) {
                    continue;
                }
                long distance = ProximityMath.OctagonalDistance(
                    record.X - (long)partyX, record.Y - (long)partyY);
                long metric = threshold != 1 ? distance - record.Entry.Dat.Extent : 0;
                if (metric >= threshold) {
                    continue;
                }
                kinds.Add(kind);
                inRange.Add(record);
            }

            int pick = PitDescent.SelectTarget(kinds);
            if (pick == PitDescent.NoTarget) {
                return false;
            }

            targetX = inRange[pick].X;
            targetY = inRange[pick].Y;
            return true;
        }

        /// <summary>
        /// Point the scan at the save's automap table. Until this is called nothing is recorded, so
        /// an above-ground zone (or a debug harness with no session) simply never marks — which is
        /// also the original's gate, since it allocates the table only in <c>g_game_mode == 2</c>.
        /// </summary>
        public void EnableAutomapRecording(EncounterVisitTable table, byte zone, int zoneKind) {
            _automapTable = table;
            _automapZone = zone;
            _automapZoneKind = zoneKind;
        }

        /// <summary>
        /// Record every automap-worthy entity the party is currently close to, faithful to the
        /// <c>worldframe_enc_rec_prox</c> call the original makes from inside its proximity scan
        /// (canassa R3D/VIS/PROXSCAN.C, both sweeps).
        ///
        /// <para>Returns how many marks were <b>written</b>, not how many were NEW:
        /// <see cref="EncounterVisitTable.MarkSeen"/> ORs the bit in and returns true whether or not
        /// it was already set, so a party standing still keeps reporting the same count. Said
        /// "newly written" until 2026-09-20, and reading it that way cost a session a wrong
        /// conclusion — a forced call returning 3 looked like proof that recording worked and
        /// persistence did not, when nothing new had been in range at all.</para>
        ///
        /// <para>This is a <b>sibling of <see cref="BuildCandidates"/>, not a step callback</b> —
        /// the original marks from the same per-frame loop that builds the visible list, so marking
        /// happens while you stand still and turn, not only when you move. It is a separate method
        /// only because the two walk different record sets (see the constructor).</para>
        ///
        /// <para>The caller supplies the zone gate: the original tests <c>g_game_mode == 2</c>, and
        /// no mark is ever written above ground. Distance is the raw octagonal distance with no
        /// allowance for the entity's size, so a big door and a small one record at the same range.
        /// </para>
        /// </summary>
        public int RecordAutomapVisits(int partyX, int partyY, int detailLevel) {
            if (_automapTable == null || _automapRecords.Count == 0) {
                return 0;
            }

            int[] thresholds = ThresholdsFor(detailLevel);
            int marked = 0;
            foreach (ProximityRecord record in _automapRecords) {
                int kind = (byte)record.Entry.Dat.EntityType;
                // The original's mark sits INSIDE the FILTER.DAT gate, so a type the detail level
                // excludes outright is not recorded either. (No automap kind is currently excluded
                // at any level, but the gate is where the original put it.)
                long threshold = thresholds != null && kind < thresholds.Length ? thresholds[kind] : 1;
                if (threshold == ProximityScan.DisabledThreshold) {
                    continue;
                }

                long distance = ProximityMath.OctagonalDistance(
                    record.X - (long)partyX, record.Y - (long)partyY);
                if (!ProximityScan.RecordsOnAutomap(kind, distance, _automapZoneKind,
                        hasAutomapRecord: true)) {
                    continue;
                }

                if (_automapTable.MarkSeen(_automapZone, record.TileX, record.TileY,
                        record.IndexInTile)) {
                    marked++;
                }
            }

            return marked;
        }

        /// <summary>
        /// <c>vislist_sort</c> (canassa R3D/VIS/VISLIST.C). Every <c>proxscan_full</c> call site in
        /// the original sorts the visible list immediately afterwards, and the collision scan reads
        /// that same sorted list — so this ordering is part of the collision behaviour, not just of
        /// rendering.
        ///
        /// <para>Two groups: models with a non-zero <c>priority</c> ("proud" geometry) are moved to
        /// the front and ordered by descending priority; everything else follows, ordered far to
        /// near. Because <see cref="TryScan"/> walks the list backwards, the practical rules are
        /// <b>nearest wins</b> and <b>priority models are consulted last</b>. Reproduced with the
        /// original's selection sort rather than a library sort, because both are unstable and only
        /// the same algorithm gives the same answer on ties.</para>
        /// </summary>
        private void SortLikeTheRenderer() {
            int count = _candidates.Count;
            if (count < 2) {
                return;
            }

            int pivot = 0;
            for (int i = 0; i < count; i++) {
                if (_records[_candidates[i]].Entry.Dat.DrawPriority != 0) {
                    Swap(pivot, i);
                    pivot++;
                }
            }

            for (int i = 0; i < pivot - 1; i++) {
                int best = i;
                int bestPriority = _records[_candidates[i]].Entry.Dat.DrawPriority;
                for (int j = i + 1; j < pivot; j++) {
                    int priority = _records[_candidates[j]].Entry.Dat.DrawPriority;
                    if (bestPriority < priority) {
                        bestPriority = priority;
                        best = j;
                    }
                }
                if (i != best) {
                    Swap(i, best);
                }
            }

            for (int i = pivot; i < count - 1; i++) {
                int best = i;
                long bestDistance = _distances[i];
                for (int j = i + 1; j < count; j++) {
                    if (bestDistance < _distances[j]) {
                        bestDistance = _distances[j];
                        best = j;
                    }
                }
                if (i != best) {
                    Swap(i, best);
                }
            }
        }

        private void Swap(int a, int b) {
            (_candidates[a], _candidates[b]) = (_candidates[b], _candidates[a]);
            (_distances[a], _distances[b]) = (_distances[b], _distances[a]);
        }

        /// <summary>
        /// The proximity scan: find the first candidate whose polygon contains the point, walking the
        /// list back to front so the last record added wins overlaps. Reports the owning entity's
        /// kind and the ground height at the point.
        /// </summary>
        public bool TryScan(int x, int y, out int kind, out int groundZ) {
            for (int c = _candidates.Count - 1; c >= 0; c--) {
                int i = _candidates[c];
                ProximityRecord record = _records[i];

                long dx = x - (long)record.X;
                long dy = y - (long)record.Y;
                if (dx > _reach[i] || dx < -_reach[i] || dy > _reach[i] || dy < -_reach[i]) {
                    continue; // cheap pre-reject; the exact AABB test below is the original's
                }

                if (TryHit(record, dx, dy, out kind, out groundZ)) {
                    return true;
                }
            }

            kind = 0;
            groundZ = 0;
            return false;
        }

        /// <summary>
        /// Would a step of <paramref name="step"/> units along <paramref name="heading"/> land on
        /// walkable ground? Only the destination point is sampled — there is no swept test, and
        /// "no polygon found" is a block, which is what keeps the party inside the authored world.
        /// </summary>
        public bool ProbeWalkable(int x, int y, ushort heading, int step, out int kind, out int groundZ) {
            var (dx, dy) = MovementMath.StepDelta(heading, step);
            return TryScan(x + dx, y + dy, out kind, out groundZ) && IsWalkableKind(kind);
        }

        // proximity_point_test
        private static bool TryHit(ProximityRecord record, long dx, long dy, out int kind, out int groundZ) {
            kind = 0;
            groundZ = 0;

            TableDatInfo dat = record.Entry.Dat;
            TableGidInfo gid = record.Entry.Gid;
            int shift = dat.VertexScale;

            // Into model space: per-model fixed-point downscale, then un-rotate by the placement yaw.
            var (mx, my) = ProximityMath.Rotate((int)(dx >> shift), (int)(dy >> shift), -record.Angle);

            if ((mx < 0 ? -mx : mx) > gid.XRadius || (my < 0 ? -my : my) > gid.YRadius) {
                return false;
            }

            GidRegion region = gid.Regions[0];
            if ((gid.Flags & 0x01) == 0) {
                int index = 0;
                while (index < gid.Regions.Count && !ProximityMath.RegionContains(gid.Regions[index], mx, my)) {
                    index++;
                }
                if (index == gid.Regions.Count) {
                    return false;
                }
                region = gid.Regions[index];
            }
            // Flags bit 0 set: the record contains every point inside its AABB, region 0 is used.

            kind = (byte)dat.EntityType;
            if (dat.DrawPriority != 0) {
                // ProximityZoneSettings.bFlat_flag — this model contributes no ground height at all.
                groundZ = 0;
            } else {
                int local = gid.IsSloped ? ProximityMath.RegionHeight(region, mx, my) : region.BaseElevation;
                groundZ = (local << shift) + record.ZBase;
            }

            return true;
        }

        // The clamp lives on FilterData so this gate and WorldEntityVisibility's render gate cannot
        // drift apart on it.
        private int[] ThresholdsFor(int detailLevel) => _filter?.DrawDistancesFor(detailLevel);

        private static bool[] BuildWalkableSet() {
            var set = new bool[24];
            foreach (int kind in new[] { 0, 1, 2, 14, 15, 23 }) {
                set[kind] = true;
            }
            return set;
        }
    }
}
