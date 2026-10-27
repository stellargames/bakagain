namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Resources.Text;
    using GameData.Setup;
    using NUnit.Framework;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>The first-run setup wizard (TASK-1014), on its real prefab and panel settings.</summary>
    public class SetupWizardViewTests {
        private GameViewResolutionScope _gameView;
        private GameObject _go;
        private MetaMenu _view;

        internal static GameFilesReport Report(params GameFileState[] states) =>
            new(GameFolder.Required.Select((n, i) => (n, states[i])).ToDictionary(p => p.n, p => p.Item2));

        internal static MetaMenu Spawn(out GameObject go) {
            go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MetaMenu.prefab"));
            return go.GetComponent<MetaMenu>();
        }

        private VisualElement Root => _view.GetComponent<UIDocument>().rootVisualElement;

        private List<Button> Buttons(string cssClass) => Root.Query<Button>(className: cssClass).ToList();

        private static string Text(string key) => LanguagePacks.BootText(key);

        [UnitySetUp]
        public IEnumerator SetUp() {
            // The owner's phone shape: a 2400x1080 landscape screen.
            _gameView = GameViewResolutionScope.Force(2400, 1080);
            yield return null;
            _view = Spawn(out _go);
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_go);
            _gameView.Restore();
        }

        [Test]
        public void TheWelcomeStepShowsOnlyItsButtonAndLinks() {
            _view.Show(null);

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Welcome));
            Assert.That(Buttons("setup-primary").Select(b => b.text), Is.EqualTo(new[] { Text(UiTemplates.BootFindFiles) }));
            Assert.That(Buttons("setup-outline"), Is.Empty);
            string[] links = Buttons("setup-link").Select(b => b.text).ToArray();
            Assert.That(links, Has.Length.EqualTo(3));
            Assert.That(links[0], Does.Contain(Text(UiTemplates.BootWhereLink)));
            Assert.That(links[1], Does.Contain(Text(UiTemplates.BootBuyLink)));
            Assert.That(links[2], Does.Contain(Text(UiTemplates.BootStuckLink)));
        }

        [Test]
        public void PlayIsDisabledUntilAllFilesAreOk() {
            string play = Text(UiTemplates.BootPlay);
            _view.Show(Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Missing));
            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Check));
            Assert.That(Root.Query<Button>().ToList().Where(b => b.text == play && b.enabledSelf), Is.Empty);

            _view.Flow.Picked("BaK", Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Ok));
            _view.Render();
            Assert.That(Root.Query<Button>().ToList().Count(b => b.text == play && b.enabledSelf), Is.EqualTo(1));
        }

        [Test]
        public void TheCheckStepOffersBothHelpLinks() {
            _view.Show(Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Missing));

            string[] links = Buttons("setup-link").Select(b => b.text).ToArray();
            Assert.That(links, Has.Some.Contains(Text(UiTemplates.BootWhereLink)));
            Assert.That(links, Has.Some.Contains(Text(UiTemplates.BootStuckLink)));
        }

        [Test]
        public void AMissingFileIsNamedInTheNote() {
            _view.Show(Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Missing));

            string note = Root.Q<Label>(className: "setup-note")?.text;
            Assert.That(note, Is.Not.Null, "a note");
            Assert.That(note, Does.Contain("STARTUP.GAM"));
            Assert.That(note, Does.Not.Contain("/").And.Not.Contain("content://"));
        }

        [Test]
        public void NoOverrideFolderOnThisScreen() {
            _view.Show(null);
            foreach (Action step in new Action[] { () => { }, _view.Flow.Next }) {
                step();
                _view.Render();
                Assert.That(Root.Query<Toggle>().ToList(), Is.Empty);
                Assert.That(Root.Query<TextField>().ToList(), Is.Empty);
                Assert.That(Root.Query<TextElement>().ToList().Select(t => t.text),
                    Has.None.Contains("verride"));
            }
        }

        /// <summary>48dp on a 2400x1080 phone of ~420 dpi, in this panel's units.</summary>
        private static float MinTouchTarget(PanelSettings panel) {
            Assert.That(panel.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
            Assert.That(panel.match, Is.EqualTo(1f), "the scale follows the screen height only");
            float physicalPx = 48f * 420f / 160f;
            return physicalPx / (1080f / panel.referenceResolution.y);
        }

        [UnityTest]
        public IEnumerator EveryButtonIsAtLeast48Tall() {
            float min = MinTouchTarget(_view.GetComponent<UIDocument>().panelSettings);
            var steps = new (string Name, Action Go)[] {
                ("Welcome", () => _view.Show(null)),
                ("Find", () => _view.Flow.Next()),
                ("Check missing", () => _view.Show(Report(GameFileState.Ok, GameFileState.Missing, GameFileState.Damaged))),
                ("Check ok", () => _view.Flow.Picked("BaK", Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Ok))),
                ("Help", () => _view.Flow.OpenHelp()),
                ("Stuck", () => _view.Flow.OpenStuck()),
            };
            foreach ((string name, Action go) in steps) {
                go();
                _view.Render();
                yield return null;
                yield return null;
                List<Button> buttons = Root.Query<Button>().ToList();
                Assert.That(buttons, Is.Not.Empty, name);
                foreach (Button b in buttons) {
                    Assert.That(b.layout.height, Is.GreaterThanOrEqualTo(min), $"{name}: '{b.text}'");
                }
            }
        }

        [TearDown]
        public void ResetPlatform() => MetaMenu.PlatformForTest = null;

        [Test]
        public void TheAndroidStepsOfferPhoneHelpAsAButtonAndStackTheirLinks() {
            MetaMenu.PlatformForTest = RuntimePlatform.Android;
            _view.Show(null);
            _view.Flow.Next();
            _view.Render();

            Assert.That(Buttons("setup-outline").Select(b => b.text), Is.EqualTo(new[] { Text(UiTemplates.BootPhoneHelpLink) }));
            Assert.That(Root.Q("Links").ClassListContains("setup-links-stacked"), Is.True);
            Assert.That(Buttons("setup-link").Select(b => b.text), Has.Some.Contains(Text(UiTemplates.BootPickOtherAndroid)));

            _view.Show(Report(GameFileState.Ok, GameFileState.Ok, GameFileState.Missing));
            Assert.That(Buttons("setup-outline").Select(b => b.text), Is.EqualTo(new[] { Text(UiTemplates.BootPhoneHelpLink) }), "the Check step too");
        }

        [Test]
        public void HelpIsPlatformSpecific() {
            foreach (RuntimePlatform platform in new[] { RuntimePlatform.Android, RuntimePlatform.WindowsPlayer, RuntimePlatform.LinuxPlayer }) {
                MetaMenu.PlatformForTest = platform;
                _view.Show(null);
                _view.Flow.OpenHelp();
                _view.Render();
                string text = string.Join("\n", Root.Query<Label>().ToList().Select(l => l.text)
                    .Concat(Root.Query<Button>().ToList().Select(b => b.text)));
                if (platform == RuntimePlatform.Android) Assert.That(text, Does.Not.Contain("Browse…"), platform.ToString());
                else Assert.That(text, Does.Not.Contain("phone"), platform.ToString());
            }
        }
    }
}
