namespace BakAgain.UI {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Draws an actor's portrait into a caller's element — the picture behind
    /// <c>ShowDialogWithFace</c>, and the same one the character sheet and the temple healer put
    /// beside their stats.
    /// </summary>
    /// <remarks>
    /// <b>Embeddable rather than a screen.</b> Every consumer of a portrait already owns a surface
    /// and redraws it on its own schedule — the healer repaints per pass rather than holding a
    /// display — so this renders into a host element and keeps no place of its own.
    ///
    /// <para><b>It carries no geometry.</b> Where and how big a portrait is belongs to the screen
    /// showing it; this only fills whatever element it is given.</para>
    ///
    /// <para><b>The six-slot cache is deliberately not reproduced.</b> The original holds six
    /// portraits and frees them all at a scene change (<see cref="ActorFaceCache"/>) because it had
    /// to; the resource cache already keeps sprites alive here, and a fixed six-slot table with
    /// last-empty-slot placement would be porting a 1993 memory constraint rather than a behaviour.
    /// What IS behaviour — that high-numbered actors simply have no portrait — is honoured.</para>
    /// </remarks>
    public static class ActorFaceView {
        /// <summary>
        /// Fills <paramref name="host"/> with <paramref name="actorNumber"/>'s portrait.
        /// </summary>
        /// <param name="alternate">
        /// The actor's second face. Both variants share one palette — there is no ACT###A.PAL —
        /// which <c>PaletteMapping</c> resolves.
        /// </param>
        /// <returns>
        /// False when this actor has no portrait, which is an ordinary answer and not a failure:
        /// <c>LoadActorFace</c> nulls the bitmap for actors at or above
        /// <see cref="ActorFaceCache.FirstFacelessActor"/> rather than reporting an error, so a
        /// caller that treats it as one will reject a perfectly ordinary speaker.
        /// </returns>
        /// <param name="sizeToSprite">
        /// Size the host to the portrait's own dimensions. Lets a caller place a face without
        /// knowing how big it is — the shipped portraits are not one size (520x558 and 360x426 both
        /// occur), so a caller that had to state a size would be inventing one.
        /// </param>
        /// <param name="hostPalette">
        /// The palette the screen showing this portrait has installed, e.g. <c>INVENTOR.PAL</c> on
        /// the character sheet. <b>Pass it.</b> An <c>ACT###.PAL</c> defines nothing outside
        /// <see cref="ActorFaceCache.FaceRangeFirst"/>..<c>FaceRangeEnd</c> and the original fills
        /// the rest from the screen's palette, so without a host the portrait's surround renders
        /// from a blank half and comes out as a black rectangle (TASK-363). Null keeps the old
        /// behaviour for a caller that genuinely does not know its screen's palette.
        /// </param>
        public static async UniTask<bool> ApplyAsync(VisualElement host, int actorNumber,
            IResourceCache resources, bool alternate = false, ILogger logger = null,
            bool sizeToSprite = false, string hostPalette = null) {
            if (host == null || resources == null) {
                return false;
            }

            // *** ACTOR 0 IS "NOBODY SPEAKS", NOT ACTOR ZERO. *** The portraits start at ACT001 and
            // there is no ACT000 in the archive, and DIALOG.C renders a record on
            // `speaker != 0 || text != empty` — so a zero here is the absence of a speaker. The
            // faceless rule alone would let it through, since 0 is below the cutoff.
            if (actorNumber <= 0 || !ActorFaceCache.HasFace(actorNumber)) {
                host.style.backgroundImage = StyleKeyword.Null;

                return false;
            }

            string key = GameData.PaletteMapping.WithHostPalette(
                ActorFaceCache.BitmapNameFor(actorNumber, alternate) + SubImageSuffix, hostPalette);
            Sprite portrait = await resources.GetOrLoadAsync<Sprite>(key);
            if (portrait == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                    "ActorFaceView: portrait {Key} did not load; the face is left empty.", key);
                host.style.backgroundImage = StyleKeyword.Null;

                return false;
            }

            // Contain rather than native size: a portrait is drawn into a rect the screen chose,
            // and the eleven shipped faces are not all the same size (520x558 and 360x426 both
            // occur), so a native-size blit would leave some hanging out of their frame.
            host.style.backgroundImage = Background.FromSprite(portrait);
            host.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            host.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            host.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            host.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            if (sizeToSprite) {
                host.style.width = portrait.rect.width;
                host.style.height = portrait.rect.height;
            }

            return true;
        }

        /// <summary>A portrait bitmap holds exactly one image.</summary>
        private const string SubImageSuffix = "#0";
    }
}
