namespace BakAgain.ResourceManagement.Loaders {
    using System;
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using InputForm = GameData.Resources.Menu.InputForm;
    using InputFieldData = GameData.Resources.Menu.InputField;

    /// <summary>
    /// Draws an IN_*.DAT input form (e.g. <c>IN_SAVE.DAT</c>'s "Directories"/"Games"
    /// column captions + their text boxes) over a REQ menu — the Unity sibling of
    /// <see cref="MenuLabelRenderer"/>, same handle/release pattern and mid-await
    /// active guard. Only the screen(s) that set an <c>inAddress</c> use this; every
    /// other screen leaves it empty (no-op), same convention as the optional LBL
    /// companion resource. Extracted field coordinates are already in canonical stage
    /// space (<c>AspectCorrection.ScaleVgaX/Y</c> ran in <c>InExtractor</c>), so no
    /// further scaling happens here.
    ///
    /// Fields are drawn <b>non-editable</b> and model-driven, faithful to the
    /// original's per-field draw routine (<c>sub_ovr140_A73</c> @0x47463): a box fill +
    /// border, text on top, and — only on the active field — either a blinking caret
    /// line (collapsed selection) or a highlight rect over just the selected substring
    /// (non-collapsed selection). Colour for all of these now comes from the theme
    /// (<c>.menu-input-field</c>/<c>.menu-input-text</c>/<c>.menu-input-caret</c>/
    /// <c>.menu-input-selection</c>) — the original per-field pen bytes
    /// (<c>InputFieldData</c>'s former BackgroundPen/BorderPen/CaretPen/TextPen/
    /// SelectionPen) are read-and-discarded at extraction and no longer modeled. The
    /// owning screen (e.g. SaveGameMenu) keeps the actual text/caret/selection model
    /// and pushes it here via <see cref="SetText"/>/<see cref="SetSelection"/>/<see cref="SetActive"/>.
    /// </summary>
    internal sealed class InputFormRenderer {
        private readonly string _inAddress;
        private readonly ILogger _logger;

        private AsyncOperationHandle<InputForm> _formHandle;
        private bool _formHandleValid;

        private readonly List<FieldView> _fields = new();

        // Caret blink cadence, matching the original's visible-blink feel (~330ms on,
        // ~330ms off — the brief's "330ms on / 660ms off" reduces to a single toggle
        // timer of this period since each tick just flips the state).
        private const long CaretBlinkIntervalMs = 330;

        // Box geometry (canonical space, matches the pre-existing hardcoded box height).
        private const int BoxHeight = 66;
        private const int BorderWidth = 3;
        private const int TextInsetX = 3;
        private const int TextInsetY = 2;
        private const int CaretWidth = 2;

        /// <summary>Per-field visual state: the elements built for one <c>InputField</c>
        /// entry, plus the text/selection/active model pushed by the controller.</summary>
        private sealed class FieldView {
            public VisualElement Container;
            public VisualElement Highlight;
            public Label Text;
            public VisualElement Caret;
            public IVisualElementScheduledItem BlinkSchedule;

            public string DisplayText = string.Empty;
            public int SelStart;
            public int SelEnd;
            public bool Active;
            public bool CaretPhaseVisible;
        }

        public InputFormRenderer(string inAddress, ILogger logger) {
            _inAddress = inAddress;
            _logger = logger;
        }

        /// <summary>Sets the displayed text for field <paramref name="index"/> and
        /// redraws its caret/highlight against the (possibly now out-of-range)
        /// selection. No-op if out of range / nothing built yet.</summary>
        public void SetText(int index, string text) {
            FieldView field = GetFieldView(index);
            if (field == null) {
                return;
            }
            field.DisplayText = text ?? string.Empty;
            field.Text.text = field.DisplayText;
            Recompute(field);
        }

        /// <summary>Sets the caret/selection range for field <paramref name="index"/>.
        /// <paramref name="selStart"/> == <paramref name="selEnd"/> is a collapsed caret;
        /// otherwise it's a text-selection highlight over that substring. No-op if out
        /// of range / nothing built yet.</summary>
        public void SetSelection(int index, int selStart, int selEnd) {
            FieldView field = GetFieldView(index);
            if (field == null) {
                return;
            }
            field.SelStart = selStart;
            field.SelEnd = selEnd;
            Recompute(field);
        }

        /// <summary>Marks field <paramref name="index"/> as the active field (draws a
        /// caret/highlight) or inactive (plain text, matching the original — only the
        /// active field ever shows a caret/selection). No-op if out of range / nothing
        /// built yet.</summary>
        public void SetActive(int index, bool active) {
            FieldView field = GetFieldView(index);
            if (field == null) {
                return;
            }
            field.Active = active;
            Recompute(field);
        }

        private FieldView GetFieldView(int index) =>
            index >= 0 && index < _fields.Count ? _fields[index] : null;

        /// <summary>Load the IN resource and draw its captions + text boxes into
        /// <paramref name="root"/>. <paramref name="isActive"/> is the owning screen's
        /// isActiveAndEnabled — checked after the async load so a screen disabled
        /// mid-load doesn't draw into a torn-down stage. <paramref name="canvasWidth"/>
        /// mirrors <see cref="MenuLabelRenderer.BuildLabels"/>'s signature (used there for
        /// centered labels); InputField carries no centering flag, so it's currently unused
        /// here — kept for call-site/API parity in case a future IN form needs it.</summary>
        public async UniTask BuildFields(VisualElement root, int canvasWidth, Func<bool> isActive) {
            ClearFields();
            if (string.IsNullOrWhiteSpace(_inAddress)) {
                return;
            }

            _formHandle = Addressables.LoadAssetAsync<InputForm>(_inAddress);
            _formHandleValid = true;
            InputForm form = await _formHandle;
            if (form == null || (isActive != null && !isActive())) {
                return;
            }

            for (int i = 0; i < form.Fields.Count; i++) {
                InputFieldData field = form.Fields[i];

                if (!string.IsNullOrEmpty(field.Label)) {
                    var caption = new Label(field.Label) {
                        style = {
                            left = field.LabelX,
                            top = field.LabelY,
                            fontSize = BakAgain.UI.GameFontText.FontSizePx,
                        }
                    };
                    caption.AddToClassList("req-label");
                    root.Add(caption);
                }

                var container = new VisualElement {
                    name = $"in-field-{i}",
                    style = {
                        position = Position.Absolute,
                        left = field.X,
                        top = field.Y,
                        width = field.Width,
                        height = BoxHeight,
                        borderTopWidth = BorderWidth,
                        borderLeftWidth = BorderWidth,
                        borderRightWidth = BorderWidth,
                        borderBottomWidth = BorderWidth,
                    }
                };
                container.AddToClassList("menu-input-field");

                var highlight = new VisualElement {
                    name = $"in-field-{i}-selection",
                    style = {
                        position = Position.Absolute,
                        top = TextInsetY,
                        left = TextInsetX,
                        width = 0,
                        height = BoxHeight - TextInsetY - BorderWidth,
                        display = DisplayStyle.None,
                    }
                };
                highlight.AddToClassList("menu-input-selection");
                container.Add(highlight);

                var text = new Label(string.Empty) {
                    style = {
                        position = Position.Absolute,
                        left = TextInsetX,
                        top = TextInsetY,
                        fontSize = BakAgain.UI.GameFontText.FontSizePx,
                    }
                };
                text.AddToClassList("req-label");
                text.AddToClassList("menu-input-text");
                container.Add(text);

                var caret = new VisualElement {
                    name = $"in-field-{i}-caret",
                    style = {
                        position = Position.Absolute,
                        top = TextInsetY,
                        left = TextInsetX,
                        width = CaretWidth,
                        height = BoxHeight - TextInsetY - BorderWidth,
                        display = DisplayStyle.None,
                    }
                };
                caret.AddToClassList("menu-input-caret");
                container.Add(caret);

                root.Add(container);
                var view = new FieldView {
                    Container = container,
                    Highlight = highlight,
                    Text = text,
                    Caret = caret,
                };
                _fields.Add(view);
                // The font/layout isn't resolved when the controller first pushes text/selection
                // (SetText/SetSelection run before the first layout pass), so MeasureTextSize returns
                // 0 and the caret/highlight land at width 0. Re-run the positioning once the label
                // has a resolved layout (and on any later resize/text change).
                text.RegisterCallback<GeometryChangedEvent>(_ => Recompute(view));
            }
        }

        /// <summary>Recomputes the caret/highlight for <paramref name="field"/> from its
        /// current text/selection/active state, per the original's field-draw rules:
        /// only the active field ever shows a caret or highlight; a collapsed selection
        /// (selStart == selEnd) draws a blinking caret line at that character position,
        /// a non-collapsed one draws a highlight rect over just the selected substring.
        /// Widths are measured against the field's own <see cref="Label"/> (so the same
        /// resolved font/size the text is actually drawn with is used), which must
        /// already be attached to the panel for USS/theme font resolution to apply.</summary>
        private void Recompute(FieldView field) {
            if (!field.Active) {
                StopBlink(field);
                field.Highlight.style.display = DisplayStyle.None;
                return;
            }

            string text = field.DisplayText ?? string.Empty;
            int selStart = Mathf.Clamp(field.SelStart, 0, text.Length);
            int selEnd = Mathf.Clamp(field.SelEnd, 0, text.Length);

            if (selStart == selEnd) {
                field.Highlight.style.display = DisplayStyle.None;
                float caretX = MeasureWidth(field.Text, Substring(text, 0, selStart));
                field.Caret.style.left = TextInsetX + caretX;
                StartBlink(field);
            } else {
                StopBlink(field);
                int lo = Mathf.Min(selStart, selEnd);
                int hi = Mathf.Max(selStart, selEnd);
                float highlightX = MeasureWidth(field.Text, Substring(text, 0, lo));
                float highlightWidth = MeasureWidth(field.Text, Substring(text, lo, hi - lo));
                field.Highlight.style.left = TextInsetX + highlightX;
                field.Highlight.style.width = highlightWidth;
                field.Highlight.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>Guarded substring — clamps to the string bounds so an out-of-range
        /// selection (e.g. mid-edit before a text/selection push race) can't throw.</summary>
        private static string Substring(string text, int start, int length) {
            start = Mathf.Clamp(start, 0, text.Length);
            length = Mathf.Clamp(length, 0, text.Length - start);
            return length <= 0 ? string.Empty : text.Substring(start, length);
        }

        /// <summary>Pixel width of <paramref name="text"/> as the panel would render it,
        /// measured against <paramref name="label"/>'s own resolved style (font/size come
        /// from the shared REQ theme's <c>.req-label</c>/universal font rule).</summary>
        private static float MeasureWidth(Label label, string text) {
            if (string.IsNullOrEmpty(text)) {
                return 0f;
            }
            return label.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
        }

        private void StartBlink(FieldView field) {
            field.CaretPhaseVisible = true;
            field.Caret.style.display = DisplayStyle.Flex;
            if (field.BlinkSchedule == null) {
                field.BlinkSchedule = field.Caret.schedule.Execute(() => {
                    field.CaretPhaseVisible = !field.CaretPhaseVisible;
                    field.Caret.style.display = field.CaretPhaseVisible ? DisplayStyle.Flex : DisplayStyle.None;
                }).Every(CaretBlinkIntervalMs);
            } else {
                field.BlinkSchedule.Resume();
            }
        }

        private void StopBlink(FieldView field) {
            field.BlinkSchedule?.Pause();
            field.CaretPhaseVisible = false;
            field.Caret.style.display = DisplayStyle.None;
        }

        private void ClearFields() {
            foreach (FieldView field in _fields) {
                field.BlinkSchedule?.Pause();
            }
            _fields.Clear();
        }

        /// <summary>Release the IN handle on screen teardown.</summary>
        public void ReleaseHandles() {
            foreach (FieldView field in _fields) {
                field.BlinkSchedule?.Pause();
            }
            if (_formHandleValid && _formHandle.IsValid()) {
                Addressables.Release(_formHandle);
            }
            _formHandleValid = false;
        }
    }
}
