namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.CutScenes;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System.Collections;
    using System.Text.RegularExpressions;
    using UnityEngine.AddressableAssets;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// James (actor 30) ships only ACT030A.BMX, yet the dialog asks for his plain face, ACT030.BMP.
    /// The original asks for it too (ASKABOUT.C, askabout_actor_spr_cache_get) and gets no table
    /// back, so no face is drawn and nothing is said. The port logged an InvalidKeyException on
    /// every chapter-2 dialog with James (Linux dev build, 2026-11-08).
    /// </summary>
    public class ActorFaceViewMissingFileTests {
        [UnityTest, RequiresShippedGameData]
        public IEnumerator AFaceFileTheArchiveLacksIsNoPortrait_andNoError() => UniTask.ToCoroutine(async () => {
            await Addressables.InitializeAsync().ToUniTask();
            var cache = new ResourceCache();
            try {
                using ExpectedLog.Capture said = ExpectedLog.CaptureAll(new Regex("ACT030"));
                Assert.IsFalse(await ActorFaceView.ApplyAsync(new VisualElement(), 30, cache),
                    "no ACT030.BMX ships, so there is no face");
                Assert.IsTrue(await ActorFaceView.ApplyAsync(new VisualElement(), 31, cache),
                    "control: ACT031.BMX ships and still loads");
                CollectionAssert.IsEmpty(said.Lines.FindAll(l => !l.Contains("[DBUG]")));
            } finally {
                cache.Dispose();
            }
        });
    }
}
