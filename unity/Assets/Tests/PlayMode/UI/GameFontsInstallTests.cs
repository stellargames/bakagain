namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="GameFonts.Install"/> makes GAME the font of every label that names none.
    /// </summary>
    /// <remarks>
    /// It does that through <c>TextSettings.defaultFontAsset</c>, obsolete since 6000.6 with no
    /// replacement named (the pragma in <c>GameFonts.Install</c>). An obsolete property can keep
    /// compiling and quietly stop doing anything; this is what notices.
    /// </remarks>
    [RequiresShippedGameData]
    public class GameFontsInstallTests {
        private const string Text = "MMMMMMMMMM";

        [SetUp]
        public void BuildTheFonts() {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                GameFonts.Build();
            }
            Assert.That(GameFonts.Game, Is.Not.Null, "GameFonts.Build() produced no font from GAME.FNT.");
        }

        [Test]
        public void ALabelThatNamesNoFont_MeasuresAsGameOnceInstalled() {
            float installed = Width(install: true, explicitGame: false);
            float game = Width(install: true, explicitGame: true);
            float builtIn = Width(install: false, explicitGame: false);

            // The control: the measure can tell the two fonts apart at all.
            Assert.That(Mathf.Abs(game - builtIn), Is.GreaterThan(game * 0.05f),
                $"GAME ({game}) and Unity's built-in font ({builtIn}) measure alike, so this proves nothing");
            Assert.That(installed, Is.EqualTo(game).Within(0.5f),
                $"a label naming no font measured {installed}, GAME measures {game} (built-in: {builtIn}): "
                + "the installed default font is not being used");
        }

        private static float Width(bool install, bool explicitGame) {
            PanelSettings settings = TestPanelSettings.Create();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            PanelTextSettings original = settings.textSettings;
            if (install) {
                GameFonts.Install(settings);
            }
            var host = new GameObject(nameof(GameFontsInstallTests));
            try {
                host.SetActive(false);
                host.AddComponent<UIDocument>().panelSettings = settings;
                host.SetActive(true);
                var label = new Label(Text);
                label.style.fontSize = 55f;
                if (explicitGame) {
                    label.style.unityFontDefinition = new StyleFontDefinition(GameFonts.Game);
                }
                host.GetComponent<UIDocument>().rootVisualElement.Add(label);
                return label.MeasureTextSize(Text, 100000f, VisualElement.MeasureMode.AtMost,
                    0f, VisualElement.MeasureMode.Undefined).x;
            } finally {
                Object.DestroyImmediate(host);
                if (install) {
                    Object.DestroyImmediate(settings.textSettings);
                    settings.textSettings = original;
                }
                Object.DestroyImmediate(settings.themeStyleSheet);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
