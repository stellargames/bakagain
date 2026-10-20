namespace BetrayalAtKrondor.Tests.Mods;

using GameData.Mods;
using Xunit;

public class VersionTests {
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("0.2.0-beta", 0, 2, 0, "beta")]
    public void Parse_Valid(string s, int ma, int mi, int pa, string? pre) {
        Assert.True(SemVersion.TryParse(s, out SemVersion v));
        Assert.Equal(new SemVersion(ma, mi, pa, pre), v);
        Assert.Equal(s, v.ToString());
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("")]
    [InlineData("1.2.3+build")]
    [InlineData("1.2.3-")]
    public void Parse_Invalid(string s) => Assert.False(SemVersion.TryParse(s, out _));

    [Theory]
    [InlineData(">=0.3.0", "0.3.0-beta", true)] // core-only compare (spec 4.1)
    [InlineData(">=0.3.0", "0.2.9", false)]
    [InlineData("^1.2.0", "1.9.0", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("^1.2.0", "1.1.9", false)]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.1", false)]
    public void Range_Satisfies(string range, string version, bool expected) {
        Assert.True(VersionRange.TryParse(range, out VersionRange r));
        Assert.True(SemVersion.TryParse(version, out SemVersion v));
        Assert.Equal(expected, r.Satisfies(v));
        Assert.Equal(range, r.ToString());
    }

    [Theory]
    [InlineData("~1.0.0")]
    [InlineData(">1.0.0")]
    [InlineData("1.x")]
    public void Range_UnsupportedForm_Invalid(string s) => Assert.False(VersionRange.TryParse(s, out _));
}
