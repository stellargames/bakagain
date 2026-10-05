namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// The travel HUD's letters, which are not a key table but a scancode coincidence.
/// </summary>
/// <remarks>
/// REQ_MAIN's action ids ARE DOS scancodes, so the original dispatches the scancode the keyboard
/// produced straight into menupage_run. These pin the pairs against the ids the REQ actually carries
/// — if one drifts, the letter silently starts pressing a different button (TASK-584).
/// </remarks>
public class TravelHotkeysTests {
    [Theory]
    [InlineData('M', 50)]   // the local map
    [InlineData('E', 18)]   // encamp
    [InlineData('C', 46)]   // cast spell
    [InlineData('R', 19)]   // follow road
    [InlineData('O', 24)]   // options
    [InlineData('B', 48)]   // bookmark
    public void ALetterNamesTheActionWhoseIdIsItsScancode(char letter, int actionId) {
        Assert.Equal(actionId, TravelHotkeys.ActionFor(letter));
    }

    [Fact]
    public void TheCaseOfTheLetterDoesNotMatter() {
        // The adapter emits lower case; a human writing the table reaches for upper.
        Assert.Equal(TravelHotkeys.ActionFor('M'), TravelHotkeys.ActionFor('m'));
    }

    [Theory]
    [InlineData('A')]
    [InlineData('Z')]
    [InlineData('4')]
    [InlineData(' ')]
    public void ALetterTheHudHasNoButtonForNamesNothing(char letter) {
        // An unmatched scancode must do nothing rather than fall through to a neighbouring id.
        Assert.Equal(TravelHotkeys.NoAction, TravelHotkeys.ActionFor(letter));
    }

    // F is no button on either page, yet both loops act on it: menupage_run hands back every
    // scancode, and WORLDLP.C:324 and MAP.C:383 both run fmap_screen_run on 0x21 (TASK-797).
    // '1'..'3' are scancodes 2..4, the portraits' ids: the member's inventory, or with Shift the
    // character sheet (WORLDLP.C:355-371, MAP.C:405-419) — TASK-798.
    [Theory]
    [InlineData('1', 2)]
    [InlineData('2', 3)]
    [InlineData('3', 4)]
    public void ADigitIsThatMembersPortrait(char key, int actionId) {
        Assert.Equal(actionId, TravelHotkeys.ActionFor(key));
    }

    [Fact]
    public void FOpensTheFullMapFromTravelAndFromTheLocalMap() {
        Assert.Equal(0x21, TravelHotkeys.ActionFor('f'));
        Assert.Equal(GameData.Resources.World.LocalMapScreen.MapAction.ShowFullMap,
            GameData.Resources.World.LocalMapScreen.ActionFor(TravelHotkeys.ActionFor('F')));
    }

    [Fact]
    public void TheMapLetterIsTheONEActionTheMapScreenAlsoCloses_On() {
        // The toggle only works because both pages carry an entry for the same id: the travel loop
        // opens on 0x32 (WORLDLP.C:316) and the map screen closes on 0x32 (MAP.C:398).
        Assert.Equal(GameData.Resources.World.LocalMapScreen.MapAction.Close,
            GameData.Resources.World.LocalMapScreen.ActionFor(TravelHotkeys.ActionFor('M')));
    }
}
