namespace BakAgain.Tests.Editor.World {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using ResourceExtraction;
    using ResourceExtraction.Extractors;
    using UnityEngine;

    /// <summary>
    /// Corpus census for the coplanar cut: across every shipped depth-sorted model, how many
    /// same-facing overlapping coplanar face pairs (the z-fighting candidates) the cut removes.
    /// </summary>
    public class CoplanarCutCorpusTests {
        private static readonly string[] Tables = {
            "Z01", "Z02", "Z03", "Z04", "Z05", "Z06", "Z07", "Z08", "Z09", "Z10", "Z11", "Z12",
            "Z10M", "Z11M", "Z12M", "COMBAT",
        };

        [Test]
        [RequiresShippedGameData]
        public void TheCutLeavesOnlyTheDeliberateExclusionsOverlapping() {
            var provider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            var census = new TblMeshConverter.CoplanarCensus();
            int models = 0;
            foreach (string id in Tables) {
                var table = provider.GetResource<ZoneTable>(id + ".TBL");
                ZoneTableExtractor.StampTextureKeys(table, id + ".TBL", provider);
                foreach (var entry in table.Entries) {
                    if (entry.Dat == null || !entry.Dat.IsDepthSorted) continue;
                    models++;
                    TblMeshConverter.CensusCoplanarOverlaps(entry.Dat, census);
                }
            }
            Debug.Log($"[CoplanarCut] {models} depth-sorted models: pairs before {census.PairsBefore}, "
                + $"after {census.PairsAfter} (textured base {census.AfterTexturedBase}, "
                + $"concave {census.AfterConcave}, off-plane {census.AfterOffPlane})");

            Assert.Greater(census.PairsBefore, 100, "the walk reached the shipped models");
            Assert.AreEqual(census.AfterTexturedBase + census.AfterConcave + census.AfterOffPlane,
                census.PairsAfter,
                "every overlap left is one of the deliberate exclusions");
        }
    }
}
