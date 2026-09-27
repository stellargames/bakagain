namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using System.Collections.Generic;
    using System.Threading;

    public interface ICutsceneFrameProcessor {
        UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken);

        UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken);

        UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state);

        void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state);

        UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken);
    }
}