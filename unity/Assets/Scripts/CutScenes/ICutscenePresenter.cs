namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;
    using System.Collections.Generic;
    using System.Threading;

    public interface ICutscenePresenter {
        UniTask<bool> PlayCutsceneAsync(string cutsceneName, ICutsceneView view, int animationNumber = 0, bool attractMode = false);
        /// <param name="holdUntil">
        /// When cancellable, the final frame is held <b>without taking input</b> until this token is
        /// cancelled — for a caller that owns its own input layer.
        ///
        /// <para>The default hold pushes an Exclusive layer, which is right for a cutscene (whose
        /// only interaction is "skip") and wrong for an interactive location, whose whole point is
        /// that the held picture is clickable: the cutscene layer would swallow every hotspot click.
        /// See <see cref="CutscenePlayer.HoldRenderingWithoutInputAsync"/>.</para>
        /// </param>
        /// <param name="backdrop">
        /// Optional full-screen image laid under everything the scene draws. A location needs one:
        /// the original blits a whole buffer over the working one before every replay, and for a
        /// location that buffer holds <c>DIALOG.SCX</c>, so the picture covers the top and the
        /// dialogue panel shows below it.
        /// </param>
        /// <param name="onHeld">
        /// Raised once the scripts have finished and the last frame is about to be held. Anything
        /// drawn ON that held picture has to wait for it: each script clears the dialog panel as it
        /// ends, so a caller that draws earlier watches its own text vanish.
        /// </param>
        UniTask<bool> PlayCutsceneTagsAsync(string cutsceneName, ICutsceneView view, IReadOnlyList<string> tags,
            bool holdRenderingAfter = false, CancellationToken holdUntil = default,
            string backdrop = null, System.Action onHeld = null);
        void Cancel();
    }
}
