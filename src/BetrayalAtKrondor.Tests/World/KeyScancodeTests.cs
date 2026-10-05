namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// A REQ entry's action id is the DOS scancode of the key that presses it (MENUPAGE.C:346-366),
/// so a key is turned into its set-1 scancode before it is matched — TASK-796.
/// </summary>
public class KeyScancodeTests {
    [Theory]
    // The options menu: Save 0x1f, Quit 0x20, New Game 0x31, Cancel 0x12.
    [InlineData('s', 0x1f)]
    [InlineData('D', 0x20)]
    [InlineData('n', 0x31)]
    [InlineData('e', 0x12)]
    // The travel HUD's letters agree with TravelHotkeys.
    [InlineData('m', 0x32)]
    [InlineData('r', 0x13)]
    // Digits: '1'..'9' are 2..10, '0' is 11 — the portraits are ids 2-4.
    [InlineData('1', 0x02)]
    [InlineData('3', 0x04)]
    [InlineData('0', 0x0b)]
    [InlineData('q', 0x10)]
    [InlineData('z', 0x2c)]
    [InlineData(' ', 0x39)]
    [InlineData(',', 0x33)]
    [InlineData('.', 0x34)]
    public void AKeyIsItsSetOneScancode(char key, int scancode) {
        Assert.Equal(scancode, KeyScancode.Of(key));
    }

    [Fact]
    public void AKeyWithoutAScancodeHereIsMinusOne() {
        Assert.Equal(-1, KeyScancode.Of('é'));
    }
}
