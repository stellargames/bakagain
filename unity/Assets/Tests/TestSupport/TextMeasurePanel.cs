namespace BakAgain.Tests.TestSupport {
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>A real panel to measure a label's text on.</summary>
    /// <remarks>
    /// <c>MeasureTextSize</c> on a label that is on no panel logs "Trying to access the DPI setting of
    /// a visual element that is not on a panel" per call: 141 lines in one full run (TASK-1004). The
    /// panel is constant pixel size at scale 1 — the scale the off-panel measure assumed — so the
    /// numbers are the same.
    /// </remarks>
    public sealed class TextMeasurePanel : IDisposable {
        private readonly GameObject _host;
        private readonly PanelSettings _settings;

        public TextMeasurePanel() {
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;
            _host = new GameObject(nameof(TextMeasurePanel));
            _host.SetActive(false);
            _host.AddComponent<UIDocument>().panelSettings = _settings;
            _host.SetActive(true);
        }

        /// <summary>The label's text size, measured while it sits on this panel.</summary>
        public Vector2 Measure(Label label, string text) {
            VisualElement root = _host.GetComponent<UIDocument>().rootVisualElement;
            root.Add(label);
            try {
                return label.MeasureTextSize(text, 100000f, VisualElement.MeasureMode.AtMost,
                    0f, VisualElement.MeasureMode.Undefined);
            } finally {
                label.RemoveFromHierarchy();
            }
        }

        public void Dispose() {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_settings.themeStyleSheet);
            Object.DestroyImmediate(_settings);
        }
    }
}
