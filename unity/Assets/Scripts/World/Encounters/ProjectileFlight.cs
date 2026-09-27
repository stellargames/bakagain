namespace BakAgain.World.Encounters {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Combat;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Flies an effect sprite from an attacker to its target, then removes it.
    /// </summary>
    /// <remarks>
    /// <b>The picture is the whole animation.</b> Three of the four effect entries in COMBAT.TBL —
    /// <c>spell</c>, <c>rock</c>, <c>spell5</c> — carry a single sprite face, and the fourth,
    /// <c>jack</c>, is a small flat-shaded solid. Either way there is no run of frames to step
    /// through and no octant to pick: what moves is the object.
    ///
    /// <para><b>It is a straight line between two placed sprites.</b> The arena has already drawn
    /// both ends by the time this runs, so the two transforms ARE the endpoints and no coordinate
    /// conversion is repeated here.</para>
    ///
    /// <para><b>It now reports where it passes</b>, which TASK-270's crystal rule asked for. The
    /// report is a world POSITION per step, not a cell: this class has no business knowing the
    /// arena grid, and the consumer already owns the position-to-cell conversion the ground click
    /// uses. The original looks the cell up per step rather than walking a Bresenham line, which is
    /// why this samples the lerp instead of rasterising it.</para>
    ///
    /// <para><b>Interception is still not built.</b> Stopping a shot on whoever stands in the way
    /// is a change to what combat DOES, not to what it draws, and it belongs with the actor rules
    /// rather than being smuggled in behind a crystal fix. The observer returns void for that
    /// reason — nothing here can halt the flight.</para>
    ///
    /// <para><b>One projectile ARCS, and it is the thrown rock.</b> The original's flight routine
    /// carries a vertical delta only for <c>action_id 0x14</c>, and when that delta puts the rock on
    /// the ground it inverts and the rock SKIPS onward, sounding a cue each time.
    /// <see cref="ThrownRockFlight"/> owns the arithmetic and the model rolls the launch delta, so
    /// this class only walks the sequence it is handed and says when a skip happened.</para>
    ///
    /// <para><b>The duration is chosen, not measured.</b> The original's flight timing has not been
    /// read out of the binary; <see cref="Seconds"/> is a tuning knob set to something that reads as
    /// a shot rather than a teleport, and it is deliberately independent of distance so a
    /// point-blank shot is still visible.</para>
    /// </remarks>
    public sealed class ProjectileFlight : MonoBehaviour {
        /// <summary>How long the whole flight takes.</summary>
        public const float Seconds = 0.35f;

        /// <summary>Fraction of the flight completed, 0 before it starts and 1 when it lands.</summary>
        /// <remarks>
        /// Observable for the reason <c>DeathCollapse.CurrentFrame</c> is: "the component was
        /// attached" is not evidence that anything moved.
        /// </remarks>
        public float Progress { get; private set; }

        /// <summary>Fly from <paramref name="from"/> to <paramref name="to"/> in the parent's space,
        /// destroying the sprite on arrival.</summary>
        /// <param name="passedOver">
        /// Called with the sprite's world position on every step of the flight, for a consumer that
        /// cares what it flew over. Null when nobody is asking, which is every shot that is not a
        /// Flamecast over a trap puzzle.
        /// </param>
        /// <param name="arc">
        /// The rock's height sequence, or null for a flat flight. Its length is the original's step
        /// count, so the flight samples it by progress rather than by frame — our frame rate is not
        /// the original's step rate and tying them would change the arc with the display.
        /// </param>
        /// <param name="onSkip">Raised once per ground contact, for the skip cue.</param>
        public void Play(Vector3 from, Vector3 to, System.Action<Vector3> passedOver = null,
            IReadOnlyList<ThrownRockFlight.Step> arc = null, System.Action onSkip = null,
            float seconds = Seconds) {
            transform.localPosition = from;
            _seconds = Mathf.Max(0.05f, seconds);
            RunAsync(from, to, passedOver, arc, onSkip).Forget();
        }

        /// <summary>This flight's duration — <see cref="Seconds"/>, or longer for a missed spell that
        /// flies on past its target at the original's speed.</summary>
        private float _seconds = Seconds;

        private async UniTaskVoid RunAsync(Vector3 from, Vector3 to, System.Action<Vector3> passedOver,
            IReadOnlyList<ThrownRockFlight.Step> arc, System.Action onSkip) {
            int lastStep = -1;
            for (float t = 0f; t < _seconds; t += Time.deltaTime) {
                Progress = Mathf.Clamp01(t / _seconds);
                Vector3 at = Vector3.Lerp(from, to, Progress);
                if (arc is { Count: > 0 }) {
                    int step = Mathf.Min(arc.Count - 1, (int)(Progress * arc.Count));
                    at.y += arc[step].Height / Converters.BakCoordinateConverter.WorldScale;
                    // Every step BETWEEN the last drawn one and this one is walked, not just the one
                    // landed on: at our frame rate the progress jumps several of the original's
                    // steps, and a skip in a stride that was passed over is a cue the player should
                    // still hear.
                    for (int i = lastStep + 1; i <= step; i++) {
                        if (arc[i].Skipped) {
                            onSkip?.Invoke();
                        }
                    }
                    lastStep = step;
                }
                transform.localPosition = at;
                passedOver?.Invoke(transform.position);
                await UniTask.Yield();
                if (this == null) {
                    return;
                }
            }
            Progress = 1f;
            transform.localPosition = to;
            Destroy(gameObject);
        }
    }
}
