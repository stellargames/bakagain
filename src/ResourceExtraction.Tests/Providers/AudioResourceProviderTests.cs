namespace ResourceExtraction.Tests.Providers;

using System.IO;
using ResourceExtraction.Providers;
using Xunit;

public class AudioResourceProviderTests {
    // A folder without FRP.SX has no sounds; the names table used to open the file anyway and throw.
    [Fact]
    public void AFolderWithoutTheSoundArchiveProvidesNoSound() {
        string folder = Directory.CreateTempSubdirectory().FullName;
        try {
            var provider = new AudioResourceProvider(folder);

            Assert.False(provider.CanProvideResource("1"));
        } finally {
            Directory.Delete(folder, true);
        }
    }
}
