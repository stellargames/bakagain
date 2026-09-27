namespace BakAgain.UI.Cursor {
    using System.Collections.Generic;
    using BakAgain.CutScenes;       // IResourceCache
    using BakAgain.Graphics;        // CanonicalConversion, Canonical
    using BakAgain.UI.InputCore;    // IPointer
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Cursor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;

    /// <summary>Software mouse cursor drawn as a top-most UI Toolkit overlay in canonical
    /// (1600×1200) space. Follows the Input System pointer (screen→canonical), supports a
    /// programmatic keyboard warp via <see cref="CursorArbiter"/>, and swaps images by index
    /// from the active BMX set (POINTER / POINTERG).
    ///
    /// <para>Software (not <c>Cursor.SetCursor</c>) so it works identically on desktop and Android
    /// and lives in the same canonical space as the menus. The per-image hotspot is the RE-derived
    /// SetPointerImage rule (index 0/1 → top-left; index ≥ 2 → centred), computed directly from the
    /// loaded sprite's size — no separate CursorSet JSON load needed.</para></summary>
    [RequireComponent(typeof(UIDocument))]
    public class CursorManager : MonoBehaviour, ICursorManager {
        [Inject] private IResourceCache _resources;
        private IPointer _pointer;

        [Inject]
        public void Construct(IPointer pointer) => _pointer = pointer;

        private readonly CursorArbiter _arbiter = new();
        private readonly Dictionary<string, (Sprite sprite, Vector2 hotspot)> _cache = new();

        // Built-in mirror of the code-named rows in generated/POINTER/cursor-map.json. Lets
        // Set(GameCursor) work without shipping cursor-map.json as an addressable; when that JSON
        // is wired as a TextAsset a CursorMap can replace this (see CursorMap, unit-tested).
        private static readonly Dictionary<GameCursor, (string file, int index)> SemanticMap = new() {
            { GameCursor.Arrow,   ("POINTER.BMX", 0) },
            { GameCursor.Wait,    ("POINTERG.BMX", 2) },
            { GameCursor.Examine, ("POINTERG.BMX", 3) },
        };

        private VisualElement _root;
        private VisualElement _cursorElement;
        private string _activeSetFile = "POINTER.BMX";
        private Vector2 _hotspot;
        private bool _hidden;

        public Vector2 CanonicalPosition => _arbiter.Position;

        private void Awake() {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _cursorElement = new VisualElement {
                name = "software-cursor",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, width = 0, height = 0 }
            };
            _root.Add(_cursorElement);
            UnityEngine.Cursor.visible = false; // desktop: hide the OS cursor; we draw our own
            SelectSet("POINTER");   // sets the active BMX name only — no resource load
        }

        // The first image load is deferred to Start: [Inject] field injection runs AFTER Awake for a
        // prefab-instantiated component, so loading in Awake would deref a null _resources (ApplyAsync
        // also guards defensively). Start runs after injection, so the arrow loads correctly.
        private void Start() {
            SetByIndex(0);          // default arrow
        }

        public void SelectSet(string setName) => _activeSetFile = setName + ".BMX";

        public void SetByIndex(int index) {
            if (index < 0) index = 0; // SetPointerImage(-1) -> default arrow
            ApplyAsync(_activeSetFile, index).Forget();
        }

        public void Set(GameCursor cursor) {
            if (!SemanticMap.TryGetValue(cursor, out (string file, int index) row)) return;
            _activeSetFile = row.file;
            SetByIndex(row.index);
        }

        public void Hide() {
            _hidden = true;
            if (_cursorElement != null) _cursorElement.style.display = DisplayStyle.None;
        }

        public void Show() {
            _hidden = false;
            if (_cursorElement != null) _cursorElement.style.display = DisplayStyle.Flex;
        }

        // Warps only the software cursor. We deliberately do NOT move the OS pointer
        // (Mouse.WarpCursorPosition): in the Editor its origin differs from Pointer.position's (Game
        // View vs OS desktop), so LateUpdate's read-back snapped the cursor to a wrong canonical point.
        // The accepted trade-off is that a physical mouse move after a keyboard warp reclaims the
        // pointer's position (CursorArbiter.OnPointerMoved) — the original game warped the hardware
        // cursor, but a software cursor decoupled from the OS pointer cannot without this hazard.
        public void WarpTo(Vector2 canonicalPosition) => _arbiter.WarpTo(canonicalPosition);

        private async UniTaskVoid ApplyAsync(string setFile, int index) {
            if (_resources == null) {
                return; // called before [Inject] ran (see Start) — no-op rather than NRE
            }
            string key = $"{setFile}#{index}";
            if (!_cache.TryGetValue(key, out (Sprite sprite, Vector2 hotspot) entry)) {
                Sprite sprite = await _resources.GetOrLoadAsync<Sprite>(key);
                if (sprite == null) return;
                entry = (sprite, ComputeHotspot(index, sprite.rect.width, sprite.rect.height));
                _cache[key] = entry;
            }
            if (_cursorElement == null) return;
            _cursorElement.style.backgroundImage = new StyleBackground(entry.sprite);
            _cursorElement.style.width = entry.sprite.rect.width;
            _cursorElement.style.height = entry.sprite.rect.height;
            _hotspot = entry.hotspot;
        }

        /// <summary>SetPointerImage (0x2abb7) hotspot rule: index 0/1 → top-left (0,0);
        /// index ≥ 2 → centred (w/2, h/2). Computed on the already-upscaled canonical sprite size,
        /// which equals the extractor's canonical hotspot.</summary>
        public static Vector2 ComputeHotspot(int index, float width, float height) =>
            index >= 2 ? new Vector2(width / 2f, height / 2f) : Vector2.zero;

        /// <summary>Top-left of the active <see cref="CanonicalStage"/> within the shared panel, in
        /// panel logical units — i.e. the pillarbox/letterbox margins under <c>Contain</c>, or
        /// (0,0) under a full-panel <c>Fill</c> stage. Read directly from the stage element's own
        /// resolved <see cref="VisualElement.worldBound"/> rather than recomputed from
        /// <see cref="Canonical.Width"/>/<see cref="Canonical.Height"/>, so this is correct for
        /// whatever frame/fit the active screen actually built its stage with. Zero until the first
        /// layout pass resolves, or if no stage exists yet (cursor parks at the panel origin for one
        /// frame, which is off-screen-safe).</summary>
        private static Vector2 StageOrigin(VisualElement stage) {
            if (stage == null) return Vector2.zero;
            Rect bounds = stage.worldBound;
            if (float.IsNaN(bounds.width) || float.IsNaN(bounds.height) || bounds.width <= 0f || bounds.height <= 0f) {
                return Vector2.zero;
            }
            return bounds.position;
        }

        // Finds the currently-active CanonicalStage anywhere under the shared panel (several
        // UIDocuments — background/menu/HUD/dialog — can each host their own stage-bearing
        // document root). More than one can be visible at once (e.g. a modal dialog, tier 10, over
        // a full-screen screen, tier 0) with DIFFERENT stages in DIFFERENT coordinate spaces — a
        // Fill dialog over a Contain screen — so which one the cursor must resolve against is
        // whichever is actually on top, not whichever a depth-first Q() over the whole panel
        // happens to reach first (unrelated to paint order; this was the bug).
        //
        // Unity inserts UIDocuments that share one PanelSettings as direct children of the panel's
        // own visualTree, already sorted ascending by sortingOrder with ties broken by
        // registration (enable) order — verified empirically on Unity 6000.4.6f1 (two
        // same-sortingOrder documents: the earlier-enabled one sorts first), which is an
        // implementation detail of UIElements' panel, not a documented contract: re-verify it on
        // a version bump. Consistent with the ordering
        // UnityProject/CLAUDE.md's tier table documents. That IS the panel's real paint order, so
        // walking panelTree's children from the end finds the topmost document first, and ties
        // resolve to whichever was registered last — matching real paint order, not an arbitrary
        // pick. A document that is attached but hidden via display:none must not win just because
        // of a stale high sortingOrder, hence the display checks below (an inactive/disabled
        // UIDocument is removed from panelTree entirely, so that case needs no separate check).
        private VisualElement FindActiveStage() {
            VisualElement panelTree = _root?.panel?.visualTree;
            if (panelTree == null) return null;
            for (int i = panelTree.childCount - 1; i >= 0; i--) {
                VisualElement documentRoot = panelTree[i];
                if (documentRoot.resolvedStyle.display == DisplayStyle.None) continue;
                VisualElement stage = CanonicalStage.Find(documentRoot);
                if (stage != null && stage.resolvedStyle.display != DisplayStyle.None) {
                    return stage;
                }
            }
            return null;
        }

        private void LateUpdate() {
            if (_hidden || _cursorElement == null || _pointer == null) return;
            PumpPointer(_pointer);
        }

        // Test seam: no device reads, drives the arbiter from IPointer.
        internal void PumpPointer(IPointer pointer) {
            if (!pointer.IsPresent) {
                _cursorElement.style.display = DisplayStyle.None; // touch/none: no software cursor
                return;
            }
            _cursorElement.style.display = DisplayStyle.Flex;
            // The cursor element lives on the full-panel root (window origin), but its position is
            // authored in the active screen's own CanonicalStage space. Shift by the stage's actual
            // top-left so (0,0) lands on the stage's origin, not the window's left edge (else the
            // cursor sits a pillar-bar too far left). Read the stage's own resolved geometry rather
            // than assuming a fixed 1600×1200 frame, so this tracks whatever fit/frame the active
            // screen actually built its stage with (Contain at any size, or a full-panel Fill).
            VisualElement stage = FindActiveStage();
            Vector2 canonicalPointer = CanonicalConversion.ScreenToCanonical(
                pointer.ScreenPosition, new Vector2(Screen.width, Screen.height), stage);
            _arbiter.OnPointerMoved(pointer.Delta, canonicalPointer);
            Vector2 draw = _arbiter.Position - _hotspot + StageOrigin(stage);
            _cursorElement.style.left = draw.x;
            _cursorElement.style.top = draw.y;
        }

        // Test hook: sets _cursorElement directly so PumpPointer can be exercised without Awake's
        // UIDocument/resource wiring.
        internal void SetCursorElementForTest(VisualElement element) => _cursorElement = element;

        // Test hook: exposes FindActiveStage's result directly, so a multi-stage test can assert
        // which VisualElement was chosen without reverse-engineering it from PumpPointer's draw math.
        internal VisualElement ResolveActiveStageForTest() => FindActiveStage();
    }
}
