namespace BakAgain.Core {
    using UnityEngine;

    /// <summary>
    /// Runtime-spawned IMGUI overlay that lists the selectable start states. Created and destroyed
    /// by <see cref="StartStateSelector.SelectAsync"/>. Drawn with OnGUI so it needs no
    /// prefab/UIDocument and stays fully isolated from the game's input-ownership stack
    /// (<c>InputLayerStack</c>); it is only ever alive before the first state is entered.
    /// </summary>
    public sealed class StartStateSelectorOverlay : MonoBehaviour {
        private StartStateSelector _selector;

        public void Bind(StartStateSelector selector) => _selector = selector;

        private void OnGUI() {
            if (_selector == null) return;

            const float width = 440f;
            const float rowHeight = 34f;
            float height = 96f + _selector.States.Count * (rowHeight + 4f);
            float x = (Screen.width - width) * 0.5f;
            float y = Mathf.Max(40f, (Screen.height - height) * 0.5f);

            GUILayout.BeginArea(new Rect(x, y, width, height), GUI.skin.box);

            var title = new GUIStyle(GUI.skin.label) {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            GUILayout.Label("Select start state", title);
            GUILayout.Space(8f);

            var button = new GUIStyle(GUI.skin.button) {
                fontSize = 15, alignment = TextAnchor.MiddleLeft, fixedHeight = rowHeight,
            };
            string defaultName = _selector.DefaultStateName;
            foreach (StartStateSelector.Entry entry in _selector.States) {
                bool isDefault = entry.Id == defaultName;
                string label = (isDefault ? "▶  " : "     ") + entry.Name;
                if (GUILayout.Button(label, button)) {
                    _selector.Choose(entry);
                }
            }

            GUILayout.EndArea();
        }
    }
}
