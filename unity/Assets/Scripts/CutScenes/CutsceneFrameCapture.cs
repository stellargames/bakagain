#if UNITY_EDITOR
namespace BakAgain.CutScenes {
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// Numbers and optionally dumps every drawn cutscene frame, so a frame of ours can be named the
    /// same way a frame of the original's can.
    ///
    /// <para>This is the Unity half of the emulator-side capture in
    /// <c>BakOverrides.CaptureStorySceneFrame</c>, which hooks <c>seg020:0x06FA</c> — the DOS
    /// animation loop's <c>call j_animationStateMachine</c>, reached only once a frame's
    /// SwapDisplayBuffer and blit have run. The analogue here is
    /// <see cref="CutsceneFrameProcessor.ProcessFrameRuntimeAsync"/> just after its commands and
    /// three render passes: the frame is complete and has not yet been held.</para>
    ///
    /// <para>It captures <see cref="CutsceneState.OutputBuffer"/> rather than the screen, which is
    /// the closer match: the cutscene's own composited output, with no pillarboxing, no 3D scene
    /// behind it and no other canvas on top. Screen grabs pick all of that up, and this session lost
    /// time reading another screen's content as ours.</para>
    ///
    /// <para>Editor-only, and off unless <see cref="OutputDirectory"/> is set:</para>
    /// <code>
    /// CutsceneFrameCapture.Begin("/tmp/ours-c21");
    /// PlayCutsceneState.SceneName = "C21";
    /// DebugStart.Select("PlayCutsceneState");
    /// // ... then CutsceneFrameCapture.FrameNumber is the count, matching the emulator's numbering
    /// </code>
    /// </summary>
    public static class CutsceneFrameCapture {
        /// <summary>Where to write frames. Null or empty disables capture entirely.</summary>
        public static string OutputDirectory;

        /// <summary>Frames drawn since the last <see cref="Begin"/>, i.e. the current frame's number.</summary>
        public static int FrameNumber;

        /// <summary>Arm capture into <paramref name="directory"/> and restart numbering at 1.</summary>
        public static void Begin(string directory) {
            OutputDirectory = directory;
            FrameNumber = 0;
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>Stop capturing. Numbering is left alone so the final count stays readable.</summary>
        public static void End() => OutputDirectory = null;

        /// <summary>Prefix for a one-shot pair, or null when none is armed.</summary>
        private static string _pairPrefix;

        /// <summary>Whether a pair is still waiting for its frame.</summary>
        public static bool PairPending => _pairPrefix != null;

        /// <summary>
        /// Ask for ONE matched pair — the cutscene's own buffer and the composited screen, from the
        /// SAME frame — written as <c>&lt;prefix&gt;-buffer.png</c> and <c>&lt;prefix&gt;-screen.png</c>.
        /// </summary>
        /// <remarks>
        /// <b>The point is the pairing, not the two files.</b> Grabbing the buffer and the screen in
        /// separate calls gives two different moments, and comparing those has already produced RE
        /// theories explaining a difference that did not exist. Arming here writes both inside
        /// <see cref="Capture" />, which the frame processor calls once its commands and three
        /// render passes are done — so the buffer is read and the screen grab is queued within one
        /// frame.
        ///
        /// <para><b>They do not contain the same thing, and that is the reason to have both.</b>
        /// The buffer is the cutscene's own composited output: no pillarboxing, no 3D scene behind
        /// it, no other canvas on top — and <b>no dialogue text</b>, because our dialogs composite on
        /// a separate canvas while the original draws text into its framebuffer. The screen has the
        /// text and everything else, cursor and overlays included. So compare the ORIGINAL's
        /// framebuffer against the screen when text is in the picture, and against the buffer when
        /// judging the cutscene's own rendering.</para>
        ///
        /// <para>Only fires while a cutscene is drawing, since nothing else calls
        /// <see cref="Capture" />. On any other screen the composited view is all there is, and
        /// <c>ScreenCapture</c> is the whole answer.</para>
        /// </remarks>
        public static void RequestPair(string pathPrefix) => _pairPrefix = pathPrefix;

        /// <summary>
        /// Count this frame and, when armed, write it as <c>frame_0001.png</c>. Failures are
        /// swallowed to a log line: a capture that throws must never break the scene it is watching.
        /// </summary>
        public static void Capture(RenderTexture output) {
            FrameNumber++;

            if (_pairPrefix != null && output != null) {
                string prefix = _pairPrefix;
                _pairPrefix = null;   // one pair per request, not one per frame
                WritePng(output, prefix + "-buffer.png");
                // Queued now, written by Unity at this frame's end — the same frame the buffer above
                // was read from, which is the whole point of the pair.
                ScreenCapture.CaptureScreenshot(prefix + "-screen.png");
            }

            if (string.IsNullOrEmpty(OutputDirectory) || output == null) {
                return;
            }

            WritePng(output, Path.Combine(OutputDirectory, $"frame_{FrameNumber:D4}.png"));
        }

        /// <summary>Read a render texture back and write it as a PNG. Never throws at the caller.</summary>
        private static void WritePng(RenderTexture output, string path) {
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try {
                RenderTexture.active = output;
                readback = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0);
                readback.Apply(false);
                File.WriteAllBytes(path, readback.EncodeToPNG());
            } catch (System.Exception e) {
                // A capture that throws must never break the scene it is watching.
                Debug.LogWarning($"CutsceneFrameCapture: {path} not written: {e.Message}");
            } finally {
                RenderTexture.active = previous;
                if (readback != null) {
                    Object.DestroyImmediate(readback);
                }
            }
        }
    }
}
#endif
