namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI.Cursor;
    using GameData.Resources.Cursor;
    using NUnit.Framework;

    public class CursorMapTests {
        // Real on-disk schema: sets[key] is an object carrying the BMX file name.
        private const string Json = @"{
            ""sets"": { ""POINTER"": { ""file"": ""POINTER.BMX"", ""images"": 3 },
                        ""POINTERG"": { ""file"": ""POINTERG.BMX"", ""images"": 27 } },
            ""cursors"": { ""Arrow"":   { ""set"": ""POINTER"",  ""index"": 0 },
                           ""Examine"": { ""set"": ""POINTERG"", ""index"": 3 } } }";

        [Test]
        public void Resolve_Arrow_ReturnsPointerSetIndex0() {
            CursorMap map = CursorMap.Parse(Json);
            (string file, int index) = map.Resolve(GameCursor.Arrow);
            Assert.AreEqual("POINTER.BMX", file);
            Assert.AreEqual(0, index);
        }

        [Test]
        public void Resolve_Examine_ReturnsPointergSetIndex3() {
            CursorMap map = CursorMap.Parse(Json);
            (string file, int index) = map.Resolve(GameCursor.Examine);
            Assert.AreEqual("POINTERG.BMX", file);
            Assert.AreEqual(3, index);
        }

        [Test]
        public void TryResolve_AbsentCursor_ReturnsFalse() {
            // Wait has no row in this JSON -> not resolvable.
            CursorMap map = CursorMap.Parse(Json);
            Assert.IsFalse(map.TryResolve(GameCursor.Wait, out _));
        }

        [Test]
        public void Parse_SimplifiedStringSchema_AlsoSupported() {
            const string simple = @"{ ""sets"": { ""POINTER"": ""POINTER.BMX"" },
                ""cursors"": { ""Arrow"": { ""set"": ""POINTER"", ""index"": 0 } } }";
            CursorMap map = CursorMap.Parse(simple);
            Assert.AreEqual("POINTER.BMX", map.Resolve(GameCursor.Arrow).file);
        }
    }
}
