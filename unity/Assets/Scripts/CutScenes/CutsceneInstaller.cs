namespace BakAgain.CutScenes {
    using VContainer;
    using VContainer.Unity;

    public class CutsceneInstaller : IInstaller {
        public void Install(IContainerBuilder builder) {
            builder.Register<ResourceCache>(Lifetime.Singleton).As<IResourceCache>();
            builder.Register<CutscenePresenter>(Lifetime.Transient);
        }
    }
}
