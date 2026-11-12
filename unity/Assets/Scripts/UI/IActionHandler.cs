namespace BakAgain.UI {
    using UnityEngine;

    internal interface IActionHandler {
        void PrimaryAction(int menuEntryActionId);
        Awaitable SecondaryAction(int menuEntryActionId);
    }

    /// <summary>A screen that takes the middle mouse button on its hotspots (the travel screen's
    /// world view: an Enhanced swing, TASK-1072).</summary>
    internal interface IMiddleActionHandler {
        void MiddleAction(int menuEntryActionId);
    }
}