#if UNITY_EDITOR
namespace BakAgain.Tests.PlayMode.CutScenes {
    using System.IO;
    using BakAgain.CutScenes;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// One request writes one matched pair — TASK-299 item (c).
    /// </summary>
    /// <remarks>
    /// <b>The pairing is the feature.</b> Grabbing the cutscene buffer and the composited screen in
    /// separate calls gives two different moments, and comparing those has already produced RE
    /// theories explaining a difference that did not exist.
    /// </remarks>
    public class CutsceneFrameCapturePairTests {
        private string _dir;
        private RenderTexture _buffer;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "bak-pair-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
            _buffer = new RenderTexture(8, 8, 0);
            _buffer.Create();
        }

        [TearDown]
        public void TearDown() {
            CutsceneFrameCapture.End();
            if (_buffer != null) {
                _buffer.Release();
                Object.DestroyImmediate(_buffer);
            }

            try {
                Directory.Delete(_dir, recursive: true);
            } catch (IOException) {
                // A leftover temp directory is not a test failure.
            }
        }

        [Test]
        public void NoPairIsPendingUntilOneIsAsked() {
            Assert.IsFalse(CutsceneFrameCapture.PairPending);
        }

        [Test]
        public void ARequestIsConsumedByTheNextFrame_NotRepeatedOnEveryOne() {
            // *** ONE PAIR PER REQUEST. *** Leaving the request armed would write a pair on every
            // cutscene frame — hundreds of files and, worse, a screen grab queued per frame.
            CutsceneFrameCapture.RequestPair(Path.Combine(_dir, "shot"));
            Assert.IsTrue(CutsceneFrameCapture.PairPending);

            CutsceneFrameCapture.Capture(_buffer);

            Assert.IsFalse(CutsceneFrameCapture.PairPending);
            FileAssert.Exists(Path.Combine(_dir, "shot-buffer.png"));
        }

        [Test]
        public void APairIsWrittenEvenWhenPerFrameCaptureIsOff() {
            // The two are independent: asking for one pair must not require arming the frame dump,
            // which is what a comparison against a single original frame actually wants.
            CutsceneFrameCapture.End();
            CutsceneFrameCapture.RequestPair(Path.Combine(_dir, "solo"));

            CutsceneFrameCapture.Capture(_buffer);

            FileAssert.Exists(Path.Combine(_dir, "solo-buffer.png"));
        }

        [Test]
        public void AFrameWithNoBufferLeavesTheRequestArmed() {
            // A null buffer is a frame that drew nothing; consuming the request there would spend it
            // on an empty pair and the caller would wait for files that never arrive.
            CutsceneFrameCapture.RequestPair(Path.Combine(_dir, "later"));

            CutsceneFrameCapture.Capture(null);

            Assert.IsTrue(CutsceneFrameCapture.PairPending);
        }
    }
}
#endif
