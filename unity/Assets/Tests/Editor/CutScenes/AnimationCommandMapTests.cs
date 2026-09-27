namespace BakAgain.Tests.Editor.CutScenes {
    using BakAgain.CutScenes;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Fences the frame-command dispatch table.
    /// </summary>
    /// <remarks>
    /// <b>An unmapped command does not fail — it disappears.</b>
    /// <see cref="AnimationCommandMap.GetAction"/> ends in <c>_ =&gt; null</c>, and
    /// <c>CutsceneFrameProcessor</c> then does <c>.Where(func =&gt; func != null)</c>. So a command
    /// type with no arm is dropped from the frame with no exception and no log: the cutscene plays,
    /// slightly wrong, forever. That is the failure mode this file exists to make loud.
    ///
    /// <para>The test does not demand that every command be mapped — several genuinely have nothing
    /// to do here. It demands that each one be a DECISION: either mapped, or named in
    /// <see cref="DeliberatelyUnmapped"/> with the reason. Adding a command type to GameData and
    /// forgetting the arm goes red.</para>
    /// </remarks>
    public class AnimationCommandMapTests {
        /// <summary>
        /// Command types that correctly have no action, and why.
        /// </summary>
        /// <remarks>
        /// Everything NOT in this set must have an arm. Anything added here is a claim that the
        /// command needs no runtime behaviour — so it wants a reason, not just a name.
        /// </remarks>
        private static readonly Dictionary<string, string> DeliberatelyUnmapped = new() {
            ["DrawImageBase"] =
                "the base of the DrawImage family; every concrete variant has its own arm and "
                + "nothing constructs a bare one",
            ["TagFrame"] =
                "consumed STRUCTURALLY, not executed: the parser lifts it onto Frame.Tag, which "
                + "CutscenePresenter indexes and CutscenePlayer's GotoTag scan searches. An arm "
                + "here would be a second, conflicting reading of the same record",
            ["NextFrame"] =
                "internal to the parser — it terminates a frame rather than doing anything in one",
            ["ObsoleteCommand0500"] = "obsolete in the original; one shipped occurrence",
            ["ObsoleteCommand0510"] = "obsolete in the original; one shipped occurrence",
        };

        // NOTE: the two "instant" screen transitions are NOT listed here. They are genuine no-ops
        // (anim_screenTransitionEffect's A014/A0B5 arm returns without copying), but they are
        // MAPPED to an explicit no-op action rather than exempted — see
        // ScreenTransitionInstantExtensions for why "verified to do nothing" should not look
        // identical to "not implemented".


        private static IEnumerable<Type> ConcreteCommandTypes() =>
            typeof(FrameCommand).Assembly.GetTypes()
                .Where(t => typeof(FrameCommand).IsAssignableFrom(t))
                .Where(t => t is { IsAbstract: false, IsInterface: false })
                .Where(t => t != typeof(FrameCommand))
                .OrderBy(t => t.Name);

        private static FrameCommand Construct(Type t) =>
            (FrameCommand)Activator.CreateInstance(t, nonPublic: true);

        /// <summary>
        /// Commands that are NOT implemented yet — a gap, not a decision.
        /// </summary>
        /// <remarks>
        /// Kept separate from <see cref="DeliberatelyUnmapped"/> on purpose: exempting these as
        /// "deliberate" would be false, and would bury six real behaviours the original performs.
        /// Every one occurs in shipped TTM data (counts below), so each is something a cutscene
        /// asks for and does not get. Tracked as TASK-178.
        /// </remarks>
        private static readonly HashSet<string> KnownGaps = new();

        /// <summary>
        /// The unmapped set is pinned EXACTLY, so it can move in neither direction unnoticed.
        /// </summary>
        /// <remarks>
        /// A new command type with no arm goes red because it is not in the list. Implementing one
        /// of the gaps ALSO goes red, because the list is then stale — which is the prompt to
        /// delete the entry rather than leave the file claiming a gap that has been filled.
        /// </remarks>
        [Test]
        public void TheUnmappedCommandsAreExactlyTheKnownGaps() {
            List<string> unmapped = ConcreteCommandTypes()
                .Where(t => !DeliberatelyUnmapped.ContainsKey(t.Name))
                .Where(t => AnimationCommandMap.GetAction(Construct(t)) == null)
                .Select(t => t.Name)
                .OrderBy(name => name)
                .ToList();

            CollectionAssert.AreEqual(KnownGaps.OrderBy(name => name).ToList(), unmapped,
                "the set of frame commands with no arm has changed. A NEW one is dropped silently "
                + "by the frame processor — give it an arm, or add it to DeliberatelyUnmapped with "
                + "a reason. A MISSING one means a gap was filled: delete it from KnownGaps.");
        }

        [Test]
        public void TheDeliberatelyUnmappedListDoesNotGoStale() {
            // The other direction: if one of these gains an arm, the entry is now a lie and should
            // be deleted rather than left contradicting the code.
            var nowMapped = DeliberatelyUnmapped.Keys.Concat(KnownGaps)
                .Select(name => ConcreteCommandTypes().FirstOrDefault(t => t.Name == name))
                .Where(t => t != null)
                .Where(t => AnimationCommandMap.GetAction(Construct(t)) != null)
                .Select(t => t.Name)
                .ToList();

            CollectionAssert.IsEmpty(nowMapped,
                "these are listed as unmapped but now have an arm — remove them from "
                + "DeliberatelyUnmapped / KnownGaps");
        }

        [Test]
        public void EveryNamedExemptionStillExists() {
            // And a third direction: a renamed or deleted command type would leave a dead entry
            // exempting nothing, quietly weakening the first test.
            var missing = DeliberatelyUnmapped.Keys.Concat(KnownGaps)
                .Where(name => ConcreteCommandTypes().All(t => t.Name != name))
                .ToList();

            CollectionAssert.IsEmpty(missing,
                "DeliberatelyUnmapped / KnownGaps names a command type that no longer exists");
        }

        [Test]
        public void EveryExemptionCarriesAReason() {
            foreach (KeyValuePair<string, string> entry in DeliberatelyUnmapped) {
                Assert.IsNotEmpty(entry.Value, entry.Key + " is exempted with no reason given");
            }
        }

        [Test]
        public void AnUnknownCommandYieldsNoAction() =>
            // The fallback itself, pinned: it must answer null rather than throw, because the
            // processor's filter is what turns that into "skip this command".
            Assert.IsNull(AnimationCommandMap.GetAction(new UnknownCommand()));

        private sealed class UnknownCommand : FrameCommand {
        }
    }
}
