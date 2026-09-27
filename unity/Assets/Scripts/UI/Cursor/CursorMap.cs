namespace BakAgain.UI.Cursor {
    using System.Collections.Generic;
    using GameData.Resources.Cursor;
    using Newtonsoft.Json.Linq;
    using System.Diagnostics;

    /// <summary>The thin semantic layer over the data-driven cursor model: resolves a
    /// <see cref="GameCursor"/> to a concrete <c>(BMX file, index)</c> from the hand-authored
    /// <c>generated/POINTER/cursor-map.json</c> RE table.
    ///
    /// <para>Most cursor selection is index-driven (<see cref="ICursorManager.SetByIndex"/> from a
    /// widget's <c>cursor</c> field); this map only covers the handful of code-named semantic
    /// cursors (Arrow/Wait/Examine). Tolerates both the rich on-disk schema
    /// (<c>sets[key] = { file, images }</c>) and the simplified <c>sets[key] = "FILE.BMX"</c> form.</para></summary>
    public class CursorMap {
        private readonly Dictionary<GameCursor, (string file, int index)> _rows = new();

        private CursorMap() { }

        public static CursorMap Parse(string json) {
            var map = new CursorMap();
            var root = JObject.Parse(json);
            var sets = (JObject)root["sets"];
            var cursors = (JObject)root["cursors"];
            Debug.Assert(cursors != null, nameof(cursors) + " != null");
            foreach (KeyValuePair<string, JToken> kv in cursors) {
                if (!System.Enum.TryParse(kv.Key, out GameCursor gc)) continue;
                string setKey = (string)kv.Value["set"];
                JToken setNode = sets[setKey];
                string file = setNode is JObject obj ? (string)obj["file"] : (string)setNode;
                int index = (int)kv.Value["index"];
                map._rows[gc] = (file, index);
            }
            return map;
        }

        public (string file, int index) Resolve(GameCursor cursor) => _rows[cursor];

        public bool TryResolve(GameCursor cursor, out (string file, int index) row) =>
            _rows.TryGetValue(cursor, out row);
    }
}
