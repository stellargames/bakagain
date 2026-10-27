namespace BakAgain.UI {
    using BakAgain.ResourceManagement.Loaders;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The main menu's "Update v0.4.1 available. Click to download." (TASK-1017): a link between the
    /// title and Start New Game with a blue-white gleam and twinkling sparkles. Not in the original.
    /// Placed off Start New Game's REQ rect, so nothing here is a screen coordinate.
    /// </summary>
    internal static class UpdateNoticeView {
        internal const string Name = "update-notice";
        private const int StartNewGame = 49; // REQ_OPT0's first button (MenuActionHandler)
        private const int SparkleCount = 3;

        internal static void Add(VisualElement root, UserInterfaceLoader loader, string tag) {
            if (root.Q(Name) != null || loader == null || !loader.TryGetElementRect(StartNewGame, out Rect button)) {
                return;
            }
            // The title (in OPTIONS0.SCX's art) ends about 1.8 button heights above Start New Game; the
            // notice is centred in that gap, three buttons wide.
            var notice = new Button(() => Application.OpenURL(BakAgain.Core.UpdateCheck.DownloadUrl)) {
                name = Name,
                text = GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.UpdateAvailable, ("version", tag)),
                style = {
                    position = Position.Absolute,
                    left = button.center.x - button.width * 1.5f, width = button.width * 3f,
                    top = button.y - button.height * 1.4f, height = button.height,
                },
            };
            notice.AddToClassList(Name);

            var sparkles = new VisualElement[SparkleCount];
            var phases = new float[SparkleCount];
            var last = new float[SparkleCount];
            for (int i = 0; i < SparkleCount; i++) {
                sparkles[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                sparkles[i].AddToClassList("update-notice__sparkle");
                notice.Add(sparkles[i]);
                phases[i] = i / (float)SparkleCount;
                Place(sparkles[i]);
            }

            float start = Time.realtimeSinceStartup;
            notice.schedule.Execute(() => {
                float t = Time.realtimeSinceStartup - start;
                // The gleam: the glow breathes and the ink shimmers between ice blue and white.
                float glow = 0.5f + 0.5f * Mathf.Sin(t * 1.2f);
                notice.style.color = Color.Lerp(new Color(0.82f, 0.9f, 1f), new Color(0.94f, 0.97f, 1f), glow);
                notice.style.textShadow = new TextShadow {
                    offset = Vector2.zero, blurRadius = 4f + 6f * glow,
                    color = new Color(0.6f, 0.85f, 1f, 0.35f + 0.25f * glow),
                };
                // Each sparkle twinkles once every 3 s, then reappears somewhere else on the text.
                for (int i = 0; i < SparkleCount; i++) {
                    float p = (t / 3f + phases[i]) % 1f;
                    float s = p < 0.2f ? 0.8f * Mathf.Sin(p / 0.2f * Mathf.PI) : 0f;
                    if (p < last[i]) {
                        Place(sparkles[i]); // a new cycle: somewhere else
                    }
                    last[i] = p;
                    sparkles[i].style.opacity = s;
                    sparkles[i].style.scale = new Scale(new Vector2(s, s));
                }
            }).Every(33);

            BakAgain.UI.CanonicalStage.GetOrCreate(root, loader.Frame).Add(notice);
        }

        private static void Place(VisualElement sparkle) {
            sparkle.style.left = Length.Percent(Random.Range(8f, 92f));
            sparkle.style.top = Length.Percent(Random.Range(10f, 80f));
        }
    }
}
