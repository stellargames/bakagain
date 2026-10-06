namespace BakAgain.UI.InputCore {
    using UnityEngine;
    /// <summary>Device-agnostic analog gameplay input (movement/look/fly). Backed by SystemInputSource's
    /// own InputActions (keyboard + gamepad); consumers inject this and gate it themselves.</summary>
    public interface IGameplayInput {
        Vector2 Move { get; }     // x = strafe/turn, y = forward/back (WASD/arrows/left stick)
        Vector2 Look { get; }     // look delta (mouse delta / right stick)
        bool Run { get; }         // sprint/fast modifier
        float Vertical { get; }   // ascend/descend for fly cameras (+1/-1)
        float Zoom { get; }       // zoom/speed delta (scroll / trigger)
        bool LeftShift { get; }   // held: the original's commands read the two shifts apart
        bool RightShift { get; }  //   (shop paging back vs to-front, combat Shift arms)
        bool Ctrl { get; }        // either Ctrl: key_is_down(0x1d) (combat's Ctrl+Q)
    }
}
