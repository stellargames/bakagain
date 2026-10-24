namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The still the cast screen opens over must look like the frame it was taken from (TASK-887).
    /// </summary>
    /// <remarks>
    /// <c>ScreenCapture.CaptureScreenshotAsTexture</c> hands back sRGB bytes in a texture flagged
    /// linear, so in this linear-colour project UI Toolkit encoded them a second time. Measured
    /// 2026-10-22 on the travel view: sky (40,81,93) on screen came up (110,152,162) on the wipe's
    /// curtains, exactly the sRGB encoding of the real value. The original wipes over the travel view
    /// itself (CSPELL.C:2114-2117 copies the front page).
    /// </remarks>
    [Category("Graphics")] // waits for a rendered frame and reads it back: CI runs these in the job with graphics
    public class CastOpenSnapshotTests {
        [UnityTest]
        public IEnumerator TheOpeningStillIsAnOpaqueSrgbTexture() => UniTask.ToCoroutine(async () => {
            await Awaitable.EndOfFrameAsync();
            Texture2D still = CastScreen.CaptureOpeningStill();
            try {
                Assert.IsTrue(still.isDataSRGB, "the screen holds sRGB values, so the texture must say so");
                Assert.AreEqual(1f, still.GetPixel(still.width / 2, still.height / 2).a, 0.001f,
                    "opaque, or the cover draws as a hole");
            } finally {
                Object.Destroy(still);
            }
        });
    }
}
