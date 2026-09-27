namespace BakAgain.ResourceManagement {
    using System;

    /// <summary>
    /// Filename helpers shared by the resource locators.
    /// </summary>
    internal static class ResourceFilename {
        /// <summary>
        /// The original game stores some image filenames with .SCR / .BMP extensions but loads the
        /// .SCX / .BMX variants — it swaps the final extension character to 'x'. Returns the filename
        /// (or bare extension) with that swap applied, preserving case; returns it unchanged otherwise.
        /// </summary>
        public static string NormalizeImageExtension(string filename) {
            if (string.IsNullOrEmpty(filename)) return filename;
            if (filename.EndsWith(".scr", StringComparison.OrdinalIgnoreCase) ||
                filename.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) {
                char last = filename[^1];
                return filename[..^1] + (char.IsUpper(last) ? 'X' : 'x');
            }
            return filename;
        }
    }
}
