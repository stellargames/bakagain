namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using System;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The two font commands run to completion.
    ///
    /// <para><b>They used to throw <see cref="NotImplementedException"/>.</b> The frame processor
    /// catches a throwing command and logs an error, so C21 and C42 — the only two shipped scripts
    /// that touch a font — each reported a failure per play while looking perfectly correct on
    /// screen. That is the worst of both: a real error line for a command with nothing to do.</para>
    ///
    /// <para><b>Why nothing to do is the right answer.</b> The slot's only reader in the original is
    /// the TTM text-drawing opcode range, and a census of every command in all 42 shipped scripts
    /// finds no text-drawing command at all — the whole font story is two SelectFontSlot(0) and two
    /// LoadFontResource("GAME.FNT"). GAME.FNT is already what the dialog surface draws in, which is
    /// where a cutscene's visible text actually comes from.</para>
    ///
    /// <para>Driven through <see cref="AnimationCommandMap.GetAction"/> rather than the extension
    /// methods, because the map is what a frame actually goes through.</para>
    /// </summary>
    public class FontCommandTests {
        private GameObject _host;
        private CutsceneState _state;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("FontCommandHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), new Color[256]);
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        /// <summary>C21 and C42 issue exactly this pair, in this order.</summary>
        [Test]
        public void TheShippedFontPair_RunsWithoutThrowing() {
            var select = new SelectFontSlot { SlotNumber = 0 };
            var load = new LoadFontResource { Filename = "GAME.FNT" };

            Assert.DoesNotThrow(() => AnimationCommandMap.GetAction(select)(_state),
                "SelectFontSlot throwing is logged as a frame error, so the two cutscenes that use "
                + "it report a failure on every play");
            Assert.DoesNotThrow(() => AnimationCommandMap.GetAction(load)(_state),
                "LoadFontResource throwing is logged as a frame error");
        }

        /// <summary>Both stay mapped: dropping them from the map would make them UNKNOWN commands
        /// rather than deliberately empty ones, which is a different thing and warns differently.
        /// </summary>
        [Test]
        public void BothFontCommands_AreStillDispatched_NotDroppedFromTheMap() {
            Assert.IsNotNull(AnimationCommandMap.GetAction(new SelectFontSlot { SlotNumber = 0 }));
            Assert.IsNotNull(AnimationCommandMap.GetAction(new LoadFontResource { Filename = "GAME.FNT" }));
        }
    }
}
