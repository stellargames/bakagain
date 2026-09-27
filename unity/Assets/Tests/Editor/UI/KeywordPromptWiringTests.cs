namespace BakAgain.Tests.Editor.UI {
    using GameData.Resources.Dialog;
    using NUnit.Framework;

    /// <summary>
    /// The ask-about prompt reaching the speech bubble — TASK-257's last piece.
    /// </summary>
    /// <remarks>
    /// <b>The prompt is the NAME PLATE with a longer string, not a heading of its own.</b>
    /// <c>ShowKeywordDialog</c> concatenates "&lt;name&gt; asked about:" and hands it to
    /// <c>dialog_draw_speech_bubble</c> — the same routine that draws an ordinary speaker name.
    /// A port looking for somewhere above the grid to write a heading finds nothing, which is why
    /// this sat unwired after the rest of the keyword flow was built.
    /// </remarks>
    public class KeywordPromptWiringTests {
        [Test]
        public void ThePromptIsTheNamePlusASuffix_ByConcatenationOnly() {
            // No placeholder substitution and no punctuation beyond the suffix, whose leading space
            // is part of it.
            Assert.AreEqual("Locklear asked about:", KeywordPrompt.PromptFor("Locklear"));
            Assert.AreEqual(" asked about:", KeywordPrompt.PromptSuffix);
        }

        [Test]
        public void NINETEENOfTheTwentyOneShippedGridsCarry255_WhichIsASENTINEL() {
            // *** THIS TEST USED TO ASSERT THE BUG. *** It was called
            // "…GetNoPlate_AndThatIsFAITHFUL" and read the absence of a heading as correct, on the
            // grounds below. The grounds are true and the conclusion was not: the name lookup does
            // answer nothing for 255 —
            Assert.IsFalse(DialogSpeakerNamePill.ResolvesToAName(255));

            // — but it is NEVER CALLED WITH 255. ExecuteDialog substitutes the running party
            // speaker at the top of its record loop (DIALOG.C:885), so every one of the nineteen
            // reaches the lookup as a party id and gets its portrait and its heading. The player
            // sees an unnamed, faceless page only in the port.
            var speakers = new DialogSpeakerSentinel();
            speakers.Begin(chapterSpeaker: 2);
            int resolved = speakers.Resolve(255, chapterSpeaker: 2);
            Assert.IsTrue(DialogSpeakerNamePill.ResolvesToAName(resolved));
            Assert.IsTrue(DialogSpeakerNamePill.IsPartySpeaker(resolved));

            // The two grids that name a party member outright resolve to themselves.
            Assert.IsTrue(DialogSpeakerNamePill.IsPartySpeaker(2));
            Assert.IsTrue(DialogSpeakerNamePill.IsPartySpeaker(3));
        }

        [Test]
        public void AnEmptyGridShowsNoPromptAtAll() {
            // The original builds the grid first and gives up if there is nothing to ask about, so
            // an NPC with no available topics shows no "asked about:" line rather than an empty box
            // under a heading.
            Assert.IsFalse(KeywordPrompt.Appears(0));
            Assert.IsTrue(KeywordPrompt.Appears(1));
        }
    }
}
