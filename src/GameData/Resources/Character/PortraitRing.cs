namespace GameData.Resources.Character;

using System;

/// <summary>
/// The Enhanced HUD's ring around a portrait: stamina then health, each as its share of the
/// combined pool the camp table also shows (<c>EffectivePool</c> over <c>EffectivePoolMax</c>).
/// </summary>
public static class PortraitRing {
    public static (double Stamina, double Health) Fractions(int stamina, int health, int poolMax) {
        if (poolMax <= 0) {
            return (0, 0);
        }
        double s = Math.Clamp((double)stamina / poolMax, 0, 1);
        double h = Math.Clamp((double)health / poolMax, 0, 1 - s);
        return (s, h);
    }
}
