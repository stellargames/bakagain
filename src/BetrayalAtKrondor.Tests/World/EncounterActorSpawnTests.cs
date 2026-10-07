namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// Spawning a zone's roaming actors, and the one field that means two things.
/// </summary>
public class EncounterActorSpawnTests {
    [Fact]
    public void TheKindAndThePersistedStateAreTheSameNumber() {
        // *** The thing to understand before anything else here. *** The renderer's "kind" and the
        // save code's "state" read the same high byte, so the two vocabularies line up exactly.
        Assert.Equal(EncounterActorSpawn.Standing, EncounterActorPersistence.Placed);
        Assert.Equal(1, EncounterActorSpawn.KindOf(EncounterActorPersistence.Removed));

        Assert.Equal(3, EncounterActorSpawn.KindOf(EncounterActorSpawn.Roaming));
        Assert.Equal(4, EncounterActorSpawn.KindOf(EncounterActorSpawn.Standing));
    }

    [Fact]
    public void TheLowBitsRideAlongAndDoNotDisturbTheKind() {
        // A roaming actor carries its walk frame and direction in the same word, so anything reading
        // the kind must mask rather than compare the whole value.
        int walking = EncounterActorSpawn.FreshlyPlacedState(frameRoll: 2, directionRoll: 1);

        Assert.Equal(3, EncounterActorSpawn.KindOf(walking));
        Assert.NotEqual(EncounterActorSpawn.Roaming, walking);
    }

    [Fact]
    public void SeedingHappensOnceAndIsReadOffTheFirstSlot() {
        Assert.True(EncounterActorSpawn.NeedsSeeding(EncounterActorPersistence.Untouched));
        Assert.False(EncounterActorSpawn.NeedsSeeding(EncounterActorPersistence.Removed));
        Assert.False(EncounterActorSpawn.NeedsSeeding(EncounterActorSpawn.Standing));
    }

    [Fact]
    public void ASlotSeedsPendingONLYForALivingRosterMember() {
        // The pass reads each named combatant's own flags, so a group already wiped out never comes
        // back — and it is the combatant table that remembers, not the encounter record.
        Assert.True(EncounterActorSpawn.SeedsAsPending(rosterSlot: 4, combatantIsDead: false));
        Assert.False(EncounterActorSpawn.SeedsAsPending(rosterSlot: 4, combatantIsDead: true));
        Assert.False(EncounterActorSpawn.SeedsAsPending(rosterSlot: -1, combatantIsDead: false));
    }

    [Fact]
    public void OnlyAPendingActorTakesItsPositionFromTheTemplate() {
        // An actor that has been placed before resumes from its stored pose; that is what lets a
        // dungeon roamer pick up where it was left.
        Assert.True(EncounterActorSpawn.PlacesFromTemplate(EncounterActorSpawn.Pending));
        Assert.False(EncounterActorSpawn.PlacesFromTemplate(EncounterActorSpawn.Roaming));
        Assert.False(EncounterActorSpawn.PlacesFromTemplate(EncounterActorSpawn.Standing));
    }

    [Fact]
    public void GoneAndUnseededActorsAreNeverPlaced() {
        Assert.False(EncounterActorSpawn.IsPlaced(EncounterActorPersistence.Removed, standingOnly: false));
        Assert.False(EncounterActorSpawn.IsPlaced(EncounterActorPersistence.Untouched, standingOnly: false));
    }

    [Fact]
    public void TheRecordFlagCanRestrictAZoneToStandingActorsOnly() {
        // Flag bit 0 set: a roaming group authored on such a record simply does not appear.
        Assert.True(EncounterActorSpawn.IsPlaced(EncounterActorSpawn.Roaming, standingOnly: false));
        Assert.False(EncounterActorSpawn.IsPlaced(EncounterActorSpawn.Roaming, standingOnly: true));
        Assert.True(EncounterActorSpawn.IsPlaced(EncounterActorSpawn.Standing, standingOnly: true));
        Assert.False(EncounterActorSpawn.IsPlaced(EncounterActorSpawn.Pending, standingOnly: true));
    }

    [Fact]
    public void EveryFreshActorStartsMidStrideAndTheyDoNotAllMatch() {
        // Placing a group on frame 0 all walking the same way makes them move in lockstep, which is
        // the tell of a ported spawn. The rolls are what break that up.
        int a = EncounterActorSpawn.FreshlyPlacedState(0, 0);
        int b = EncounterActorSpawn.FreshlyPlacedState(2, 1);

        Assert.NotEqual(a, b);
        Assert.Equal(0, a & EncounterActorSpawn.WalkDirectionBit);
        Assert.Equal(EncounterActorSpawn.WalkDirectionBit, b & EncounterActorSpawn.WalkDirectionBit);
        Assert.Equal(3, EncounterActorSpawn.KindOf(a));
        Assert.Equal(3, EncounterActorSpawn.KindOf(b));
    }

    [Fact]
    public void EveryFreshFrameIsWithinTheCycle() {
        for (var roll = 0; roll < EncounterActorSpawn.WalkFrameCount; roll++) {
            int state = EncounterActorSpawn.FreshlyPlacedState(roll, 0);
            Assert.InRange(state & 3, 0, EncounterActorSpawn.WalkFrameCount - 1);
        }
    }

    [Fact]
    public void SAVINGAWandererStopsItPermanently() {
        // *** Looks like a bug, and is what the game does. *** persist_actor_placed writes 0x400
        // whatever the actor was; nothing promotes standing back to roaming, and the movement updater
        // ignores every kind but roaming. So a saved wanderer comes back stopped and stays stopped.
        Assert.Equal(EncounterActorSpawn.Standing, EncounterActorPersistence.Placed);
        Assert.NotEqual(EncounterActorSpawn.Roaming, EncounterActorPersistence.Placed);

        // And a standing actor still places — it is stopped, not absent.
        Assert.True(EncounterActorSpawn.IsPlaced(EncounterActorPersistence.Placed, standingOnly: false));
        Assert.False(EncounterActorSpawn.PlacesFromTemplate(EncounterActorPersistence.Placed));
    }
}
