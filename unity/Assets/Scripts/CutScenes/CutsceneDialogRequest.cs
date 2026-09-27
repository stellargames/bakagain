namespace BakAgain.CutScenes {
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using UnityEngine;

    /// <summary>
    /// Queued by frame commands (<see cref="AnimationCommands.DialogCommandExtensions"/>)
    /// and drained by <see cref="CutscenePlayer.ProcessDialogRequests"/>. Carries
    /// either a clear signal or a <see cref="DialogEntry"/> plus the active
    /// cutscene palette and whether the dialog should wait for player input.
    /// </summary>
    public class CutsceneDialogRequest {
        /// <summary>The entry to render. Null when <see cref="Clear"/> is set.</summary>
        public DialogEntry Entry { get; set; }

        /// <summary>
        /// Active cutscene palette to push to the dialog manager before the
        /// show call. <see cref="DialogManager.SetActivePalette"/> sets the
        /// palette text and chrome pens resolve against.
        /// </summary>
        public Color[] Palette { get; set; }

        /// <summary>
        /// If true the dialog awaits a mouse/key dismiss (narrative pause); if
        /// false it renders and the cutscene continues immediately (auto-advance
        /// / cutscene-driven pacing).
        /// </summary>
        public bool WaitForInput { get; set; }

        /// <summary>If true, hide any active dialog panel instead of showing one.</summary>
        public bool Clear { get; set; }

        /// <summary>A chapter book to show before the scene goes on (e.g. "C94.BOK"), or null.</summary>
        public string Book { get; set; }
    }
}
