namespace BakAgain.Core {
    using BakAgain.Core.Services;
#if UNITY_EDITOR
    using BakAgain.Core.States.Debug;
#endif
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Chooses which entry point the game boots into. On <see cref="SelectAsync"/> it shows a small
    /// IMGUI overlay (<see cref="StartStateSelectorOverlay"/>) listing the entries; the agent/MCP
    /// path drives the same choice without UI via <see cref="DebugStart"/>. Entries are
    /// <see cref="IGameFlow"/> calls (there is no state machine) plus Editor-only debug scripts.
    /// Entry ids keep the historical *State type names so PlayerPrefs defaults and
    /// <c>DebugStart.Select("InGameState")</c> callers keep working.
    /// </summary>
    public sealed class StartStateSelector {
        public readonly struct Entry {
            public readonly string Name;       // display label
            public readonly string Id;         // stable id (historical *State type name)
            public readonly Func<UniTask> Run; // the entry point
            public Entry(string name, string id, Func<UniTask> run) { Name = name; Id = id; Run = run; }
        }

        private const string PrefKey = "BakAgain.DebugStart.LastState";

        private readonly ILogger<StartStateSelector> _logger;
        private readonly List<Entry> _entries;
        private UniTaskCompletionSource<Func<UniTask>> _pending;
        private static string _preselect;

        public IReadOnlyList<Entry> States => _entries;

        /// <summary>Id of the last-chosen entry (defaults to the intro), used to mark the default row.</summary>
        public string DefaultStateName => PlayerPrefs.GetString(PrefKey, "IntroCutsceneState");

        public StartStateSelector(ILogger<StartStateSelector> logger, IGameFlow flow
#if UNITY_EDITOR
            , BookTestState bookTest, TestCutsceneState testCutscene,
            ModelDebugState modelDebug, WorldTestState worldTest, PlayCutsceneState playCutscene
#endif
            ) {
            _logger = logger;
            _entries = new List<Entry> {
                new Entry("Intro Cutscene", "IntroCutsceneState", flow.Boot),
                new Entry("Main Menu", "MainMenuState", flow.ShowMainMenu),
                new Entry("New Game → Chapter Scenes", "ChapterScenesState", () => flow.StartNewGame()),
                new Entry("New Game (skip scenes)", "NewGameState", () => flow.StartNewGame(playChapterScenes: false)),
                new Entry("In-Game (World)", "InGameState", flow.EnterWorld),
#if UNITY_EDITOR
                new Entry("[dbg] Book Test", "BookTestState", bookTest.RunAsync),
                new Entry("[dbg] Cutscene Test", "TestCutsceneState", testCutscene.RunAsync),
                new Entry("[dbg] Play Cutscene", "PlayCutsceneState", playCutscene.RunAsync),
                new Entry("[dbg] Model Debug", "ModelDebugState", modelDebug.RunAsync),
                new Entry("[dbg] World Test", "WorldTestState", worldTest.RunAsync),
#endif
            };
        }

        /// <summary>
        /// Resolve the entry point: honour a pending preselect (set via <see cref="DebugStart"/>)
        /// without showing UI; otherwise show the overlay and await a click.
        /// </summary>
        public async UniTask<Func<UniTask>> SelectAsync() {
            Func<UniTask> pre = TakePreselect();
            if (pre != null) {
                return pre;
            }
            // The picker is a development tool. A release player boots the way the original does,
            // through the intro; before this, a release build sat on a blank screen waiting for it.
            if (!Application.isEditor && !Debug.isDebugBuild) {
                return _entries[0].Run;
            }

            _pending = new UniTaskCompletionSource<Func<UniTask>>();
            var go = new GameObject("StartStateSelectorOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<StartStateSelectorOverlay>().Bind(this);

            Func<UniTask> chosen;
            try {
                chosen = await _pending.Task;
            } finally {
                _pending = null;
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            return chosen;
        }

        /// <summary>Commit a choice (called by the overlay buttons).</summary>
        public void Choose(Entry entry) {
            PlayerPrefs.SetString(PrefKey, entry.Id);
            PlayerPrefs.Save();
            _logger.LogInformation("Start entry selected: {entry}", entry.Id);
            _pending?.TrySetResult(entry.Run);
        }

        /// <summary>
        /// Choose by id or name (e.g. "InGameState" or "Main Menu"). If the menu is showing it is
        /// dismissed with that choice; otherwise the choice is remembered for the next
        /// <see cref="SelectAsync"/>. Returns false for an unknown name.
        /// </summary>
        public bool SelectByName(string name) {
            Entry? e = Resolve(name);
            if (e == null) {
                _logger.LogWarning("Unknown start entry '{name}'. Known: {known}", name, string.Join(", ", StateNames()));
                return false;
            }
            if (_pending != null) Choose(e.Value);
            else _preselect = name;
            return true;
        }

        public Entry? Resolve(string name) {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Entry e in _entries) {
                if (string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    return e;
                }
            }
            return null;
        }

        public IEnumerable<string> StateNames() {
            foreach (Entry e in _entries) yield return e.Id;
        }

        private Func<UniTask> TakePreselect() {
            if (string.IsNullOrEmpty(_preselect)) return null;
            Entry? e = Resolve(_preselect);
            _preselect = null;
            if (e == null) return null;
            _logger.LogInformation("Start entry preselected (no menu): {entry}", e.Value.Id);
            return e.Value.Run;
        }

        internal static void SetPreselect(string name) => _preselect = name;
    }

    /// <summary>
    /// Stable static seam to the start-entry selector, mirroring <c>UiDriver</c>. Lets the Editor
    /// MCP (<c>execute_code</c>) and PlayMode tests pick the boot entry with no synthetic input:
    /// <c>BakAgain.Core.DebugStart.Select("InGameState")</c>. Set at container build.
    /// </summary>
    public static class DebugStart {
        public static StartStateSelector Active { get; set; }

        /// <summary>
        /// Drops the selector left behind by the previous play session.
        /// </summary>
        /// <remarks>
        /// <see cref="Active"/> is assigned once per container build and never cleared, so it
        /// outlives its session as a static. Runs before the first scene of every play session, so a
        /// selector can only ever be dispatched to while the session that built it is alive — see
        /// <see cref="Select"/> for what a stale one costs.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearActiveOnPlaySessionStart() => Active = null;

        /// <summary>Choose a start entry by id (historical *State type name) or display name.</summary>
        /// <remarks>
        /// <b>A selector is only live while the game is playing.</b> Called from the Editor MCP the
        /// normal sequence is Select-then-EnterPlaymode, and at that moment
        /// <see cref="Application.isPlaying"/> is false while <see cref="Active"/> still holds the
        /// PREVIOUS session's selector — with its <c>SelectAsync</c> still awaiting a choice if that
        /// session was stopped with the overlay up.
        ///
        /// <para>Handing the choice to that one is not merely useless: <c>Choose</c> completes the
        /// dead session's task, whose continuation then resumes inside the NEW session's player loop
        /// and runs the entry (<c>EnterWorld</c>) immediately — before
        /// <c>GameInitializationService</c> has registered the resource locator. STARTUP.GAM then
        /// fails to load with "No Location found", hydration aborts, and because the choice went to
        /// the dead selector the static preselect the new session reads was never set, so the world
        /// is never entered at all. That is TASK-189, which looked like a flaky Addressables
        /// registration and was really this.</para>
        /// </remarks>
        public static void Select(string stateName) {
            if (Active != null && Application.isPlaying) {
                Active.SelectByName(stateName);
            } else {
                StartStateSelector.SetPreselect(stateName);
            }
        }
    }
}
