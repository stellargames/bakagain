namespace BakAgain.UI.InputCore {
    // The intent seam. Two producers call it: the device InputAdapter (added later) and the
    // agent/test UiDriver. Decoupling intent from input source is what makes the game drivable
    // without synthetic OS input.
    public interface IUiCommands {
        void MoveFocus(NavDirection direction);
        void Activate();
        void Cancel();
        void Accelerator(char character);
        void Skip(char key = '\0');
        IInputLayer TopLayer { get; }
        bool IsModal { get; }
    }
}
