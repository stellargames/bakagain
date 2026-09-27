namespace BakAgain.Tests.Editor.UI {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using BakAgain.CutScenes;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Credits;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Exercises the actual <see cref="CreditsView"/> renderer — not just the data model — because
    /// the one failure mode this conversion (VGA constants -> LayoutHint boxes on
    /// <c>CreditsData.Layout</c>) exists to prevent is a use site quietly wired to the wrong
    /// <see cref="CreditsLayout"/> property, or a view that still cherry-picks individual fields
    /// instead of handing the whole box to <c>LayoutApplier.Apply</c>. That bug compiles clean and
    /// produces a plausible-looking screen, so only a test that reads back real
    /// <c>VisualElement.style</c> values built from a <em>distinctive</em>, deliberately
    /// non-faithful layout can catch it: if the view still read a hardcoded constant (or only part
    /// of a box) instead of the injected layout, these assertions would see the old faithful number
    /// (or nothing at all) instead of the distinctive one and fail.
    ///
    /// <para><b>Covered</b> (asserted against distinctive values, one call into <c>BuildLayout</c>):
    /// <c>Title.Top</c> and <c>Title.Height</c> (the latter would NOT flow through a view that
    /// manually copied only <c>.Top</c> instead of calling
    /// <c>LayoutApplier.Apply(title, _layout.Title)</c>), <c>Window.Top</c> and <c>Window.Height</c>
    /// (no more <c>WindowBottom - WindowTop</c> arithmetic — Height is the box itself now),
    /// <c>Row.Height</c> (both the row's own height and the per-line vertical advance produce the
    /// same row here since only one line is supplied), <c>Row.Left</c>/<c>Row.Right</c> (now land on
    /// the row CONTAINER itself, not on individually-positioned role/name labels — see
    /// <see cref="BuildLayout_WithPercentRowInsets_ResolvesTheRowsEdgesAsPercentNotPixel"/> below for
    /// why), and <c>FontSize</c>.</para>
    ///
    /// <para><b>Not covered</b> — reaching them needs more than one <c>BuildLayout</c> call and is
    /// disproportionate for this pilot test: <c>FadeTopBand</c>/<c>FadeBottomBand</c> (only read
    /// inside the scroll loop, <c>RunScroll</c>/<c>UpdateFade</c>, never called here),
    /// <c>LeaderDotPitch</c>/<c>LeaderDotRadius</c>/<c>LeaderGap</c> and the leader element's own
    /// <c>flexGrow</c>/<c>bottom</c>/<c>height</c> (the dot positions only materialize inside
    /// <c>DrawLeaderDots</c>, invoked from <c>generateVisualContent</c>, which needs a real panel
    /// repaint to fire — not exercised by a bare method call). There is no more <c>CenterX</c> —
    /// horizontal centering is <see cref="CreditsLayout.Title"/>'s <c>Anchor = TopCenter</c> now,
    /// not a separate dead constant.</para>
    ///
    /// <para>The exact faithful (original) values are asserted on the .NET side in
    /// <c>ResourceExtraction.Tests.Layout.CreditsLayoutTests</c> — not duplicated here.</para>
    /// </summary>
    public class CreditsViewLayoutTests {
        private readonly List<GameObject> _spawned = new();
        private readonly List<Object> _scratchAssets = new();

        // BuildLayout fires LoadBackground(stage).Forget(), which awaits
        // _resourceCache.GetOrLoadAsync<Sprite>(...) — a real IResourceCache is never injected in
        // this reflection-driven test (Construct() is for the full DI path), so without a stub the
        // call NREs on a null _resourceCache and CreditsView logs the failure as an Error, which the
        // Unity Test Framework treats as an unhandled failure. Returning null here exercises the
        // same "no background" branch LoadBackground already handles.
        private sealed class NullResourceCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class => UniTask.FromResult<T>(null);
            public void Clear() { }
        }

        // FINDING 4 regression coverage: an override supplying "Layout": null (CreditsData.Layout
        // is a settable reference property, so this is a legal on-disk shape) must not NRE inside
        // PlayAsync/BuildLayout. Returns a CreditsData with Layout explicitly null for "CRED.DAT"
        // so the test exercises PlayAsync's own null-coalescing fallback, not a stub that never
        // has the problem in the first place.
        private sealed class NullLayoutResourceCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                if (key == "CRED.DAT" && typeof(T) == typeof(CreditsData)) {
                    var data = new CreditsData("CRED.DAT") { Title = "TITLE_PROBE", Layout = null };
                    data.Lines.Add(new CreditLine { Role = "ROLE_PROBE", Name = "NAME_PROBE" });
                    return UniTask.FromResult(data as T);
                }
                return UniTask.FromResult<T>(null);
            }
            public void Clear() { }
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in _spawned) {
                if (go != null) {
                    Object.DestroyImmediate(go);
                }
            }
            _spawned.Clear();
            foreach (Object asset in _scratchAssets) {
                if (asset != null) {
                    Object.DestroyImmediate(asset);
                }
            }
            _scratchAssets.Clear();
        }

        // Deliberately unlike the faithful defaults (246/324/624/210/215/66/48 — see the .NET-side
        // CreditsLayoutTests) so a view still reading the deleted constants, or cherry-picking only
        // part of a box instead of calling LayoutApplier.Apply with the whole thing, produces the
        // WRONG number here (or leaves a style unset) rather than coincidentally matching.
        private static CreditsLayout DistinctiveLayout() => new CreditsLayout {
            Title = new LayoutHint {
                Anchor = LayoutAnchor.TopCenter,
                Top = LayoutLength.Px(777f),
                // Title models no Height by default (it sizes to its own content) — setting one
                // here anyway proves BuildLayout hands the WHOLE _layout.Title box to
                // LayoutApplier.Apply rather than manually copying just Top.
                Height = LayoutLength.Px(88f),
            },
            Window = new LayoutHint {
                Top = LayoutLength.Px(555f),
                Height = LayoutLength.Px(444f),
            },
            Row = new LayoutHint {
                Left = LayoutLength.Px(333f),
                Right = LayoutLength.Px(1111f),
                Height = LayoutLength.Px(42f),
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Wrap = false,
                    Justify = LayoutFlowJustify.SpaceBetween,
                    Align = LayoutFlowAlign.Start,
                },
            },
            FontSize = LayoutLength.Px(99f),
        };

        private static CreditsData DistinctiveData() {
            var data = new CreditsData("CRED.DAT") {
                Title = "TITLE_PROBE",
                Layout = DistinctiveLayout(),
            };
            data.Lines.Add(new CreditLine { Role = "ROLE_PROBE", Name = "NAME_PROBE" });
            return data;
        }

        // Mirrors PlayAsync's own real-world setup: a UIDocument-backed GameObject, then
        // CreditsView added on top. Awake() (fired synchronously by AddComponent) deactivates the
        // GameObject — exactly as it does in the running game — so the test re-activates it before
        // driving BuildLayout, the same way PlayAsync does before calling it.
        private CreditsView SpawnView() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            _scratchAssets.Add(settings);
            var go = new GameObject("CreditsViewTestHost");
            _spawned.Add(go);
            UIDocument document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            CreditsView view = go.AddComponent<CreditsView>();
            view.gameObject.SetActive(true);
            SetResourceCache(view, new NullResourceCache());
            return view;
        }

        private static void SetResourceCache(CreditsView view, IResourceCache cache) {
            FieldInfo field = typeof(CreditsView).GetField("_resourceCache", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(view, cache);
        }

        private static void SetStack(CreditsView view, InputLayerStack stack) {
            FieldInfo field = typeof(CreditsView).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(view, stack);
        }

        // CreditsView has no public seam for injecting a layout or driving a build outside
        // PlayAsync (which needs a live IResourceCache) — reflection stands in for that seam
        // rather than restructuring the view to suit the test.
        private static void SetLayout(CreditsView view, CreditsLayout layout) {
            FieldInfo field = typeof(CreditsView).GetField("_layout", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(view, layout);
        }

        private static void InvokeBuildLayout(CreditsView view, CreditsData data, bool revealRare = false) {
            MethodInfo method = typeof(CreditsView).GetMethod("BuildLayout", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(view, new object[] { data, revealRare });
        }

        [Test]
        public void BuildLayout_ReadsTitleWindowRowGeometryFromTheInjectedLayoutBoxes_NotHardcodedConstants() {
            CreditsView view = SpawnView();
            CreditsData data = DistinctiveData();
            SetLayout(view, data.Layout);

            InvokeBuildLayout(view, data);

            VisualElement root = view.GetComponent<UIDocument>().rootVisualElement;
            VisualElement stage = root.Q("BakStage");
            Assert.IsNotNull(stage, "BuildLayout should have created the canonical stage under the document root");

            List<Label> labels = stage.Query<Label>().ToList();

            Label title = labels.Find(l => l.text == "TITLE_PROBE");
            Assert.IsNotNull(title, "title label (text == credits.Title) not found");
            Assert.AreEqual(777f, title.style.top.value.value,
                "Title.Top must come from the injected layout via LayoutApplier.Apply, not a hardcoded constant");
            Assert.AreEqual(88f, title.style.height.value.value,
                "Title.Height must flow through LayoutApplier.Apply(title, _layout.Title) as a whole box — " +
                "a view that only copied Top by hand would miss this");

            VisualElement window = stage.Q("CreditsWindow");
            Assert.IsNotNull(window, "CreditsWindow element not found");
            Assert.AreEqual(555f, window.style.top.value.value,
                "Window.Top must come from the injected layout");
            Assert.AreEqual(444f, window.style.height.value.value,
                "Window.Height must come straight from the injected layout — no more " +
                "WindowBottom - WindowTop arithmetic");

            VisualElement scroller = stage.Q("CreditsScroller");
            Assert.IsNotNull(scroller, "CreditsScroller element not found");
            Assert.AreEqual(1, scroller.childCount, "one row for the one CreditLine supplied");
            VisualElement row = scroller[0];
            Assert.AreEqual(42f, row.style.height.value.value,
                "Row.Height must come from the injected layout");
            // Role/name are flow children now (justify-content: space-between + the leader's
            // flexGrow), not individually absolutely-positioned labels — so it's the ROW
            // container's own left/right that must carry Row.Left/Right, which is the use site
            // most likely to be mis-wired (LayoutApplier.Apply's Flow branch does not itself
            // write insets — see LayoutApplier.Apply — so CreditsView finishes them by hand).
            Assert.AreEqual(333f, row.style.left.value.value,
                "Row.Left must come from the injected layout and land on the row container");
            Assert.AreEqual(1111f, row.style.right.value.value,
                "Row.Right must come from the injected layout and land on the row container");

            Label role = labels.Find(l => l.text == "ROLE_PROBE");
            Assert.IsNotNull(role, "role label (text == line.Role) not found");
            Assert.AreEqual(99f, role.style.fontSize.value.value,
                "FontSize must come from the injected layout");

            Label name = labels.Find(l => l.text == "NAME_PROBE");
            Assert.IsNotNull(name, "name label (text == line.Name) not found");
        }

        // Step 6 (the point of the whole exercise): phases 1-2 could not write this test because
        // the coordinate model read raw floats — there was nothing that could resolve as anything
        // OTHER than pixels. Now that Row.Left/Right are LayoutLength boxes, an override can ask
        // for percentages, and this asserts the row's actual UI Toolkit style reflects that unit,
        // not just the number.
        [Test]
        public void BuildLayout_WithPercentRowInsets_ResolvesTheRowsEdgesAsPercentNotPixel() {
            CreditsView view = SpawnView();
            var layout = new CreditsLayout {
                Row = new LayoutHint {
                    Left = LayoutLength.Percent(10f),
                    Right = LayoutLength.Percent(10f),
                    Height = LayoutLength.Px(66f),
                    Flow = new LayoutFlow {
                        Direction = LayoutFlowDirection.Row,
                        Wrap = false,
                        Justify = LayoutFlowJustify.SpaceBetween,
                        Align = LayoutFlowAlign.Start,
                    },
                },
            };
            var data = new CreditsData("CRED.DAT") { Title = "TITLE_PROBE", Layout = layout };
            data.Lines.Add(new CreditLine { Role = "ROLE_PROBE", Name = "NAME_PROBE" });
            SetLayout(view, layout);

            InvokeBuildLayout(view, data);

            VisualElement scroller = view.GetComponent<UIDocument>().rootVisualElement.Q("CreditsScroller");
            Assert.AreEqual(1, scroller.childCount);
            VisualElement row = scroller[0];

            Assert.AreEqual(LengthUnit.Percent, row.style.left.value.unit,
                "Row.Left = Percent(10) must resolve as a percent-unit style, not pixels — the reflow " +
                "assertion phases 1-2 could not make because the old model only ever carried raw floats");
            Assert.AreEqual(10f, row.style.left.value.value);
            Assert.AreEqual(LengthUnit.Percent, row.style.right.value.unit,
                "Row.Right = Percent(10) must resolve as a percent-unit style, not pixels");
            Assert.AreEqual(10f, row.style.right.value.value);
        }

        // Regression coverage for the Phase 2b box-conversion bug a play-tester actually hit: the
        // leader's dotted-line dots rendered at the TOP of the row (or above it) instead of near
        // the bottom, between the role and name. Root cause: `bottom` means different things for
        // an absolutely-positioned element (an edge anchor, measured from the parent's bottom) and
        // a flex child (an offset that shifts the element's flow position). The leader used to be
        // Position.Absolute (bottom = an anchor); the conversion made it a flex child of Row but
        // kept the same `leader.style.bottom = Row.Height * 0.25f`, which on a flex child instead
        // shifts it UP from its flow position (the row's top, since Row.Flow.Align = Start) — so it
        // ends up near y ≈ -16.5 instead of y ≈ 44.5.
        //
        // A style-property assertion (e.g. "leader.style.bottom == 16.5") would pass against this
        // exact bug — the style value is unchanged, only what it MEANS changed — so this test reads
        // back the leader's actual resolved layout rect (post-Yoga), which is the only thing that
        // can tell absolute-bottom-as-anchor apart from flex-bottom-as-offset. That requires a real
        // layout pass, hence [UnityTest] + yield return null (a bare [Test] never lets Yoga run —
        // see the class doc's "Not covered" paragraph).
        [UnityTest]
        public IEnumerator BuildLayout_LeaderResolvesNearTheRowsBottom_NotAboveTheRow() {
            CreditsView view = SpawnView();
            var data = new CreditsData("CRED.DAT") { Title = "TITLE_PROBE" };
            data.Lines.Add(new CreditLine { Role = "ROLE_PROBE", Name = "NAME_PROBE" });
            // The faithful defaults (CreditsLayout()'s own field initializers): Row.Height = 66,
            // LeaderDotRadius = 2.5 -> leader height 5. Baseline span (absolute bottom = 16.5,
            // height 5): yMin = 66 - 16.5 - 5 = 44.5, yMax = 66 - 16.5 = 49.5.
            SetLayout(view, new CreditsLayout());

            InvokeBuildLayout(view, data);

            // Let Yoga resolve the flex layout — leader.layout is NaN until a panel update runs.
            yield return null;
            yield return null;

            VisualElement scroller = view.GetComponent<UIDocument>().rootVisualElement.Q("CreditsScroller");
            Assert.AreEqual(1, scroller.childCount);
            VisualElement row = scroller[0];
            Assert.AreEqual(66f, row.layout.height, 0.01f, "sanity: row height should be the faithful default");

            VisualElement leader = row.Q(name: "Leader");
            Assert.IsNotNull(leader, "Leader element not found in the row");

            const float expectedTop = 44.5f;
            const float expectedBottom = 49.5f;
            // 1px tolerance: Yoga rounds resolved layout to whole display pixels, so 44.5 can come
            // back as 44 or 45 — the bug this guards against is off by 60+ px (the whole row
            // height), nowhere near this tolerance.
            Assert.AreEqual(expectedTop, leader.layout.yMin, 1f,
                $"Leader's top edge should sit at ~{expectedTop}px within the row (25% up from the " +
                $"bottom, baseline placement) but was {leader.layout.yMin} — a negative-ish value " +
                "here means the leader is rendering above the row, the reported regression.");
            Assert.AreEqual(expectedBottom, leader.layout.yMax, 1f,
                $"Leader's bottom edge should sit at ~{expectedBottom}px within the row but was {leader.layout.yMax}");
        }

        [Test]
        public async System.Threading.Tasks.Task PlayAsync_WithNullLayoutFromTheResourceCache_FallsBackToFaithfulDefaults() {
            CreditsView view = SpawnView();
            SetResourceCache(view, new NullLayoutResourceCache());
            SetStack(view, new InputLayerStack());

            // Cancelled up front: RunScroll checks the token before advancing Time.deltaTime, so
            // PlayAsync returns immediately once BuildLayout has run — this test is only proving
            // the null-Layout fallback doesn't throw, not exercising the scroll animation.
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool threw = false;
            try {
                await view.PlayAsync(0, cts.Token);
            } catch (System.Exception) {
                threw = true;
            }

            Assert.IsFalse(threw, "PlayAsync must not throw when the loaded CreditsData.Layout is null");

            FieldInfo layoutField = typeof(CreditsView).GetField("_layout", BindingFlags.NonPublic | BindingFlags.Instance);
            var layout = (CreditsLayout)layoutField.GetValue(view);
            Assert.IsNotNull(layout, "CreditsView must fall back to a CreditsLayout(), not leave _layout null");
            Assert.AreEqual(LayoutLength.Px(246f), layout.Title.Top,
                "the fallback must be the faithful CreditsLayout defaults");
        }

        [Test]
        public void OnlyAnEntryWithBOTHHalvesGetsLeaderDots() {
            // *** FOUND BY COMPARING AGAINST THE RUNNING ORIGINAL, not by reading our own code. ***
            // The original guards the whole dot loop on both halves of the pair being non-empty
            // (`strings[i][0] != 0 && strings[i+1][0] != 0`, CREDITS.C:131), so an entry's second and
            // later names — a name with no role — get NO dots. Ours drew a leader whenever the NAME
            // was set, which put a full-width dotted rule under every continuation line: the
            // original's "Raymond E. Feist" under "ORIGINAL STORY:" sits bare, ours was underlined.
            CreditsView view = SpawnView();
            var data = new CreditsData("CRED.DAT") { Title = "T", Layout = DistinctiveLayout() };
            data.Lines.Add(new CreditLine { Role = "ORIGINAL STORY:", Name = "Neal Hallford" });
            data.Lines.Add(new CreditLine { Role = string.Empty, Name = "Raymond E. Feist" });
            SetLayout(view, data.Layout);

            InvokeBuildLayout(view, data);

            VisualElement stage = view.GetComponent<UIDocument>().rootVisualElement.Q("BakStage");
            List<VisualElement> leaders = stage.Query<VisualElement>("Leader").ToList();

            Assert.AreEqual(1, leaders.Count,
                "the full entry gets a leader and the continuation line does not");
        }

        [Test]
        public void ARoleWithNoNameGetsNoLeaderEither() {
            // The same guard's other half. A heading-only line has nothing to lead TO.
            CreditsView view = SpawnView();
            var data = new CreditsData("CRED.DAT") { Title = "T", Layout = DistinctiveLayout() };
            data.Lines.Add(new CreditLine { Role = "SPECIAL THANKS:", Name = string.Empty });
            SetLayout(view, data.Layout);

            InvokeBuildLayout(view, data);

            VisualElement stage = view.GetComponent<UIDocument>().rootVisualElement.Q("BakStage");
            Assert.AreEqual(0, stage.Query<VisualElement>("Leader").ToList().Count);
        }
    

        /// <summary>
        /// A continuation line — a name with no role — still sits in the NAME COLUMN.
        /// </summary>
        /// <remarks>
        /// <b>This regressed once and only a side-by-side caught it.</b> The row is
        /// <c>SpaceBetween</c> and it is the flex-grown LEADER that pushes the name to the right
        /// edge; when the dot fix stopped building a leader for role-less lines, the name became the
        /// row's only child and fell back to the LEFT. Against the running original — whose
        /// role-less names all end on one right edge, a whole column of them under "MONSTERS
        /// PORTRAYED BY:" — ours ran down the far left of the plate.
        ///
        /// <para>So the two rules have to be asserted TOGETHER: no dots, and still right-aligned.
        /// Testing either alone is what let one fix break the other.</para>
        /// </remarks>
        [Test]
        public void AContinuationLineHasNoLeaderButStaysInTheNameColumn() {
            CreditsView view = SpawnView();
            CreditsData data = DistinctiveData();
            data.Lines.Clear();
            data.Lines.Add(new CreditLine { Role = string.Empty, Name = "NAME_PROBE" });
            SetLayout(view, DistinctiveLayout());
            InvokeBuildLayout(view, data);

            VisualElement scroller =
                view.GetComponent<UIDocument>().rootVisualElement.Q("CreditsScroller");
            Assert.AreEqual(1, scroller.childCount);
            VisualElement row = scroller[0];

            Assert.IsNull(row.Q("Leader"),
                "a line with no role gets no dots — the original guards the dot loop on BOTH halves");
            Assert.AreEqual(Justify.FlexEnd, row.resolvedStyle.justifyContent,
                "without the leader to push it, the name must be justified to the row's end or it "
                + "falls back to the left");
        }

        /// <summary>A role line keeps the leader, and therefore the row's default justification.</summary>
        /// <remarks>The pair: this is what the test above must NOT break.</remarks>
        [Test]
        public void ARoleLineStillGetsItsLeader() {
            CreditsView view = SpawnView();
            SetLayout(view, DistinctiveLayout());
            InvokeBuildLayout(view, DistinctiveData());

            VisualElement row =
                view.GetComponent<UIDocument>().rootVisualElement.Q("CreditsScroller")[0];

            Assert.IsNotNull(row.Q("Leader"), "a role/name pair is exactly what the dots are for");
        }
}
}
