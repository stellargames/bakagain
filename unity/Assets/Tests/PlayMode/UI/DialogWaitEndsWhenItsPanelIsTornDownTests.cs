namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System;
    using System.Collections;
    using System.Reflection;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A dialog's wait must end when its own panel is torn down, not only when the player dismisses
    /// it or a caller cancels.
    /// </summary>
    /// <remarks>
    /// TASK-563. <c>RemovePanel</c> detaches the panel and the scrim and nulls <c>_choiceResult</c>,
    /// and before this fix neither wait could observe any of it: <c>WaitForDismiss</c> spins on a
    /// pointer event a detached scrim can never receive. Anything that takes the screen down from
    /// outside — a navigator pop, the camp screen opening — therefore stranded the awaiting task for
    /// the rest of the session.
    ///
    /// <para>Measured 2026-09-16 in the walkthrough: the travel screen played a condition
    /// announcement (dialog 0x40) while the party walked, the camp screen tore its panel down, and
    /// <c>PartyUpkeepService._announcing</c> stayed true — which wedges camping, because
    /// <c>CampMenu.RestAsync</c>'s <c>finally</c> is the only writer of <c>LastRestTicks</c> and
    /// <c>RestQuality</c>. <c>ShowById</c> has ~120 production callers and every one shared the
    /// exposure, which is why the guard belongs here rather than in any one caller.</para>
    ///
    /// <para>The wait is driven through the REAL private method by reflection: it touches only its
    /// <c>scrim</c> argument and <c>Time</c>, so it needs no DI, no resources and no constructed
    /// screen — but it is the shipped loop, not a re-implementation of it.</para>
    /// </remarks>
    public class DialogWaitEndsWhenItsPanelIsTornDownTests {
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator WaitForDismiss_EndsWhenItsScrimIsDetached_NotOnlyOnItsOwnDismissal() =>
            UniTask.ToCoroutine(async () => {
                var go = new GameObject("DialogManagerUnderTest");
                PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                try {
                    UIDocument document = go.AddComponent<UIDocument>();
                    document.panelSettings = panelSettings;
                    DialogManager dialogs = go.AddComponent<DialogManager>();

                    var scrim = new VisualElement { name = "Scrim" };
                    document.rootVisualElement.Add(scrim);
                    await UniTask.Yield();
                    Assert.IsNotNull(scrim.panel,
                        "the scrim must start ATTACHED, or the test proves nothing about detaching it");

                    MethodInfo wait = typeof(DialogManager).GetMethod("WaitForDismiss",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.IsNotNull(wait, "WaitForDismiss is the shipped wait this fix guards");

                    // dismissed: () => false and no auto-dismiss deadline, so the ONLY thing that can
                    // end this wait is the detach under test.
                    var running = (UniTask)wait.Invoke(dialogs, new object[] {
                        scrim, (Func<bool>)(() => false), CancellationToken.None, null,
                    });
                    System.Threading.Tasks.Task observed = running.AsTask();

                    for (var frame = 0; frame < 10; frame++) {
                        await UniTask.Yield();
                    }
                    Assert.IsFalse(observed.IsCompleted,
                        "the wait must still be running while its scrim is attached and undismissed — "
                        + "otherwise the assertion below would pass for the wrong reason");

                    scrim.RemoveFromHierarchy();

                    var guard = 0;
                    while (!observed.IsCompleted && guard < 300) {
                        await UniTask.Yield();
                        guard++;
                    }

                    Assert.IsTrue(observed.IsCompleted,
                        "a wait whose scrim has been torn down must end: before TASK-563's fix it "
                        + "span forever, stranding PlayAnnouncementsAsync with _announcing true and "
                        + "wedging every later rest for the session");
                    Assert.IsFalse(observed.IsFaulted,
                        "the teardown is a normal end to the wait, not a fault — RestAsync's finally "
                        + "must run, not be skipped by an exception the caller does not expect");
                } finally {
                    UnityEngine.Object.DestroyImmediate(go);
                    UnityEngine.Object.DestroyImmediate(panelSettings);
                }
            });
    }
}
