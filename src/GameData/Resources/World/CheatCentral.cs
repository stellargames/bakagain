namespace GameData.Resources.World;

/// <summary>
/// The two hidden "-> CHEAT CENTRAL <-" menus of the shipped game (TASK-692; owner: port them,
/// 2026-10-04).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Knock-knock</b>, <c>townscene_cheat_menu_screen</c> (TOWNSCN.C:684-750), REQ_KNOC:
///   raised from the travel screen by Right Shift + Alt + key 0x29 (the ` key), only when RESOURCE.CFG
///   carries the knockknock switch (WORLDLP.C:379-384).</item>
///   <item><b>Chest</b>, <c>townscene_chest_open_with_cipher</c> (TOWNSCN.C:752-820), REQ_CHET:
///   raised from the map screen by Right Shift + Alt and HOLDING ` (MAP.C:423-433), behind the
///   chapter's cipher puzzle.</item>
/// </list>
/// Both draw "-> CHEAT CENTRAL &lt;-" and "Enjoy with caution..." over DIALOG.SCX and leave on action 1.
/// </remarks>
public static class CheatCentral {
    public enum Effect {
        None,
        AddGold,
        OpenItemChest,
        PlayLine,
        SkipChapter,
        HealParty,
        LearnAllSpells,
        UnlockAllTeleports,
        Leave,
    }

    public const string Title = "-> CHEAT CENTRAL <-";
    public const string Subtitle = "Enjoy with caution...";

    /// <summary>
    /// Both captions, in canonical (square, 1600x1200) units: the original centres them at VGA
    /// x 0xa0 with tops at y 0x14 and 0x23, and draws the shadow one VGA pixel down-right.
    /// </summary>
    public const int CaptionCentreX = 0xa0 * 5;
    public const int TitleY = 0x14 * 6;
    public const int SubtitleY = 0x23 * 6;
    public const int CaptionShadowX = 5;
    public const int CaptionShadowY = 6;

    public const string KnockKnockReq = "REQ_KNOC.DAT";
    public const string ChestReq = "REQ_CHET.DAT";
    public const string Backdrop = "DIALOG.SCX";

    public const int LeaveActionId = 1;

    /// <summary>Case 0x81 adds this to the purse, in its own units (royals).</summary>
    public const int GoldBonus = 5000;

    /// <summary>The knock-knock chest: the fixed object at zone 0, (50, 0), opened as chapter 9.</summary>
    public const int KnockKnockChestX = 50;
    public const int KnockKnockChestChapter = 9;

    /// <summary>The cipher chest: the fixed object at zone 0, (0x3c, chapter).</summary>
    public const int ChestX = 0x3c;

    /// <summary>0x249f1b: the knock-knock menu's dialog (case 0x83).</summary>
    public const int KnockKnockLine = 0x249f1b;
    /// <summary>The cipher chest's prompt, success, failure and chapter-skip records.</summary>
    public const int ChestPrompt = 0x249f1c;
    public const int ChestOpened = 0x249f1d;
    public const int ChestFailed = 0x249f1e;
    public const int ChestSkipConfirm = 0x249f1f;

    /// <summary>TOWN_VISITED(i), i &lt; 15 (GSTATE.H:79): the flags the teleport list reads ("All: Teleports").</summary>
    public const int TownVisitedBase = 6480;
    public const int TownCount = 15;

    /// <summary>
    /// How long the map screen wants ` held. The original spins 500,000 polls (MAP.C:426), a time
    /// that depends on the CPU; ponytail: one second stands in, measure a 486 if it matters.
    /// </summary>
    public const float ChestHoldSeconds = 1f;

    /// <summary>The scancode both entries test with the modifiers: the ` key.</summary>
    public const int TriggerScanCode = 0x29;

    public static Effect KnockKnockEffect(int actionId) => actionId switch {
        0x81 => Effect.AddGold,
        0x82 => Effect.OpenItemChest,
        0x83 => Effect.PlayLine,
        0x84 => Effect.SkipChapter,
        0x85 => Effect.HealParty,
        0x86 => Effect.LearnAllSpells,
        0x87 => Effect.UnlockAllTeleports,
        LeaveActionId => Effect.Leave,
        _ => Effect.None,
    };

    /// <summary>
    /// The cipher chest's menu. Its chapter skip (0x31) acts only with Right Shift and Alt held and
    /// neither Left Shift nor Ctrl (TOWNSCN.C:794-801), and asks first (0x249f1f).
    /// </summary>
    public static Effect ChestEffect(int actionId, bool rightShift, bool alt, bool leftShift, bool ctrl) =>
        actionId switch {
            0x81 => Effect.HealParty,
            0x82 => Effect.OpenItemChest,
            0x31 => rightShift && alt && !leftShift && !ctrl ? Effect.SkipChapter : Effect.None,
            LeaveActionId => Effect.Leave,
            _ => Effect.None,
        };

    /// <summary>The modifier test both entry points make: Right Shift and Alt, not Left Shift or Ctrl.</summary>
    public static bool ModifiersMatch(bool rightShift, bool alt, bool leftShift, bool ctrl) =>
        rightShift && alt && !leftShift && !ctrl;

    /// <summary>"All spells" sets each of the three 16-bit masks to 0xffff (TOWNSCN.C:735-739).</summary>
    public const ushort AllSpellsMask = 0xffff;
}
