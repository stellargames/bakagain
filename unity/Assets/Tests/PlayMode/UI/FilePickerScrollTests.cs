namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using GameData.Resources.Menu;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A selection the screen moved itself (SAVE/LOAD's own Up/Down, which end in a Refresh) is
    /// scrolled into view one row at a time, like the original's widget_list_scroll ->
    /// listwidget_ensure_visible (canassa UI/WIDGET.C:215, UI/LISTWDG.C:333).
    /// </summary>
    public class FilePickerScrollTests {
        private sealed class Source : IFilePickerSource {
            public int Selected;
            public Rect GetPickerRect(int actionId) => new Rect(0, 0, 100, 100);
            public int GetVisibleRows(int actionId) => 5;
            public int GetItemCount(int actionId) => 19;
            public string GetItemLabel(int actionId, int index) => "row" + index;
            public int GetSelectedIndex(int actionId) => Selected;
            public void OnItemSelected(int actionId, int index) => Selected = index;
            public void OnItemActivated(int actionId, int index) { }
        }

        private static string FirstRow(VisualElement root) =>
            root.Q(className: "file-picker-row").Q<Label>("caption").text;

        [Test]
        public void Refresh_ScrollsTheSelectedRowIntoView_OneRowAtATime() {
            var source = new Source();
            var renderer = new FilePickerRenderer(source);
            var root = new VisualElement();
            renderer.Add(new UiElement { ActionId = 7, Visible = true }, root);

            for (int i = 1; i <= 7; i++) {
                source.Selected = i;
                renderer.Refresh(7);
            }
            Assert.That(FirstRow(root), Is.EqualTo("row3"), "row 7 sits on the bottom line");

            for (int i = 6; i >= 2; i--) {
                source.Selected = i;
                renderer.Refresh(7);
            }
            Assert.That(FirstRow(root), Is.EqualTo("row2"), "row 2 sits on the top line");
        }
    }
}
