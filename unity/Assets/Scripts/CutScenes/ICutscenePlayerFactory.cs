namespace BakAgain.CutScenes
{
    public interface ICutscenePlayerFactory
    {
        CutscenePlayer Create(CutsceneState cutsceneState);
    }
}
