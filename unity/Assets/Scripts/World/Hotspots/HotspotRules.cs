namespace BakAgain.World.Hotspots {
    using GameData.Resources.GameState;
    using GameData.Resources.World;

    /// <summary>
    /// The arithmetic behind the hotspot passes, split out from <see cref="HotspotService"/> so the
    /// boundaries can be tested without a loaded zone. The gate helpers live here rather than in
    /// either pass because <b>both</b> passes apply them — the activate pass to decide whether the
    /// step stands, and the dispatch pass again before running anything.
    /// </summary>
    public static class HotspotRules {
        /// <summary>Save-state key of the "this encounter has been fought" flag.
        /// <c>ENCOUNTER_FOUGHT(idx) = idx + 5220</c> (canassa <c>SRC/GAME/STATE/GSTATE.H</c>).</summary>
        public const int EncounterFoughtKeyBase = 5220;

        /// <summary>Encounter indices at or above this are treated as permanently fought.</summary>
        public const int EncounterIndexLimit = 1000;

        /// <inheritdoc cref="EncounterFoughtKeyBase"/>
        public static int EncounterFoughtKey(long encounterNumber) =>
            EncounterFoughtKeyBase + (int)encounterNumber;

        /// <summary>
        /// Whether an encounter index is out of range, which <c>hotspotevt_enc_fought_read</c> reports
        /// as already-fought rather than as an error — so the encounter simply never fires.
        /// </summary>
        public static bool EncounterIndexOutOfRange(long encounterNumber) =>
            encounterNumber < 0 || encounterNumber >= EncounterIndexLimit;

        /// <summary>
        /// Whether a scouting attempt spots the ambush: <c>RND(100) &lt;= bestScouting</c>.
        /// </summary>
        /// <param name="roll">A roll in <c>[0, 100)</c>.</param>
        /// <remarks>
        /// The comparison is <b>inclusive</b> in the original, so a party with Scouting 0 still spots
        /// an ambush on a roll of 0 — a 1-in-100 chance rather than never. Worth preserving: it is the
        /// difference between "unskilled" and "impossible".
        /// </remarks>
        public static bool ScoutingSpots(int roll, int bestScouting) => roll <= bestScouting;

        /// <summary>
        /// Whether a chance-gated hotspot fires — <c>RND(100) &lt;= chance</c>, the same
        /// <b>inclusive</b> comparison the scouting roll uses. A record with chance 0 therefore still
        /// fires on a roll of 0, and one with chance 100 always does.
        /// </summary>
        public static bool ChanceFires(int roll, int chance) => roll <= chance;

        /// <summary>
        /// <c>hotspotevt_available</c>: whether a hotspot may act at all.
        /// </summary>
        /// <remarks>
        /// The two record gates are <b>asymmetric and easy to swap</b>: <c>Requires</c> must be set
        /// (0 blocks), <c>Forbids</c> must be clear (non-zero blocks). Getting them the wrong way
        /// round leaves a hotspot that fires exactly when it should not.
        /// </remarks>
        public static bool Available(IHotspotHost host, TileEventTrigger trigger, int index) =>
            host != null
            && !Done(host, index)
            && host.ReadGlobal(host.ScoutTriedFlagKey(index)) == 0
            && !Forbidden(host, trigger)
            && !Unmet(host, trigger);

        /// <summary>Whether the persistent per-(zone, chunk, hotspot) "already done" flag is set.</summary>
        public static bool Done(IHotspotHost host, int index) =>
            host.ReadGlobal(host.DoneFlagKey(index)) != 0;

        /// <summary>Whether the record's <c>Forbids</c> gate is currently blocking it.</summary>
        public static bool Forbidden(IHotspotHost host, TileEventTrigger trigger) {
            int? key = GlobalKeyOf(trigger?.Forbids);

            return key.HasValue && host.ReadGlobal(key.Value) != 0;
        }

        /// <summary>Whether the record's <c>Requires</c> gate is not yet satisfied.</summary>
        public static bool Unmet(IHotspotHost host, TileEventTrigger trigger) {
            int? key = GlobalKeyOf(trigger?.Requires);

            return key.HasValue && host.ReadGlobal(key.Value) == 0;
        }

        /// <summary>Apply the record's own state mutation, if it carries one.</summary>
        public static void ApplyOnFire(IHotspotHost host, TileEventTrigger trigger) {
            if (trigger?.OnFire is SetFlagEffect effect) {
                host.WriteGlobal(effect.Flag, effect.Set ? 1 : 0);
            }
        }

        /// <summary>
        /// Recover the raw save-state key a decoded gate condition came from. The original stores one
        /// u16 key per gate and tests it with a bare <c>!= 0</c>; the extractor decodes that key into
        /// a typed <see cref="Condition"/>, and each subtype documents the key range it occupies.
        /// Reversing that gives back exactly the test the engine performs.
        /// </summary>
        public static int? GlobalKeyOf(Condition condition) {
            switch (condition) {
                case null: return null;
                case FlagCondition c: return c.Flag;
                case VarCondition c: return 30000 + c.Var;
                case PartyCondition c: return 40000 + c.Check;
                case HasItemCondition c: return 50000 + c.Item;
                case HasNoteCondition c: return 51000 + c.Note;
                case SpellTimerActiveCondition c: return 52000 + c.Timer;
                case RandomCondition c: return 53000 + c.Bound;
                case RawGlobalCondition c: return c.Key;
                default: return null;
            }
        }
    }
}
