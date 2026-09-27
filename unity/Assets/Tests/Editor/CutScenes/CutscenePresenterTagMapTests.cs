namespace BakAgain.Tests.Editor.CutScenes {
    using BakAgain.CutScenes;
    using GameData.Resources.Animation;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The tag map is what turns a script's SCENE NUMBER into a frame index. Both of
    /// <see cref="CutscenePresenter"/>'s entry points build it, and until now each had its own copy
    /// of the walk — two copies of the rule that decides where a cutscene starts.
    /// </summary>
    public class CutscenePresenterTagMapTests {
        private static Frame Tagged(int? tag) => new Frame { Tag = tag };

        [Test]
        public void OnlyTaggedFramesAppear() {
            var frames = new List<Frame> { Tagged(null), Tagged(7), Tagged(null), Tagged(9) };

            Dictionary<int, int> map = CutscenePresenter.MapTagsToFrames(frames);

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(1, map[7]);
            Assert.AreEqual(3, map[9]);
        }

        /// <summary>
        /// <b>A repeated tag keeps the LAST frame carrying it</b>, because the walk assigns rather
        /// than adds. No shipped file repeats a tag, so this is a decision about mod-authored data —
        /// pinned so a "first wins" rewrite cannot slip in unnoticed.
        /// </summary>
        [Test]
        public void ARepeatedTagKeepsTheLastFrame() {
            var frames = new List<Frame> { Tagged(4), Tagged(4), Tagged(4) };

            Assert.AreEqual(2, CutscenePresenter.MapTagsToFrames(frames)[4]);
        }

        [Test]
        public void TagZeroIsARealTagAndNotAnAbsentOne() {
            // Tag is int?, so 0 is a value like any other. Treating it as "no tag" would silently
            // drop a scene whose number is 0 — and scene numbers start there.
            Dictionary<int, int> map = CutscenePresenter.MapTagsToFrames(new List<Frame> { Tagged(0) });

            Assert.IsTrue(map.ContainsKey(0));
            Assert.AreEqual(0, map[0]);
        }

        [Test]
        public void NoFramesGivesAnEmptyMapRatherThanNull() {
            Assert.IsNotNull(CutscenePresenter.MapTagsToFrames(new List<Frame>()));
            Assert.IsEmpty(CutscenePresenter.MapTagsToFrames(new List<Frame>()));
        }

        [Test]
        public void ANullFrameListIsAnEmptyMap() =>
            Assert.IsEmpty(CutscenePresenter.MapTagsToFrames(null));
    }
}
