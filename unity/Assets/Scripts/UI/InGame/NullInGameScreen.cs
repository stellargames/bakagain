namespace BakAgain.UI.InGame {
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;

    /// <summary>No-op <see cref="IInGameScreen"/> used while the InGameScreen prefab is
    /// pending, so InGameState's dependency still resolves and the world renders to screen.</summary>
    public sealed class NullInGameScreen : IInGameScreen {
        public bool IsVisible => false;
        public void SetWorldCamera(UnityEngine.Camera worldCamera) { }
        public void SetMovement(BakAgain.World.PartyMovement movement) { }

        public void SetWorldLoopSeam(System.Action pump) { }
        public UniTask ShowAsync() => UniTask.CompletedTask;
        public UniTask HideAsync() => UniTask.CompletedTask;
    }
}
