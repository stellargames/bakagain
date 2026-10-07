namespace GameData.Resources.Dialog;

/// <summary>
/// Putting the topic grid on screen and reading the answer — IDA <c>ShowKeywordDialog</c>
/// (ovr144 @0x4b0fd). The last step of the keyword flow.
/// </summary>
/// <remarks>
/// <b>A chosen topic jumps straight to its branch's target — it does not latch a flag.</b> This is
/// where the keyword path and the choice path genuinely diverge: a choice menu writes
/// <see cref="DialogChoiceMenu.ValueWrittenForChoice"/> to the branch's key and lets the
/// conditional branch loop discover it, while a keyword reads the target dialog id straight out of
/// the branch record and goes there. The only flag the keyword path writes is the asked-about flag
/// (<see cref="KeywordMenu.AskedFlag"/>), which greys the topic out next time.
///
/// <para>The grid runs its own input loop, and the farewell's action id is 1 because <b>1 is the
/// dismiss code</b> (<see cref="DialogChoiceMenu.DismissedResult"/>): choosing the farewell and
/// dismissing the prompt are the same path, and the conversation ends.</para>
/// </remarks>
public static class KeywordPrompt {
    /// <summary>
    /// The line above the grid.
    /// </summary>
    /// <remarks>
    /// The original concatenates the name and the EXE's " asked about:", so the name goes in
    /// verbatim. The port says the same through a template (<see cref="Text.UiTemplates.AskedAbout"/>,
    /// TASK-776), whose English is exactly that concatenation and which a translation can reorder.
    /// </remarks>
    public static string PromptFor(string speakerName) =>
        Text.UiTemplates.Format(Text.UiTemplates.AskedAbout, ("name", speakerName));
}
