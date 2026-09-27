namespace BakAgain.UI.InputCore {
    using Cysharp.Threading.Tasks;

    // Stable static handle to the live input core, set once at container build. Lets the editor
    // MCP (execute_code) and PlayMode tests drive the game through the SAME intents the input
    // adapter produces — no synthetic OS input. This is the agent-drivability seam.
    public static class UiDriver {
        public static IUiCommands Commands { get; set; }
        public static InputLayerStack Stack { get; set; }

        /// <summary>
        /// Sends ONE intent and waits for the screen to react to it.
        /// </summary>
        /// <returns>
        /// Whether a layer took the intent. False means nothing was listening — a different problem
        /// from "it was taken and changed nothing", and the two are worth telling apart.
        /// </returns>
        /// <remarks>
        /// *** ONE CALL IS ONE STEP, AND THAT IS THE WHOLE POINT. *** Intents are consumed at most
        /// once per frame, so a batch of them issued inside a single <c>eval</c> advances a screen
        /// ONCE and silently drops the rest — forty <c>Activate()</c> calls in one call moved a
        /// screen a single step, which reads as "the screen is stuck" rather than as a mistake in
        /// the driving. Folding the frame wait into the call makes that impossible to trip over,
        /// which is why this exists rather than a note telling people to remember.
        ///
        /// <para>It drives <see cref="Stack"/> rather than <see cref="Commands"/> because
        /// <see cref="IUiCommands"/> returns void: the stack already knows whether a layer took the
        /// intent and the command seam throws that away.</para>
        ///
        /// <para><paramref name="frames"/> is how long to let the reaction run. One is enough for a
        /// layer that acts synchronously; a screen that awaits its own work needs more, and passing
        /// a larger number is the honest way to say "this step takes a while" rather than sleeping.
        /// </para>
        /// </remarks>
        public static async UniTask<bool> StepAsync(UiIntent intent, int frames = 1) {
            if (Stack == null) {
                return false;
            }

            bool taken = Stack.DispatchIntent(intent);
            for (var frame = 0; frame < frames; frame++) {
                await UniTask.Yield();
            }

            return taken;
        }

        /// <inheritdoc cref="StepAsync" />
        public static UniTask<bool> ActivateAsync(int frames = 1) =>
            StepAsync(UiIntent.Activate(), frames);

        /// <inheritdoc cref="StepAsync" />
        public static UniTask<bool> CancelAsync(int frames = 1) =>
            StepAsync(UiIntent.Cancel(), frames);

        /// <inheritdoc cref="StepAsync" />
        public static UniTask<bool> MoveFocusAsync(NavDirection direction, int frames = 1) =>
            StepAsync(UiIntent.Move(direction), frames);
    }
}
