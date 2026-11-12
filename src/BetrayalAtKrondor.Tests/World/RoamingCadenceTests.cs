namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// How often a roaming actor steps, relative to the party (TASK-106).
/// </summary>
/// <remarks>
/// <b>The cadence has been recorded wrong twice.</b> The second note said only
/// <c>approachWalkToScenePosition</c> passes animate=1 and the main loop passes 0, so roaming was
/// metered by DISTANCE, one roaming step per quarter-cell the party covered. That ran monsters at the
/// party's own pace, four times the original's at the default Large step (owner, 2026-11-01). The
/// byte-matched source says otherwise: the world loop stores <c>worldmove_party_attempt_move</c>'s
/// result (WORLDLP.C:264), passes it as <c>animate</c> on the next render (WORLDLP.C:157), which
/// steps every roamer once (WORLDHIT.C:64-66), then clears it (WORLDLP.C:203). IDA agrees: MainGameLoop calls drawMap with <c>push 0</c> once
/// before the loop (0x6c310) and with <c>push di</c>, the move result, inside it (0x6c3b1); the old note
/// read only the first. One roaming tick per
/// party step, whatever the step length (MOVEMENT.DAT: 400, 800 or 1,600).
/// The Unity side is <c>WorldRuntime.AdvanceRoamingActors</c>.
/// </remarks>
public class RoamingCadenceTests {
    private const int RoamingStepDistance = RoadTravel.CellSize / 4;

    [Fact]
    public void AROAMINGSTEPIsExactlyAQuarterCell() {
        // *** This is not a tuning constant. *** The waypoints sit on the cell lattice and arrival
        // is tested by EXACT EQUALITY, so four steps have to land precisely on the next cell.
        Assert.Equal(0x190, RoamingStepDistance);
        Assert.Equal(RoadTravel.CellSize, RoamingStepDistance * 4);
        Assert.Equal(RoamingStepDistance, RoamingMovement.StepDistance);
    }

    // TASK-1018: one roaming tick per party step EVENT, not per distance. worldmove_party_attempt_move
    // returns 1 for a step that stands, a step a hotspot rolled back and a refused step that pivots, and
    // the world loop steps every roamer on each (WORLDLP.C:157, 264). Measured in the original on
    // c11-graves at Large: 400 per press, the press that only turned the party included.
    [Theory]
    [InlineData(1, 1)]      // a Classic step, a stood-back step or a pivot: one roaming step
    [InlineData(4, 0)]      // an Enhanced glide part at Large, alone: not yet
    public void ONERoamingTickPerPartyStepEvent(int parts, int expected) {
        Assert.Equal(expected, RoamingMovement.TicksForStep(0, parts).Ticks);
    }

    [Fact]
    public void ENHANCEDGlideSubStepsAddUpToOnePartyStep() {
        // Enhanced walking moves the party in Small sub-steps; at Large four of them are one party step,
        // and the carry is what makes them count as one roaming tick, not four.
        var carry = 0;
        var ticks = 0;
        for (var i = 0; i < 4; i++) {
            (int t, int c) = RoamingMovement.TicksForStep(carry, 4);
            ticks += t;
            carry = c;
        }

        Assert.Equal(1, ticks);
        Assert.Equal(0, carry);
    }

    [Fact]
    public void ASTATIONARYPatternNeverSteps() {
        // Pattern 0 has no waypoints, so a tick must be a no-op rather than walking it off in
        // whatever direction it happens to face.
        Assert.False(RoamingMovement.Moves(RoamingMovement.Pattern.Stationary));

        var pose = new RoamingMovement.Pose(1000, 2000, 0x4000);
        RoamingMovement.Pose after = RoamingMovement.Tick(
            pose, RoamingMovement.Pattern.Stationary, null, null);

        Assert.Equal(pose.X, after.X);
        Assert.Equal(pose.Y, after.Y);
        Assert.Equal(pose.Heading, after.Heading);
    }
}
