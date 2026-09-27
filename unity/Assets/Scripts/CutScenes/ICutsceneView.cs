namespace BakAgain.CutScenes {
    using BakAgain.Graphics;
    using BakAgain.UI.Navigation;

    /// <summary>
    /// The cutscene playback surface. An <see cref="IScreen"/> — the scripts that play cutscenes
    /// (GameFlow.Boot's attract loop, the ChapterScenesPlayer, debug scripts) push it onto the
    /// ScreenNavigator for the duration of a sequence; the presenter's own Show/Hide calls within
    /// that lifetime remain direct canvas toggles (content-level, not navigation).
    /// </summary>
    public interface ICutsceneView : IScreen {
        void Show();
        void Hide();
        UiImage Canvas { get; }
    }
}
