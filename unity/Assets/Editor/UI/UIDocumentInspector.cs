namespace BakAgain.Editor.UI {
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Adds an "Export Visual Tree to UXML" button to every <see cref="UIDocument"/>
    /// inspector. Screens built procedurally at runtime (via
    /// <c>UserInterfaceLoader</c>) have no source UXML; this captures the live tree
    /// (in Play Mode) to a <c>.uxml</c> asset through a save-file dialog, using
    /// <see cref="VisualTreeUxmlExporter"/>.
    /// <para>Uses <see cref="Editor.DrawDefaultInspector"/> rather than delegating to
    /// Unity's built-in UIDocument editor, so drawing this inspector never itself
    /// triggers the source-asset rebuild that would wipe procedural content.</para>
    /// </summary>
    [CustomEditor(typeof(UIDocument))]
    public class UIDocumentInspector : Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            var document = (UIDocument)target;
            bool hasTree = Application.isPlaying
                && document.rootVisualElement != null
                && document.rootVisualElement.childCount > 0;

            using (new EditorGUI.DisabledScope(!hasTree)) {
                if (GUILayout.Button("Export Visual Tree to UXML…")) {
                    Export(document);
                }
            }
            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode and open this screen, then export its generated visual tree.",
                    MessageType.Info);
            } else if (!hasTree) {
                EditorGUILayout.HelpBox("This document has no generated content to export yet.",
                    MessageType.Info);
            }
        }

        private static void Export(UIDocument document) {
            VisualElement root = document.rootVisualElement;
            if (root == null || root.childCount == 0) {
                EditorUtility.DisplayDialog("Export Visual Tree",
                    "No generated visual tree found on this document.", "OK");
                return;
            }

            string suggested = SanitizeFileName(document.gameObject.name);
            string path = EditorUtility.SaveFilePanelInProject(
                "Export Visual Tree to UXML", suggested, "uxml",
                "Choose where to save the generated UXML asset (must be under Assets/).");
            if (string.IsNullOrEmpty(path)) {
                return; // cancelled
            }

            // Reference the panel's theme so the exported document carries its own
            // styling (font, class rules, colours) and previews correctly in UI
            // Builder — the screen's look lives entirely in that theme, not the UXML.
            string styleSrc = document.panelSettings != null
                ? StyleSrcFor(document.panelSettings.themeStyleSheet)
                : null;
            string uxml = VisualTreeUxmlExporter.Export(root, out int droppedImages, styleSrc);
            File.WriteAllText(path, uxml);
            AssetDatabase.ImportAsset(path);

            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null) {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
            Debug.Log($"[UIDocument] Exported visual tree to '{path}'." +
                (droppedImages > 0
                    ? $" {droppedImages} element(s) had a runtime background image that could not be " +
                      "serialised (no asset path); their class-based styling is preserved."
                    : string.Empty), asset);
        }

        // Build a UXML <Style> src reference (project://database/...?fileID&guid) for
        // a stylesheet/theme asset, so the exported document can attach it.
        private static string StyleSrcFor(Object asset) {
            if (asset == null
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId)) {
                return null;
            }
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) {
                return null;
            }
            string encoded = path.Replace(" ", "%20");
            return $"project://database/{encoded}?fileID={fileId}&guid={guid}&type=2";
        }

        private static string SanitizeFileName(string name) {
            foreach (char c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(name) ? "ExportedVisualTree" : name;
        }
    }
}
