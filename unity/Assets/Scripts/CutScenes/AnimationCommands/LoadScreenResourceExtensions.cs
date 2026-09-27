namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Models;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class LoadScreenResourceExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadScreenResourceExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this LoadScreenResource args) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                IndexedTexture backgroundImage = cutsceneState.Resources.Get<IndexedTexture>(args.Filename);

                if (backgroundImage == null) {
                    Logger.LogError("Failed to load {ResourceType} with filename {Filename}", nameof(IndexedTexture), args.Filename);

                    return;
                }

                Drawing.DrawImage(backgroundImage, 0, 0, Orientation.Normal, Vector2.one, cutsceneState);
                cutsceneState.CopyBuffer(cutsceneState.CurrentDrawBufferIndex, cutsceneState.BackgroundBufferIndex);
            };
        }
    }
}