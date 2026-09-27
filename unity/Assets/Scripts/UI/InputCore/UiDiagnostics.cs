namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Says who owns input and who is drawing, in one string.
    ///
    /// <para>Both halves matter, and confusing them is expensive. On 2026-09-02 three separate
    /// misdiagnoses came from the same shape: a screen rendered correctly, something else drew over
    /// it, and the symptom read as "our renderer drew nothing". `[dbg] Cutscene Test` replaying
    /// G_MISC in a <c>while(true)</c> was mistaken twice for a stale frame, and the Main Menu drawing
    /// over a cutscene once more. Input ownership would not have shown any of that — DRAWING would.</para>
    ///
    /// <para>Which is why this reports the input stack AND the render order, rather than the input
    /// stack alone: the layer holding input is frequently not the thing you are looking at.</para>
    /// </summary>
    public static class UiDiagnostics {
        /// <summary>The IMGUI overlay <c>StartStateSelector</c> creates; it draws above everything
        /// and is invisible to both enumerations below, so it is checked for by name.</summary>
        private const string SelectorOverlay = "StartStateSelectorOverlay";

        /// <summary>Human-readable snapshot of input ownership and draw order.</summary>
        public static string Describe() {
            var text = new StringBuilder();
            AppendInput(text);
            AppendDrawing(text);

            return text.ToString();
        }

        /// <summary>The input half alone — usable without a scene, which is what the tests drive.</summary>
        public static string DescribeStack(InputLayerStack stack) {
            var text = new StringBuilder();
            AppendStack(text, stack);

            return text.ToString();
        }

        private static void AppendInput(StringBuilder text) {
            InputLayerStack stack = UiDriver.Stack;
            if (stack == null) {
                text.AppendLine("INPUT: no stack (UiDriver.Stack is null — not in a running session?)");

                return;
            }
            AppendStack(text, stack);
        }

        private static void AppendStack(StringBuilder text, InputLayerStack stack) {
            if (stack == null) {
                text.AppendLine("INPUT: no stack");

                return;
            }
            IInputLayer target = stack.ResolveInputTarget();
            text.AppendLine($"INPUT (modal: {stack.IsModal}, target: {Name(target)})");
            IReadOnlyList<IInputLayer> layers = stack.Layers;
            if (layers.Count == 0) {
                text.AppendLine("  (stack empty — nothing owns input)");

                return;
            }
            for (int i = 0; i < layers.Count; i++) {
                IInputLayer layer = layers[i];
                string marker = ReferenceEquals(layer, target) ? "  <- target" : "";
                string live = stack.IsInteractable(layer) ? "interactable" : "blocked";
                text.AppendLine($"  [{i}] {Name(layer),-28} {layer.CaptureMode,-10} {live}{marker}");
            }
        }

        private static void AppendDrawing(StringBuilder text) {
            text.AppendLine("DRAWING (topmost last)");
            var rows = new List<(float Order, string Line)>();

            foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) {
                if (doc == null || !doc.isActiveAndEnabled) {
                    continue;
                }
                rows.Add((doc.sortingOrder, $"  UIDocument {doc.name,-30} sort {doc.sortingOrder}"));
            }
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) {
                if (canvas == null || !canvas.isActiveAndEnabled) {
                    continue;
                }
                rows.Add((canvas.sortingOrder, $"  Canvas     {canvas.name,-30} sort {canvas.sortingOrder}"));
            }
            rows.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (rows.Count == 0) {
                text.AppendLine("  (nothing enabled)");
            }
            foreach ((float _, string line) in rows) {
                text.AppendLine(line);
            }

            // OnGUI draws after everything and belongs to no enumeration above, so a caller staring
            // at a blank capture would get no hint from the lists. Name the one we ship.
            if (GameObject.Find(SelectorOverlay) != null) {
                text.AppendLine($"  IMGUI      {SelectorOverlay} PRESENT  <- draws OVER all of the above");
            }
        }

        private static string Name(IInputLayer layer) =>
            layer == null ? "(none)" : string.IsNullOrEmpty(layer.Id) ? layer.GetType().Name : layer.Id;
    }
}
