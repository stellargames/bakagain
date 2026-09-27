namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine.ResourceManagement.ResourceLocations;

    /// <summary>
    /// <see cref="BakResourceLocator"/> must resolve the resources that are SYNTHESIZED IN CODE
    /// and have no member in <c>KRONDOR.001</c> — today <c>DIALSTYL.DAT</c> (the dialog style
    /// table, which the original kept in the executable's data segment).
    ///
    /// <para><b>What breaks without it.</b> The locator's guard falls through to a dictionary
    /// lookup, and a resource with no archive member misses. <c>Locate</c> then returns
    /// <c>false</c>, Addressables finds no location, and the resource exists <i>only when the
    /// player has an override file</i> — the shipped, faithful default becomes unreachable and
    /// every unmodded dialog falls back to <c>DialogResourceLoader</c>'s error path. Deleting
    /// <c>!isDialogStyleTable</c> from the <c>||</c> chain compiles cleanly and leaves the whole
    /// suite green; this is the test that goes red.</para>
    /// </summary>
    public class BakResourceLocatorSynthesizedResourceTests {
        [SetUp]
        public void RequireShippedGameData() {
            // BakResourceLocator's constructor opens the archive to build its key dictionary.
            ShippedGameData.RequireOrIgnore();
        }

        [Test]
        public void DialogStyleTable_ResolvesWithNoOverridePresent_ThoughItIsNotAnArchiveMember() {
            var locator = new BakResourceLocator();

            // The premise, asserted rather than assumed: the key really is absent from the
            // archive dictionary, so a true result below can only come from the special case.
            Assert.IsFalse(
                locator.Keys.Any(key => string.Equals(
                    key.ToString(), DialogStyleTable.ResourceId, System.StringComparison.OrdinalIgnoreCase)),
                "DIALSTYL.DAT is synthesized in code; if it ever becomes a real archive member "
                + "this test's premise (and the locator's special case) needs revisiting");

            bool located = locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations);

            Assert.IsTrue(located,
                "without this, the shipped dialog style table is only reachable when modded");
            Assert.AreEqual(1, locations.Count);
            Assert.AreEqual(nameof(BakResourceProvider), locations[0].ProviderId,
                "it must resolve to the provider that synthesizes the shipped table");
            Assert.AreEqual(DialogStyleTable.ResourceId, locations[0].PrimaryKey);
        }

        /// <summary>
        /// The key is normalised before the special case is tested, so an author (or a call site)
        /// writing the id in any case resolves the same way — matching the <c>OrdinalIgnoreCase</c>
        /// comparison the clause is written with.
        /// </summary>
        [Test]
        public void DialogStyleTable_ResolvesCaseInsensitively() {
            var locator = new BakResourceLocator();

            Assert.IsTrue(locator.Locate("dialstyl.dat", typeof(DialogStyleTable), out IList<IResourceLocation> locations));
            Assert.AreEqual(DialogStyleTable.ResourceId, locations[0].PrimaryKey);
        }

        /// <summary>
        /// The negative control. Without it, a <c>Locate</c> that had been broken into returning
        /// <c>true</c> unconditionally would satisfy the assertions above.
        /// </summary>
        [Test]
        public void AKeyThatIsNeitherSynthesizedNorInTheArchive_DoesNotResolve() {
            var locator = new BakResourceLocator();

            Assert.IsFalse(locator.Locate("NOSUCHR.DAT", typeof(DialogStyleTable), out IList<IResourceLocation> locations));
            Assert.IsEmpty(locations);
        }
    }
}
