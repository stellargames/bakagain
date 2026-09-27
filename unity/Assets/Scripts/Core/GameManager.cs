using BakAgain.Core.Services;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace BakAgain.Core
{
    /// <summary>
    /// App entry after resource initialization: asks the <see cref="StartStateSelector"/> which
    /// entry point to boot into (normally <c>GameFlow.Boot</c> — the intro attract loop; debug
    /// entries jump straight to a flow or debug script) and runs it. There is no state machine —
    /// transitions are <see cref="Services.IGameFlow"/> scripts (2026-07-12 architecture doc §3.1).
    /// </summary>
    public class GameManager
    {
        private readonly StartStateSelector _startStateSelector;
        private readonly IPreferencesService _preferences;
        private readonly ILogger<GameManager> _logger;

        public GameManager(StartStateSelector startStateSelector, IPreferencesService preferences,
            ILogger<GameManager> logger)
        {
            _startStateSelector = startStateSelector;
            _preferences = preferences;
            _logger = logger;
        }

        public async UniTask StartGame()
        {
            try {
                // *** BEFORE ANY FLOW RUNS. *** PreferencesService.Current is a lazy getter that
                // manufactures a Preferences from its C# field initializers and stamps it
                // Id = "USER" -- indistinguishable from a real load. Until 2026-09-10 the ONLY
                // caller of EnsureLoadedAsync was the preferences SCREEN, so unless the player
                // opened that menu the whole game ran on those initializers and never read
                // preferences.json or the shipped DEFAULT.DAT.
                //
                // Measured against the original on the same save: the party walked 200 units per
                // keypress where the original walked 100, because StepSize fell back to Medium
                // (MOVEMENT.DAT 800) instead of the persisted Small (400).
                //
                // Note what this does NOT break, because the obvious reading is wrong: all three
                // presets are 6.667 units per game-second (400/60, 800/120, 1600/240), so the
                // preset is granularity, not speed, and a journey costs the same game time either
                // way. What it changes is the size of one press -- so every step COUNT is off by
                // the preset ratio, which is what makes trigger rects, reach tests and any
                // scripted route land somewhere the original would not.
                await _preferences.EnsureLoadedAsync();

                Func<UniTask> run = await _startStateSelector.SelectAsync();
                await run();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during game flow");
            }
        }
    }
}
