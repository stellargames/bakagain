namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Converters;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Palette;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class LoadPaletteResourceExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadPaletteResourceExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this LoadPaletteResource args) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: Filename={Filename}", args.Filename);

                PaletteResource paletteResource = cutsceneState.Resources.Get<PaletteResource>(args.Filename);

                if (paletteResource == null) {
                    Logger.LogError("Failed to load {ResourceType} with filename {Filename}", nameof(PaletteResource), args.Filename);

                    return;
                }
                cutsceneState.SetPalette(paletteResource.Colors.ToUnity());
            };
        }
    }
}