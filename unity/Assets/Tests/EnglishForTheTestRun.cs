using BakAgain.ResourceManagement;
using NUnit.Framework;

/// <summary>
/// Every test runs in English, whatever language the developer last chose (TASK-780).
/// </summary>
/// <remarks>
/// The language is a PlayerPref the Editor shares with the batch test process, so a session left in
/// the pseudo language turned 23 text assertions red. In the global namespace so it wraps the whole
/// assembly; tests that need a language set it themselves and restore it.
/// </remarks>
[SetUpFixture]
public class EnglishForTheTestRun {
    private string _saved;

    [OneTimeSetUp]
    public void UseEnglish() {
        _saved = BakResourceSettings.Language;
        BakResourceSettings.Language = "en";
        LanguagePacks.Reload();
    }

    [OneTimeTearDown]
    public void Restore() {
        BakResourceSettings.Language = _saved;
        LanguagePacks.Reload();
    }
}
