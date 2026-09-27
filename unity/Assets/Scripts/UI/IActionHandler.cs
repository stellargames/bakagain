namespace BakAgain.UI {
    using UnityEngine;

    internal interface IActionHandler {
        void PrimaryAction(int menuEntryActionId);
        Awaitable SecondaryAction(int menuEntryActionId);
    }
}