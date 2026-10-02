namespace BakAgain.Tests.CutScenes {
    using BakAgain.CutScenes;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Characterisation tests for the ADS script interpreter — the conditional logic that decides
    /// which animations an .ADS actually plays.
    ///
    /// <para>Written because the cutscene engine had <b>no tests at all</b> despite being the most
    /// complex subsystem in the project, and because TASK-132 wants this exact logic extracted so a
    /// GDS location scene can share it. A refactor of untested branchy code is a coin flip; these
    /// pin the behaviour first.</para>
    ///
    /// <para>They describe what the code <i>does</i>, not what it ought to do. Where that is
    /// surprising it is called out rather than smoothed over.</para>
    /// </summary>
    public class ScriptProcessorTests {
        private static List<(CutsceneAction Action, int Scene)> Run(
            string script, int chapter = 1, params int[] played) =>
            ScriptProcessor.Process(script, chapter, new HashSet<int>(played))
                .Select(c => (c.Item1, c.Item2))
                .ToList();

        [Test]
        public void ContinuePlaysTheSceneJustAsStartDoes() {
            // 0x2000 is anim_script_object_restart (ANIMSCR.C:649): rewind to the scene's start and
            // arm it; 0x2005 arms without rewinding. From a fresh state both play the scene from its
            // first frame. g_town's LAMUT-MAIN is the one shipped script that uses it, and skipping
            // it left LaMut's picture blank (TASK-717).
            Assert.IsTrue(ScriptProcessor.Plays(CutsceneAction.Start));
            Assert.IsTrue(ScriptProcessor.Plays(CutsceneAction.Continue));
            Assert.IsFalse(ScriptProcessor.Plays(CutsceneAction.Stop));
        }

        [Test]
        public void ASceneTheScriptStopsIsABackgroundLoop() {
            // C31's CHAP3SCENE1 starts scene 3 (the fire, which falls into scene 4's GotoFrame loop)
            // alongside scene 1, and scene 2's block STOPs it. The original runs it beside scene 1;
            // a player that runs scenes one at a time must not wait on it (TASK-718).
            const string c31 = "IF NOT PLAYED scene_1\n    START scene_1\n    START scene_3\nEND IF\n"
                + "IF PLAYED scene_1 AND IF NOT PLAYED scene_2\n    STOP scene_3\n    START scene_2\nEND IF\n";
            CollectionAssert.AreEquivalent(new[] { 3 }, ScriptProcessor.StoppedScenes(c31));
            CollectionAssert.IsEmpty(ScriptProcessor.StoppedScenes("IF NOT PLAYED scene_2\n    START scene_14\n    START scene_2\nEND IF\n"));
        }

        [Test]
        public void AClickBetweenDialogsDoesNotEndTheScene() {
            // The original's cutscene loop (GMAIN.C:355-366) reads no input while a scene plays;
            // only the dialogs consume clicks. Ending the scene on Activate threw away every line
            // still to come — C31's scene 2 lost "They are here." and James's two closing lines.
            Assert.IsFalse(CutscenePlayer.ActivateEndsTheScene(attractMode: false));
            Assert.IsTrue(CutscenePlayer.ActivateEndsTheScene(attractMode: true), "the intro still exits on any input");
        }

        [Test]
        public void AnEmptyScriptYieldsNothing() {
            Assert.IsEmpty(Run(string.Empty));
            Assert.IsEmpty(Run(null));
        }

        [Test]
        public void ThePlayedSceneSetIsRequired() {
            Assert.Throws<System.ArgumentNullException>(
                () => ScriptProcessor.Process("START scene_1", 1, null).ToList());
        }

        [Test]
        public void TheThreeSceneCommandsAreRecognised() {
            List<(CutsceneAction Action, int Scene)> commands =
                Run("START scene_1\nCONTINUE scene_2\nSTOP scene_3");

            Assert.AreEqual(
                new[] { (CutsceneAction.Start, 1), (CutsceneAction.Continue, 2), (CutsceneAction.Stop, 3) },
                commands.ToArray());
        }

        [Test]
        public void UnrecognisedLinesAreIgnored() {
            Assert.IsEmpty(Run("PLAY sound_5\n\n   \nWIBBLE"));
        }

        [Test]
        public void AMalformedSceneNumberIsNotACommand() {
            // ParseSceneNumber throws FormatException, which TryProcessSceneCommand swallows.
            Assert.IsEmpty(Run("START scene_notanumber"));
        }

        // ---- conditions ----------------------------------------------------------------------

        [Test]
        public void ChapterAtLeastGatesOnTheCurrentChapter() {
            Assert.IsEmpty(Run("IF CHAPTER >= 5\nSTART scene_1\nEND IF", chapter: 4));
            Assert.AreEqual(1, Run("IF CHAPTER >= 5\nSTART scene_1\nEND IF", chapter: 5).Count);
            Assert.AreEqual(1, Run("IF CHAPTER >= 5\nSTART scene_1\nEND IF", chapter: 9).Count);
        }

        [Test]
        public void ChapterAtMostGatesTheOtherWay() {
            Assert.AreEqual(1, Run("IF CHAPTER <= 3\nSTART scene_1\nEND IF", chapter: 3).Count);
            Assert.IsEmpty(Run("IF CHAPTER <= 3\nSTART scene_1\nEND IF", chapter: 4));
        }

        [Test]
        public void PlayedAndNotPlayedReadTheSuppliedSet() {
            const string script = "IF PLAYED scene_7\nSTART scene_1\nEND IF";
            Assert.IsEmpty(Run(script));
            Assert.AreEqual(1, Run(script, 1, 7).Count);

            const string inverse = "IF NOT PLAYED scene_7\nSTART scene_1\nEND IF";
            Assert.AreEqual(1, Run(inverse).Count);
            Assert.IsEmpty(Run(inverse, 1, 7));
        }

        [Test]
        public void AnUnknownConditionIsFalseSoItsBlockIsSkipped() {
            // EvaluateSingleCondition falls through to false rather than throwing, so a condition
            // the interpreter does not understand silently hides its whole block.
            Assert.IsEmpty(Run("IF WEATHER IS RAIN\nSTART scene_1\nEND IF"));
        }

        [Test]
        public void ConditionsCombineAcrossAnAndLine() {
            const string script = "IF CHAPTER >= 2\nAND\nIF NOT PLAYED scene_9\nSTART scene_1\nEND IF";

            Assert.AreEqual(1, Run(script, chapter: 2).Count);
            Assert.IsEmpty(Run(script, chapter: 1));           // first condition fails
            Assert.IsEmpty(Run(script, 2, 9));                 // second fails
        }

        [Test]
        public void ConditionsAlsoCombineInlineOnOneLine() {
            const string script = "IF CHAPTER >= 2 AND IF CHAPTER <= 4\nSTART scene_1\nEND IF";

            Assert.AreEqual(1, Run(script, chapter: 3).Count);
            Assert.IsEmpty(Run(script, chapter: 1));
            Assert.IsEmpty(Run(script, chapter: 5));
        }

        // ---- blocks --------------------------------------------------------------------------

        [Test]
        public void ElseRunsOnlyWhenTheIfDidNot() {
            const string script = "IF CHAPTER >= 5\nSTART scene_1\nELSE\nSTART scene_2\nEND IF";

            Assert.AreEqual(new[] { (CutsceneAction.Start, 1) }, Run(script, chapter: 5).ToArray());
            Assert.AreEqual(new[] { (CutsceneAction.Start, 2) }, Run(script, chapter: 4).ToArray());
        }

        [Test]
        public void CommandsAfterEndIfRunRegardless() {
            const string script = "IF CHAPTER >= 9\nSTART scene_1\nEND IF\nSTART scene_2";

            Assert.AreEqual(new[] { (CutsceneAction.Start, 2) }, Run(script, chapter: 1).ToArray());
            Assert.AreEqual(new[] { (CutsceneAction.Start, 1), (CutsceneAction.Start, 2) },
                Run(script, chapter: 9).ToArray());
        }

        [Test]
        public void ANestedBlockInsideASkippedOneStaysSkipped() {
            const string script =
                "IF CHAPTER >= 9\n" +
                "  IF CHAPTER >= 1\n" +
                "    START scene_1\n" +
                "  END IF\n" +
                "END IF";

            // The inner condition is true, but the outer block is skipped, so nothing runs.
            Assert.IsEmpty(Run(script, chapter: 1));
            Assert.AreEqual(1, Run(script, chapter: 9).Count);
        }

        [Test]
        public void ANestedElseInsideASkippedBlockAlsoStaysSkipped() {
            const string script =
                "IF CHAPTER >= 9\n" +
                "  IF CHAPTER >= 9\n" +
                "    START scene_1\n" +
                "  ELSE\n" +
                "    START scene_2\n" +
                "  END IF\n" +
                "END IF";

            // Chapter 1 skips the outer block; the inner ELSE must not leak out of it.
            Assert.IsEmpty(Run(script, chapter: 1));
        }

        [Test]
        public void SiblingBlocksAreEvaluatedIndependently() {
            const string script =
                "IF CHAPTER >= 9\nSTART scene_1\nEND IF\n" +
                "IF CHAPTER >= 1\nSTART scene_2\nEND IF";

            Assert.AreEqual(new[] { (CutsceneAction.Start, 2) }, Run(script, chapter: 1).ToArray());
        }

        [Test]
        public void LeadingWhitespaceAndIndentationAreIgnored() {
            Assert.AreEqual(1, Run("   IF CHAPTER >= 1\n\t\tSTART scene_1\n   END IF").Count);
        }

        [Test]
        public void TheResultIsLazySoTheSetIsReadWhenEnumerated() {
            // Process is an iterator; nothing is evaluated until enumeration. Worth knowing before
            // anyone mutates playedScenes between building and consuming the sequence.
            var played = new HashSet<int>();
            IEnumerable<(CutsceneAction, int)> pending =
                ScriptProcessor.Process("IF PLAYED scene_3\nSTART scene_1\nEND IF", 1, played);

            played.Add(3);

            Assert.AreEqual(1, pending.Count());
        }
    }
}
