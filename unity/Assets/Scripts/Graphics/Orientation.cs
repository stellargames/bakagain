namespace BakAgain.Graphics {
    using System;

    [Flags]
    public enum Orientation {
        Normal = 0,
        FlippedHorizontally = 1,
        FlippedVertically = 2,
        Rotated180 = FlippedHorizontally | FlippedVertically,
    }
}