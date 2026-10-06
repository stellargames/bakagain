namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using NUnit.Framework;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    /// A dialog still open when its manager is destroyed — leaving Play Mode, quitting the app — must
    /// tear down quietly.
    /// </summary>
    /// <remarks>
    /// Seen 2026-10-06: Play Mode was stopped over DDX 1800034, the wait ended on the teardown, and the
    /// <c>finally</c>'s <c>Deactivate</c> read <c>gameObject</c> on the destroyed manager. The
    /// MissingReferenceException was logged as "entry 1800034 threw while rendering", rethrown, and
    /// reached UniTask's unobserved-exception handler. <c>Deactivate</c> is called from all four exits
    /// of a play, so the guard is there.
    /// </remarks>
    public class DialogDeactivateAfterDestroyTests {
        [Test]
        public void Deactivate_OnADestroyedManager_DoesNotThrow() {
            var go = new GameObject("DialogManagerUnderTest");
            DialogManager dialogs = go.AddComponent<DialogManager>();
            Object.DestroyImmediate(go);

            MethodInfo deactivate = typeof(DialogManager).GetMethod("Deactivate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(deactivate, "Deactivate is the shipped teardown this guards");

            Assert.DoesNotThrow(() => {
                try {
                    deactivate.Invoke(dialogs, new object[] { null });
                } catch (TargetInvocationException e) {
                    throw e.InnerException!;
                }
            });
        }
    }
}
