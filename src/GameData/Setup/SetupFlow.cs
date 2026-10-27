namespace GameData.Setup;

public enum SetupStep { Welcome, Find, Check, Help, Stuck }

/// <summary>The setup wizard's step model. The view renders whatever this says.</summary>
public sealed class SetupFlow {
    private SetupStep _from = SetupStep.Welcome;

    public SetupStep Step { get; private set; } = SetupStep.Welcome;
    public GameFilesReport? Report { get; private set; }
    /// <summary>Display name of what was picked (folder, zip), never a URI.</summary>
    public string? SourceName { get; private set; }
    /// <summary>Reason or text chosen by the caller; not formatted here.</summary>
    public string? Message { get; private set; }

    public bool CanPlay => Step == SetupStep.Check && Report?.AllOk == true;

    public void Start(GameFilesReport? existing) {
        Report = existing;
        Message = null;
        Step = existing == null ? SetupStep.Welcome : SetupStep.Check;
    }

    public void Next() {
        if (Step == SetupStep.Welcome) Step = SetupStep.Find;
    }

    public void Picked(string sourceName, GameFilesReport report) {
        SourceName = sourceName;
        Report = report;
        Message = null;
        Step = SetupStep.Check;
    }

    public void PickFailed(string message) => Message = message;

    public void Cancelled() => Message = null;

    public void ChooseAgain() {
        if (Step != SetupStep.Check) return;
        Message = null;
        Step = SetupStep.Find;
    }

    public void OpenHelp() => Open(SetupStep.Help);

    public void OpenStuck() => Open(SetupStep.Stuck);

    public void Back() {
        if (Step is SetupStep.Help or SetupStep.Stuck) Step = _from;
    }

    private void Open(SetupStep card) {
        if (Step is not (SetupStep.Help or SetupStep.Stuck)) _from = Step;
        Step = card;
    }
}
