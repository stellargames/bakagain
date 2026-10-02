namespace ResourceExtraction;

using GameData.Resources.Data;

/// <summary>
/// Synthesizes the ChapterCatalog by probing the archive (IResourceProvider.CanProvideResource) —
/// there is no single source file, the same way BookParchment is synthesized from BOOK.SCX. Faithful
/// to the original's Contents replay (playChapterAnimationsAndBook chapter,1 then chapter,2): each
/// chapter gets its CHAPTER{N} intro animation plus parts 1 and 2 (whichever book or animation exists). Higher
/// books (C{N}3+) are in-game chapter beats, not Contents-replayable, so they are not included.
/// </summary>
public static class ChapterCatalogBuilder {
    private const int ChapterCount = 9;
    private const int MaxContentsParts = 2; // the original Contents replay plays parts 1 and 2

    public static ChapterCatalog Build(string id, IResourceProvider provider) {
        var catalog = new ChapterCatalog(id);
        for (int n = 1; n <= ChapterCount; n++) {
            var chapter = new Chapter {
                Number = n,
                ContentsActionId = n + 1,             // CONTENTS.DAT: chapter 1 = actionId 2 .. chapter 9 = 10
                IntroAnimation = $"CHAPTER{n}",        // presenter-facing (probe adds ".ADS")
            };
            for (int p = 1; p <= MaxContentsParts; p++) {
                // The book and the animation are independent: the original shows the book if it
                // opens and plays the ADS if it exists (GMAIN.C:348-368). C22/C42/C62/C72/C82 ship
                // an ADS and no book, and that ADS is the chapter's whole close.
                string bookArchive = $"C{n}{p}.BOK";
                string animArchive = $"C{n}{p}.ADS";
                bool hasBook = provider.CanProvideResource(bookArchive);
                bool hasAnimation = provider.CanProvideResource(animArchive);
                if (!hasBook && !hasAnimation) {
                    continue; // part absent -> skip
                }
                chapter.Parts.Add(new ChapterPart {
                    Book = hasBook ? bookArchive : string.Empty,                // WITH extension
                    Animation = hasAnimation ? $"C{n}{p}" : string.Empty,       // WITHOUT
                });
            }
            catalog.Chapters.Add(chapter);
        }
        return catalog;
    }
}
