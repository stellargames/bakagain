namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Core.States;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Loaders;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Chapter marking + click for the Contents screen. The REQ chapter entries are Visible=false
    /// TextButtons (Hidden → unpickable), so instead of toggling them this component builds its own
    /// transparent click areas at the reached chapters' REQ rects (from the catalog's ContentsActionId
    /// + UserInterfaceLoader.TryGetElementRect) and reveals CONT2.SCX over the locked rows — the
    /// original's cont2 blit, boundary taken from the first locked chapter's rect (canonical, no VGA).
    /// </summary>
    [RequireComponent(typeof(UserInterfaceLoader))]
    public sealed class ContentsChapterOverlay : MonoBehaviour {
        private const string Cont2Address = "CONT2.SCX";

        /// <summary>Raised with the chapter Number when a reached chapter's click area is left-clicked.</summary>
        public event Action<int> ChapterClicked;

        private UserInterfaceLoader _loader;
        private IResourceProviderService _resources;
        private static readonly ILogger _logger = LogManager.LoggerFactory.CreateLogger(nameof(ContentsChapterOverlay));
        private int _currentChapter = 1;
        private VisualElement _root;   // canonical stage
        private readonly List<VisualElement> _added = new();
        private int _generation;

        private void Awake() {
            _loader = GetComponent<UserInterfaceLoader>();
        }

        /// <summary>Supply the resource service. Called by the sibling <see cref="ContentsMenu"/>
        /// (which IS [Inject]-ed) at construction — this component is a sibling and not injected
        /// directly. Safe before the loader builds; the first Configure/Built triggers the rebuild.</summary>
        public void Init(IResourceProviderService resources) {
            _resources = resources;
        }

        private void OnEnable() {
            _loader.Built += OnBuilt;
            _loader.Cleared += OnCleared;
            if (_loader.IsBuilt) {
                RebuildAsync().Forget();
            }
        }

        private void OnDisable() {
            _loader.Built -= OnBuilt;
            _loader.Cleared -= OnCleared;
            Clear();
        }

        /// <summary>Set the furthest reached chapter (call before the screen is shown / rebuilt).</summary>
        public void Configure(int currentChapter) {
            _currentChapter = currentChapter < 1 ? 1 : currentChapter;
            if (_loader != null && _loader.IsBuilt) {
                RebuildAsync().Forget();
            }
        }

        private void OnBuilt(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> _) => RebuildAsync().Forget();
        private void OnCleared() => Clear();

        /// <summary>(Re)builds the chapter click areas + CONT2 overlay. Generation-guarded: the
        /// loader's Built event and a Configure(...) call can both fire RebuildAsync close
        /// together, so a superseded rebuild drops stale results after each await instead of
        /// adding duplicate click areas / overlays.</summary>
        private async UniTaskVoid RebuildAsync() {
            Clear(); // invalidates any in-flight rebuild/overlay from a prior call
            int gen = ++_generation;
            var docRoot = GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement;
            if (docRoot == null || _resources == null) {
                return;
            }
            _root = CanonicalStage.GetOrCreate(docRoot, _loader.Frame);

            ChapterCatalog catalog = await _resources.LoadAssetAsync<ChapterCatalog>(ChapterCatalog.ResourceId, this);
            if (gen != _generation) {
                return; // superseded or disposed while awaiting
            }
            if (catalog == null) {
                _logger.LogWarning("ContentsChapterOverlay: chapter catalog unavailable.");
                return;
            }

            // Click areas for reached chapters; find the first LOCKED chapter's rect for the CONT2 boundary.
            // Chapters is a plain mutable (moddable) list, not guaranteed ascending — order by Number.
            float lockedTop = -1f;
            foreach (Chapter chapter in catalog.Chapters.OrderBy(c => c.Number)) {
                if (!_loader.TryGetElementRect(chapter.ContentsActionId, out Rect rect)) {
                    continue;
                }
                // Replayable = chapters moved PAST (strictly before current); the current chapter is
                // visible-but-not-clickable. CONT2 masks the not-yet-reached rows (strictly after
                // current), so the current chapter's row stays unmasked. See ChapterScenes.IsReplayable.
                if (ChapterScenes.IsReplayable(chapter.Number, _currentChapter)) {
                    AddClickArea(rect, chapter.Number);
                } else if (chapter.Number > _currentChapter && lockedTop < 0f) {
                    lockedTop = rect.y;   // top of the first not-yet-reached chapter row
                }
            }

            // Reveal CONT2.SCX over the locked region (from the first locked row down). None if all reached.
            if (lockedTop >= 0f) {
                await AddCont2OverlayAsync(lockedTop, gen);
            }
        }

        private void AddClickArea(Rect rect, int chapterNumber) {
            var area = new VisualElement {
                name = "chapter_click_" + chapterNumber,
                pickingMode = PickingMode.Position,   // transparent but clickable (SCX title shows through)
                style = {
                    position = Position.Absolute,
                    left = rect.x, top = rect.y, width = rect.width, height = rect.height,
                },
            };
            int n = chapterNumber;
            area.AddManipulator(new Clickable(() => ChapterClicked?.Invoke(n)));
            _root.Add(area);
            _added.Add(area);
        }

        /// <summary>Loads and reveals the CONT2.SCX overlay. Generation-guarded (see
        /// <see cref="RebuildAsync"/>) — drops the result if superseded while awaiting.</summary>
        private async UniTask AddCont2OverlayAsync(float lockedTop, int gen) {
            Sprite cont2 = await _resources.LoadAssetAsync<Sprite>(Cont2Address, this);
            if (gen != _generation) {
                return; // superseded or disposed while awaiting
            }
            if (cont2 == null) {
                return;
            }
            // Clip container over the locked region; a full-screen CONT2 child offset up by lockedTop
            // so only the locked rows show (mirrors the original's downward reveal).
            var clip = new VisualElement {
                name = "cont2_reveal",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, top = lockedTop, right = 0, bottom = 0,
                    overflow = Overflow.Hidden,
                },
            };
            var img = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    // Sized via anchors, not an explicit width/height, so it covers the *stage's*
                    // real size (_root — a fixed Contain box or a full-panel Fill; _root IS the
                    // CanonicalStage, see RebuildAsync) rather than a hardcoded 1600x1200 assumption.
                    // left/right=0 spans clip's full width (== _root's width, unreduced by the
                    // crop). top=-lockedTop + bottom=0 makes the implied height = clip's own
                    // (already lockedTop-shorter) height + lockedTop == _root's full height — the
                    // same trick clip itself uses below to size against its parent's real bounds.
                    left = 0, right = 0, top = -lockedTop, bottom = 0,
                    backgroundImage = Background.FromSprite(cont2),
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            };
            clip.Add(img);
            _root.Add(clip);
            _added.Add(clip);
        }

        /// <summary>Test seam: exercises the CONT2 reveal geometry (the stage-relative anchor
        /// sizing) directly, without standing up the full RebuildAsync/ChapterCatalog/
        /// UserInterfaceLoader pipeline this component normally needs. Sets the fields
        /// RebuildAsync would have and calls the real reveal method.</summary>
        internal UniTask RevealCont2ForTest(VisualElement stage, IResourceProviderService resources, float lockedTop) {
            _root = stage;
            _resources = resources;
            int gen = ++_generation;
            return AddCont2OverlayAsync(lockedTop, gen);
        }

        private void Clear() {
            _generation++; // invalidate any in-flight RebuildAsync/AddCont2OverlayAsync
            foreach (VisualElement e in _added) {
                e.RemoveFromHierarchy();
            }
            _added.Clear();
            if (_resources != null) {
                _resources.ReleaseAssets(this);
            }
        }
    }
}
