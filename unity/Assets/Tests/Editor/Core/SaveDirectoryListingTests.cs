namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using NUnit.Framework;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Which save directories Restore and Save list (TASK-948). Classic is the original's rule: one
    /// directory per <c>.G##</c> number 1..20, sorted by number, the first one found winning
    /// (MAINMENU.C:1626-1660, :1713-1731). Enhanced lists every directory, by number and then name.
    /// Runs against a scratch folder, never the player's Saves.
    /// </summary>
    public class SaveDirectoryListingTests {
        private EnhancedPrefsStash _enhanced;
        private string _root;
        private SaveGameDirectoryService _saves;

        [SetUp]
        public void SetUp() {
            _enhanced = EnhancedPrefsStash.Take();
            _root = Path.Combine(Path.GetTempPath(), "t948-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _saves = new SaveGameDirectoryService(new NullLogger<SaveGameDirectoryService>()) { SaveRoot = _root };
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _enhanced.Restore();
        }

        private void Make(params string[] names) {
            foreach (string n in names) Directory.CreateDirectory(Path.Combine(_root, n));
        }

        private async Task<string> Listed() =>
            string.Join(" ", (await _saves.ListDirectoriesAsync()).Select(d => $"{d.DisplayName}/{d.Number}"));

        [Test]
        public async Task Classic_ListsOnePerNumber_SortedByNumber_FirstByNameWins_DropsG00AndOutOfRange() {
            GameOptions.Enhanced = false;
            Make("zeta.G02", "dir.G01", "Lamut.G03", "old.G00", "big.G21", "Bob.G01", "arena.G01", "notasave");

            Assert.That(await Listed(), Is.EqualTo("ARENA/1 ZETA/2 LAMUT/3"));
        }

        [Test]
        public async Task Enhanced_ListsEveryDirectory_SortedByNumberThenName() {
            GameOptions.Enhanced = true;
            Make("zeta.G02", "dir.G01", "Lamut.G03", "old.G00", "big.G21", "Bob.G01", "arena.G01", "notasave");

            Assert.That(await Listed(), Is.EqualTo("OLD/0 ARENA/1 BOB/1 DIR/1 ZETA/2 LAMUT/3 BIG/21"));
        }

        // MAINMENU.C:920-938: the typed name is matched against the LISTED labels only, so the name of
        // a hidden duplicate matches nothing and the game makes <name>.G<next free number>.
        [Test]
        public async Task Classic_SaveToAHiddenDuplicateName_MakesANewDirectoryAtTheNextFreeNumber() {
            GameOptions.Enhanced = false;
            Make("arena.G01", "dir.G01", "DIR2.G02");
            Assert.That((await _saves.ListDirectoriesAsync()).Any(d => d.DisplayName == "DIR"), Is.False,
                "dir.G01 is hidden behind arena.G01");

            string created = await _saves.CreateDirectoryAsync("dir");

            Assert.That(created, Is.EqualTo("dir.G03"));
            Assert.That(await Listed(), Is.EqualTo("ARENA/1 DIR2/2 DIR/3"));
        }

        // Controls: what a listed directory does is unchanged.
        [Test]
        public async Task Enhanced_HiddenInClassic_IsListedAndKeepsItsSlots() {
            GameOptions.Enhanced = true;
            Make("arena.G01", "dir.G01");
            File.WriteAllBytes(Path.Combine(_root, "dir.G01", "SAVE01.GAM"), new byte[10]);

            SaveDirectoryInfo dir = (await _saves.ListDirectoriesAsync()).Single(d => d.DisplayName == "DIR");
            Assert.That(dir.Name, Is.EqualTo("dir.G01"));
            Assert.That(_saves.LowestFreeSlotIndex(dir.Name), Is.EqualTo(2));
        }

        // The original makes GAMES\SAVES.G01 when the dialog opens (MkSaveGameDir); an unlisted .G00
        // is not a directory the Classic list can select.
        [Test]
        public async Task Classic_OnlyAG00_SeedsSavesG01() {
            GameOptions.Enhanced = false;
            Make("old.G00");

            Assert.That(await _saves.EnsureDefaultDirectoryAsync(), Is.EqualTo("SAVES.G01"));
            Assert.That(await Listed(), Is.EqualTo("SAVES/1"));
        }
    }
}
