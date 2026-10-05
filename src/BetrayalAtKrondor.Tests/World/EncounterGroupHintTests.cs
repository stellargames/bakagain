namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;
using static GameData.Resources.World.EncounterGroupHint;

/// <summary>
/// Clicking a live encounter group in the world — <c>wcursor_encounter_hint</c>
/// (canassa INPUT/WCURSOR.C:1332-1370), TASK-795.
/// </summary>
public class EncounterGroupHintTests {
    private const uint Now = 100_000;

    [Fact]
    public void ASecondaryClickSaysTheyHaveNotNoticedThem() {
        Assert.Equal(Outcome.NotNoticed, Resolve(false, 12, true, 0, Now));
        Assert.Equal(0xfa, DialogFor(Outcome.NotNoticed));
    }

    [Fact]
    public void TheTwoSpecialRecordsAnswerBeforeTheGates() {
        Assert.Equal(Outcome.TooMany, Resolve(true, 0x3f, false, 0, Now));
        Assert.Equal(0x130, DialogFor(Outcome.TooMany));
        Assert.Equal(Outcome.RecordDialog, Resolve(true, 0x40, false, 0, Now));
    }

    [Fact]
    public void AGatedRecordDoesNothing() {
        Assert.Equal(Outcome.Nothing, Resolve(true, 12, false, 0, Now));
    }

    [Fact]
    public void AGroupNotPickedOutWithinADayIsPickedOutNow() {
        Assert.Equal(Outcome.PickedOut, Resolve(true, 12, true, 0, Now));
        Assert.Equal(Outcome.PickedOut, Resolve(true, 12, true, Now - TicksPerDay, Now));
        Assert.Equal(0xfb, DialogFor(Outcome.PickedOut));
    }

    [Fact]
    public void AGroupPickedOutWithinTheDayIsAlreadyPicked() {
        Assert.Equal(Outcome.AlreadyPicked, Resolve(true, 12, true, Now - TicksPerDay + 1, Now));
        Assert.Equal(0xfc, DialogFor(Outcome.AlreadyPicked));
    }

    [Fact]
    public void TheVisitTableSitsRightAfterTheFoughtTableAndRoundTrips() {
        Assert.Equal(EncounterFoughtTimes.BodyOffset + EncounterFoughtTimes.SaveSize, EncounterVisitedTimes.BodyOffset);
        var body = new byte[EncounterObjectStates.BodyOffset];
        var times = new EncounterVisitedTimes();
        times.Stamp(12, 777);
        Assert.True(times.Save(body));

        var back = new EncounterVisitedTimes();
        back.Load(body);
        Assert.Equal(777u, back.VisitedAt(12));
        Assert.Equal(777u, System.BitConverter.ToUInt32(body, EncounterVisitedTimes.BodyOffset + 12 * 4));
    }
}
