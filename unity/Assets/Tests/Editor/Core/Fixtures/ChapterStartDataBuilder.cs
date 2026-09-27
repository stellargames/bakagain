namespace BakAgain.Tests.Editor.Core.Fixtures {
    using GameData.Resources.Data;
    using GameData.Resources.Location;

    /// <summary>
    /// Builder for hand-crafted <see cref="ChapterStartData"/> fixtures. Defaults match the
    /// real CHAP1.DAT (zone 1, tile 10,16, full-map marker at 116,75 icon 18) so most tests
    /// only override what they care about.
    /// </summary>
    public sealed class ChapterStartDataBuilder {
        private string _id = "CHAP1.DAT";
        private int _chapterNumber = 1;
        private int _gold = 0;
        private int _gameTime = 57600;
        private int _zone = 1;
        private int _tileX = 10;
        private int _tileY = 16;
        private int _tileXOffset = 18;
        private int _tileYOffset = 25;
        private int _zRotation = unchecked((short)0x8000);
        private FullMapIcon _icon = new FullMapIcon(true, 116f / 320f * 100f, 75f / 200f * 100f, 18);

        public ChapterStartDataBuilder WithChapter(int chapter) {
            _chapterNumber = chapter;
            return this;
        }

        public ChapterStartDataBuilder WithGold(int gold) {
            _gold = gold;
            return this;
        }

        public ChapterStartDataBuilder WithGameTime(int gameTime) {
            _gameTime = gameTime;
            return this;
        }

        public ChapterStartDataBuilder WithLocation(int zone, int tileX, int tileY) {
            _zone = zone;
            _tileX = tileX;
            _tileY = tileY;
            return this;
        }

        public ChapterStartDataBuilder WithFullMapIcon(FullMapIcon icon) {
            _icon = icon;
            return this;
        }

        public ChapterStartData Build() {
            var location = new Location {
                ZoneNumber = _zone,
                X = _tileX,
                Y = _tileY,
                XOffset = _tileXOffset,
                YOffset = _tileYOffset,
                ZRotation = _zRotation
            };

            int positionX = _tileX * 64000 + _tileXOffset * 1600 + 800;
            int positionY = _tileY * 64000 + _tileYOffset * 1600 + 800;

            return new ChapterStartData(
                _id,
                _chapterNumber,
                _gold,
                _gameTime,
                timeSnapshot: 0,
                partyDeathState: 0,
                chapterTransitionPending: 0,
                location,
                positionX,
                positionY,
                _icon);
        }
    }
}
