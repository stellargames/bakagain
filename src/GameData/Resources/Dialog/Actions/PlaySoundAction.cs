namespace GameData.Resources.Dialog.Actions;

/// <remarks><b>Deliberately unread</b>: no shipped dialog uses action type 11 (DIALOG.C:1313 plays the sound once); the shipped sound action is type 12, PlayAudio. TASK-1006.</remarks>
public class PlaySoundAction : DialogActionBase {
    public int AudioId { get; set; }
}