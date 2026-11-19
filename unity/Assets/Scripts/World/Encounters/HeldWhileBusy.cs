namespace BakAgain.World.Encounters {
    /// <summary>
    /// A press that waits while the arena is busy: the original reads no input during a blocking
    /// attack (COMBAT.C:2361-2362), so a key pressed then is read once it ends (TASK-1100).
    /// </summary>
    public sealed class HeldWhileBusy {
        private bool _pending;

        /// <summary>One frame: whether the press acts now.</summary>
        public bool Poll(bool pressed, bool busy) {
            _pending |= pressed;
            if (busy || !_pending) {
                return false;
            }
            _pending = false;
            return true;
        }
    }
}
