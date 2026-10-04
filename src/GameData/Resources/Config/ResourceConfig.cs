namespace GameData.Resources.Config;

using System;

/// <summary>
/// The player's RESOURCE.CFG, read the way <c>parse_krondor_cfg</c> reads it (CFGPARSE.C:46-100).
/// </summary>
/// <remarks>
/// <b>A token scanner, not a key=value parser.</b> The original reads whitespace-separated tokens;
/// on a recognised key it reads two more, the <c>=</c> and the value. Comment lines are just tokens
/// that match nothing. Only the switches the port honours are kept.
/// </remarks>
public sealed class ResourceConfig {
    /// <summary>
    /// The "knockknock" switch, which unlocks the travel screen's CHEAT CENTRAL menu (TASK-692).
    /// Set only when its value is exactly 29 characters long (CFGPARSE.C:71-74).
    /// </summary>
    public bool KnockKnock { get; private set; }

    /// <summary>"bookmarkverify": the bookmark's confirmation prompt, on unless set to 0.</summary>
    public bool BookmarkVerify { get; private set; } = true;

    /// <summary>The value length that unlocks <see cref="KnockKnock"/>.</summary>
    public const int KnockKnockValueLength = 29;

    public static ResourceConfig Parse(string? text) {
        var config = new ResourceConfig();
        if (string.IsNullOrEmpty(text)) {
            return config;
        }
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++) {
            string key = tokens[i];
            if (i + 2 >= tokens.Length) {
                break;
            }
            if (key.Equals("knockknock", StringComparison.OrdinalIgnoreCase)) {
                config.KnockKnock = Clip(tokens[i + 2]).Length == KnockKnockValueLength;
                i += 2;
            } else if (key.Equals("bookmarkverify", StringComparison.OrdinalIgnoreCase)) {
                config.BookmarkVerify = int.TryParse(Clip(tokens[i + 2]), out int v) ? v != 0 : false;
                i += 2;
            }
        }
        return config;
    }

    /// <summary><c>fscanf(" %40s")</c> reads at most 40 characters of a token.</summary>
    private static string Clip(string token) => token.Length > 40 ? token.Substring(0, 40) : token;
}
