namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Tests.PlayMode.UI.Inventory;
    using BakAgain.World.Hotspots;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// A town's entry prompt comes back on every step onto its trigger, a visit or not: the original's
    /// town path (<c>hotspotevt_show_record_message</c> / <c>hotspotevt_action_enter_town</c>,
    /// HOTSPOT.C:618-652) sets neither the done flag nor the per-tile silence. The port silenced it
    /// after a visit, so leaving LaMut and walking west crossed the gate column without a word and
    /// reached hills the original never lets the party see (owner, 2026-10-19).
    /// </summary>
    public class TownPromptRepeatsTests {
        private const int Cell = 1600;

        private sealed class AlwaysYes : NoOpDialogs {
            public int Asked;

            public override UniTask<int> ShowChoiceIndexById(int id, System.Threading.CancellationToken ct = default) {
                Asked++;
                return UniTask.FromResult(0);   // branch 0: Yes, go in
            }
        }

        [Test]
        public void AfterAVisitTheNextStepOntoTheGateAsksAgain() {
            var dialogs = new AlwaysYes();
            var service = new HotspotService(null, null, new GameSession(), dialogs);
            // LaMut's gate as shipped: T011014 Town, the one column x 14, y 11..27.
            service.SetZoneForTest(1,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> {
                        new TileEventTrigger { Type = TileEventType.Town, EntryNumber = 0, StartX = 14, EndX = 14, StartY = 11, EndY = 27 },
                    },
                },
                town: new DefFamilyFile<DefTownEntry>("DEF_TOWN.DAT", new List<DefRecord<DefTownEntry>> {
                    new DefRecord<DefTownEntry> { Status = 1, Payload = new DefTownEntry { GdsSceneNumber = 1, DialogId = 1500008 } },
                }));

            service.ActivateAtPartyPosition((14 * Cell) + 800, (17 * Cell) + 800);
            Assert.AreEqual(1, dialogs.Asked, "the gate asks");

            service.ActivateAtPartyPosition((15 * Cell) + 800, (20 * Cell) + 800);   // back out of town, east of the gate
            service.ActivateAtPartyPosition((14 * Cell) + 800, (20 * Cell) + 800);   // and west onto it again
            Assert.AreEqual(2, dialogs.Asked, "and asks again: there is no getting past LaMut's gate unasked");
        }
    }
}
