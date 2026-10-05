namespace GameData.Resources.Dialog;

/// <summary>
/// What a party member's portrait is drawn on inside a location — DIALOG.C:978-987.
/// </summary>
/// <remarks>
/// In a location (<c>g_dialog_in_scene</c>) a speaker from the character pool gets the scene
/// script's image slot <see cref="Slot"/> blitted under the face first, and that slot's palette
/// fills the portrait's surround. The location scripts load a plain backdrop there for this
/// (<c>g_bkbar2</c> brick in the TVRN2/4 taverns, <c>g_bkston</c>, <c>g_bkwood</c>, …), so a
/// party member is not painted over the room the NPC stands in. Any other speaker, or a script
/// that left the slot empty, is drawn over the scene as it stands.
/// </remarks>
public static class PartySpeakerBackdrop {
    /// <summary>The script's image and palette slot holding the backdrop.</summary>
    public const int Slot = 5;

    /// <summary><c>CHARACTER_POOL_SIZE</c>: speaker ids 1..6 are the party (GMAIN.H:7).</summary>
    public const int CharacterPoolSize = 6;

    /// <summary>Whether this speaker is drawn on the backdrop (<c>wSpeaker_id &lt;= CHARACTER_POOL_SIZE</c>).</summary>
    public static bool Applies(int speakerActor) => speakerActor >= 1 && speakerActor <= CharacterPoolSize;
}
