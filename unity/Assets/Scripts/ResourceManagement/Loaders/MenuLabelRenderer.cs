namespace BakAgain.ResourceManagement.Loaders {
    using System;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using LabelSet = GameData.Resources.Label.LabelSet;
    using LabelAttributes = GameData.Resources.Label.LabelAttributes;
    using LabelRole = GameData.Resources.Label.LabelRole;
    using GameLabel = GameData.Resources.Label.Label;

    /// <summary>
    /// Draws the companion LBL resource (group headings + option/checkbox text) over a
    /// REQ menu — the Unity equivalent of the original drawAllLabels. Colour comes from
    /// the theme now: every label gets <c>.req-label</c>, plus <c>.req-label--title</c>
    /// when <see cref="GameLabel.Role"/> is <see cref="LabelRole.Title"/> (the theme's
    /// parchment colour + drop shadow). Centered labels centre across the canvas.
    /// Extracted from <see cref="UserInterfaceLoader"/>, which owns the serialized label
    /// address and calls <see cref="BuildLabels"/> from its build loop. Position is set
    /// inline; colour/size/wrap/alignment come from the classes.
    /// </summary>
    internal sealed class MenuLabelRenderer {
        private readonly string _labelAddress;
        private readonly ILogger _logger;

        private AsyncOperationHandle<LabelSet> _labelHandle;
        private bool _labelHandleValid;

        public MenuLabelRenderer(string labelAddress, ILogger logger) {
            _labelAddress = labelAddress;
            _logger = logger;
        }

        /// <summary>Load the LBL resource and draw its labels into <paramref name="root"/>.
        /// <paramref name="isActive"/> is the owning screen's isActiveAndEnabled — checked
        /// after the async load so a screen disabled mid-load doesn't draw stale labels.</summary>
        public async UniTask BuildLabels(VisualElement root, int canvasWidth, Func<bool> isActive) {
            if (string.IsNullOrWhiteSpace(_labelAddress)) {
                return;
            }

            _labelHandle = Addressables.LoadAssetAsync<LabelSet>(_labelAddress);
            _labelHandleValid = true;
            LabelSet labelSet = await _labelHandle;
            if (labelSet == null || (isActive != null && !isActive())) {
                return;
            }

            foreach (GameLabel entry in labelSet.Labels) {
                if (string.IsNullOrEmpty(entry.Text)) {
                    continue;
                }
                bool centered = entry.Attributes.HasFlag(LabelAttributes.Centered);
                // Position is data-driven (inline); colour + size/wrap/alignment come
                // from .req-label (+ --title / --centered).
                var label = new Label(entry.Text) {
                    style = {
                        top = entry.YPosition,
                    }
                };
                label.AddToClassList("req-label");
                if (entry.Role == LabelRole.Title) {
                    label.AddToClassList("req-label--title");
                }
                if (centered) {
                    label.AddToClassList("req-label--centered");
                    // Centred titles (e.g. "Preferences") span the canvas width.
                    label.style.left = 0;
                    label.style.width = canvasWidth;
                } else {
                    label.style.left = entry.XPosition;
                }
                root.Add(label);
            }
        }

        /// <summary>Release the LBL handle on screen teardown.</summary>
        public void ReleaseHandles() {
            if (_labelHandleValid && _labelHandle.IsValid()) {
                Addressables.Release(_labelHandle);
            }
            _labelHandleValid = false;
        }
    }
}
