namespace BakAgain.UI.Cursor {
    using GameData.Resources.Cursor;
    using UnityEngine;

    /// <summary>No-op <see cref="ICursorManager"/> used until the CursorOverlay prefab is assigned in
    /// <c>RootLifetimeScope</c>. Ignores every call and leaves the OS cursor as-is, so the container
    /// still builds (and Phase-4 consumers still resolve the dependency) while the prefab is pending.</summary>
    public sealed class NullCursorManager : ICursorManager {
        public void SelectSet(string setName) { }
        public void SetByIndex(int index) { }
        public void Set(GameCursor cursor) { }
        public void Hide() { }
        public void Show() { }
        public void WarpTo(Vector2 canonicalPosition) { }
        public Vector2 CanonicalPosition => Vector2.zero;
    }
}
