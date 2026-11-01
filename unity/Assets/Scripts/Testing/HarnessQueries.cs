#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace BakAgain.Testing {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.World;
    using GameData.Resources.Combat;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using VContainer.Unity;

    /// <summary>
    /// The hook's queries (<see cref="HarnessHook"/>). Read-only: each one reads state or converts a position
    /// to a screen pixel. Pixels are window client pixels with the origin top-left, as X11 has them.
    /// </summary>
    internal static class HarnessQueries {
        /// <summary>A damage float as it appeared: what it said, over whom, and when.</summary>
        internal readonly struct Float {
            internal readonly string Text;
            internal readonly int Slot;
            internal readonly bool Party;
            internal readonly float At;
            internal readonly int Frame;
            internal Float(string text, int slot, bool party) {
                Text = text; Slot = slot; Party = party; At = Time.realtimeSinceStartup; Frame = Time.frameCount;
            }
        }

        private const int FloatsKept = 32;
        private static readonly Queue<Float> Floats = new();

        /// <summary>Called where a damage number is floated (WorldRuntime.FloatDamageNumbers).</summary>
        internal static void NoteFloat(string text, int slot, bool party) {
            Floats.Enqueue(new Float(text, slot, party));
            while (Floats.Count > FloatsKept) Floats.Dequeue();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearOnPlaySessionStart() => Floats.Clear();

        internal static string Answer(string line) {
            string[] a = (line ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return Refuse("empty request");
            switch (a[0]) {
                case "window": return Ok(new Dictionary<string, object> {
                    ["width"] = Screen.width, ["height"] = Screen.height, ["uiScale"] = UiScale() });
                case "screen": return Screen_();
                case "combat": return Combat();
                case "log": return Log();
                case "travel": return Travel();
                case "visuals": return Visuals();
                case "rect": return a.Length == 2 ? Rect(a[1]) : Refuse("usage: rect <name>");
                case "cell":
                    return a.Length == 3 && int.TryParse(a[1], out int cx) && int.TryParse(a[2], out int cy)
                        ? Cell(cx, cy) : Refuse("usage: cell <x> <y>");
                case "combatant": return a.Length == 2 ? CombatantAt(a[1]) : Refuse("usage: combatant <letter|p<slot>>");
                default: return Refuse("unknown query " + a[0]);
            }
        }

        internal static string Refuse(string error) => Json(new Dictionary<string, object> { ["ok"] = false, ["error"] = error });

        private static string Ok(Dictionary<string, object> fields) {
            var all = new Dictionary<string, object> {
                ["ok"] = true, ["t"] = Time.realtimeSinceStartup, ["frame"] = Time.frameCount };
            foreach (KeyValuePair<string, object> f in fields) all[f.Key] = f.Value;
            return Json(all);
        }

        // ---- state ----

        private static T Resolve<T>() where T : class {
            foreach (LifetimeScope scope in UnityEngine.Object.FindObjectsByType<LifetimeScope>()) {
                if (scope.Container != null && scope.Container.TryResolve(out T t)) return t;
            }
            return null;
        }

        private static World.Hotspots.HotspotService Hotspots() => Resolve<WorldRuntime>()?.HarnessHotspots;

        private static UI.Combat.Enhanced.EnhancedCombatController Controller() =>
            UnityEngine.Object.FindAnyObjectByType<UI.InGame.InGameScreen>()?.CombatController;

        private static string Screen_() {
            UI.InputCore.IInputLayer top = UI.InputCore.UiDriver.Stack?.Top;
            World.Hotspots.HotspotService hs = Hotspots();
            bool busy = hs != null && (hs.ArenaBusy || hs.AutoResolving);
            return Ok(new Dictionary<string, object> {
                ["top"] = top?.GetType().Name, ["layers"] = UI.InputCore.UiDriver.Stack?.Layers.Count ?? 0,
                ["fight"] = hs?.FightInProgress ?? false, ["idle"] = !busy });
        }

        private static CombatEncounter Encounter(out string refusal) {
            refusal = null;
            World.Hotspots.HotspotService hs = Hotspots();
            if (hs == null || !hs.FightInProgress || hs.Combat?.Encounter == null) {
                refusal = "not in a fight";
                return null;
            }
            return hs.Combat.Encounter;
        }

        private static string Who(CombatEncounter e, Combatant c) {
            if (c == null) return null;
            int foe = e.Enemies.IndexOf(c);
            return foe >= 0 ? BoardMarks.LetterFor(foe).ToString() : "p" + c.PartySlot;
        }

        private static string Combat() {
            CombatEncounter e = Encounter(out string refusal);
            if (e == null) return Refuse(refusal);
            var all = new List<object>();
            foreach (Combatant c in e.Party) all.Add(Describe(e, c, "party"));
            foreach (Combatant c in e.Enemies) all.Add(Describe(e, c, "foe"));
            return Ok(new Dictionary<string, object> { ["turn"] = Who(e, e.Current), ["combatants"] = all });
        }

        private static Dictionary<string, object> Describe(CombatEncounter e, Combatant c, string side) =>
            new() {
                ["who"] = Who(e, c), ["side"] = side, ["x"] = c.X, ["y"] = c.Y,
                ["health"] = c.Health, ["stamina"] = c.Stamina, ["dead"] = c.IsDead };

        private static string Log() {
            World.Hotspots.HotspotService hs = Hotspots();
            FightLog log = hs?.Combat?.Log;
            if (log == null) return Refuse("no fight log");
            int shown = Controller()?.LogShown ?? log.Count;
            return Ok(new Dictionary<string, object> { ["lines"] = new List<string>(log.Last(log.Count)), ["shown"] = shown });
        }

        private static string Travel() {
            GameSession s = Resolve<GameSession>();
            if (s == null) return Refuse("no game session");
            return Ok(new Dictionary<string, object> {
                ["zone"] = (int)s.CurrentZone, ["x"] = s.PositionX, ["y"] = s.PositionY,
                ["heading"] = (int)s.Rotation, ["time"] = s.GameTimeIn2Seconds });
        }

        private static string Visuals() {
            CombatEncounter e = Encounter(out string refusal);
            if (e == null) return Refuse(refusal);
            World.Hotspots.HotspotService hs = Hotspots();
            var sprites = new List<object>();
            foreach (World.Encounters.ArenaCombatant m in UnityEngine.Object.FindObjectsByType<World.Encounters.ArenaCombatant>()) {
                World.Encounters.AttackSwing swing = m.GetComponent<World.Encounters.AttackSwing>();
                sprites.Add(new Dictionary<string, object> {
                    ["who"] = Who(e, hs.CombatantFor(m.RosterSlot, m.PartyMember)), ["corpse"] = false,
                    ["swing"] = swing != null ? swing.CurrentFrame : -1 });
            }
            foreach (World.Encounters.ArenaCorpse m in UnityEngine.Object.FindObjectsByType<World.Encounters.ArenaCorpse>()) {
                World.Encounters.DeathCollapse collapse = m.GetComponent<World.Encounters.DeathCollapse>();
                sprites.Add(new Dictionary<string, object> {
                    ["who"] = Who(e, hs.CombatantFor(m.RosterSlot, m.PartyMember)), ["corpse"] = true,
                    ["collapse"] = collapse != null ? collapse.CurrentFrame : -1 });
            }
            var floats = new List<object>();
            foreach (Float f in Floats) {
                floats.Add(new Dictionary<string, object> {
                    ["text"] = f.Text, ["who"] = Who(e, hs.CombatantFor(f.Slot, f.Party)), ["t"] = f.At, ["frame"] = f.Frame });
            }
            return Ok(new Dictionary<string, object> { ["sprites"] = sprites, ["floats"] = floats });
        }

        // ---- where things are on screen ----

        private static float UiScale() {
            foreach (UIDocument d in UnityEngine.Object.FindObjectsByType<UIDocument>()) {
                IPanel p = d.rootVisualElement?.panel;
                if (p != null && p.visualTree.layout.width > 0) return Screen.width / p.visualTree.layout.width;
            }
            return 0f;
        }

        private static string Rect(string name) {
            foreach (UIDocument d in UnityEngine.Object.FindObjectsByType<UIDocument>()) {
                VisualElement root = d.rootVisualElement;
                if (root?.panel == null) continue;
                foreach (VisualElement v in root.Query<VisualElement>(name).ToList()) {
                    if (v.resolvedStyle.display == DisplayStyle.None || !v.visible || v.worldBound.width <= 0) continue;
                    float sx = Screen.width / root.panel.visualTree.layout.width;
                    float sy = Screen.height / root.panel.visualTree.layout.height;
                    UnityEngine.Rect b = v.worldBound;
                    return Ok(new Dictionary<string, object> {
                        ["x"] = Mathf.RoundToInt(b.x * sx), ["y"] = Mathf.RoundToInt(b.y * sy),
                        ["w"] = Mathf.RoundToInt(b.width * sx), ["h"] = Mathf.RoundToInt(b.height * sy) });
                }
            }
            return Refuse("no element " + name);
        }

        private static string Pixel(Vector3 world) {
            Camera cam = Resolve<WorldRuntime>()?.WorldCamera;
            if (cam == null) return Refuse("no world camera");
            Vector3 p = cam.WorldToScreenPoint(world);
            if (p.z <= 0) return Refuse("behind the camera");
            return Ok(new Dictionary<string, object> {
                ["x"] = Mathf.RoundToInt(p.x), ["y"] = Mathf.RoundToInt(Screen.height - p.y) });
        }

        private static string Cell(int x, int y) {
            if (Encounter(out string refusal) == null) return Refuse(refusal);
            return Hotspots().CellCentreWorld(x, y) is Vector3 w ? Pixel(w) : Refuse($"no cell {x},{y}");
        }

        private static string CombatantAt(string who) {
            CombatEncounter e = Encounter(out string refusal);
            if (e == null) return Refuse(refusal);
            World.Hotspots.HotspotService hs = Hotspots();
            foreach (World.Encounters.ArenaCombatant m in UnityEngine.Object.FindObjectsByType<World.Encounters.ArenaCombatant>()) {
                if (Who(e, hs.CombatantFor(m.RosterSlot, m.PartyMember)) != who) continue;
                Vector3 feet = m.transform.position;
                return Pixel((feet + World.BillboardSprite.DrawnTopCentre(m.transform)) / 2f);
            }
            return Refuse("no combatant " + who);
        }

        private static string Json(object v) => Newtonsoft.Json.JsonConvert.SerializeObject(v);
    }
}
#endif
