namespace BetrayalAtKrondor.Tests.Data;

using ResourceExtraction;
using System.Text;
using Xunit;

/// <summary>
/// A save stores names in CP437, so the original can load it (TASK-786). A letter CP437 lacks is
/// written as its base letter rather than as '?'.
/// </summary>
public class Cp437NameTests {
    static Cp437NameTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static string RoundTrip(string name) =>
        Encoding.GetEncoding(437).GetString(SaveGameWriter.EncodeCp437(name));

    [Theory]
    [InlineData("Krondor", "Krondor")]
    [InlineData("Dürer ß é Ñ", "Dürer ß é Ñ")]   // all in CP437: unchanged
    [InlineData("Łódź", "Lódz")]                   // Ł and ź are not; ó is
    [InlineData("Čapek Šťastný", "Capek Stastny")]
    [InlineData("Øresund œuvre", "Oresund oeuvre")] // no decomposition: spelled out
    public void ANameKeepsWhatCp437HasAndSpellsTheRestWithBaseLetters(string name, string saved) {
        Assert.Equal(saved, RoundTrip(name));
    }

    [Fact]
    public void ACharacterWithNoLatinFormBecomesAQuestionMark() {
        Assert.Equal("??", RoundTrip("名前"));
    }
}
