namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <b>Every line of every page sits on the page above OK and Cancel, and every bookmark is in the
    /// window</b>, at 4:3, 16:9 and a 20:9 phone. The pages do not scroll (the key list excepted), so a
    /// line added to a full page runs off it: the Combat speed row ran under Save in 0.7.6 (forum p=96).
    /// </summary>
    public class EnhancedSettingsRowsFitTests : EnhancedSettingsBookFixture {
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EveryLineOfEveryPageFitsAboveOkAndCancel([Values(1600u, 1920u, 2400u)] uint width) =>
            UniTask.ToCoroutine(async () => {
                uint height = width == 1600u ? 1200u : 1080u;
                GameViewResolutionScope view = GameViewResolutionScope.Force(width, height);
                await Frames(2);
                try {
                    await OpenAsync();
                    var problems = new List<string>();
                    foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                        await ClickAsync("bookmark-" + EnhancedSettingsScreen.NameOf(page));
                        await Frames(2);
                        float top = Named("ok").worldBound.yMin;
                        foreach (SettingItem item in EnhancedSettingsPages.ItemsOn(page)) {
                            if (item.Kind == SettingKind.KeyList) continue;
                            VisualElement line = Named(item.Id);
                            if (line.worldBound.yMax > top + 0.5f) problems.Add($"{page}/{item.Id} bottom {line.worldBound.yMax:0} > OK top {top:0}");
                        }
                        Rect window = Root.worldBound;
                        foreach (SettingsPage p in EnhancedSettingsPages.Order) {
                            Rect b = Named("bookmark-" + EnhancedSettingsScreen.NameOf(p)).worldBound;
                            if (b.xMax > window.xMax + 0.5f || b.xMin < window.xMin - 0.5f)
                                problems.Add($"{page}: bookmark {p} {b.xMin:0}..{b.xMax:0} outside the window {window.xMin:0}..{window.xMax:0}");
                        }
                    }
                    Assert.IsEmpty(problems, $"{width}x{height}:\n  " + string.Join("\n  ", problems));
                } finally {
                    await CloseAsync();
                    view.Restore();
                }
            });
    }
}
