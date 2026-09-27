namespace BakAgain.CutScenes {
    using BakAgain.Audio;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using VContainer;

    public class CutscenePlayerFactory : ICutscenePlayerFactory {
        private readonly IObjectResolver _resolver;
        private readonly IResourceCache _resourceCache;
        private readonly ICutsceneFrameProcessor _frameProcessor;
        private readonly IDialogManager _dialogManager;
        private readonly InputLayerStack _stack;

        public CutscenePlayerFactory(IObjectResolver resolver, IResourceCache resourceCache, ICutsceneFrameProcessor frameProcessor, IDialogManager dialogManager, InputLayerStack stack) {
            _resolver = resolver;
            _resourceCache = resourceCache;
            _frameProcessor = frameProcessor;
            _dialogManager = dialogManager;
            _stack = stack;
        }

        public CutscenePlayer Create(CutsceneState cutsceneState) {
            var midiPlaybackManager = _resolver.Resolve<MidiPlaybackManager>();

            return new CutscenePlayer(cutsceneState, _stack, midiPlaybackManager, _frameProcessor, _dialogManager,
                ShowBookAsync);
        }

        // Resolved per call rather than injected: the book view and navigator sit above the cutscene
        // layer in the container, and ChapterScenesPlayer shows its part books the same way.
        private async Cysharp.Threading.Tasks.UniTask ShowBookAsync(string book) {
            var navigator = _resolver.Resolve<BakAgain.UI.Navigation.IScreenNavigator>();
            var view = _resolver.Resolve<BakAgain.Book.IBookView>();
            await navigator.Push((BakAgain.UI.Navigation.IScreen)view);
            try {
                await _resolver.Resolve<BakAgain.Book.IBookPresenter>().ShowBookAsync(book);
            } finally {
                await navigator.Pop();
            }
        }
    }
}
