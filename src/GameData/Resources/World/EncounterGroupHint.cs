namespace GameData.Resources.World;

/// <summary>
/// What clicking a live encounter group in the world does — <c>wcursor_encounter_hint</c>
/// (WCURSOR.C:1332-1370), reached from the viewport hit-test for any encounter actor that is not a
/// corpse (WCURSOR.C:83-90).
/// </summary>
/// <remarks>
/// A primary click "picks the group out": it stamps <see cref="EncounterVisitedTimes"/>, and that
/// stamp is what the surprise roll reads when the fight starts (HOTSPOT.C:513, :763 —
/// <see cref="CombatEncounterOpening.WasRecentlyVisited"/>). Planning an attack is how the party
/// gets the drop.
/// </remarks>
public static class EncounterGroupHint {
    public enum Outcome {
        /// <summary>The record's gates are closed; the click does nothing past its sound.</summary>
        Nothing,
        /// <summary>Secondary click: 0xfa, "The @1 hadn't noticed them".</summary>
        NotNoticed,
        /// <summary>Record 0x3f: 0x130, too many of them.</summary>
        TooMany,
        /// <summary>Record 0x40: the record's own main dialog (DialogId1), if it has one.</summary>
        RecordDialog,
        /// <summary>Not picked out in the last day: stamp the visit, then 0xfb.</summary>
        PickedOut,
        /// <summary>Picked out within the day: 0xfc, "that is the group we picked out".</summary>
        AlreadyPicked,
    }

    /// <summary>The click sound, before anything else (WCURSOR.C:1336).</summary>
    public const int ClickSound = 0x30;

    /// <summary>A day in two-second ticks — the <c>/ 0xa8c0 &gt;= 1</c> test.</summary>
    public const uint TicksPerDay = 0xa8c0;

    public const long TooManyRecord = 0x3f;
    public const long RecordDialogRecord = 0x40;

    public static Outcome Resolve(bool isPrimary, long record, bool gatesPass, uint visitedAt, uint now) {
        if (!isPrimary) {
            return Outcome.NotNoticed;
        }
        if (record == TooManyRecord) {
            return Outcome.TooMany;
        }
        if (record == RecordDialogRecord) {
            return Outcome.RecordDialog;
        }
        if (!gatesPass) {
            return Outcome.Nothing;
        }
        return visitedAt == 0 || ((long)now - visitedAt) / TicksPerDay >= 1
            ? Outcome.PickedOut
            : Outcome.AlreadyPicked;
    }

    /// <summary>The dialog an outcome plays, or 0 for none of its own.</summary>
    public static int DialogFor(Outcome outcome) => outcome switch {
        Outcome.NotNoticed => 0xfa,
        Outcome.TooMany => 0x130,
        Outcome.PickedOut => 0xfb,
        Outcome.AlreadyPicked => 0xfc,
        _ => 0,
    };
}

/// <summary>
/// When each encounter group was last picked out — <c>GAM_ENC_VISITED_TIME</c>, the 700-entry table
/// right after <see cref="EncounterFoughtTimes"/> (GSTATE.H:100-102). Written by
/// <see cref="EncounterGroupHint"/>, read by the surprise roll.
/// </summary>
public sealed class EncounterVisitedTimes {
    public const int BodyOffset = EncounterFoughtTimes.BodyOffset + EncounterFoughtTimes.SaveSize;

    // Same shape as the fought table, one table further on.
    private readonly EncounterFoughtTimes _times = new();

    public void Load(byte[] body) => _times.Load(body, BodyOffset);
    public bool Save(byte[] body) => _times.Save(body, BodyOffset);
    public void Stamp(long encounter, uint gameTime) => _times.Stamp(encounter, gameTime);
    public uint VisitedAt(long encounter) => _times.FoughtAt(encounter);
}
