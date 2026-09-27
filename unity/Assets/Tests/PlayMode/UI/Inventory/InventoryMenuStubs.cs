namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Inert collaborators shared by the <c>InventoryMenu</c> test fixtures. They exist so
    /// <c>InventoryMenu.Construct</c> can be satisfied without dragging real resource loading,
    /// dialogs or navigation into a geometry or gesture test — every method is a no-op that
    /// completes immediately.</summary>
    internal sealed class NoOpResources : IResourceProviderService {
        public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
            UniTask.FromResult<T>(null);

        public void ReleaseAssets(object owner) { }
    }

    internal class NoOpDialogs : BakAgain.UI.IDialogManager {
        public UniTask ShowEntry(GameData.Resources.Dialog.DialogEntry entry,
            System.Threading.CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask DisplayEntry(GameData.Resources.Dialog.DialogEntry entry,
            System.Threading.CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask ShowEntry(GameData.Resources.Dialog.DialogPlay play,
            System.Threading.CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask DisplayEntry(GameData.Resources.Dialog.DialogPlay play,
            System.Threading.CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask<VisualElement> BuildStyledBoxAsync(GameData.Resources.Dialog.DialogEntry entry,
            VisualElement host) => UniTask.FromResult<VisualElement>(null);

        public void ClearDialog() { }

        public void LetClicksThroughPanel() { }

        public void SetActivePalette(Color[] palette) { }

        // virtual so a fixture that needs to observe the call (MenderQuoteTests) can override
        // just this one and keep the rest of the inert surface.
        public virtual UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) =>
            UniTask.FromResult(BakAgain.UI.DialogManager.NoDialogResult);

        public UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
            System.Threading.CancellationToken ct = default) =>
            UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);

        public UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) =>
            UniTask.FromResult(false);

        // -1 = "nothing resolved", which every caller treats as walking away.
        public UniTask<int> ShowChoiceById(int id, System.Threading.CancellationToken ct = default) =>
            UniTask.FromResult(-1);

        public UniTask<int> ShowChoiceIndexById(int id, System.Threading.CancellationToken ct = default) =>
            UniTask.FromResult(-1);
    }

    internal sealed class NoOpNavigator : BakAgain.UI.Navigation.IScreenNavigator {
        public BakAgain.UI.Navigation.IScreen Current => null;

        public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;

        public UniTask Push(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;

        // A bare harness has no stack, so a screen is never "still up" — completing at once keeps
        // a test from hanging on a screen nothing can close.
        public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) =>
            UniTask.CompletedTask;

        public UniTask Pop() => UniTask.CompletedTask;

        public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;

        public UniTask Clear() => UniTask.CompletedTask;
    }
}
