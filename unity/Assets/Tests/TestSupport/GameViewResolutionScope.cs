namespace BakAgain.Tests.TestSupport {
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Forces the Game view to a fixed resolution for a test, and puts it back afterwards —
    /// <b>without leaving a trail of saved presets behind</b> (TASK-243).
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> The obvious spelling —
    /// <c>SetCustomRenderingResolution(w, h, "MyTest-1280x1024")</c> to force, then the same call
    /// with the original size and a <c>"MyTest-restore"</c> name to put it back — quietly persists
    /// BOTH names as custom Game view sizes in the Editor's own preferences
    /// (<c>Preferences/Editor-5.x/GameViewSizes.asset</c>). The third argument is not a label, it is
    /// the preset's identity. Six call sites across three fixtures had accumulated thirteen of them.
    ///
    /// <para><b>Two consequences, and the second is the one that bites.</b> The presets pile up
    /// unboundedly; and the suite finishes with one of them SELECTED, so an Editor opened after a
    /// test run shows whatever resolution the last test happened to force. That is how a
    /// <c>*-restore</c> entry once captured a 321x531 window and every later run inherited it — which
    /// then failed a UI test on a machine that had never seen that size, and cost a session's
    /// debugging aimed at the renderer.</para>
    ///
    /// <para><b>The fix is two rules.</b> Every fixture forces through <b>one shared preset
    /// name</b>, because Unity updates an existing preset with that name rather than appending —
    /// measured: three consecutive full suites produced exactly one entry per distinct name. And the
    /// restore puts back the <b>selected index</b> rather than minting a second preset, so the Game
    /// view returns to whatever the developer had chosen (the built-in Full HD 1920x1080, normally)
    /// instead of to a test's leftovers.</para>
    ///
    /// <para><b>The port targets 1920x1080</b>, so a suite that silently leaves a 5:4 Game view
    /// behind is not a cosmetic annoyance — it is the wrong aspect to be looking at.</para>
    /// </remarks>
    public struct GameViewResolutionScope {
        /// <summary>
        /// The one preset every fixture reuses. <b>Never make this per-fixture again</b> — a
        /// distinct name is a distinct saved preset, for ever.
        /// </summary>
        private const string SharedPresetName = "BakTests (temporary)";

        private PlayModeWindow.PlayModeViewTypes _originalViewType;
        private uint _originalWidth;
        private uint _originalHeight;
        private int _originalSizeIndex;
        private bool _haveSizeIndex;

        /// <summary>Force the Game view to <paramref name="width"/> x <paramref name="height"/>.</summary>
        public static GameViewResolutionScope Force(uint width, uint height) {
            var scope = new GameViewResolutionScope {
                _originalViewType = PlayModeWindow.GetViewType(),
            };
            PlayModeWindow.GetRenderingResolution(out scope._originalWidth, out scope._originalHeight);
            scope._haveSizeIndex = TryGetSelectedSizeIndex(out scope._originalSizeIndex);

            PlayModeWindow.SetCustomRenderingResolution(width, height, SharedPresetName);
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            return scope;
        }

        /// <summary>Put the Game view back. Safe to call from a <c>finally</c>.</summary>
        public void Restore() {
            // Restoring the INDEX is what avoids minting a second preset. The fallback does mint,
            // but only ever under the one shared name, so the worst case is still a single entry.
            if (!_haveSizeIndex || !TrySetSelectedSizeIndex(_originalSizeIndex)) {
                PlayModeWindow.SetCustomRenderingResolution(
                    _originalWidth, _originalHeight, SharedPresetName);
            }
            PlayModeWindow.SetViewType(_originalViewType);
        }

        // GameView.selectedSizeIndex is internal, so this is reflection by necessity. Both helpers
        // fail soft: a future Editor that renames the property costs us the tidy restore, not a red
        // suite.
        private static PropertyInfo SelectedSizeIndexProperty(out System.Type gameViewType) {
            gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return gameViewType?.GetProperty("selectedSizeIndex",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static bool TryGetSelectedSizeIndex(out int index) {
            index = -1;
            PropertyInfo property = SelectedSizeIndexProperty(out System.Type gameViewType);
            if (property == null) {
                return false;
            }
            foreach (Object window in Resources.FindObjectsOfTypeAll(gameViewType)) {
                index = (int)property.GetValue(window);
                return true;
            }
            return false;
        }

        private static bool TrySetSelectedSizeIndex(int index) {
            PropertyInfo property = SelectedSizeIndexProperty(out System.Type gameViewType);
            if (property == null || index < 0) {
                return false;
            }
            var restored = false;
            foreach (Object window in Resources.FindObjectsOfTypeAll(gameViewType)) {
                property.SetValue(window, index);
                ((EditorWindow)window).Repaint();
                restored = true;
            }
            return restored;
        }
    }
}
