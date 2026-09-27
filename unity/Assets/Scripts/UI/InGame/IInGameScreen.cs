namespace BakAgain.UI.InGame {
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// The unified in-game/travel screen: FRAME.SCR chrome + the 3D world in the
    /// REQ_MAIN viewport (hotspot_192) + compass + party heads + REQ_MAIN buttons, on a
    /// single UIDocument. An <see cref="IScreen"/> — InGameState makes it the navigator
    /// stack's root (ResetTo) after building the zone; the in-game menu and loot screen
    /// are pushed over it. A NullInGameScreen keeps it resolvable while the prefab is pending.
    /// </summary>
    public interface IInGameScreen : IScreen {
        bool IsVisible { get; }

        /// <summary>Provide the camera that renders the 3D world into the viewport. Call before showing.</summary>
        void SetWorldCamera(UnityEngine.Camera worldCamera);

        /// <summary>Provide the movement controller; nav buttons/keys drive it. Call before showing.</summary>
        void SetMovement(BakAgain.World.PartyMovement movement);

        /// <summary>
        /// Work the world loop owes once per iteration, run by the screen's own frame update.
        /// </summary>
        /// <remarks>
        /// <b>This screen's update IS the world loop</b> — it is already where the ambient-sound
        /// driver and the pit drop are ticked, "where the original ticks it". Flow hands in the jobs
        /// it needs noticed there rather than the screen learning about them: the first is the
        /// chapter transition, which a dialog requests by writing a flag and the original's
        /// <c>MainGameLoop</c> acts on at the top of its next pass.
        ///
        /// <para><b>A flag, not an event, and deliberately.</b> Raising the transition the moment the
        /// dialog writes the flag runs it INSIDE that dialog — and <c>GameFlow.RunExclusive</c> drops
        /// a call while it is busy, so the transition would either vanish or tear the world down
        /// under a live dialog. Noticing it a frame later is both simpler and what the original
        /// does.</para>
        /// </remarks>
        void SetWorldLoopSeam(System.Action pump);
    }
}
