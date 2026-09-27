namespace BakAgain.UI {
    using BakAgain.Graphics;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Presentation Stage (canonical-spine plan, Step 4 — realized as a
    /// pure helper rather than a MonoBehaviour). Hosts all screen content in a
    /// single element, sized and fit according to the resource's own
    /// <see cref="DesignFrame"/> (<see cref="UserInterface.Frame"/>,
    /// <see cref="CreditsData.Frame"/>, …) rather than a hardcoded constant —
    /// the frame is the single place that decides whether the game
    /// pillarboxes or fills the window.
    ///
    /// <para>
    /// The shared <c>PanelSettings</c> (reference resolution 1600×1200,
    /// match-height) makes the panel's logical height exactly 1200 while its
    /// logical width follows the window aspect (≈2133 at 16:9). Absolute
    /// canonical coordinates would therefore land left-aligned unless the
    /// stage compensates:
    /// <list type="bullet">
    /// <item><see cref="LayoutFit.Contain"/> — a fixed <c>frame.Width ×
    /// frame.Height</c> box, flex-centered in the panel. This is today's
    /// classic pillarboxed 4:3 presentation, now sized from the frame rather
    /// than from <see cref="Canonical.Width"/>/<see cref="Canonical.Height"/>.
    /// Windows narrower than the frame's aspect currently crop the stage
    /// equally on both sides. A frame with non-positive dimensions falls back
    /// to <see cref="Canonical.Width"/>/<see cref="Canonical.Height"/> with a
    /// warning — this guard only applies here, since Contain is the only fit
    /// that actually consumes the dimensions.</item>
    /// <item><see cref="LayoutFit.Fill"/> (enhanced mode, Phase 4) — the
    /// stage spans the panel (100% width/height), so percentage lengths and
    /// anchors resolve against the real window size instead of a fixed box.
    /// The frame's dimensions are irrelevant here (the stage always spans its
    /// parent), so a zero-size Fill frame is harmless and does not warn.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Order-independence.</b> Several components on one screen (background
    /// loader, REQ loader, controller) call <see cref="GetOrCreate"/> against
    /// the same document root, and not every caller has a frame-bearing
    /// resource in hand — a caller with none passes <c>frame: null</c>, which
    /// resolves to the canonical fallback. Because completion order between
    /// those callers is not guaranteed, the reconciliation is real-beats-
    /// fallback rather than first-wins: a later call with a real frame upgrades
    /// a stage that was created from a fallback; a later fallback call never
    /// overwrites an already-real stage; two different real frames for the
    /// same stage is a genuine conflict the data should not produce, so it is
    /// kept as the first one seen and logged rather than silently resolved.
    /// </para>
    ///
    /// <para>
    /// <b>What callers may hand in.</b> Every entry point takes a document root
    /// as a bare <see cref="VisualElement"/>, so a caller holding the wrong
    /// element compiles cleanly — and the consequences would be invisible
    /// (picking and RenderTexture sizing keep working, against the wrong box).
    /// All three inputs therefore resolve the same stage: the UIDocument root
    /// (the intended one), the stage itself (<c>Q</c> matches the element it is
    /// called on), and an element from inside the stage (resolved by climbing,
    /// and reported once as an error — see
    /// <see cref="ResolveEnclosingStage"/>). Pinned by
    /// <c>CanonicalStageTests</c>, since which of these silently degrades is
    /// exactly the thing a green build cannot tell you.
    /// </para>
    /// </summary>
    public static class CanonicalStage {
        private const string StageName = "BakStage";

        // Tracked on the stage's own VisualElement.userData so reconciliation on a later
        // GetOrCreate call can tell a real frame from the canonical fallback, independent of call
        // order. Not a DesignFrame property — provenance ("did a resource actually supply this")
        // is a Unity-side, call-site concern, not part of the engine-independent model.
        private sealed class StageFrameState {
            public DesignFrame Frame;
            public bool IsFallback;
            // Set once the "handed an element from inside the stage" misuse has been reported for
            // this stage, so a per-frame caller reports it once rather than every frame.
            public bool InsideStageMisuseReported;
        }

        /// <summary>Returns the existing stage element, or null if none was
        /// created yet (or the root was cleared). Use for teardown paths that
        /// must not re-create the stage.
        ///
        /// <para><paramref name="documentRoot"/> may also BE the stage — <c>Q</c> matches the
        /// element it is called on — which is what makes the "stage passed where a document root is
        /// expected" mix-up harmless rather than silent (see <see cref="ResolveEnclosingStage"/> for
        /// the variant that is not).</para></summary>
        public static VisualElement Find(VisualElement documentRoot) {
            if (documentRoot == null) {
                return null;
            }
            return documentRoot.Q(StageName) ?? ResolveEnclosingStage(documentRoot);
        }

        /// <summary>
        /// The one way to get "which element do I hand this?" wrong that a downward query cannot
        /// absorb: an element from INSIDE the stage (<c>hotspot_192</c>, the world viewport element,
        /// a REQ widget). The stage is then an ancestor, so <c>Q</c> finds nothing and the caller
        /// silently maps hit-tests and RenderTexture sizes through <see cref="ScreenRect"/>'s
        /// fallback box for its whole lifetime — the exact failure class that survives a green build
        /// and a green suite.
        ///
        /// <para>So climb to it and say so: the geometry the caller gets is right, and the call site
        /// that is still wrong gets named once (an error, not a warning — this is a programming
        /// mistake, and it fails any test that trips it). Reported once per stage because the
        /// callers run per frame.</para>
        /// </summary>
        private static VisualElement ResolveEnclosingStage(VisualElement element) {
            for (VisualElement p = element.parent; p != null; p = p.parent) {
                if (p.name != StageName) {
                    continue;
                }
                // Reported only when there is somewhere to record that it has been: these resolvers
                // run per frame, and an unbounded error log can grow the Editor until it is
                // OOM-killed. A stage element with no
                // state was not created by GetOrCreate, which this codebase never does.
                var state = p.userData as StageFrameState;
                if (state != null && !state.InsideStageMisuseReported) {
                    state.InsideStageMisuseReported = true;
                    Debug.LogError(
                        $"CanonicalStage: resolved the stage by climbing out of '{element.name}', which "
                        + "is inside the stage — this API expects the UIDocument root (the stage itself "
                        + "is fine too). Geometry is correct, the call site is not: pass "
                        + "`uiDocument.rootVisualElement`.");
                }
                return p;
            }
            return null;
        }

        /// <summary>
        /// <see cref="Find"/>, memoised: hands back <paramref name="cached"/> when it already
        /// resolved, and otherwise re-runs the lookup. The single owner of the "keep re-trying
        /// until the stage exists" rule that every long-lived consumer of
        /// <see cref="ScreenRect"/> needs.
        ///
        /// <para>Why it is a rule and not a caller's business: which component creates the stage is
        /// deliberately unspecified (see the class doc's "Order-independence" section), so a
        /// consumer built or attached before that component ran gets null — and a consumer that
        /// captured that null <em>once</em> (in a constructor or an <c>Attach</c>) would then map
        /// hit-tests and RenderTexture sizes through <see cref="ScreenRect"/>'s fallback box for
        /// its entire lifetime, turning a one-frame approximation into a permanent wrong answer
        /// with only a single logged warning to show for it. Memoising rather than looking up every
        /// time keeps the steady state a null check: <see cref="Find"/> is a <c>Q()</c> tree walk
        /// and callers like <c>WorldViewportView.Tick</c> run per frame.</para>
        /// </summary>
        public static VisualElement FindOrCached(VisualElement cached, VisualElement documentRoot) {
            return cached ?? Find(documentRoot);
        }

        /// <summary>
        /// The <see cref="DesignFrame"/> a stage was built with, readable synchronously and
        /// before any layout pass — which is the whole point: <c>worldBound</c> is the honest
        /// answer to "how big is this stage on screen", but it reads 0/NaN until UI Toolkit has
        /// laid out, and several callers must decide geometry before that.
        ///
        /// <para><paramref name="isFallback"/> reports provenance: true when the stage was
        /// created by a caller with no frame-bearing resource in hand (see the class doc's
        /// "Order-independence" section). A caller that must not act on a guessed frame should
        /// check it rather than treat the canonical fallback as authoritative.</para>
        ///
        /// <para>Read-only by design. The frame is owned by <see cref="GetOrCreate"/> and
        /// <see cref="Reconcile"/>; this must not become a second way to mutate it. Enforced by
        /// returning a defensive copy — <paramref name="frame"/> is never the live instance held
        /// in the stage's state (which, via callers like <c>DialogManager</c> passing a
        /// <c>DialogStyleTable</c>'s own <c>Frame</c> straight into <see cref="GetOrCreate"/>, is
        /// frequently the very same instance as a cached shared resource). Mirrors the
        /// clone-on-read contract
        /// <see cref="GameData.Resources.Layout.LayoutHint.Clone"/> already establishes for the
        /// same reason (see <c>DialogManager.ResolveArea</c>). Unlike <c>LayoutHint</c>,
        /// <see cref="DesignFrame"/> has no nested reference-typed fields, so the copy is
        /// constructed inline here rather than via a <c>DesignFrame.Clone()</c> added to
        /// <c>GameData</c> — that would add a cross-repo DLL rebuild for a three-field value copy
        /// with exactly one caller.</para>
        /// </summary>
        public static bool TryGetFrame(VisualElement stage, out DesignFrame frame, out bool isFallback) {
            frame = null;
            isFallback = false;
            var state = stage?.userData as StageFrameState;
            if (state == null) {
                return false;
            }
            frame = new DesignFrame { Width = state.Frame.Width, Height = state.Frame.Height, Fit = state.Frame.Fit };
            isFallback = state.IsFallback;
            return true;
        }

        /// <summary>
        /// A <see cref="DesignFrame"/> derived from <see cref="Canonical.Width"/>/
        /// <see cref="Canonical.Height"/> (<see cref="LayoutFit.Contain"/>) — what
        /// <paramref name="frame"/>: null in <see cref="GetOrCreate"/> resolves to.
        /// </summary>
        private static DesignFrame FallbackFrame() =>
            new DesignFrame { Width = Canonical.Width, Height = Canonical.Height };

        /// <summary>
        /// Returns the stage element for a document root, creating and fitting it on
        /// first use per <paramref name="frame"/>. Pass <c>null</c> when the caller has
        /// no design-frame-bearing resource in hand (e.g. <see cref="BackgroundImageLoader"/>,
        /// which only ever loads a background <c>Sprite</c>) — this resolves to the canonical
        /// fallback (<see cref="Canonical.Width"/>/<see cref="Canonical.Height"/>, Contain) and is
        /// tracked as such so a later real frame still wins (see the class doc's
        /// "Order-independence" section). Idempotent: multiple components on one screen share the
        /// same stage regardless of call order. Children added to the stage position absolutely in
        /// the frame's coordinate space.
        /// </summary>
        public static VisualElement GetOrCreate(VisualElement documentRoot, DesignFrame frame) {
            // Q matches documentRoot itself, so a caller that hands in the stage gets it back;
            // ResolveEnclosingStage covers the one case a downward query cannot see — an element
            // from inside the stage, where creating would nest a SECOND stage in the first and lay
            // its children out through the frame transform twice.
            VisualElement stage = documentRoot.Q(StageName) ?? ResolveEnclosingStage(documentRoot);
            if (stage != null) {
                Reconcile(stage, frame);
                return stage;
            }

            // The root overlays the whole panel, absolutely positioned so it
            // stays out of the shared panel container's flex flow (several
            // UIDocuments share one runtime panel — an in-flow root would
            // displace the others; see the old "background pushed up" bug).
            documentRoot.style.position = Position.Absolute;
            documentRoot.style.left = 0;
            documentRoot.style.top = 0;
            documentRoot.style.right = 0;
            documentRoot.style.bottom = 0;
            documentRoot.style.flexDirection = FlexDirection.Column;
            documentRoot.style.alignItems = Align.Center;
            documentRoot.style.justifyContent = Justify.Center;

            bool isFallback = frame == null;
            DesignFrame effective = frame ?? FallbackFrame();

            // *** THE STAGE IS A CONTAINER; IT MUST NOT EAT CLICKS. *** VisualElement defaults to
            // PickingMode.Position, so a stage picked over its whole 1600x1200 area and swallowed
            // every click that hit no entry. Harmless for a pushed screen — the document beneath is
            // deactivated, so there was nothing to fall through to anyway — but the combat HUD is
            // raised with SetActive rather than a navigator push, leaving InGameScreen live
            // underneath. Its stage sits above the travel one and took EVERY arena click: target
            // selection, corpse looting and tile picking all died here, silently, because the stage
            // is not a button and so nothing even logged a rejection. Children keep their own
            // picking mode, so the buttons are unaffected. See TASK-267.
            stage = new VisualElement { name = StageName, pickingMode = PickingMode.Ignore };
            ApplyFrame(stage, effective);
            stage.userData = new StageFrameState { Frame = effective, IsFallback = isFallback };
            documentRoot.Add(stage);
            return stage;
        }

        // Sizes/fits an already-created stage per a resolved (non-null) frame. Shared by the
        // create path and the real-beats-fallback upgrade path in Reconcile.
        private static void ApplyFrame(VisualElement stage, DesignFrame frame) {
            if (frame.Fit == LayoutFit.Fill) {
                // Dimensions are irrelevant under Fill (the stage spans its parent either way), so
                // the non-positive-dimension guard below deliberately does not run on this branch —
                // it would log a "falling back" warning that never actually happens under Fill.
                stage.style.width = Length.Percent(100);
                stage.style.height = Length.Percent(100);
            } else {
                int width = frame.Width;
                int height = frame.Height;
                if (width <= 0 || height <= 0) {
                    // A default-constructed or malformed frame must not silently collapse the
                    // stage — fall back to the canonical asset-space dimensions and say so. A
                    // mod override shipping a bad frame should degrade, not take the screen down.
                    Debug.LogWarning(
                        $"CanonicalStage: DesignFrame has non-positive dimensions ({width}x{height}); "
                        + $"falling back to canonical {Canonical.Width}x{Canonical.Height}.");
                    width = Canonical.Width;
                    height = Canonical.Height;
                }
                stage.style.width = width;
                stage.style.height = height;
            }
            stage.style.flexShrink = 0;
            stage.style.flexGrow = 0;
        }

        // Reconciles a later GetOrCreate call's frame against the one already applied to an
        // existing stage. Real beats fallback regardless of call order; two different real frames
        // is a conflict (kept as the existing one, logged); an equal frame (by value) is a silent
        // no-op. See the class doc's "Order-independence" section for the motivating scenario.
        private static void Reconcile(VisualElement stage, DesignFrame incoming) {
            bool incomingIsFallback = incoming == null;
            DesignFrame incomingEffective = incoming ?? FallbackFrame();

            var state = stage.userData as StageFrameState;
            if (state == null) {
                // Defensive: a "BakStage" element that exists but wasn't created through
                // GetOrCreate (so never got its state recorded) — adopt the incoming frame as
                // authoritative rather than guess.
                stage.userData = new StageFrameState { Frame = incomingEffective, IsFallback = incomingIsFallback };
                return;
            }

            if (FramesEqual(state.Frame, incomingEffective)) {
                return; // same effective frame — no-op, no warning, regardless of provenance
            }

            if (state.IsFallback && !incomingIsFallback) {
                // Real beats fallback, regardless of which call happened first — upgrade.
                ApplyFrame(stage, incomingEffective);
                state.Frame = incomingEffective;
                state.IsFallback = false;
                return;
            }

            if (!state.IsFallback && incomingIsFallback) {
                return; // keep the already-applied real frame; a later fallback never overwrites it
            }

            if (!state.IsFallback) {
                // Two different real frames for the same stage — the data shouldn't produce this
                // (a screen has exactly one design frame). Surface it instead of silently keeping
                // whichever call happened to run first.
                Debug.LogWarning(
                    $"CanonicalStage: conflicting DesignFrame for an already-built stage — keeping "
                    + $"{Describe(state.Frame)}, ignoring {Describe(incomingEffective)}.");
                return;
            }

            // Both sides are the (deterministic) fallback but somehow differ — unreachable in
            // practice since FallbackFrame() always returns the same values. Keep the existing one
            // silently; neither side is an authoritative resource, so there is nothing to warn about.
        }

        private static bool FramesEqual(DesignFrame a, DesignFrame b) {
            if (ReferenceEquals(a, b)) {
                return true;
            }
            if (a == null || b == null) {
                return false;
            }
            return a.Width == b.Width && a.Height == b.Height && a.Fit == b.Fit;
        }

        private static string Describe(DesignFrame frame) => $"{frame.Width}x{frame.Height}/{frame.Fit}";

        /// <summary>
        /// Resolves <paramref name="stage"/>'s current on-screen rectangle (Unity screen-rect
        /// convention: physical pixels, origin bottom-left) from its resolved
        /// <see cref="VisualElement.worldBound"/> — the single conversion callers like
        /// <c>WorldViewportView</c>/<c>WorldInteractionController</c> need to feed
        /// <see cref="World.IWorldViewport.ToScreenRect"/> the box the stage actually occupies,
        /// whether that's a pillarboxed <c>Contain</c> box or the full <c>Fill</c> window.
        ///
        /// <para><c>worldBound</c> is in the panel's own logical units (the shared
        /// <c>PanelSettings</c> uses reference-resolution 1600×1200 match-height scaling — see the
        /// class doc — so the panel's logical size is <em>not</em> device pixels except by
        /// coincidence). Converting to physical pixels needs the panel's scale factor; since
        /// ScaleWithScreenSize keeps the panel's logical aspect equal to the physical aspect, that
        /// factor is uniform in x and y and is recovered from <c>panel.visualTree.layout</c> vs
        /// <see cref="Screen"/> without needing a public scale API.</para>
        ///
        /// <para><c>worldBound</c> reads zero-sized or NaN before the stage's first UI Toolkit
        /// layout pass (or if <paramref name="stage"/> is null / not yet attached to a panel).
        /// Rather than hand back that bogus rect — which would hit-test or size a RenderTexture
        /// against garbage — this falls back to <see cref="ContainFallbackRect"/> and reports the
        /// fact via <paramref name="isFallback"/> so the caller can log it once, naming itself. A
        /// silently wrong hit-test is exactly the failure mode this guard exists to remove.</para>
        /// </summary>
        public static Rect ScreenRect(VisualElement stage, out bool isFallback) {
            // The mirror of the mix-up Find absorbs: handed the document root instead of the stage,
            // this would measure the whole panel and hand back Fill geometry on a Contain game —
            // silently, since a plausible rect comes back either way. Resolve rather than measure
            // whatever turned up (null when nothing resolves, which takes the fallback below).
            if (stage != null && stage.name != StageName) {
                stage = Find(stage);
            }
            IPanel panel = stage?.panel;
            Rect wb = stage?.worldBound ?? default;
            Rect panelBounds = panel?.visualTree.layout ?? default;
            bool invalid = panel == null || wb.width <= 0f || wb.height <= 0f || panelBounds.height <= 0f
                || float.IsNaN(wb.x) || float.IsNaN(wb.y) || float.IsNaN(wb.width) || float.IsNaN(wb.height)
                || float.IsNaN(panelBounds.height);
            if (invalid) {
                isFallback = true;
                return ContainFallbackRect();
            }
            isFallback = false;
            float scale = Screen.height / panelBounds.height;
            float x = wb.x * scale;
            float width = wb.width * scale;
            float height = wb.height * scale;
            // worldBound is top-left origin / y-down, in panel logical units; scale to device
            // pixels, then flip to Unity's bottom-left-origin Screen-rect convention.
            float y = Screen.height - (wb.y + wb.height) * scale;
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// The box <see cref="ScreenRect"/> hands back when there is no resolved stage to measure:
        /// the classic <see cref="LayoutFit.Contain"/> pillarbox — the largest centred
        /// <see cref="Canonical.Width"/>×<see cref="Canonical.Height"/> region of the window, at one
        /// uniform scale. This is exactly the math <c>WorldViewport.ToScreenRect</c> carried inline
        /// before it started mapping through the stage, and it needs no stage, no panel and no
        /// layout pass — which is the point, since every caller of this path has none of those.
        ///
        /// <para><b>Why not the raw window.</b> Handing back <c>(0, 0, Screen.width,
        /// Screen.height)</c> and letting <c>ToScreenRect</c> map proportionally into it does not
        /// degrade to "no stage yet" — it degrades to <see cref="LayoutFit.Fill"/> geometry, on a
        /// game whose shipped resources are all Contain. <c>WorldViewportView.Attach</c> allocates
        /// its RenderTexture synchronously, before the stage's first layout, so this fires on a
        /// normal show: at 1920×1080 the raw-window fallback put the world pick rect at
        /// (78, 475.2, 1764, 545.4) instead of the correct (298.5, 475.2, 1323, 545.4). The Contain
        /// box is right for every shipped screen and is a far closer approximation than Fill even on
        /// a genuinely Fill-authored one, where the stage resolves a frame later regardless.</para>
        ///
        /// <para>Canonical rather than the stage's own <see cref="DesignFrame"/> deliberately: this
        /// path exists precisely for a stage that is null or unresolved, and
        /// <see cref="FallbackFrame"/> — canonical, Contain — is what <see cref="GetOrCreate"/>
        /// itself assumes when no resource states otherwise.</para>
        /// </summary>
        private static Rect ContainFallbackRect() {
            float scale = Mathf.Min(Screen.width / (float)Canonical.Width, Screen.height / (float)Canonical.Height);
            float width = Canonical.Width * scale;
            float height = Canonical.Height * scale;
            return new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        }
    }
}
