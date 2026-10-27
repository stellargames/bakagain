namespace BetrayalAtKrondor.Tests.Setup;

using System.Collections.Generic;
using GameData.Setup;
using Xunit;

public class SetupFlowTests {
    private static GameFilesReport Report(GameFileState state) {
        var files = new Dictionary<string, GameFileState>();
        foreach (string n in GameFolder.Required) files[n] = state;
        return new GameFilesReport(files);
    }

    private static readonly GameFilesReport Good = Report(GameFileState.Ok);
    private static readonly GameFilesReport Bad = Report(GameFileState.Missing);

    [Fact]
    public void AFreshStartOpensOnWelcome() {
        var f = new SetupFlow();
        f.Start(null);
        Assert.Equal(SetupStep.Welcome, f.Step);
    }

    [Fact]
    public void ABrokenSetupOpensOnCheck() {
        var f = new SetupFlow();
        f.Start(Bad);
        Assert.Equal(SetupStep.Check, f.Step);
        Assert.Same(Bad, f.Report);
        Assert.False(f.CanPlay);
    }

    [Fact]
    public void AnAllOkReportOpensOnCheckAndCanPlay() {
        var f = new SetupFlow();
        f.Start(Good);
        Assert.Equal(SetupStep.Check, f.Step);
        Assert.True(f.CanPlay);
    }

    [Fact]
    public void PlayIsOnlyPossibleWhenAllFilesAreOk() {
        var f = new SetupFlow();
        f.Next();
        Assert.False(f.CanPlay);
        f.Picked("BaK", Bad);
        Assert.False(f.CanPlay);
        f.Picked("BaK", Good);
        Assert.True(f.CanPlay);
        Assert.Equal("BaK", f.SourceName);
    }

    [Fact]
    public void PickedMovesToCheckAndClearsMessageEvenWhenFailing() {
        var f = new SetupFlow();
        f.Next();
        f.PickFailed("denied");
        Assert.Equal(SetupStep.Find, f.Step);
        Assert.Equal("denied", f.Message);
        f.Picked("x", Bad);
        Assert.Equal(SetupStep.Check, f.Step);
        Assert.Null(f.Message);
    }

    [Fact]
    public void CancelKeepsTheStepAndClearsTheMessage() {
        var f = new SetupFlow();
        f.Next();
        f.PickFailed("denied");
        f.Cancelled();
        Assert.Equal(SetupStep.Find, f.Step);
        Assert.Null(f.Message);
    }

    [Fact]
    public void ChooseAgainGoesFromCheckToFindKeepingTheReport() {
        var f = new SetupFlow();
        f.Picked("x", Bad);
        f.ChooseAgain();
        Assert.Equal(SetupStep.Find, f.Step);
        Assert.Null(f.Message);
        Assert.Same(Bad, f.Report);
    }

    [Fact]
    public void BackFromHelpReturnsToTheStepItCameFrom() {
        var f = new SetupFlow();
        f.Next();
        f.OpenHelp();
        Assert.Equal(SetupStep.Help, f.Step);
        f.Back();
        Assert.Equal(SetupStep.Find, f.Step);
    }

    [Fact]
    public void SwitchingBetweenHelpAndStuckStillBacksOutToTheOriginalStep() {
        var f = new SetupFlow();
        f.Next();
        f.OpenHelp();
        f.OpenStuck();
        Assert.Equal(SetupStep.Stuck, f.Step);
        f.OpenHelp();
        f.Back();
        Assert.Equal(SetupStep.Find, f.Step);
    }

    [Fact]
    public void BackOutsideHelpOrStuckDoesNothing() {
        var f = new SetupFlow();
        f.Next();
        f.Back();
        Assert.Equal(SetupStep.Find, f.Step);
    }
}
