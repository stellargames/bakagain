namespace BakAgain.Core.States {
    /// <summary>Which of a chapter's catalog scenes to play. StartOnly = intro + the first part
    /// (chapter start); Full = intro + every part (Contents replay); EndOnly = the second part alone
    /// (the chapter's close, played when it is finished).</summary>
    public enum ChapterScenesMode {
        StartOnly,
        Full,
        EndOnly,
    }
}
