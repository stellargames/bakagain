#if UNITY_EDITOR || ENABLE_PROFILER
namespace BakAgain.Testing {
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
                case "options": return Options();
                case "log": return Log();
                case "travel": return Travel();
                case "party": return Party();
                case "visuals": return Visuals();
                case "rect":
                    return a.Length == 2 ? Rect(a[1], 0)
                        : a.Length == 3 && int.TryParse(a[2], out int index) && index >= 0 ? Rect(a[1], index)
                        : Refuse("usage: rect <name>|.<class> [index]");
                case "cell":
                    return a.Length == 3 && int.TryParse(a[1], out int cx) && int.TryParse(a[2], out int cy)
                        ? Cell(cx, cy) : Refuse("usage: cell <x> <y>");
                case "path":
                    return a.Length >= 3 && int.TryParse(a[1], out int px) && int.TryParse(a[2], out int py)
                        ? Path(px, py, a.Length > 3 && int.TryParse(a[3], out int cap) ? cap : 6000, a.Skip(4))
                        : Refuse("usage: path <x> <y> [maxNodes] [avoid x0,x1,y0,y1 ...]");
                case "castpx":
                    return a.Length == 3 && int.TryParse(a[1], out int sid) && int.TryParse(a[2], out int pw)
                        ? CastPixel(sid, pw) : Refuse("usage: castpx <spellId|-1> <power>");
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

        /// <summary>
        /// What the hero whose turn it is can do, for the combat bot: each living foe's true thrust and
        /// swing odds (and whether it is in reach), each foe's odds against each hero, the cells a move
        /// can reach, and which stones are lit. Hidden numbers included: the bot is a test driver.
        /// </summary>
        private static string Options() {
            CombatEncounter e = Encounter(out string refusal);
            if (e == null) return Refuse(refusal);
            BakAgain.Combat.CombatRuntime cr = Hotspots().Combat;
            Combatant hero = e.Current;
            if (hero == null || !hero.IsPartyMember || hero.IsDead) return Refuse("not a party turn");
            int Max(Combatant c, GameData.ActorAttribute a) {
                GameData.Resources.Character.ActorStat[] st = cr.StatsFor(c);
                return st != null && (int)a < st.Length && st[(int)a] != null ? st[(int)a].Max : 0;
            }
            int Base(Combatant c, GameData.ActorAttribute a) {
                GameData.Resources.Character.ActorStat[] st = cr.StatsFor(c);
                return st != null && (int)a < st.Length && st[(int)a] != null ? st[(int)a].Base : 0;
            }
            object Odds(Combatant from, Combatant to, CombatActionDispatch.MeleeAttack k) {
                (int chance, int damage) = cr.HarnessMeleeOdds(from, to, k);
                return new Dictionary<string, object> { ["chance"] = chance, ["damage"] = damage };
            }
            Dictionary<string, object> Body(Combatant c) => new() {
                ["who"] = Who(e, c), ["x"] = c.X, ["y"] = c.Y, ["health"] = c.Health, ["stamina"] = c.Stamina,
                ["maxHealth"] = Max(c, GameData.ActorAttribute.Health),
                ["maxStamina"] = Max(c, GameData.ActorAttribute.Stamina), ["speed"] = c.Speed,
                ["casting"] = Base(c, GameData.ActorAttribute.AccuracyCasting) };
            var foes = new List<object>();
            foreach (Combatant f in e.Enemies) {
                if (f == null || f.IsDead) continue;
                Dictionary<string, object> d = Body(f);
                IReadOnlyList<(int X, int Y)> path = cr.ForecastThrust(hero, f);
                d["reach"] = path == null ? -1 : path.Count - 1;
                d["beside"] = CombatGrid.OrthogonallyAdjacent(hero.X, hero.Y, f.X, f.Y);
                d["thrust"] = Odds(hero, f, CombatActionDispatch.MeleeAttack.Thrust);
                d["swing"] = Odds(hero, f, CombatActionDispatch.MeleeAttack.Swing);
                var vs = new Dictionary<string, object>();
                foreach (Combatant p in e.Party) {
                    if (p != null && !p.IsDead) vs[Who(e, p)] = Odds(f, p, CombatActionDispatch.MeleeAttack.Thrust);
                }
                d["vs"] = vs;
                foes.Add(d);
            }
            var party = new List<object>();
            foreach (Combatant p in e.Party) {
                if (p != null && !p.IsDead) party.Add(Body(p));
            }
            var moves = new List<object>();
            for (int x = 0; x < CombatGrid.Width; x++) {
                for (int y = 0; y < CombatGrid.Height; y++) {
                    if ((x, y) == (hero.X, hero.Y)) continue;
                    IReadOnlyList<(int X, int Y)> walk = cr.ForecastWalk(hero, x, y);
                    if (walk != null && walk.Count > 1 && walk[walk.Count - 1] == (x, y)) moves.Add(new[] { x, y });
                }
            }
            var stones = new Dictionary<string, object>();
            var why = new Dictionary<string, object>();
            foreach (KeyValuePair<CombatCommands.Command, ActionAvailability> kv in cr.AvailabilityFor(hero)) {
                stones[kv.Key.ToString()] = kv.Value.Enabled;
                if (!kv.Value.Enabled) why[kv.Key.ToString()] = kv.Value.ReasonKey;
            }
            // Each castable spell with, per living foe, {chance, damage, freezes} at every power in its band.
            var spells = new List<object>();
            foreach ((int id, string name, int min, int max) in cr.HarnessCastableSpells(hero)) {
                var at = new Dictionary<string, object>();
                int targeting = cr.Spells.Spells[id].TargetingType;
                foreach (Combatant f in e.Enemies) {
                    if (f == null || f.IsDead || !Hotspots().HarnessSpellAimTakes(hero, f, targeting)) continue;
                    var byPower = new List<object>();
                    for (int power = min; power <= max; power++) {
                        (int chance, int damage, bool freezes) = cr.HarnessCastOdds(hero, f, id, power);
                        byPower.Add(new Dictionary<string, object> {
                            ["power"] = power, ["chance"] = chance, ["damage"] = damage, ["freezes"] = freezes });
                    }
                    at[Who(e, f)] = byPower;
                }
                spells.Add(new Dictionary<string, object> {
                    ["id"] = id, ["name"] = name, ["min"] = min, ["max"] = max, ["vs"] = at });
            }
            return Ok(new Dictionary<string, object> {
                ["hero"] = Body(hero), ["party"] = party, ["foes"] = foes, ["moves"] = moves, ["stones"] = stones,
                ["why"] = why, ["spells"] = spells });
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

        /// <summary>
        /// A breadth-first plan from the party to (x, y) over the step gate's own probe, on the party's real
        /// step lattice (underground it is a quarter of the overworld step). Plans only; the driver walks it
        /// with real keys. Returns the cardinal headings in order and how close the best node got.
        /// </summary>
        private static string Path(int tx, int ty, int cap, IEnumerable<string> avoidArgs) {
            GameSession s = Resolve<GameSession>();
            PartyMovement mv = Resolve<WorldRuntime>()?.Movement;
            if (s == null || mv?.Collision == null) return Refuse("not travelling");
            var avoid = new List<int[]>();
            foreach (string r in avoidArgs) {
                int[] v = r.Split(',').Select(int.Parse).ToArray();
                if (v.Length == 4 && !(s.PositionX >= v[0] && s.PositionX < v[1] && s.PositionY >= v[2] && s.PositionY < v[3])) avoid.Add(v);
            }
            // A key press is a glide of GlideStepsPerStep Small steps, each probed on its own (TASK-966), so an
            // edge is that many Small probes in one heading — a single Preferences-sized probe passes where a tap bumps.
            int sub = mv.WorldStepSpeed / mv.GlideStepsPerStep >> (mv.Underground ? 2 : 0);
            int subs = mv.GlideStepsPerStep;
            int step = sub * subs;
            // Nodes are real positions, reached by the same StepDelta the step uses, in all eight headings
            // (a Mac corridor runs diagonally); a half-step grid only dedupes them.
            int cell = Math.Max(1, step / 2);
            var start = (s.PositionX, s.PositionY);
            var came = new Dictionary<(int, int), ((int, int) prev, int h, (int, int) pos)> {
                [(start.Item1 / cell, start.Item2 / cell)] = ((int.MinValue, 0), -1, start) };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);
            (int, int) best = start;
            double bestDist = double.MaxValue;
            int nodes = 0;
            while (queue.Count > 0 && nodes < cap) {
                (int wx, int wy) = queue.Dequeue();
                nodes++;
                double d = Math.Sqrt((double)(tx - wx) * (tx - wx) + (double)(ty - wy) * (ty - wy));
                if (d < bestDist) { bestDist = d; best = (wx, wy); }
                if (d < step) break;
                mv.Collision.BuildCandidates(wx, wy, mv.DetailLevel);
                for (int h = 0; h < 8; h++) {
                    ushort yaw = (ushort)(h * 8192);
                    var (dx, dy) = MovementMath.StepDelta(yaw, sub);
                    int nx = wx, ny = wy;
                    bool open = true;
                    for (int k = 0; k < subs && open; k++) {
                        if (k > 0) mv.Collision.BuildCandidates(nx, ny, mv.DetailLevel);
                        // A pit is walkable on purpose, and falling in ends the game (PitDescent): never plan
                        // through one -- the run's party went down a sewer shaft at (708000,730400).
                        open = mv.Collision.ProbeWalkable(nx, ny, yaw, sub, out int kind, out _)
                            && kind != GameData.Resources.World.PitDescent.PitTerrainKind;
                        nx += dx; ny += dy;
                    }
                    if (subs > 1) mv.Collision.BuildCandidates(wx, wy, mv.DetailLevel);
                    var key = (nx / cell, ny / cell);
                    if (!open || came.ContainsKey(key)) continue;
                    if (avoid.Any(v => nx >= v[0] && nx < v[1] && ny >= v[2] && ny < v[3])) continue;
                    came[key] = ((wx / cell, wy / cell), yaw, (nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
            mv.Collision.BuildCandidates(s.PositionX, s.PositionY, mv.DetailLevel); // leave the gate as the party had it
            var headings = new List<int>();
            for (var k = (best.Item1 / cell, best.Item2 / cell); came[k].h >= 0; k = came[k].prev) headings.Add(came[k].h);
            headings.Reverse();
            return Ok(new Dictionary<string, object> {
                ["nodes"] = nodes, ["step"] = step, ["best"] = (int)bestDist, ["headings"] = headings });
        }

        private static string Party() {
            GameSession s = Resolve<GameSession>();
            if (s == null || s.ActivePartyIndices == null || s.ActivePartyIndices.Length == 0) return Refuse("no game session");
            var members = new List<object>();
            foreach (byte member in s.ActivePartyIndices) {
                var items = new List<string>();
                foreach (GameData.Resources.Inventory.RuntimeItem item in s.GetActorInventory(member)?.Items
                             ?? new List<GameData.Resources.Inventory.RuntimeItem>()) {
                    items.Add((s.ObjectInfo?.GetById(item.ObjectId)?.Name ?? item.ObjectId.ToString()) + " x" + item.Variable);
                }
                var conditions = new Dictionary<string, object>();
                GameData.Resources.Character.ActorConditions held = s.ConditionsOf(member);
                foreach (GameData.ActorCondition c in System.Enum.GetValues(typeof(GameData.ActorCondition))) {
                    if (held != null && held[c] > 0) conditions[c.ToString()] = held[c];
                }
                members.Add(new Dictionary<string, object> { ["character"] = (int)member, ["items"] = items,
                    ["health"] = s.PeekEffectiveStat(member, GameData.ActorAttribute.Health),
                    ["stamina"] = s.PeekEffectiveStat(member, GameData.ActorAttribute.Stamina),
                    ["conditions"] = conditions });
            }
            return Ok(new Dictionary<string, object> { ["gold"] = s.PartyGold, ["members"] = members });
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

        /// <summary>The <paramref name="index"/>th visible element with USS name <paramref name="key"/>, or class
        /// <c>.class</c>, across every UIDocument.</summary>
        private static string Rect(string key, int index) {
            bool byClass = key.StartsWith(".");
            foreach (UIDocument d in UnityEngine.Object.FindObjectsByType<UIDocument>()) {
                VisualElement root = d.rootVisualElement;
                if (root?.panel == null) continue;
                List<VisualElement> found = byClass
                    ? root.Query<VisualElement>(className: key.Substring(1)).ToList()
                    : root.Query<VisualElement>(key).ToList();
                foreach (VisualElement v in found) {
                    UnityEngine.Rect wb = v.worldBound;
                    // Not laid out yet (NaN) or empty: not somewhere a click can go.
                    if (v.resolvedStyle.display == DisplayStyle.None || !v.visible
                        || !(wb.width > 0 && wb.height > 0) || float.IsNaN(wb.x) || float.IsNaN(wb.y)) continue;
                    if (index-- > 0) continue;
                    float sx = Screen.width / root.panel.visualTree.layout.width;
                    float sy = Screen.height / root.panel.visualTree.layout.height;
                    UnityEngine.Rect b = v.worldBound;
                    return Ok(new Dictionary<string, object> {
                        ["x"] = Mathf.RoundToInt(b.x * sx), ["y"] = Mathf.RoundToInt(b.y * sy),
                        ["w"] = Mathf.RoundToInt(b.width * sx), ["h"] = Mathf.RoundToInt(b.height * sy) });
                }
            }
            return Refuse("no element " + key);
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

        /// <summary>Where to click on the open cast screen: a spell's symbol, or (spellId -1) a power's gem.</summary>
        private static string CastPixel(int spellId, int power) {
            UI.Spells.CastScreen cast = UnityEngine.Object.FindAnyObjectByType<UI.Spells.CastScreen>();
            if (cast == null || !cast.isActiveAndEnabled) return Refuse("no cast screen");
            return cast.HarnessPixel(spellId, power) is (int x, int y)
                ? Ok(new Dictionary<string, object> { ["x"] = x, ["y"] = y })
                : Refuse(spellId >= 0 ? $"spell {spellId} is not drawn" : $"no ring position for power {power}");
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
