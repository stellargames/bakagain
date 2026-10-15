namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A dialog over Enhanced full-screen travel sits in the travel frame's wood (TASK-867): shown
    /// while the world fills the window, never on the faithful screen, which draws its own.
    /// </summary>
    public class DialogTravelFrameTests {
        [SetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();   // FRAME.SCX is a real load

        [UnityTest]
        public IEnumerator FullScreenWorld_TheDialogGetsTheFrame() => UniTask.ToCoroutine(async () => {
            var (found, strips, display) = await Render(fullScreenOption: true, worldFillsWindow: true);
            Assert.IsTrue(found, "a boxed dialog over full-screen travel gets the frame");
            Assert.AreEqual(5, strips, "four strips around the viewport and the compass patch: the world shows through the middle");
            Assert.AreEqual(DisplayStyle.Flex, display);
        });

        [UnityTest]
        public IEnumerator WorldInTheFaithfulRect_TheFrameStaysHidden() => UniTask.ToCoroutine(async () => {
            var (found, _, display) = await Render(fullScreenOption: true, worldFillsWindow: false);
            Assert.IsTrue(found);
            Assert.AreEqual(DisplayStyle.None, display,
                "a fight or the faithful screen already draws its frame there");
        });

        [UnityTest]
        public IEnumerator OptionOff_NoFrame() => UniTask.ToCoroutine(async () => {
            Assert.IsFalse((await Render(fullScreenOption: false, worldFillsWindow: true)).Found);
        });

        private static async UniTask<(bool Found, int Strips, DisplayStyle Display)> Render(bool fullScreenOption, bool worldFillsWindow) {
            bool enhanced = GameOptions.Enhanced, fullScreen = GameOptions.GetFeature(EnhancedFeature.FullScreenTravel);
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, fullScreenOption);
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
                try {
                    host.GetComponent<UIDocument>().panelSettings = settings;
                    VisualElement root = host.GetComponent<UIDocument>().rootVisualElement;
                    // The travel screen's world view, as the frame finds it: window-sized under
                    // full-screen, a narrow rect otherwise.
                    root.Add(new VisualElement {
                        name = "WorldViewport",
                        style = {
                            position = Position.Absolute, left = 0, top = 0,
                            width = worldFillsWindow ? 4000 : 10, height = worldFillsWindow ? 3000 : 10,
                        },
                    });
                    DialogManager manager = host.AddComponent<DialogManager>();
                    typeof(DialogManager).GetField("_worldViewport", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(manager, new BakAgain.World.WorldViewport());
                    manager.SetActivePalette(new Color[256]);
                    await manager.DisplayEntry(new DialogEntry {
                        Text = "\tGorath seemed distant.",
                        DialogType = DialogType.Normal,
                        Flags = DialogEntryFlags.Legacy10 | DialogEntryFlags.SkipOpenWipe,
                    });
                    for (int i = 0; i < 3; i++) await UniTask.Yield();
                    VisualElement frame = root.Q("BakDialogTravelFrame");
                    return (frame != null, frame?.childCount ?? 0, frame?.resolvedStyle.display ?? DisplayStyle.None);
                } finally {
                    Object.DestroyImmediate(host);
                    Object.DestroyImmediate(settings);
                    GameOptions.Enhanced = enhanced;
                    GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, fullScreen);
                }
            }
        }
    }
}
