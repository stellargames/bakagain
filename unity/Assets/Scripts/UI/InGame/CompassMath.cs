namespace BakAgain.UI.InGame {
    /// <summary>
    /// Pure heading → horizontal scroll mapping for the compass strip. Heading is the
    /// BaK ushort angle (65536 = 360°); the result is the fraction of the strip width
    /// to offset, wrapping at a full turn. The strip's zero-point and direction are
    /// calibrated in-Editor against COMPASS.BMX (see CompassView).
    /// </summary>
    public static class CompassMath {
        public static float ScrollFraction(ushort heading) {
            return heading / 65536f;
        }
    }
}
