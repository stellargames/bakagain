namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using BakAgain.UI;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using UnityEngine.UIElements;

    /// <summary>
    /// The open book around a full-screen parchment: the facing page across the straight edge, the
    /// shade on the spine, the table everywhere else. It follows whatever surface the page is drawn
    /// on, and goes when that surface leaves the screen.
    /// </summary>
    [RequiresShippedGameData] // the facing page is the player's DIALOG.SCX
    public class BookSpreadBackdropTests {
        private readonly BookSpreadLayout _layout = new();
        private GameObject _host;

        [SetUp]
        public void SetUp() {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
            }
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_host);
            _host = null;
            if (BookSpreadBackdrop.Exists) {
                Object.DestroyImmediate(BookSpreadBackdrop.Instance.gameObject);
            }
        }

        // A page whose edge columns are opaque (straight) or mostly transparent (torn).
        private static Texture2D Page(bool leftStraight, bool rightStraight) {
            var page = new Texture2D(40, 30, TextureFormat.RGBA32, false);
            var pixels = new Color32[40 * 30];
            for (int y = 0; y < 30; y++) {
                for (int x = 0; x < 40; x++) {
                    bool torn = (x == 0 && !leftStraight) || (x == 39 && !rightStraight);
                    pixels[y * 40 + x] = new Color32(200, 180, 120, (byte)(torn && y % 5 != 0 ? 0 : 255));
                }
            }
            page.SetPixels32(pixels);
            page.Apply();
            return page;
        }

        // A uGUI surface half the size of a 4:3 stage, centred, as BookView and CutSceneView draw theirs.
        private RawImage Surface(string name = "Page") {
            _host ??= new GameObject("BookSpreadHost");
            var canvasObject = new GameObject(name + "Canvas");
            canvasObject.transform.SetParent(_host.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var image = new GameObject(name).AddComponent<RawImage>();
            image.transform.SetParent(canvasObject.transform, false);
            image.rectTransform.sizeDelta = new Vector2(Screen.height * 4f / 3f, Screen.height) * 0.5f;
            return image;
        }

        // DIALOG.SCX arrives asynchronously; the facing page is drawn once it has.
        private static IEnumerator FacingPageLoaded() {
            for (int f = 0; f < 300 && !BookSpreadBackdrop.Instance.FacingPage.GetComponent<RawImage>().enabled; f++) {
                yield return null;
            }
            yield return null;
        }

        private static Rect ScreenRect(RectTransform rect) {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        [UnityTest]
        public IEnumerator ALeftBoundPage_HasTheFacingPageAndShadeOnItsLeft() {
            RawImage surface = Surface();
            BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
            yield return FacingPageLoaded();

            BookSpreadBackdrop b = BookSpreadBackdrop.Instance;
            Rect stage = ScreenRect(surface.rectTransform);
            Rect facing = ScreenRect(b.FacingPage);
            Rect shade = ScreenRect(b.Shade);
            Assert.AreEqual(SpineSide.Left, b.Spine);
            Assert.IsTrue(b.Drawn);
            Assert.AreEqual(stage.xMin, facing.xMax, 0.5f, "the facing page meets the straight edge");
            Assert.AreEqual(stage.yMin - _layout.FacingPageDrop * stage.height, facing.yMin, 0.5f, "and sits a little lower");
            Assert.AreEqual(stage.width, facing.width, 0.5f);
            Assert.IsTrue(b.FacingPage.GetComponent<RawImage>().enabled, "DIALOG.SCX is drawn as the facing page");
            Assert.AreEqual(-1f, b.FacingPage.GetComponent<RawImage>().uvRect.width, "mirrored: DIALOG.SCX is straight on its left too");
            Assert.AreEqual(stage.xMin, shade.center.x, 0.5f, "the shade is centred on the spine");
            Assert.AreEqual(_layout.ShadeWidth * stage.height, shade.width, 0.5f);
        }

        // The full map is a loose sheet on the table (owner, 2026-10-12): the table and the sheet's
        // shadow, no facing page, no binding, and no spine for a page turn to find.
        [UnityTest]
        public IEnumerator ASheet_LiesOnTheTableWithItsShadowAndNoBook() {
            RawImage surface = Surface();
            BookSpreadBackdrop.ShowSheet(surface);
            yield return null;

            BookSpreadBackdrop b = BookSpreadBackdrop.Instance;
            Rect stage = ScreenRect(surface.rectTransform);
            Rect shadow = ScreenRect(b.SheetShadow.rectTransform);
            Assert.IsTrue(b.Drawn, "the table is drawn");
            Assert.AreEqual(SpineSide.NotAPage, b.Spine, "not a page: nothing turns it");
            Assert.IsTrue(b.SheetShadow.enabled);
            Assert.IsFalse(b.FacingPage.GetComponent<RawImage>().enabled, "no facing page");
            Assert.IsFalse(b.Shade.GetComponentInParent<Canvas>().enabled, "no binding");
            float reach = _layout.SheetShadowWidth * stage.height;
            Assert.AreEqual(stage.xMin - reach, shadow.xMin, 0.5f, "the shadow reaches past the sheet");
            Assert.Less(shadow.yMin, stage.yMin - reach, "and falls a little below it");

            surface.enabled = false;
            yield return null;
            Assert.IsFalse(b.Drawn, "and goes with the sheet");
        }

        // The book's facing page is its own parchment, mirrored and level, so top and bottom line up
        // (owner, 2026-10-12): DIALOG.SCX's tears sit 12-18 px in from the edge, the book's at 0-9.
        [UnityTest]
        public IEnumerator AFacingPageOfItsOwn_IsDrawnLevelAndMirrored() {
            RawImage surface = Surface();
            Texture2D page = Page(leftStraight: true, rightStraight: false);
            BookSpreadBackdrop.Show(surface, page, facingPage: page);
            yield return null;

            Rect stage = ScreenRect(surface.rectTransform);
            RawImage facing = BookSpreadBackdrop.Instance.FacingPage.GetComponent<RawImage>();
            Assert.IsTrue(facing.enabled);
            Assert.AreEqual(stage.yMin, ScreenRect(facing.rectTransform).yMin, 0.5f, "level with the shown page");
            Assert.AreEqual(stage.xMin, ScreenRect(facing.rectTransform).xMax, 0.5f);
            Assert.AreEqual(-1f, facing.uvRect.width, "mirrored: its straight edge on the binding");
            Assert.AreEqual(page.width, facing.texture.width, "the page itself, not DIALOG.SCX");
        }

        [UnityTest]
        public IEnumerator ARightBoundPage_HasThemOnItsRight() {
            RawImage surface = Surface();
            BookSpreadBackdrop.Show(surface, Page(leftStraight: false, rightStraight: true));
            yield return FacingPageLoaded();

            BookSpreadBackdrop b = BookSpreadBackdrop.Instance;
            Rect stage = ScreenRect(surface.rectTransform);
            Assert.AreEqual(SpineSide.Right, b.Spine);
            Assert.AreEqual(stage.xMax, ScreenRect(b.FacingPage).xMin, 0.5f);
            Assert.AreEqual(1f, b.FacingPage.GetComponent<RawImage>().uvRect.width, "DIALOG.SCX's straight edge is already on the binding side");
            Assert.AreEqual(stage.xMax, ScreenRect(b.Shade).center.x, 0.5f);
        }

        // A panel torn down both sides (CAST.SCX's lower third) is not a bound page: no book, no table.
        [UnityTest]
        public IEnumerator APageFrayedOnBothSides_DrawsNothing() {
            BookSpreadBackdrop.Show(Surface(), Page(leftStraight: false, rightStraight: false));
            yield return null;

            Assert.AreEqual(SpineSide.NotAPage, BookSpreadBackdrop.Instance.Spine);
            Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn);
        }

        [UnityTest]
        public IEnumerator AnOpaqueImage_IsNotAPage_AndHidesWhatIsBelow() {
            BookSpreadBackdrop.Show(Surface("Menu"), Page(leftStraight: true, rightStraight: false));
            BookSpreadBackdrop.Show(Surface("Picture"), Page(leftStraight: true, rightStraight: true));
            yield return null;

            Assert.AreEqual(SpineSide.NotAPage, BookSpreadBackdrop.Instance.Spine);
            Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn);
        }

        [UnityTest]
        public IEnumerator TheLatestLiveSurfaceWins_AndTheOneBelowReturns() {
            RawImage below = Surface("Cutscene");
            RawImage above = Surface("Book");
            BookSpreadBackdrop.Show(below, Page(leftStraight: true, rightStraight: false));
            BookSpreadBackdrop.Show(above, Page(leftStraight: false, rightStraight: true));
            yield return null;
            Assert.AreEqual(SpineSide.Right, BookSpreadBackdrop.Instance.Spine);

            above.canvas.enabled = false;   // BookView.Hide
            yield return null;
            Assert.AreEqual(SpineSide.Left, BookSpreadBackdrop.Instance.Spine);
        }

        [UnityTest]
        public IEnumerator ASurfaceThatLeavesTheScreen_TakesTheBookWithIt() {
            RawImage surface = Surface();
            BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
            yield return null;
            Assert.IsTrue(BookSpreadBackdrop.Instance.Drawn);

            surface.canvas.gameObject.SetActive(false);   // no Hide call
            yield return null;
            Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn);
            Assert.AreEqual(SpineSide.NotAPage, BookSpreadBackdrop.Instance.Spine);
        }

        [UnityTest]
        public IEnumerator TheSpreadFollowsTheSurfaceWhenItMoves() {
            RawImage surface = Surface();
            BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
            yield return null;

            surface.rectTransform.sizeDelta *= 0.5f;   // a resize or rotation
            yield return null;
            Assert.AreEqual(ScreenRect(surface.rectTransform).xMin, ScreenRect(BookSpreadBackdrop.Instance.FacingPage).xMax, 0.5f);
        }

        [UnityTest]
        public IEnumerator AUiToolkitStage_IsFollowedUntilItsDocumentGoes() {
            _host = new GameObject("StageHost");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                var document = _host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var stage = new VisualElement { style = { position = Position.Absolute, left = 100, top = 0, width = 400, height = 300 } };
                document.rootVisualElement.Add(stage);
                BookSpreadBackdrop.Show(stage, Page(leftStraight: true, rightStraight: false));
                for (int f = 0; f < 3; f++) {
                    yield return null;
                }
                Assert.IsTrue(BookSpreadBackdrop.Instance.Drawn);
                float scale = Screen.height / stage.panel.visualTree.layout.height;
                Assert.AreEqual(100f * scale, ScreenRect(BookSpreadBackdrop.Instance.FacingPage).xMax, 1f);

                _host.SetActive(false);
                yield return null;
                Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn);
            } finally {
                Object.DestroyImmediate(settings);
            }
        }

        // ScreenBase.HideAsync fades a leaving screen's root to transparent before it deactivates: the
        // book goes with its page instead of standing round an empty slot of table (seen 2026-10-22,
        // main menu -> chapter page).
        [UnityTest]
        public IEnumerator AStageFadingOut_TakesTheBookWithIt() {
            _host = new GameObject("StageHost");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                var document = _host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var stage = new VisualElement { style = { position = Position.Absolute, left = 100, top = 0, width = 400, height = 300 } };
                document.rootVisualElement.Add(stage);
                BookSpreadBackdrop.Show(stage, Page(leftStraight: true, rightStraight: false));
                for (int f = 0; f < 3; f++) {
                    yield return null;
                }
                Assert.AreEqual(1f, BookLight(), 1e-4f);

                document.rootVisualElement.style.opacity = 0.25f;
                for (int f = 0; f < 3; f++) {
                    yield return null;
                }
                Assert.AreEqual(0.25f, BookLight(), 0.01f);
            } finally {
                Object.DestroyImmediate(settings);
            }
        }

        // The full map fades with a CanvasGroup (FullMapView.FadeAsync): the table goes with the sheet.
        [UnityTest]
        public IEnumerator ASheetFadingOut_TakesTheTableWithIt() {
            RawImage surface = Surface();
            BookSpreadBackdrop.ShowSheet(surface);
            yield return null;
            Assert.AreEqual(1f, BookSpreadBackdrop.Instance.TableImage.color.r, 1e-4f);

            surface.canvas.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
            yield return null;
            Assert.AreEqual(0.4f, BookSpreadBackdrop.Instance.TableImage.color.r, 0.01f);
        }

        [UnityTest]
        public IEnumerator AStageNotYetLaidOut_DrawsNothing() {
            _host = new GameObject("StageHost");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                var document = _host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var stage = new VisualElement();
                document.rootVisualElement.Add(stage);
                BookSpreadBackdrop.Show(stage, Page(leftStraight: true, rightStraight: false));
                Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn, "Show itself draws nothing before layout");
                yield return null;
            } finally {
                Object.DestroyImmediate(settings);
            }
        }

        // The travel frame's edge is a dark brown (~20,8,4) and the tears are the palette's black, opaque
        // as loaded: only exact black is torn, however dark the paper.
        [UnityTest]
        public IEnumerator ADarkEdgeIsPaper_OnlyExactBlackIsTorn() {
            var page = new Texture2D(40, 30, TextureFormat.RGBA32, false);
            var pixels = new Color32[40 * 30];
            for (int y = 0; y < 30; y++) {
                for (int x = 0; x < 40; x++) {
                    pixels[y * 40 + x] = x == 0 ? new Color32(20, 8, 4, 255)
                        : x == 39 && y % 5 != 0 ? new Color32(0, 0, 0, 255)
                        : new Color32(200, 180, 120, 255);
                }
            }
            page.SetPixels32(pixels);
            page.Apply();

            BookSpreadBackdrop.Show(Surface(), page);
            yield return null;
            Assert.AreEqual(SpineSide.Left, BookSpreadBackdrop.Instance.Spine);
        }

        [Test]
        public void APageIsDrawnFromAnUntornCopy_APictureAsItIs() {
            Texture2D page = Page(leftStraight: true, rightStraight: false);
            Texture2D picture = Page(leftStraight: true, rightStraight: true);

            Texture2D drawn = BookSpreadBackdrop.WithTearsOpen(page);
            Assert.AreNotSame(page, drawn, "the tears are cleared in a copy");
            Assert.AreEqual(page.width, drawn.width);
            Assert.AreSame(drawn, BookSpreadBackdrop.WithTearsOpen(page), "made once per page");
            Assert.AreSame(picture, BookSpreadBackdrop.WithTearsOpen(picture));
        }

        [UnityTest]
        public IEnumerator TheMainMenusBackground_ShowsTheBook_UntilTheScreenCloses() {
            _host = new GameObject("MenuHost");
            _host.SetActive(false);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                _host.AddComponent<UIDocument>().panelSettings = settings;
                _host.AddComponent<BakAgain.ResourceManagement.Loaders.BackgroundImageLoader>();   // OPTIONS0.SCX by default
                _host.SetActive(true);
                for (int f = 0; f < 300 && !(BookSpreadBackdrop.Exists && BookSpreadBackdrop.Instance.Drawn); f++) {
                    yield return null;
                }
                Assert.IsTrue(BookSpreadBackdrop.Exists, "the menu asked for the book");
                Assert.AreEqual(SpineSide.Left, BookSpreadBackdrop.Instance.Spine, "OPTIONS0.SCX is straight on the left");
                yield return FacingPageLoaded();
                Assert.IsTrue(BookSpreadBackdrop.Instance.FacingPage.GetComponent<RawImage>().enabled,
                    "the facing page loads even though the menu asked from inside its own load's callback");

                _host.SetActive(false);   // what the navigator does on Pop
                yield return null;
                Assert.IsFalse(BookSpreadBackdrop.Instance.Drawn);
            } finally {
                Object.DestroyImmediate(settings);
            }
        }

        // ------------------------------------------------------------------ cutscene palette fades
        // The original fades its whole 320x200 screen (TTM.C:554 fade-out, :582 fade-in, palette_set_scaled), so the book
        // around a page fades with it: the table, the facing page and the crease, not the page alone.

        private CutsceneState CutsceneOn(RawImage surface) {
            surface.texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var palette = new Color[256];
            for (int i = 0; i < 256; i++) {
                palette[i] = new Color(i / 255f, i / 255f, i / 255f, 1f);
            }
            return new CutsceneState(new UiImage(surface), palette);
        }

        // The light on the book: the table's, after checking the facing page and the crease agree.
        private float BookLight() {
            BookSpreadBackdrop b = BookSpreadBackdrop.Instance;
            Color table = b.TableImage.color;
            Color shade = b.Shade.GetComponent<RawImage>().color;
            Assert.AreEqual(table, b.FacingImage.color, "the facing page has the table's light");
            Assert.AreEqual(table.r, shade.r, 1e-4f, "and so does the crease");
            Assert.AreEqual(_layout.ShadeOpacity, shade.a, 1e-4f, "the crease keeps its own opacity");
            return table.r;
        }

        [UnityTest]
        public IEnumerator AWholeScreenFadeOut_TakesTheBookDownWithThePage() => UniTask.ToCoroutine(async () => {
            RawImage surface = Surface();
            CutsceneState state = CutsceneOn(surface);
            try {
                BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
                await UniTask.Yield();
                Assert.AreEqual(1f, BookLight(), 1e-4f, "lit before the fade");

                // C11's last frame: the screen filled black, then FadeOut(0, 256, 0, 4) ramps the frame
                // as it began down to black over ~1.1 s.
                await new FillArea { X = 0, Y = 0, Width = 1600, Height = 1200 }.ToAction()(state);
                Awaitable fade = new FadeOut { Start = 0, Length = 256, Color = 0, Speed = 4 }.ToAction()(state);
                try {
                    await UniTask.Delay(400, ignoreTimeScale: true);
                    float mid = BookLight();
                    Assert.Less(mid, 0.95f, "the book dims while the page does");
                    Assert.Greater(mid, 0.05f, "gradually, not at once");
                } finally {
                    await fade;
                }
                Assert.AreEqual(0f, BookLight(), 1e-4f, "and stays dark: the page under it was filled black");
            } finally {
                state.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator AFadeOutOfAPageThatIsStillThere_LightsTheBookAgain() => UniTask.ToCoroutine(async () => {
            // C62 frame 5 and C93 frame 92 fill only the picture, then fade the whole palette: when the
            // palette comes back (TTM.C:578) the parchment is lit again, so the book must be too.
            RawImage surface = Surface();
            CutsceneState state = CutsceneOn(surface);
            try {
                BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
                await new FillArea { X = 75, Y = 66, Width = 1445, Height = 606 }.ToAction()(state);
                await new FadeOut { Start = 0, Length = 256, Color = 0, Speed = 1 }.ToAction()(state);
                Assert.AreEqual(1f, BookLight(), 1e-4f);
            } finally {
                state.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator AFadeOfThePictureOnly_LeavesTheBookLit() => UniTask.ToCoroutine(async () => {
            // FadeOut(16, 240, ...) dims the picture inside the page. The parchment is palette 1-15
            // (DIALOG.SCX uses 0-15 only), so the page stays lit and so does the book.
            RawImage surface = Surface();
            CutsceneState state = CutsceneOn(surface);
            try {
                BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
                Awaitable fade = new FadeOut { Start = 16, Length = 240, Color = 16, Speed = 4 }.ToAction()(state);
                try {
                    await UniTask.Delay(400, ignoreTimeScale: true);
                    Assert.AreEqual(1f, BookLight(), 1e-4f, "lit through the fade");
                } finally {
                    await fade;
                }
                Assert.AreEqual(1f, BookLight(), 1e-4f, "and after it");
            } finally {
                state.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator AFadeInThatReachesTheParchment_BringsTheBookUpFromBlack() => UniTask.ToCoroutine(async () => {
            // C72 opens with FadeIn(0, 239, ...): not the whole palette, but all of the parchment.
            RawImage surface = Surface();
            CutsceneState state = CutsceneOn(surface);
            try {
                BookSpreadBackdrop.Show(surface, Page(leftStraight: true, rightStraight: false));
                Awaitable fade = new FadeIn { Start = 0, Length = 239, Color = 0, Speed = 3 }.ToAction()(state);
                try {
                    Assert.Less(BookLight(), 0.1f, "the first step is drawn before the fade waits: black");
                } finally {
                    await fade;
                }
                Assert.AreEqual(1f, BookLight(), 1e-4f);
            } finally {
                state.Dispose();
            }
        });
    }
}
