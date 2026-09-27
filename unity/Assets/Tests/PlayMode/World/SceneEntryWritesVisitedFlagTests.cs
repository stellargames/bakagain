namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.World.Scenes;
    using GameData.Resources.Location;
    using GameData.Resources.Scene;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Entering a scene records the visit, through the method the scene loader calls.
    /// </summary>
    /// <remarks>
    /// <c>TOWNSCN.C</c>:364-367 writes it in the entry block that also starts the scene music:
    /// <code>
    /// if ((g_pCurrentTownScene->wFlags &amp; 0x80) != 0)
    ///     gstate_event_write(TOWN_VISITED(g_pCurrentTownScene->wFlags &amp; 0x7f), 1);
    /// </code>
    /// with <c>#define TOWN_VISITED(idx) ((idx) + 6480)</c> (GSTATE.H:79), which is our
    /// <see cref="TeleportMenu.VisitedFlagFor"/>. Nothing in the port wrote it, so no temple ever
    /// became a teleport destination and the rift map refused for ever. TASK-561.
    ///
    /// <para>Driven through <see cref="LocationScreen.Bind"/> — the entry point the scene player
    /// calls — rather than the private writer, so the test fails if the call site is removed.</para>
    /// </remarks>
    public class SceneEntryWritesVisitedFlagTests {
        private const int TempleTwoEntryWord = 130;   // GDS70B: 0x82 -> gate set, index 2
        private const int LaMutStreetEntryWord = 1;   // GDS1A:  gate clear -> records nothing
        private const int SmithEntryWord = 0;         // GDS52A: nothing at all

        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) {
                UnityEngine.Object.DestroyImmediate(_go);
            }
        }

        private GameSession BindSceneWith(int entryFlagWord) {
            var session = new GameSession();
            _go = new GameObject("LocationScreenUnderTest");
            var screen = _go.AddComponent<LocationScreen>();
            // Bind touches only the scene, the visit counters, this write and the barding refill —
            // the last returns early without a container, so the rest of the graph can stay null.
            screen.Construct(dialogs: null, pointer: null, session: session, teleport: null,
                teleportScreen: null, healScreen: null, inn: null, resources: null,
                inventoryMenu: null, navigator: null);
            // GdsScene takes its resource id: there is no parameterless constructor.
            screen.Bind(new GdsScene("GDS70B.DAT") { EntryFlagWord = entryFlagWord },
                sceneNumber: 70, sceneLetter: 2);
            return session;
        }

        [Test]
        public void EnteringATempleRecordsItAsVisited() {
            GameSession session = BindSceneWith(TempleTwoEntryWord);

            Assert.AreEqual(1, session.GetGlobalValue(TeleportMenu.VisitedFlagFor(2)) ?? 0,
                "GDS70B carries 0x82, so entering it sets TOWN_VISITED(2) = 6482");
        }

        /// <summary>
        /// The 0x80 bit is a gate: a scene without it records nothing.
        /// </summary>
        /// <remarks>
        /// The control that gives the test above its meaning. LaMut's street carries 1 and the
        /// Hawk's Hollow smith 0, so neither is a place the rift map can send you. Writing the word
        /// unmasked would set flags 129 and 130 — a band belonging to something else entirely — and
        /// would still look like a pass without this.
        /// </remarks>
        [Test]
        public void AnOrdinarySceneRecordsNothing() {
            GameSession session = BindSceneWith(LaMutStreetEntryWord);

            Assert.AreEqual(0, session.GetGlobalValue(TeleportMenu.VisitedFlagFor(1)) ?? 0,
                "the gate bit is clear, so no visit is recorded");
            Assert.AreEqual(0, session.GetGlobalValue(LaMutStreetEntryWord) ?? 0,
                "and the raw word is not written as a flag either");
        }

        [Test]
        public void ASceneWithNoEntryWordRecordsNothing() {
            GameSession session = BindSceneWith(SmithEntryWord);

            for (int temple = 1; temple <= TeleportMenu.TempleCount; temple++) {
                Assert.AreEqual(0, session.GetGlobalValue(TeleportMenu.VisitedFlagFor(temple)) ?? 0,
                    $"temple {temple} must stay unvisited");
            }
        }
    }
}
