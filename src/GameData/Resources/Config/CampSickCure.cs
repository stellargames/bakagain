namespace GameData.Resources.Config;

/// <summary>When resting in camp cures Sick (ENCAMP.C:74-77, 150-158; UI_Encamp@0x703d0).</summary>
public static class CampSickCure {
    /// <summary>Hours the camp screen must have been open.</summary>
    public const int Hours = 13;

    public static bool Due(bool pending, long visitStart, long now) =>
        pending && now / EncampDial.TicksPerHour - visitStart / EncampDial.TicksPerHour >= Hours;
}
