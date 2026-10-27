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
    [InlineData("0.4.1-beta", "0.4.0-beta", true)]
    [InlineData("0.4.0", "0.4.0-beta", true)] // a release outranks its own prerelease
    [InlineData("0.4.0-beta", "0.4.0", false)]
    [InlineData("0.4.0-beta", "0.4.0-beta", false)]
    [InlineData("0.4.0-rc", "0.4.0-beta", true)]
    [InlineData("0.3.2-beta", "0.4.0-beta", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    public void IsNewerThan(string latest, string current, bool expected) {
        Assert.True(SemVersion.TryParse(latest, out SemVersion l));
        Assert.True(SemVersion.TryParse(current, out SemVersion c));
        Assert.Equal(expected, l.IsNewerThan(c));
    }

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

    [Theory]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-.a")]
    [InlineData("1.2.3-a_b")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-beta.01")]
    public void Parse_Invalid_LeadingZerosAndBadPrerelease(string s) => Assert.False(SemVersion.TryParse(s, out _));

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-alpha-1.x-y")]
    [InlineData("1.2.3-0")]
    [InlineData("1.2.3-0a")]
    public void Parse_Valid_PrereleaseIdentifiers(string s) {
        Assert.True(SemVersion.TryParse(s, out SemVersion v));
        Assert.Equal(s, v.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Range_NullOrWhitespace_Invalid(string? s) => Assert.False(VersionRange.TryParse(s!, out _));

    // Spec 4.1: ^X is "same major, at least X". There is no npm-style special case below 1.0.
    [Theory]
    [InlineData("^0.2.0", "0.9.0", true)]
    [InlineData("^0.2.0", "0.1.9", false)]
    [InlineData("^0.2.0", "1.0.0", false)]
    public void Range_Caret_ZeroMajor_IsSameMajor(string range, string version, bool expected) {
        Assert.True(VersionRange.TryParse(range, out VersionRange r));
        Assert.True(SemVersion.TryParse(version, out SemVersion v));
        Assert.Equal(expected, r.Satisfies(v));
    }

    [Fact]
    public void Range_BaseWithPrerelease_ComparesCore() {
        Assert.True(VersionRange.TryParse(">=1.2.0-beta", out VersionRange r));
        Assert.True(SemVersion.TryParse("1.2.0", out SemVersion plain));
        Assert.True(SemVersion.TryParse("1.1.9", out SemVersion older));
        Assert.True(r.Satisfies(plain));
        Assert.False(r.Satisfies(older));
        Assert.Equal(">=1.2.0-beta", r.ToString());
    }
}
