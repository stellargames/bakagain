namespace BakAgain.UI.InputCore {
    public sealed class UiCommands : IUiCommands {
        private readonly InputLayerStack _stack;

        public UiCommands(InputLayerStack stack) {
            _stack = stack;
        }

        public void MoveFocus(NavDirection direction) => _stack.DispatchIntent(UiIntent.Move(direction));
        public void Activate() => _stack.DispatchIntent(UiIntent.Activate());
        public void Cancel() => _stack.DispatchIntent(UiIntent.Cancel());
        public void Accelerator(char character) => _stack.DispatchIntent(UiIntent.Accelerator(character));
        public void Skip() => _stack.DispatchIntent(UiIntent.Skip());
        public IInputLayer TopLayer => _stack.Top;
        public bool IsModal => _stack.IsModal;
    }
}
