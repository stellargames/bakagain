namespace BetrayalAtKrondor.Tests.Logging;

using System;
using System.IO;
using global::ResourceExtraction.Extractors.Dialog;
using Xunit;

/// <summary>
/// Reading a game file writes nothing to the console: Unity sends console output into Player.log,
/// and the extractors' field-by-field trace made a few days of play a 7 MB log, too big to attach to
/// a bug report (forum p=69, TASK-1127). The trace is opt-in (BAK_EXTRACT_TRACE=1).
/// </summary>
[Collection("Console")]
public class ExtractorQuietTests {
    [Fact]
    public void ReadingADialogFileWritesNothingToTheConsole() {
        string? path = Find("DIAL_Z00.DDX");
        if (path == null) {
            return;   // no game data here
        }
        TextWriter before = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try {
            using FileStream stream = File.OpenRead(path);
            Assert.NotNull(new DdxExtractor().Extract("DIAL_Z00.DDX", stream));
        } finally {
            Console.SetOut(before);
        }
        Assert.Equal(string.Empty, captured.ToString());
    }

    private static string? Find(string file) {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            string path = Path.Combine(dir, "OriginalGame", file);
            if (File.Exists(path)) {
                return path;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
