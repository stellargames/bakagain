namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Location;
    using GameData.Resources.World;

    /// <summary>
    /// The town the party is at, for the Enhanced status line (TASK-866): the town whose full-map
    /// icon covers the party's own map marker (<see cref="FullMapTowns.NameUnder"/>). Re-read only
    /// when the party changes map tile; the tables load in the background, so the first frames of a
    /// zone show no name.
    /// </summary>
    internal sealed class PartyTownName {
        private readonly GameSession _session;
        private readonly IResourceProviderService _resources;
        private readonly object _owner;
        private FullMapTowns _towns;
        private FullMapPositions _positions;
        private ZoneRef _zoneRef;
        private int _refZone = -1;
        private (int Zone, int X, int Y) _tile = (-1, 0, 0);
        private bool _loading;
        private string _name;

        public PartyTownName(GameSession session, IResourceProviderService resources, object owner) {
            _session = session;
            _resources = resources;
            _owner = owner;
        }

        /// <summary>The town's name, or null away from every town.</summary>
        public string Current {
            get {
                if (_session == null || _resources == null) {
                    return null;
                }
                int zone = _session.CurrentZone;
                if (_refZone != zone) {
                    if (!_loading) {
                        LoadAsync(zone).Forget();
                    }
                    return null;
                }
                var tile = (zone, WorldTileCache.TileOf(_session.PositionX), WorldTileCache.TileOf(_session.PositionY));
                if (tile != _tile) {
                    _tile = tile;
                    _name = _towns == null || _positions == null || _zoneRef == null
                        ? null
                        : _towns.NameUnder(_positions.MarkerFor(zone, _zoneRef, _session.PositionX, _session.PositionY,
                            _session.Rotation));
                }
                return _name;
            }
        }

        private async UniTaskVoid LoadAsync(int zone) {
            _loading = true;
            try {
                _towns ??= await _resources.LoadAssetAsync<FullMapTowns>("FMAP_TWN.DAT", _owner);
                _positions ??= await _resources.LoadAssetAsync<FullMapPositions>("FMAP_XY.DAT", _owner);
                _zoneRef = await _resources.LoadAssetAsync<ZoneRef>($"Z{zone:D2}REF.DAT", _owner);
            } finally {
                _refZone = zone;   // a zone without a table shows no name rather than retrying every frame
                _tile = (-1, 0, 0);
                _loading = false;
            }
        }
    }
}
