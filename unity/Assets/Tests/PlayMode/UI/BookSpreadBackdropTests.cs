namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
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

        [UnityTest]
        public IEnumerator ALoosePage_GetsOnlyTheTable() {
            BookSpreadBackdrop.Show(Surface(), Page(leftStraight: false, rightStraight: false));
            yield return null;

            BookSpreadBackdrop b = BookSpreadBackdrop.Instance;
            Assert.AreEqual(SpineSide.Loose, b.Spine);
            Assert.IsTrue(b.Drawn, "the table is drawn");
            Assert.IsFalse(b.FacingPage.GetComponent<RawImage>().enabled);
            Assert.IsFalse(b.Shade.GetComponentInParent<Canvas>().enabled);
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

            Texture2D drawn = BookSpreadBackdrop.Show(Surface("Page"), page);
            Assert.AreNotSame(page, drawn, "the tears are cleared in a copy");
            Assert.AreEqual(page.width, drawn.width);
            Assert.AreSame(drawn, BookSpreadBackdrop.Show(Surface("Again"), page), "made once per page");
            Assert.AreSame(picture, BookSpreadBackdrop.Show(Surface("Picture"), picture));
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
    }
}
