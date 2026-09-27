namespace BakAgain.Tests.PlayMode.UI.Character {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI.Character;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine.UIElements;

    /// <summary>
    /// The compact character sheet as it reaches the screen — <c>charscreen_info_draw</c> @0x580fe.
    /// The arithmetic itself is pinned in the .NET model tests; these cover what the renderer does
    /// with it, which is where a faithful model can still be drawn wrongly.
    /// </summary>
    [TestFixture]
    public class CharacterSheetViewTests {
        private GameSession _session;
        private VisualElement _host;
        private CharacterSheetView _view;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(new SaveGameBuilder().WithBackingBody().WithPartyActors().Build(),
                GameSessionSource.NewGame);
            _host = new VisualElement();
            _view = new CharacterSheetView();
        }

        [Test]
        public void OnlyTheFirstTwoRatingsAreDrawnAsCurrentOfMaximum() {
            Render();

            // Two rows print the separator, each as text plus its drop shadow. Speed and Strength
            // are bare numbers even though the renderer has their maximum in hand.
            string of = GameData.Resources.Text.UiStrings.Get(CharacterSheetPanelRow.SeparatorKey);
            Assert.That(Labels().Count(label => label.text == of), Is.EqualTo(2 * 2));
        }

        [Test]
        public void EveryRatingSitsOnItsOwnRowAndInsideThePanel() {
            Render();

            var rows = new List<float>();
            for (var attribute = 0; attribute < CharacterSheetPanelRow.Count; attribute++) {
                float y = CharacterSheetPanelRow.RowY(attribute);
                Assert.That(LabelsAt(y).Any(l => l.text == ActorLabels.AttributeName(attribute)),
                    $"attribute {attribute} is not on its row");
                rows.Add(y);
            }
            Assert.That(rows.Distinct().Count(), Is.EqualTo(rows.Count), "two ratings share a row");
        }

        [Test]
        public void TheValueIsPositionedByItsRightEdge() {
            Render();

            // DisplayText's alignment 2 subtracts the measured width, so the label must be shifted
            // by its OWN width — a left-positioned value would drift with the number of digits.
            Label value = LabelsAt(CharacterSheetPanelRow.RowY(0))
                .First(l => l.style.left.value.value == CharacterSheetPanelRow.ValueRightX);
            Assert.That(value.style.translate.value.x.value, Is.EqualTo(-100f));
        }

        [Test]
        public void LookingAtTheSheetClearsTheChangeMarks() {
            int key = CharacterSheetPanelRow.ChangedFlagFor(Character, 0);
            _session.SetGlobalValue(key, 1);

            Render();

            // Read-and-clear: a rating highlights exactly once, and looking is the acknowledgement.
            // Only reading would leave every improvement highlighted for the rest of the game.
            Assert.That(_session.GetGlobalValue(key), Is.EqualTo(0));
        }

        [Test]
        public void AnAfflictionShowsInTheRatingItCosts() {
            Render();
            string sober = ValueOnRow(Stamina);

            _view.Clear();
            _session.ConditionsOf(Character)[ActorCondition.Drunk] = 100;
            Render();

            // The sheet is where a player sees what being drunk costs them: the penalty belongs in
            // the value the row prints, not only in the rolls made behind it. Drunk spares Health,
            // Speed and Strength, so Stamina is the one of these four rows that must move.
            Assert.That(ValueOnRow(Stamina), Is.Not.EqualTo(sober));
            Assert.That(ValueOnRow(Health), Is.EqualTo(ValueOnRowAfterSobering()));
        }

        [Test]
        public void AfflictionsCloseUpInsteadOfSittingOnTheirOwnConditionsLine() {
            ActorConditions conditions = _session.ConditionsOf(Character);
            conditions[ActorCondition.Poisoned] = 40; // condition 2
            conditions[ActorCondition.Starving] = 15; // condition 5

            Render();

            // Numbered by what was drawn, not by which condition it is: two afflictions fill the
            // first two lines whichever two they are.
            Assert.That(LabelsAt(CharacterSheetLayout.ConditionLineY(1)), Is.Not.Empty);
            Assert.That(LabelsAt(CharacterSheetLayout.ConditionLineY(2)), Is.Not.Empty);
            Assert.That(LabelsAt(CharacterSheetLayout.ConditionLineY(3)), Is.Empty);
            Assert.That(LabelsAt(CharacterSheetLayout.ConditionLineY(6)), Is.Empty);
        }

        [Test]
        public void AConditionReadsAsItsNameAndItsStrength() {
            _session.ConditionsOf(Character)[ActorCondition.Drunk] = 40;

            Render();

            Assert.That(Labels().Any(l => l.text == "Drunk (40%)"));
        }

        [Test]
        public void AnUnafflictedMemberGetsOneWordOnALineOfItsOwn() {
            Render();

            string normal = GameData.Resources.Text.UiStrings.Get(CharacterSheetLayout.NormalKey);
            Assert.That(Labels().Any(l => l.text == normal));
            // Three original pixels below where the first condition line would have been. They
            // never appear together, so aligning them would only ever show as a wobble between one
            // character and the next — but it would be the wrong sheet.
            Assert.That(LabelsAt(CharacterSheetLayout.ConditionLineY(1)), Is.Empty);
            Assert.That(LabelsAt(CharacterSheetLayout.NormalY), Is.Not.Empty);
        }

        [Test]
        public void ASecondRenderReplacesTheFirstRatherThanDrawingOverIt() {
            Render();
            int drawn = _host.childCount;

            Render();

            // The healer redraws the sheet on every pass, so a render that added to the last one
            // would pile up labels on top of each other and leak the previous member's numbers.
            Assert.That(_host.childCount, Is.EqualTo(drawn));
        }

        [Test]
        public void ClearingTakesEverythingWithIt() {
            Render();

            _view.Clear();

            Assert.That(_host.childCount, Is.Zero);
        }

        /// <summary>The current value printed on a row — the right-aligned label, not its shadow.</summary>
        private string ValueOnRow(int attribute) =>
            LabelsAt(CharacterSheetPanelRow.RowY(attribute))
                .First(l => l.style.left.value.value == CharacterSheetPanelRow.ValueRightX).text;

        /// <summary>Health read back with the affliction cleared, to show the penalty was the cause
        /// rather than the redraw.</summary>
        private string ValueOnRowAfterSobering() {
            _session.ConditionsOf(Character)[ActorCondition.Drunk] = 0;
            _view.Clear();
            Render();

            return ValueOnRow(Health);
        }

        private const int Health = 0;
        private const int Stamina = 1;

        // ---- the full sheet's lower half ------------------------------------------------------

    [Test]
    public void TheCompactSheetDrawsNoneOfTheLowerHalfsRows() {
        Render();

        // The loop over the lower half's twelve rows tests the caller's flag before its first
        // iteration, so a compact sheet is the panel and the condition list and nothing else.
        foreach (int attribute in LowerHalfAttributes()) {
            Assert.That(LabelsAt(CharacterSheetRow.RowY(attribute) + CharacterSheetRow.TextOffsetY),
                Is.Empty, $"attribute {attribute} was drawn on a compact sheet");
        }
    }

    [Test]
    public void TheFullSheetDrawsEveryRatingOnItsOwnRow() {
        RenderFull();

        foreach (int attribute in LowerHalfAttributes()) {
            string name = ActorLabels.AttributeName(attribute);
            Assert.That(
                LabelsAt(CharacterSheetRow.RowY(attribute) + CharacterSheetRow.TextOffsetY)
                    .Any(label => label.text == name),
                $"attribute {attribute} ({name}) is not on its row");
        }
    }

    [Test]
    public void AChangedRatingHighlightsItsNumberAsWellAsItsWord() {
        // The routine pushes its chosen pens twice, once for each. Highlighting only the name would
        // leave the number the player is looking for in the ordinary colour.
        int attribute = CharacterSheetLayout.LowerHalfFirstAttribute;
        _session.SetGlobalValue(CharacterSheetRow.ChangedFlagFor(Character, attribute), 1);

        RenderFull();

        Assert.That(_session.GetGlobalValue(CharacterSheetRow.ChangedFlagFor(Character, attribute)),
            Is.EqualTo(0), "reading the sheet must clear the mark on a lower-half row too");
    }

    [Test]
    public void TheSheetSitsOnAFullFrameParchmentBeneathTheScreensOwnButtons() {
        var button = new VisualElement { name = "ReqButton" };
        _host.Add(button);
        UnityEngine.Sprite parchment = UnityEngine.Sprite.Create(new UnityEngine.Texture2D(4, 4),
            new UnityEngine.Rect(0, 0, 4, 4), UnityEngine.Vector2.zero);

        _view.RenderAsync(_host, Character, _session, new EverySprite(parchment), palette: null)
            .GetAwaiter().GetResult();

        // charscreen_info_draw copies the whole of page 1 before it draws anything, so the parchment
        // is edge to edge and under everything — the REQ's buttons included, which were built first.
        VisualElement bottom = _host[0];
        Assert.That(bottom.name, Is.EqualTo("BakCharacterSheetParchment"));
        Assert.That(bottom.style.backgroundImage.value.sprite, Is.SameAs(parchment));
        Assert.That(bottom.style.right.value.value, Is.EqualTo(0f));
        Assert.That(bottom.style.bottom.value.value, Is.EqualTo(0f));
        Assert.That(_host.IndexOf(button), Is.GreaterThan(0));
    }

    private sealed class EverySprite : BakAgain.CutScenes.IResourceCache {
        private readonly UnityEngine.Sprite _sprite;

        public EverySprite(UnityEngine.Sprite sprite) => _sprite = sprite;

        public Cysharp.Threading.Tasks.UniTask<T> GetOrLoadAsync<T>(string key) where T : class =>
            Cysharp.Threading.Tasks.UniTask.FromResult(_sprite as T);

        public void Clear() { }
    }

    private static System.Collections.Generic.IEnumerable<int> LowerHalfAttributes() {
        for (var row = 0; row < CharacterSheetLayout.LowerHalfAttributeCount; row++) {
            yield return CharacterSheetLayout.LowerHalfFirstAttribute + row;
        }
    }

    private void RenderFull() =>
        _view.RenderAsync(_host, Character, _session, sprites: null, palette: null, logger: null,
                fullSheet: true)
            .GetAwaiter().GetResult();

    private void Render() =>
            // No sprite cache: the portrait and the frame pieces are the parts that need one, and
            // they are absent rather than wrong without it. Everything asserted here is text.
            _view.RenderAsync(_host, Character, _session, sprites: null, palette: null)
                .GetAwaiter().GetResult();

        private IEnumerable<Label> Labels() => _host.Children().OfType<Label>();

        private List<Label> LabelsAt(float y) =>
            Labels().Where(label => label.style.top.value.value == y).ToList();

        private const int Character = 0;
    }
}