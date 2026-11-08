namespace BakAgain.UI.Rest {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Graphics;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Config;
    using GameData.Resources.Text;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced camp screen help (TASK-980): a stone "?" at the party table's corner opens a parchment
    /// panel under the camp panel that explains resting in words, with one line per member from
    /// <see cref="CampOutlookRule"/>. Port-only help; the rules stay the original's.
    /// </summary>
    public sealed class CampInfoView {
        public const string ButtonName = "camp-info-button";
        public const string PanelName = "camp-info-panel";
        public const string MemberLineClass = "camp-info-member";

        private VisualElement _button;
        private VisualElement _panel;
        private bool _open;

        /// <summary>Whether the explanation is showing.</summary>
        public bool IsOpen => _open;

        /// <summary>Builds (or rebuilds) the button and panel on <paramref name="stage"/> under the camp
        /// panel <paramref name="campPanel"/> (canonical); removes them when Enhanced is off.</summary>
        public void Build(VisualElement stage, GameSession session, Rect campPanel) {
            Clear();
            if (stage == null || session == null || !session.IsActive || !GameOptions.Enhanced) {
                return;
            }

            _button = new VisualElement { name = ButtonName, pickingMode = PickingMode.Position };
            _button.AddToClassList("enhanced-camp-info-button");
            _button.style.position = Position.Absolute;
            // The party table's own origin (ENCAMP.C's name column and heading row): its empty corner.
            _button.style.left = CampPartyStats.NameX;
            _button.style.top = CampPartyStats.HeadingY;
            // The button is the touch target; the stone inside it is smaller than a finger needs.
            var disc = new VisualElement { pickingMode = PickingMode.Ignore };
            disc.AddToClassList("enhanced-camp-info-disc");
            StoneButton.Dress(disc);
            var mark = new Label("?") { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("enhanced-camp-info-mark");
            disc.Add(mark);
            _button.Add(disc);
            _button.RegisterCallback<ClickEvent>(_ => Toggle());
            stage.Add(_button);

            _panel = new VisualElement { name = PanelName, pickingMode = PickingMode.Position };
            _panel.AddToClassList("enhanced-camp-info-panel");
            _panel.style.position = Position.Absolute;
            _panel.style.left = campPanel.x;
            _panel.style.top = campPanel.yMax;
            _panel.style.width = campPanel.width;
            _panel.style.backgroundImage = EnhancedArt.Load("parchment");
            foreach (string key in RuleKeys) {
                Line(UiTemplates.Format(key), "enhanced-camp-info-line");
            }
            foreach (string line in MemberLines(session)) {
                Line(line, "enhanced-camp-info-line").AddToClassList(MemberLineClass);
            }
            _panel.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
            // A longer text (a translation) than the room under the camp panel rises over it rather than off screen.
            VisualElement panel = _panel;
            panel.RegisterCallback<GeometryChangedEvent>(_ => {
                float overflow = panel.layout.yMax - Canonical.Height;
                if (overflow > 0.5f) {
                    panel.style.top = panel.layout.y - overflow;
                }
            });
            stage.Add(_panel);
        }

        public void Toggle() {
            _open = !_open;
            if (_panel != null) {
                _panel.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void Clear() {
            _button?.RemoveFromHierarchy();
            _panel?.RemoveFromHierarchy();
            _button = _panel = null;
        }

        private static readonly string[] RuleKeys = {
            UiTemplates.CampInfoPool, UiTemplates.CampInfoRate, UiTemplates.CampInfoConditions,
            UiTemplates.CampInfoNearDeath, UiTemplates.CampInfoModes,
        };

        /// <summary>One sentence per active member, in roster order.</summary>
        public static List<string> MemberLines(GameSession session) {
            var lines = new List<string>();
            string[] names = session.PartyActorNames;
            foreach (byte id in session.ActivePartyIndices) {
                ActorStat[] stats = session.StatsOf(id);
                if (stats == null) {
                    continue;
                }
                CampOutlook outlook = CampOutlookRule.For(stats[(int)ActorAttribute.Health],
                    stats[(int)ActorAttribute.Stamina], session.ConditionsOf(id), id);
                string name = id < names.Length ? names[id] : string.Empty;
                lines.Add(UiTemplates.Format(KeyFor(outlook), ("name", (object)name)));
            }
            return lines;
        }

        private static string KeyFor(CampOutlook outlook) => outlook switch {
            CampOutlook.FullyRested => UiTemplates.CampOutlookFullyRested,
            CampOutlook.NearDeath => UiTemplates.CampOutlookNearDeath,
            CampOutlook.LosesGround => UiTemplates.CampOutlookLosesGround,
            CampOutlook.NoGain => UiTemplates.CampOutlookNoGain,
            CampOutlook.BeyondCamp => UiTemplates.CampOutlookBeyondCamp,
            CampOutlook.HealthFirst => UiTemplates.CampOutlookHealthFirst,
            _ => UiTemplates.CampOutlookStaminaNext,
        };

        private Label Line(string text, string cls) {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            _panel.Add(l);
            return l;
        }
    }
}
