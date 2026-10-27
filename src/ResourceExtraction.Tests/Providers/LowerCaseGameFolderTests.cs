namespace ResourceExtraction.Tests.Providers;

using System;
using System.IO;
using ResourceExtraction.Providers;
using Xunit;

public class LowerCaseGameFolderTests {
    private static string? FindGameDir() {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            string candidate = Path.Combine(dir, "OriginalGame", "KRONDOR.001");
            if (File.Exists(candidate)) {
                return Path.GetDirectoryName(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [SkippableFact]
    public void ALowerCaseGameFolderLoads() {
        string? gameDir = FindGameDir();
        Skip.If(gameDir == null, "OriginalGame/KRONDOR.001 not found");

        string copy = Directory.CreateTempSubdirectory().FullName;
        try {
            foreach (string name in new[] { "KRONDOR.001", "STARTUP.GAM" }) {
                File.Copy(Path.Combine(gameDir!, name), Path.Combine(copy, name.ToLowerInvariant()));
            }

            var provider = new GeneralResourceProvider(copy);

            Assert.True(provider.CanProvideResource("STARTUP.GAM"));
            using Stream s = provider.GetResourceStream("STARTUP.GAM");
            Assert.Equal(new FileInfo(Path.Combine(gameDir!, "STARTUP.GAM")).Length, s.Length);
        } finally {
            Directory.Delete(copy, true);
        }
    }
}
