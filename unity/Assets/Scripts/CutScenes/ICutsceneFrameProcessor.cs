namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using System.Collections.Generic;
    using System.Threading;

    public interface ICutsceneFrameProcessor {
        /// <param name="showDialogs">
        /// Shows the queued dialog requests. Called as soon as a command queues one, so the line is
        /// up before the rest of the frame runs — the original shows it mid-frame and waits
        /// (TTMDLG.C:84-99). Null leaves them for the caller after the frame.
        /// </param>
        UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
            CancellationToken cancellationToken, System.Func<UniTask> showDialogs = null);

        UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken);

        UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state);

        void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state);

        UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken);
    }
}