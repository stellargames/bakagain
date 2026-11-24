namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Resources.Combat;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine.ResourceManagement.ResourceLocations;

    /// <summary>
    /// The combat affinity tables ship with the game under <see cref="CombatAffinityTables.ResourceId"/>
    /// (TASK-1143), and a mod replaces them with <c>Overrides/DAT/AFFINITY.json</c>: the whole document,
    /// in the shape of <c>generated/EXE/combat-affinity.json</c>. Same proof shape as
    /// <see cref="OverrideResourceProviderTextAssetTests"/>: the real locator, the real provider step.
    /// </summary>
    public class CombatAffinityOverrideTests {
        private TempOverrideDirectory _overrides;

        [SetUp]
        public void BorrowTheOverrideDirectory() {
            _overrides = new TempOverrideDirectory();
        }

        [TearDown]
        public void ReturnTheOverrideDirectory() {
            _overrides?.Dispose();
            _overrides = null;
        }

        [Test]
        public void AModsAffinityFileReplacesTheShippedTables() {
            CombatAffinityTables shipped = CombatAffinityTables.CreateShipped();
            int[] flee = (int[])shipped.AiFleeThresholds.Clone();
            flee[0] += 7;
            shipped.AiFleeThresholds = flee;
            _overrides.Write("DAT", "AFFINITY.json", Newtonsoft.Json.JsonConvert.SerializeObject(shipped));

            bool located = new OverrideResourceLocator().Locate(
                CombatAffinityTables.ResourceId, typeof(CombatAffinityTables), out IList<IResourceLocation> at);
            Assert.IsTrue(located, "AFFINITY.DAT must resolve to Overrides/DAT/AFFINITY.json");

            var tables = (CombatAffinityTables)OverrideResourceProvider.LoadOverrideObject(
                at[0].InternalId, typeof(CombatAffinityTables), CombatAffinityTables.ResourceId);

            Assert.AreEqual(flee[0], tables.AiFleeThresholds[0], "the mod's value");
            Assert.AreEqual(CombatAffinityTables.CreateShipped().AiFleeThresholds[0] + 7, tables.AiFleeThresholds[0]);
            CollectionAssert.AreEqual(CombatAffinityTables.CreateShipped().ClassGroupModifier[0],
                tables.ClassGroupModifier[0], "the rest of the document came through");
            Assert.AreEqual(CombatAffinityTables.CreatureClassCount, tables.Creatures.Count);
        }
    }
}
