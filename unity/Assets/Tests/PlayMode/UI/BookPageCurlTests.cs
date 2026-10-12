namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Book;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;

    /// <summary>
    /// The Enhanced page turn (TASK-872): the page on screen is grabbed, turned over the spine, and the
    /// sheet goes when the turn ends, leaving the next page. No book around the page: no turn.
    /// </summary>
    [RequiresShippedGameData] // the backdrop loads the player's DIALOG.SCX on start
    public class BookPageCurlTests {
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
            if (BookPageCurl.Exists) {
                Object.DestroyImmediate(BookPageCurl.Instance.gameObject);
            }
        }

        // A left-bound parchment, half a 4:3 stage, on an overlay canvas as BookView draws it.
        private RawImage BookPage() {
            _host = new GameObject("CurlHost");
            var canvas = _host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var image = new GameObject("Page").AddComponent<RawImage>();
            image.transform.SetParent(_host.transform, false);
            image.rectTransform.sizeDelta = new Vector2(Screen.height * 4f / 3f, Screen.height) * 0.5f;
            var page = new Texture2D(40, 30, TextureFormat.RGBA32, false);
            var pixels = new Color32[40 * 30];
            for (int i = 0; i < pixels.Length; i++) {
                bool torn = i % 40 == 39 && i / 40 % 5 != 0;   // the outer edge frayed, the spine straight
                pixels[i] = torn ? new Color32(0, 0, 0, 255) : new Color32(200, 180, 120, 255);
            }
            page.SetPixels32(pixels);
            page.Apply();
            image.texture = page;
            return image;
        }

        [UnityTest]
        public IEnumerator APageInABook_TurnsOverAndTheSheetGoes() => UniTask.ToCoroutine(async () => {
            RawImage page = BookPage();
            BookSpreadBackdrop.Show(page, (Texture2D)page.texture, facingPage: (Texture2D)page.texture);
            await UniTask.Yield();
            BookPageCurl curl = BookPageCurl.Instance;

            Assert.IsTrue(await curl.CaptureAsync(), "a page with the book drawn around it can be turned");
            int frames = 0;
            bool shownMidTurn = false;
            await curl.PlayAsync(() => {
                shownMidTurn |= curl.GetComponent<Canvas>().enabled;
                return ++frames > 5;
            });

            Assert.IsTrue(shownMidTurn, "the sheet is drawn while the page turns");
            Assert.IsFalse(curl.GetComponent<Canvas>().enabled, "and gone once it has");
        });

        [UnityTest]
        public IEnumerator APageWithNoBookAroundIt_IsNotTurned() => UniTask.ToCoroutine(async () => {
            BookPage();

            Assert.IsFalse(await BookPageCurl.Instance.CaptureAsync(), "nothing is drawn around it to turn it over: cut");
        });

        // The navigator asks before every transition; anything but a screen that draws an SCX page is
        // left to it, untouched.
        [UnityTest]
        public IEnumerator AScreenThatDrawsNoPage_IsLeftToTheCaller() => UniTask.ToCoroutine(async () => {
            RawImage page = BookPage();
            BookSpreadBackdrop.Show(page, (Texture2D)page.texture, facingPage: (Texture2D)page.texture);
            await UniTask.Yield();
            bool changed = false;

            bool turned = await BookPageCurl.TryTurnAsync(_host, () => { changed = true; return UniTask.CompletedTask; });

            Assert.IsFalse(turned);
            Assert.IsFalse(changed, "the caller makes the change itself");
        });
    }
}
