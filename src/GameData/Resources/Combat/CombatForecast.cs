namespace GameData.Resources.Combat;

using System.Collections.Generic;

/// <summary>What the enhanced combat HUD can ask to preview.</summary>
public enum ForecastAction { Move, Thrust, Swing, Inspect, Defend, Rest, Retreat, Auto, Pack }

/// <summary>
/// What an action would do, before the player commits to it (enhanced combat HUD).
/// </summary>
/// <param name="HitPips">1..3 from the TRUE hit chance (3 likely, 2 even, 1 unlikely); 0 = not a blow.</param>
/// <param name="HitPercentAbout">The chance, only once the foe's Defense is revealed. The parry roll
/// penalty is a modifier the player cannot know and is left out, hence "about".</param>
/// <param name="DamageAbout">The damage, only once the foe's Defense is revealed; before that the
/// band <see cref="DamageLow"/>..<see cref="DamageHigh"/> is all that is said.</param>
/// <param name="ReasonKey">A <c>UiTemplates</c> key: why not allowed, why warned, or a note; empty if none.</param>
public sealed record CombatForecast(
    ForecastAction Action, int TargetSlot, IReadOnlyList<(int Col, int Row)> Path,
    int HitPips, int? HitPercentAbout, int DamageLow, int DamageHigh, int? DamageAbout,
    int StaminaCost, int HealthCost, int RestGain, bool EndsTurn,
    bool Allowed, bool Warned, string ReasonKey);
