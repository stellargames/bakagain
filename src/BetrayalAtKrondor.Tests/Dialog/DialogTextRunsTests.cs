namespace BetrayalAtKrondor.Tests.Dialog;

using GameData.Resources.Dialog;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// DDX inline formatting — <c>drawCharacter</c> @0x15eef-0x15fa5.
/// </summary>
public class DialogTextRunsTests {
    private const int BodyPen = 0;

    /// <summary>Decodes text written in the format's markup, as the loader hands it to the renderer.</summary>
    private static List<DialogTextRuns.Run> Decode(string markup, int bodyPen = BodyPen) {
        string text = DialogTextRuns.FromMarkup(markup);
        return DialogTextRuns.Decode(text, 0, text.Length, bodyPen);
    }

    private static string Slice(string text, DialogTextRuns.Run r) =>
        text.Substring(r.Start, r.Length);

    [Fact]
    public void PlainTextIsOneUnstyledRun() {
        const string text = "hello there";
        List<DialogTextRuns.Run> runs = Decode(text);

        Assert.Single(runs);
        Assert.Equal(text, Slice(text, runs[0]));
        Assert.False(runs[0].Italic);
        Assert.Equal(BodyPen, runs[0].Pen);
    }

    [Fact]
    public void ControlCodesAreNeverPartOfARun() {
        string text = DialogTextRuns.FromMarkup("a<i/>bc");
        List<DialogTextRuns.Run> runs = DialogTextRuns.Decode(text, 0, text.Length, BodyPen);

        // The codes are text bytes in the source; emitting one would print a stray CP437 glyph.
        Assert.DoesNotContain(runs, r => Slice(text, r).Any(DialogTextRuns.IsControlCode));
        Assert.Equal("abc", string.Concat(runs.Select(r => Slice(text, r))));
    }

    // The Seven Pillars' voice (DIAL_Z19) puts 0xE3 before every letter. FONT.C:217-218 reads a
    // 0xE_ byte exactly like its 0xF_ twin, so it is italic and never a glyph.
    [Fact]
    public void TheE0RowIsTheSameControlCodesAsTheF0Row() {
        Assert.Equal("<i/>W<i/>e x", DialogTextRuns.FromCp437("πWπe °x"));
        Assert.Equal(DialogTextRuns.FromCp437("≤x"), DialogTextRuns.FromCp437("πx"));
    }

    [Fact]
    public void TheTwoHighlightCodesAreOneTag() {
        // 0xF1 and 0xF2 share a case in the original.
        Assert.Equal("<hi/>x <hi/>y", DialogTextRuns.FromCp437("±x ≥y"));
    }

    [Fact]
    public void EveryCodeTheOriginalActsOnHasATag() {
        Assert.Equal("<reset/><hi/><i/><shift2/><shift/>", DialogTextRuns.FromCp437("≡±≤⌠⌡"));
    }

    [Fact]
    public void CodesTheOriginalIgnoresAreDropped() {
        // Low nibbles 6-15 have no arm in drawCharacter: nothing drawn, no style moved, no width.
        Assert.Equal("ab", DialogTextRuns.FromCp437("a÷≈°∙·√ⁿ²■\u00a0Φb"));
    }

    [Fact]
    public void ARepeatedHighlightIsKept_ItIsNotIdempotent() {
        // From body pen 1 the first highlight gives pen 0 and the second pen 5.
        Assert.Equal("<hi/><hi/>x", DialogTextRuns.FromCp437("±±x"));
        Assert.Equal(5, Decode("<hi/><hi/>x", bodyPen: 1)[0].Pen);
    }

    [Fact]
    public void TranslatedTextMayUseAnyCharacter() {
        // The reason for the markup: ß and Greek are letters in a translation, not control codes.
        const string german = "Straße αβγ";
        List<DialogTextRuns.Run> runs = Decode(german);

        Assert.Single(runs);
        Assert.Equal(german, DialogTextRuns.FromMarkup(german));
        Assert.False(runs[0].Italic);
    }

    [Fact]
    public void AnUnknownTagIsText() {
        Assert.Equal("<b>x</b>", DialogTextRuns.FromMarkup("<b>x</b>"));
    }

    [Fact]
    public void PlainItalicLeavesThePenAlone() {
        List<DialogTextRuns.Run> runs = Decode("<i/>x", bodyPen: 0x0B);

        Assert.True(runs[0].Italic);
        Assert.Equal(0x0B, runs[0].Pen);
    }

    [Fact]
    public void ASpaceResetsBothItalicAndPen() {
        // This is what makes styling one wrapped LINE at a time equivalent to styling the block:
        // no style can survive a break.
        string text = DialogTextRuns.FromMarkup("<hi/>one two");
        List<DialogTextRuns.Run> runs = DialogTextRuns.Decode(text, 0, text.Length, BodyPen);

        Assert.Equal(2, runs.Count);
        Assert.Equal("one", Slice(text, runs[0]));
        Assert.True(runs[0].Italic);
        Assert.Equal(" two", Slice(text, runs[1]));
        Assert.False(runs[1].Italic);
        Assert.Equal(BodyPen, runs[1].Pen);
    }

    [Fact]
    public void ResetEndsAStyledRunMidWord() {
        string text = DialogTextRuns.FromMarkup("<hi/>ab<reset/>cd");
        List<DialogTextRuns.Run> runs = DialogTextRuns.Decode(text, 0, text.Length, BodyPen);

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Italic);
        Assert.Equal("ab", Slice(text, runs[0]));
        Assert.False(runs[1].Italic);
        Assert.Equal("cd", Slice(text, runs[1]));
    }

    [Fact]
    public void TheDoubleRemapIsNotTheSingleOneTwice_ItHasItsOwnFirstStep() {
        // 0xF4 falls THROUGH into 0xF5, so it applies an extra first step first. From pen 0 the
        // single remap gives 1; the double gives 0 -> 0x0A -> 0. Treating them as one code, or
        // applying the same step twice, both produce a different colour.
        Assert.Equal(1, Decode("<shift/>x")[0].Pen);
        Assert.Equal(0, Decode("<shift2/>x")[0].Pen);
    }

    [Fact]
    public void TheRemapDependsOnTheCurrentPen_NotOnlyOnTheCode() {
        // Why a control code cannot be interpreted in isolation.
        Assert.Equal(1, Decode("<shift/>x", bodyPen: 0)[0].Pen);
        Assert.Equal(0x0B, Decode("<shift/>x", bodyPen: 1)[0].Pen);
        Assert.Equal(0, Decode("<shift/>x", bodyPen: 0x0A)[0].Pen);
    }

    [Fact]
    public void ARangeIsDecodedFromTheDefaultState() {
        // What lets the wrap run before styling: each line starts fresh, as drawTextString does.
        string text = DialogTextRuns.FromMarkup("<hi/>one two");
        // Index 3 is the 'e' of "one" — inside the italic run in the full string, but decoded on
        // its own it starts from the default state, exactly as a wrapped line does.
        List<DialogTextRuns.Run> runs = DialogTextRuns.Decode(text, 3, text.Length, BodyPen);

        Assert.Single(runs);
        Assert.Equal("e two", Slice(text, runs[0]));
        Assert.False(runs[0].Italic);
    }
}
