namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A choice dialog whose panel is torn down while it is still opening must end its play.
    /// </summary>
    /// <remarks>
    /// TASK-641. <c>ShowEntryCore</c> awaits between building its panel and starting its wait (the
    /// keyword load, the speaker pill, the open wipe's geometry wait), and the wait read the panel
    /// back from the shared <c>_activePanel</c> field. A <c>ClearDialog</c> or a concurrent show in
    /// that window nulls the field, so the wait captured null — and TASK-563's "a detached panel ends
    /// its wait" guard reads null as "no panel to watch". The choice loop then polled a fresh answer
    /// box nobody can write, forever, and the play's <c>finally</c> never ran: the
    /// <c>GameSession.DialogsPlaying</c> count stayed up and froze the world loop (TASK-637).
    /// </remarks>
    public class DialogPlayEndsWhenItsPanelIsTornDownBeforeItsWaitTests {
        [SetUp]
        public void RequireShippedGameData() {
            ShippedGameData.RequireOrIgnore();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AChoiceShow_ClearedDuringItsOpenWipe_EndsItsPlay() =>
            UniTask.ToCoroutine(async () => {
                using (new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var settings = ScriptableObject.CreateInstance<PanelSettings>();
                    settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    settings.match = 1f;
                    settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        host.GetComponent<UIDocument>().panelSettings = settings;
                        DialogManager manager = host.AddComponent<DialogManager>();
                        manager.SetActivePalette(new Color[256]);

                        // A Yes/No record (0x200) with the open wipe on (flags otherwise 0).
                        System.Threading.Tasks.Task play = manager
                            .ShowEntry(new DialogEntry { Flags = DialogEntryFlags.TextWithChoice })
                            .AsTask();

                        // Until the panel is on the stage, then tear it down mid-open — before the
                        // show has reached its wait.
                        for (int frame = 0; frame < 300
                            && manager.GetComponent<UIDocument>().rootVisualElement?.Q("BakDialogPanel") == null; frame++) {
                            await UniTask.Yield();
                        }
                        Assert.IsNotEmpty(manager.OpenPlaysDescription,
                            "the play must be open before the teardown, or this proves nothing");
                        manager.ClearDialog();

                        for (int frame = 0; frame < 120 && !play.IsCompleted; frame++) {
                            await UniTask.Yield();
                        }

                        Assert.IsTrue(play.IsCompleted,
                            "a choice show torn down before its wait must end its play; still open: "
                            + manager.OpenPlaysDescription);
                        Assert.IsEmpty(manager.OpenPlaysDescription);
                    } finally {
                        Object.DestroyImmediate(host);
                        Object.DestroyImmediate(settings);
                    }
                }
            });
    }
}
