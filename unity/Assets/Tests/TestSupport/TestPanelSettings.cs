namespace BakAgain.Tests.TestSupport {
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>A PanelSettings for a test panel, with an (empty) theme.</summary>
    /// <remarks>
    /// A bare <c>CreateInstance&lt;PanelSettings&gt;()</c> logs "No Theme Style Sheet set to
    /// PanelSettings, UI will not render properly" every time its panel is touched: 255 lines in one
    /// full PlayMode run (TASK-1004). An empty theme draws exactly what no theme did, without the line.
    /// </remarks>
    public static class TestPanelSettings {
        public static PanelSettings Create() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            return settings;
        }
    }
}
