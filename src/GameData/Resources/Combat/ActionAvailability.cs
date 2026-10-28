namespace GameData.Resources.Combat;

/// <summary>
/// Whether one combat stone can be used this turn, and why not (or why with a warning).
/// </summary>
/// <param name="Enabled">The stone can be pressed. A warned stone is still enabled.</param>
/// <param name="Warned">Pressing works but is not what the player hopes (Rest while sick).</param>
/// <param name="ReasonKey">The <c>UiTemplates</c> key of the reason, empty when there is none.</param>
/// <param name="Reason">The reason, formatted with the hero's name or the condition; empty when none.</param>
public readonly record struct ActionAvailability(
    bool Enabled, bool Warned, string ReasonKey, string Reason = "") {
    public static readonly ActionAvailability On = new(true, false, "");
}
