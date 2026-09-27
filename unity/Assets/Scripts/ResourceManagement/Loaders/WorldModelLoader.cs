namespace BakAgain.ResourceManagement.Loaders {
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.World;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Resolves and caches one <see cref="WorldModel"/> per TBL TypeId for a zone. Builds every
    /// model from the zone's TBL via <see cref="TblModelConverter"/>.
    /// </summary>
    public sealed class WorldModelLoader : IDisposable {
        private readonly ZoneTable _tbl;
        private readonly WorldEntityRenderContext _ctx;
        private readonly Dictionary<int, WorldModel> _cache = new();

        public WorldModelLoader(ZoneTable tbl, WorldEntityRenderContext ctx) {
            _tbl = tbl;
            _ctx = ctx;
        }

        public async UniTask<WorldModel> GetAsync(int typeId) {
            if (_cache.TryGetValue(typeId, out var cached)) return cached;
            var entry  = _tbl.Entries[typeId];
            var meta   = WorldModelMetadata.FromEntry(entry, typeId);
            var visual = await TblModelConverter.BuildVisualAsync(entry, _ctx);
            var model  = new WorldModel(visual, meta);   // Resources null — TBL owns nothing
            _cache[typeId] = model;
            return model;
        }

        public void Dispose() {
            foreach (var m in _cache.Values) {
                if (m.Visual != null) UnityEngine.Object.Destroy(m.Visual);
                m.Resources?.Dispose();
            }
            _cache.Clear();
        }
    }
}
