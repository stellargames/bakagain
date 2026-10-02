namespace BetrayalAtKrondor.Tests.Data;

using System.Collections.Generic;
using System.IO;
using GameData.Resources;
using GameData.Resources.Data;
using ResourceExtraction;
using Xunit;

public class ChapterCatalogBuilderTests {
    // Minimal fake: the builder only calls CanProvideResource; everything else throws.
    private sealed class FakeProvider(HashSet<string> present) : IResourceProvider {
        public ResourceExtraction.ResourceType ResourceType => ResourceExtraction.ResourceType.General;
        public bool CanProvideResource(string resourceId) => present.Contains(resourceId.ToUpper());
        public Stream GetResourceStream(string resourceId) => throw new System.NotSupportedException();
        public IDictionary<string, (long, uint)> GetDictionary(ResourceExtraction.ResourceType? type = null)
            => throw new System.NotSupportedException();
        public T GetResource<T>(string resourceId) where T : IResource => throw new System.NotSupportedException();
    }

    [Fact]
    public void Build_ProducesNineChapters_WithIntroAndActionId() {
        // Every CHAPTER{N}.ADS + both parts present.
        var present = new HashSet<string>();
        for (int n = 1; n <= 9; n++) {
            present.Add($"CHAPTER{n}.ADS");
            present.Add($"C{n}1.BOK"); present.Add($"C{n}1.ADS");
            present.Add($"C{n}2.BOK"); present.Add($"C{n}2.ADS");
        }
        ChapterCatalog catalog = ChapterCatalogBuilder.Build(ChapterCatalog.ResourceId, new FakeProvider(present));

        Assert.Equal(9, catalog.Chapters.Count);
        Chapter c1 = catalog.Chapters[0];
        Assert.Equal(1, c1.Number);
        Assert.Equal(2, c1.ContentsActionId);      // chapter N -> actionId N+1
        Assert.Equal("CHAPTER1", c1.IntroAnimation); // presenter-facing, no extension
        Assert.Equal(2, c1.Parts.Count);
        Assert.Equal("C11.BOK", c1.Parts[0].Book);   // book WITH extension
        Assert.Equal("C11", c1.Parts[0].Animation);  // animation WITHOUT extension
        Assert.Equal("C12.BOK", c1.Parts[1].Book);
    }

    [Fact]
    public void Build_OmitsMissingPart2_AndMissingAnimations() {
        // Chapter 1 has part 1 (book only, no animation) and NO part 2.
        var present = new HashSet<string> { "CHAPTER1.ADS", "C11.BOK" };
        for (int n = 2; n <= 9; n++) {
            present.Add($"CHAPTER{n}.ADS");
            present.Add($"C{n}1.BOK"); present.Add($"C{n}1.ADS");
        }
        ChapterCatalog catalog = ChapterCatalogBuilder.Build(ChapterCatalog.ResourceId, new FakeProvider(present));

        Chapter c1 = catalog.Chapters[0];
        Assert.Single(c1.Parts);                        // no part 2 -> only 1 part
        Assert.Equal("C11.BOK", c1.Parts[0].Book);
        Assert.Equal(string.Empty, c1.Parts[0].Animation); // no C11.ADS -> empty animation

        Chapter c2 = catalog.Chapters[1];               // ch2 here has part 1 only (book + animation)
        Assert.Single(c2.Parts);
        Assert.Equal("C21.BOK", c2.Parts[0].Book);
        Assert.Equal("C21", c2.Parts[0].Animation);
    }

    [Fact]
    public void Build_KeepsAPartWhoseAnimationShipsWithoutABook() {
        // Chapter 6 ships C62.ADS but no C62.BOK. gmain_play_chapter_cutscene(6, 2, 1) shows the
        // book if it opens and plays the ADS regardless (GMAIN.C:348-368), and C62.ADS is the
        // whole chapter-6 close: it opens C63.BOK itself and plays the elf court. Keying the
        // part on the book dropped it, so the port closed chapters 2, 4, 6, 7 and 8 with nothing.
        var present = new HashSet<string> { "CHAPTER6.ADS", "C61.BOK", "C61.ADS", "C62.ADS" };
        ChapterCatalog catalog = ChapterCatalogBuilder.Build(ChapterCatalog.ResourceId, new FakeProvider(present));

        Chapter c6 = catalog.Chapters[5];
        Assert.Equal(2, c6.Parts.Count);
        Assert.Equal(string.Empty, c6.Parts[1].Book);
        Assert.Equal("C62", c6.Parts[1].Animation);
    }
}
