namespace BakAgain.UI.InputCore {
    public enum UiIntentKind { MoveFocus, Activate, Cancel, Accelerator, Skip }

    public readonly struct UiIntent {
        public UiIntentKind Kind { get; }
        public NavDirection Direction { get; }
        public char Character { get; }

        private UiIntent(UiIntentKind kind, NavDirection direction, char character) {
            Kind = kind;
            Direction = direction;
            Character = character;
        }

        public static UiIntent Move(NavDirection direction) => new UiIntent(UiIntentKind.MoveFocus, direction, '\0');
        public static UiIntent Activate() => new UiIntent(UiIntentKind.Activate, default, '\0');
        public static UiIntent Cancel() => new UiIntent(UiIntentKind.Cancel, default, '\0');
        public static UiIntent Accelerator(char character) => new UiIntent(UiIntentKind.Accelerator, default, character);
        /// <summary>A key or click no other intent claims. <paramref name="key"/> names the few keys a
        /// screen loop reads by scancode (Space, ',' and '.'); '\0' for everything else.</summary>
        public static UiIntent Skip(char key = '\0') => new UiIntent(UiIntentKind.Skip, default, key);
    }
}
