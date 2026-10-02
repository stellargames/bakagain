namespace BetrayalAtKrondor.Tests.Animation;

using GameData.Resources.Animation;
using Xunit;

/// <summary>
/// What a cutscene's dialog command asks for (TASK-159).
/// </summary>
/// <remarks>
/// The cutscene engine is the least-covered system in the project, and these rules were the part of
/// it reachable without standing up a player: a dispatch on two fields, a key derivation and a
/// six-way mode split. All three were previously inline in Unity — the key derivation in four
/// separate places — so nothing could fail if one copy drifted.
/// </remarks>
public class CutsceneDialogCommandTests {
    [Fact]
    public void THETWOFIELDSAreReadTogether() {
        // *** Dialog16Id 0 is three different commands. *** Reading it alone, a port clears the
        // dialog plate when the scene meant to turn a book page.
        Assert.Equal(CutsceneDialogCommand.Kind.Clear, CutsceneDialogCommand.KindOf(0, 255));
        Assert.Equal(CutsceneDialogCommand.Kind.BookAnimation, CutsceneDialogCommand.KindOf(0, 0));
        Assert.Equal(CutsceneDialogCommand.Kind.BookAnimation, CutsceneDialogCommand.KindOf(0, 20));
        Assert.Equal(CutsceneDialogCommand.Kind.None, CutsceneDialogCommand.KindOf(0, 21));
    }

    [Fact]
    public void MINUSONEIsADrawCommand_NotDialog1599999() {
        // It blits from image slot 2 and is deliberately unimplemented. Falling through to the
        // display arm would ask for a dialog id below the base — a lookup that cannot succeed and
        // would log a missing-entry warning on every frame that carries one.
        Assert.Equal(CutsceneDialogCommand.Kind.None, CutsceneDialogCommand.KindOf(-1, 0));
        Assert.Equal(CutsceneDialogCommand.Kind.None, CutsceneDialogCommand.KindOf(-1, 255));
    }

    [Fact]
    public void APOSITIVEIdShowsADialogInEveryModeButTwo() {
        foreach (int arg2 in new[] { 0, 1, 3, 4, 5, 255 }) {
            Assert.Equal(CutsceneDialogCommand.Kind.Display,
                CutsceneDialogCommand.KindOf(12, arg2));
        }
    }

    [Fact]
    public void ModeTwoOpensTheChapterBookTheIdSpells() {
        // TTMDLG.C:110: case 2 is gmain_play_chapter_intro(arg / 10, arg % 10). C93.TTM's 94 is the
        // epilogue, C94.BOK; C41.TTM's 44..46 are C44..C46.
        Assert.Equal(CutsceneDialogCommand.Kind.OpenBook, CutsceneDialogCommand.KindOf(94, 2));
        Assert.Equal("C94.BOK", CutsceneDialogCommand.BookFor(94));
        Assert.Equal("C23.BOK", CutsceneDialogCommand.BookFor(23));
        // Id 0 with 2 stays a book-page turn step (INTRO.TTM), never a book called C00.
        Assert.Equal(CutsceneDialogCommand.Kind.BookAnimation, CutsceneDialogCommand.KindOf(0, 2));
    }

    [Fact]
    public void ANEGATIVEIdOtherThanMinusOneDoesNothing() {
        // Not observed in the shipped data, but the display arm is guarded by `> 0` rather than
        // `!= 0`, and this pins that rather than leaving it to the next reader to re-derive.
        Assert.Equal(CutsceneDialogCommand.Kind.None, CutsceneDialogCommand.KindOf(-7, 0));
    }

    [Fact]
    public void THECLEARTestBeatsTheBookStepRange() {
        // The order is load-bearing the moment MaxBookStep ever widens: 255 must stay a clear.
        // Asserted through the public constants so that widening the range fails HERE rather than
        // silently turning every clear into a book step.
        Assert.True(CutsceneDialogCommand.ClearArg > CutsceneDialogCommand.MaxBookStep,
            "if the step range ever reaches the clear arg, KindOf's ordering is the only thing "
            + "keeping them apart — and this test is the warning");
        Assert.Equal(CutsceneDialogCommand.Kind.Clear,
            CutsceneDialogCommand.KindOf(0, CutsceneDialogCommand.ClearArg));
    }

    [Fact]
    public void THEDDXKEYTakesTheFULLIdNotTheField() {
        // *** The trap the four inline copies invited. *** Three call sites hold the raw field and
        // must add the base; one already holds a resolved id and must not. Adding it twice lands in
        // DIAL_Z32 and finds nothing.
        int full = CutsceneDialogCommand.DialogIdFor(12);
        Assert.Equal(1600012, full);
        Assert.Equal("DIAL_Z16.DDX", CutsceneDialogCommand.DdxKeyFor(full));
        Assert.NotEqual("DIAL_Z16.DDX",
            CutsceneDialogCommand.DdxKeyFor(CutsceneDialogCommand.DialogIdFor(full)));
    }

    [Theory]
    [InlineData(1600000, "DIAL_Z16.DDX")]
    [InlineData(1699999, "DIAL_Z16.DDX")]
    [InlineData(1700000, "DIAL_Z17.DDX")]
    [InlineData(3000000, "DIAL_Z30.DDX")]
    public void THEKEYIsZeroPaddedToTwoDigits(int dialogId, string expected) =>
        // "DIAL_Z6.DDX" resolves to nothing; the D2 is not cosmetic.
        Assert.Equal(expected, CutsceneDialogCommand.DdxKeyFor(dialogId));

    [Fact]
    public void WAITINGForInputIsNotARange() {
        // *** Not contiguous. *** Both `arg2 != 3` and `arg2 >= 4` look like reasonable
        // simplifications and both are wrong.
        const GameData.Resources.Dialog.DialogEntryFlags none = 0;
        Assert.True(CutsceneDialogCommand.WaitsForInput(0, none));    // narrative, waits
        Assert.False(CutsceneDialogCommand.WaitsForInput(2, none));   // open book
        Assert.False(CutsceneDialogCommand.WaitsForInput(3, none));   // narrative, auto-advances
        Assert.True(CutsceneDialogCommand.WaitsForInput(5, none));    // dialog_play_record
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void MODES1And4WaitOnlyWhenTheRECORDSaysSo(int arg2) {
        // Both are dialog_show_by_key(key, 0) (TTMDLG.C:104-117), which waits only if the record
        // carries 0x20 and not 0x40 (DIALOG.C:673-686). C42's conversation is mode 1 with 0x20 on
        // every line but two: the port treated 1 as "select font", never waited, and ran fifteen
        // lines past in a few seconds.
        Assert.True(CutsceneDialogCommand.WaitsForInput(arg2,
            GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard));
        Assert.False(CutsceneDialogCommand.WaitsForInput(arg2,
            GameData.Resources.Dialog.DialogEntryFlags.SkipWait));
        Assert.False(CutsceneDialogCommand.WaitsForInput(arg2,
            GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard
            | GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer));
    }

    [Fact]
    public void ANUNKNOWNModeDoesNotWait() {
        // A cutscene that stops for a keypress nobody knows to give is unrecoverable; one that
        // advances through an unknown mode merely looks wrong. The safe default is the one that
        // keeps playing.
        Assert.False(CutsceneDialogCommand.WaitsForInput(99, 0));
        Assert.False(CutsceneDialogCommand.WaitsForInput(-1, 0));
    }

    [Fact]
    public void ANarrativeLineWaitsWhateverTheRecordsOwnWaitBitsSay() {
        // TTMDLG.C case 0 draws the record and calls dialog_wait_for_acknowledge(100, 0, 1, 0): the
        // FLAGS argument is 0, so the record's SkipWait/auto-advance bits never apply. C31's lines all
        // carry SkipWait, and honouring it flashed five of them past in a frame.
        var record = new GameData.Resources.Dialog.DialogEntry {
            Flags = GameData.Resources.Dialog.DialogEntryFlags.SkipWait
                | GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer
                | GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard
                | GameData.Resources.Dialog.DialogEntryFlags.ChapterScaledTimer
                | GameData.Resources.Dialog.DialogEntryFlags.FixedStripePattern,
        };

        GameData.Resources.Dialog.DialogEntry shown = CutsceneDialogCommand.NarrativeWaitEntry(record);

        Assert.Equal(GameData.Resources.Dialog.DialogEntryFlags.FixedStripePattern, shown.Flags);
        Assert.True((record.Flags & GameData.Resources.Dialog.DialogEntryFlags.SkipWait) != 0, "the cached record is untouched");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AShowByKeyLineKeepsTheRecordsFlags(int arg2) {
        // dialog_show_by_key waits with the RECORD's flags (DIALOG.C:685), and 0x20 there is what
        // keeps it waiting for the click. Stripping it as mode 0 does timed C42's lines out.
        var record = new GameData.Resources.Dialog.DialogEntry {
            Flags = GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard,
        };
        Assert.Same(record, CutsceneDialogCommand.WaitingEntry(arg2, record));
    }

    [Fact]
    public void ANarrativeLineIsShownWithItsWaitBitsCleared() {
        var record = new GameData.Resources.Dialog.DialogEntry {
            Flags = GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard,
        };
        Assert.Equal((GameData.Resources.Dialog.DialogEntryFlags)0,
            CutsceneDialogCommand.WaitingEntry(0, record).Flags);
    }
}
