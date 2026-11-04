namespace BakAgain.Core {
    using System;

    /// <summary>
    /// The build test harness's launch argument <c>-bak-load &lt;dir&gt; &lt;slot&gt;</c>: a development player
    /// boots straight into slot <c>slot</c> of save directory <c>dir</c> under the save root
    /// (docs/specs/2026-11-01-build-test-harness-design.md). <see cref="StartStateSelector"/> honours it.
    /// </summary>
    internal static class BootLoad {
        internal const string Flag = "-bak-load";

        /// <summary>Slot files are SAVE00..SAVE99.GAM.</summary>
        private const int MaxSlot = 99;

        internal static bool TryParse(string[] args, out string dir, out int slot) {
            dir = null;
            slot = -1;
            int i = Array.IndexOf(args, Flag);
            if (i < 0 || i + 2 >= args.Length) {
                return false;
            }
            if (!int.TryParse(args[i + 2], out int s) || s < 0 || s > MaxSlot || string.IsNullOrEmpty(args[i + 1])) {
                return false;
            }
            dir = args[i + 1];
            slot = s;
            return true;
        }
    }
}
