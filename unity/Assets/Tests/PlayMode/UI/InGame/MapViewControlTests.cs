namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.InGame;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The overhead map's pointer controls against a real panel and a camera looking straight down,
    /// driven by synthetic pointer events (touch included, which the Editor's mouse cannot make).
    /// </summary>
    public class MapViewControlTests {
        private const float Height = 100f;

        private GameViewResolutionScope _gameView;
        private GameObject _host, _camGo;
        private Camera _cam;
        private MapViewControl _view;
        private VisualElement _surface;
        private readonly List<int> _zooms = new();
        private readonly EnhancedMapLayout _layout = new();

        [UnitySetUp]
        public IEnumerator SetUp() {
            _gameView = GameViewResolutionScope.Force(1920, 1080);
            yield return null;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            _host = new GameObject("~MapViewHost");
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            var viewport = new VisualElement {
                // A fixed box: the document root does not stretch to the panel in a bare test document.
                style = { position = Position.Absolute, left = 0, top = 0, width = 800, height = 400 },
            };
            document.rootVisualElement.Add(viewport);

            _camGo = new GameObject("~MapViewCamera");
            _cam = _camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.fieldOfView = 30f;
            _cam.aspect = 2f;
            _cam.transform.SetPositionAndRotation(new Vector3(0, Height, 0), Quaternion.Euler(90f, 0f, 0f));

            _zooms.Clear();
            _view = new MapViewControl(_layout, steps => _zooms.Add(steps));
            _view.Attach(viewport, _cam);
            _surface = viewport.Q("BakMapGestures");
            yield return null;
            yield return null;
            Assert.Greater(_surface.worldBound.height, 0f, "the surface is laid out");
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _view.Detach();
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_host);
            _gameView.Restore();
            yield return null;
        }

        private float HalfH => Height * Mathf.Tan(15f * Mathf.Deg2Rad);
        private Vector2 Centre => _surface.worldBound.center;

        [UnityTest]
        public IEnumerator TheGroundFollowsADrag() {
            Send<PointerDownEvent>(0, Centre);
            Send<PointerMoveEvent>(0, Centre + new Vector2(100f, 0f));
            Send<PointerUpEvent>(0, Centre + new Vector2(100f, 0f));
            float perPx = 2f * HalfH / _surface.worldBound.height;
            Assert.AreEqual(-100f * perPx, _view.Pan.x, 0.01f, "dragging right moves the camera left");
            Assert.AreEqual(0f, _view.Pan.y, 0.01f);
            Assert.AreEqual(0, _view.Turn);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePartyNeverLeavesTheView() {
            Send<PointerDownEvent>(0, Centre);
            Send<PointerMoveEvent>(0, Centre + new Vector2(5000f, -5000f));
            _view.Clamp(Height);
            Assert.AreEqual(-HalfH * _cam.aspect * _layout.PanLimitFraction, _view.Pan.x, 0.01f);
            Assert.AreEqual(-HalfH * _layout.PanLimitFraction, _view.Pan.y, 0.01f, "panel y runs down; the camera goes the other way");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARightDragTurnsAndADoubleClickRecentres() {
            Send<PointerDownEvent>(0, Centre, button: 1);
            Send<PointerMoveEvent>(0, Centre + new Vector2(_surface.worldBound.width, 0f), button: 1);
            Send<PointerUpEvent>(0, Centre, button: 1);
            Assert.AreEqual(Vector2.zero, _view.Pan, "a right-drag does not pan");
            Assert.AreEqual(0x10000 - (int)(0x10000 * _layout.TurnPerViewWidth), _view.Turn, 2, "a view's width is the set share of a turn");

            Send<PointerDownEvent>(0, Centre);
            Send<PointerMoveEvent>(0, Centre + new Vector2(50f, 0f));
            Send<PointerUpEvent>(0, Centre);
            Send<PointerDownEvent>(0, Centre, clickCount: 2);
            Send<PointerUpEvent>(0, Centre);
            Assert.AreEqual(Vector2.zero, _view.Pan);
            Assert.AreEqual(0, _view.Turn);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FingersApartZoomInAndTogetherZoomOut() {
            Vector2 a = Centre - new Vector2(50f, 0f), b = Centre + new Vector2(50f, 0f);
            Send<PointerDownEvent>(1, a, touch: true);
            Send<PointerDownEvent>(2, b, touch: true);
            Send<PointerMoveEvent>(2, Centre + new Vector2(80f, 0f), touch: true);
            Assert.AreEqual(1, _zooms.Count, "100 -> 130 apart is past the step ratio");
            Assert.AreEqual(-1, _zooms[0], "apart is in: the camera comes down");
            Send<PointerMoveEvent>(2, Centre - new Vector2(10f, 0f), touch: true);
            Assert.AreEqual(2, _zooms.Count);
            Assert.AreEqual(+1, _zooms[1]);
            Send<PointerUpEvent>(1, a, touch: true);
            Send<PointerUpEvent>(2, b, touch: true);
            yield return null;
        }

        private void Send<T>(int id, Vector2 at, int button = 0, int clickCount = 1, bool touch = false)
                where T : PointerEventBase<T>, new() {
            using T e = PointerEventBase<T>.GetPooled(new FakePointer {
                pointerId = id, position = at, localPosition = at, button = button, clickCount = clickCount,
                pointerType = touch ? UnityEngine.UIElements.PointerType.touch : UnityEngine.UIElements.PointerType.mouse,
                pressedButtons = 1 << button, isPrimary = id <= 1,
            });
            e.target = _surface;
            _surface.SendEvent(e);
        }

        private sealed class FakePointer : IPointerEvent {
            public int pointerId { get; set; }
            public string pointerType { get; set; }
            public bool isPrimary { get; set; }
            public int button { get; set; }
            public int pressedButtons { get; set; }
            public Vector3 position { get; set; }
            public Vector3 localPosition { get; set; }
            public Vector3 deltaPosition { get; set; }
            public float deltaTime { get; set; }
            public int clickCount { get; set; }
            public float pressure { get; set; }
            public float tangentialPressure { get; set; }
            public float altitudeAngle { get; set; }
            public float azimuthAngle { get; set; }
            public float twist { get; set; }
            public Vector2 tilt { get; set; }
            public PenStatus penStatus { get; set; }
            public Vector2 radius { get; set; }
            public Vector2 radiusVariance { get; set; }
            public EventModifiers modifiers { get; set; }
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }
    }
}
