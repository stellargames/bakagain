namespace GameData.Resources.Book;

using System.Collections.Generic;

/// <summary>
/// The illuminated capitals in BOOK.BMX (TASK-781). A book's first paragraph is stored without its
/// first letter, and the page carries one of these pictures to draw it.
/// </summary>
/// <remarks>
/// Which letter each picture is was read off the art; the shipped books confirm each one, the
/// paragraph beside it continuing the word ("olden sweat" beside #4, so #4 is a G, not a C). The
/// other BOOK.BMX pictures (#7-#14) are vines.
/// </remarks>
public static class BookDropCaps {
    private static readonly Dictionary<int, char> Letters = new() {
        [0] = 'B', [1] = 'P', [2] = 'J', [3] = 'L', [4] = 'G', [5] = 'A', [6] = 'T',
        [15] = 'O', [16] = 'I', [17] = 'D', [18] = 'S',
    };

    /// <summary>
    /// The books whose capital is a whole word rather than the start of one: C21's "A whisper".
    /// Nothing in the data says so, and every other book's capital starts its word.
    /// </summary>
    private static readonly HashSet<string> CapitalIsAWord = new(System.StringComparer.OrdinalIgnoreCase) { "C21" };

    /// <summary>The capital's letter, or null for a picture that is not one.</summary>
    public static char? LetterOf(int imageNumber) => Letters.TryGetValue(imageNumber, out char c) ? c : null;

    /// <summary>The picture that draws <paramref name="letter"/>, or null if the game has none.</summary>
    public static int? ImageFor(char letter) {
        foreach (KeyValuePair<int, char> entry in Letters) {
            if (entry.Value == letter) {
                return entry.Key;
            }
        }
        return null;
    }

    /// <summary>The text the capital and its paragraph read as together.</summary>
    public static string Join(string bookId, char capital, string rest) =>
        capital + (CapitalIsAWord.Contains(System.IO.Path.GetFileNameWithoutExtension(bookId)) ? " " : "") + rest;
}
