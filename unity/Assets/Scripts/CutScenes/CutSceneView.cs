namespace BakAgain.CutScenes {
    using BakAgain.Graphics;
    using UnityEngine;
    using UnityEngine.UI;
    using VContainer;

    public class CutSceneView : MonoBehaviour, ICutsceneView {
        // While the original resolution is 320x200, the aspect ratio it was designed for is 4:3
        private const float OriginalGameAspectRatio = 4f / 3f;

        [SerializeField]
        private Canvas cutSceneCanvas;

        private int _lastScreenHeight;
        private int _lastScreenWidth;
        private RawImage _rawImage;
        private CutscenePresenter _presenter;

        public UiImage Canvas => new(_rawImage);

        private void Awake() {
            _rawImage = cutSceneCanvas.GetComponent<RawImage>();
            if (!_rawImage) {
                var backgroundObject = new GameObject("Background");
                backgroundObject.transform.SetParent(cutSceneCanvas.transform);
                _rawImage = backgroundObject.AddComponent<RawImage>();
            }

            PrepareCanvas();
            Hide();
        }

        private void Update() {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) {
                UpdateAspectRatio();
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
            }
        }

        private void OnApplicationQuit() {
            _presenter?.Cancel();
        }

        [Inject]
        public void Construct(CutscenePresenter presenter) {
            _presenter = presenter;
        }

        public void Show() {
            // *** THE CANVAS OUTLIVES THE CUTSCENE, SO IT MUST BE BLANKED ON THE WAY IN. ***
            // This view is reused for every cutscene, and its texture still holds the LAST frame of
            // the previous one. Showing it without clearing let a frame of the intro sit on screen
            // until the next scene's first draw landed — visible when starting a new game from the
            // main menu, reported from play. Creating the texture black was not enough: that only
            // covers the first use.
            ClearCanvasToBlack();
            cutSceneCanvas.enabled = true;
        }

        /// <summary>Fills the canvas with opaque black — a cutscene always begins from black.</summary>
        private void ClearCanvasToBlack() {
            if (_rawImage == null || _rawImage.texture is not Texture2D canvas) {
                return;
            }

            var blank = new Color32[canvas.width * canvas.height];
            for (var i = 0; i < blank.Length; i++) {
                blank[i] = new Color32(0, 0, 0, 255);
            }
            canvas.SetPixels32(blank);
            canvas.Apply();
        }

        public void Hide() {
            cutSceneCanvas.enabled = false;
        }

        public Cysharp.Threading.Tasks.UniTask ShowAsync() {
            Show();
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        public Cysharp.Threading.Tasks.UniTask HideAsync() {
            Hide();
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        private void UpdateAspectRatio() {
            float screenAspect = (float)Screen.width / Screen.height;
            _rawImage.rectTransform.sizeDelta = screenAspect > OriginalGameAspectRatio
                ? new Vector2((int)(Screen.height * OriginalGameAspectRatio), Screen.height)
                : new Vector2(Screen.width, (int)(Screen.width / OriginalGameAspectRatio));
        }

        private void PrepareCanvas() {
            int width = Screen.width;
            int height = Screen.height;

            if ((float)width / height > OriginalGameAspectRatio) {
                width = (int)(height * OriginalGameAspectRatio);
            } else {
                height = (int)(width / OriginalGameAspectRatio);
            }

            var canvasTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            // *** A NEW Texture2D CONTAINS GARBAGE, AND IT WAS BEING SHOWN. *** Apply() alone
            // uploads whatever the allocation happened to hold — in practice a bright, near-white
            // fill. The navigator fades this view IN when it is pushed, so the player saw that
            // garbage fade up before the cutscene's own opening FadeOut(0,256) blacked it out: a
            // white screen fading in and out before the intro, reported from play.
            canvasTexture.Apply();
            _rawImage.texture = canvasTexture;
            ClearCanvasToBlack();

            // Set letterboxing
            UpdateAspectRatio();
            // Set anchors to center
            _rawImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rawImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            // Set pivot to center
            _rawImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            // Set position to zero
            _rawImage.rectTransform.anchoredPosition = Vector2.zero;
        }
    }
}
