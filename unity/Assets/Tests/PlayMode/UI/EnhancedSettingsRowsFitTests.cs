namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <b>Every row of the Enhanced panel sits above Save and Cancel.</b> The columns do not scroll, so a
    /// row added to a full column runs under the buttons: the Shadows row did once, and the Combat speed
    /// row in 0.7.6 (forum p=96, its caption covered by Save).
    /// </summary>
    public class EnhancedSettingsRowsFitTests {
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator NoRowRunsUnderTheButtons() => UniTask.ToCoroutine(async () => {
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: DI has not built the screens");
            var screen = (EnhancedSettingsScreen)scope.Container.Resolve(typeof(EnhancedSettingsScreen));
            bool wasActive = screen.gameObject.activeSelf;
            try {
                await ((IScreen)screen).ShowAsync(fade: false);
                VisualElement root = screen.GetComponent<UIDocument>().rootVisualElement;
                for (int frame = 0; frame < 10; frame++) await UniTask.Yield();

                VisualElement buttons = root.Q(className: "enhanced-settings__buttons");
                Assert.NotNull(buttons, "the panel's button strip");
                List<VisualElement> rows = root.Query(className: "enhanced-settings__column")
                    .Children<VisualElement>().ToList();
                Assert.That(rows.Any(r => r.name == "combat_speed"), "the Combat speed row is on the panel");
                float top = buttons.worldBound.yMin;
                List<string> under = rows.Where(r => r.resolvedStyle.display != DisplayStyle.None && r.worldBound.yMax > top + 0.5f)
                    .Select(r => $"{(r as Button)?.Q<Label>("caption")?.text ?? r.name} (bottom {r.worldBound.yMax:0} > {top:0})").ToList();
                Assert.IsEmpty(under, "rows under Save/Cancel:\n  " + string.Join("\n  ", under));
            } finally {
                if (!wasActive) await ((IScreen)screen).HideAsync(fade: false);
                screen.gameObject.SetActive(wasActive);
            }
        });
    }
}
