#if UNITY_EDITOR
namespace BakAgain.Editor.Input {
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;

    /// <summary>
    /// Drops state-change monitors whose action asset has been destroyed, on entering play mode.
    /// </summary>
    /// <remarks>
    /// <b>The Editor's InputSystem outlives play sessions, and so do monitors left on it.</b> A
    /// monitor registered by an <c>InputActionState</c> whose asset was destroyed while its maps
    /// were still enabled stays in <c>InputManager</c>'s per-device listener list. It cannot ever
    /// receive meaningful input again — the state behind it is gone — but it is still dispatched to,
    /// and every mouse movement then logs <i>"Map index out of range in ProcessControlStateChange"</i>
    /// followed by a NullReferenceException, once per stray, for the rest of the Editor's life.
    ///
    /// <para><b>What is measured, and what is not.</b> A production play session neither creates
    /// these nor leaves any behind — entering and exiting play from a fresh domain is clean. Running
    /// the PlayMode suite leaves exactly six, they are silent in edit mode, and they begin throwing
    /// the moment play mode is entered. So the person who sees the storm is whoever plays after a
    /// test run, which is how it reached the user. <b>The test that strands them has not been
    /// identified</b> — see the task — and this does not pretend to fix it. It removes the strays so
    /// they cannot poison the next play session.</para>
    ///
    /// <para><b>The predicate is narrow on purpose.</b> Only monitors owned by an action map whose
    /// asset is a destroyed <see cref="Object"/> are removed. A destroyed asset cannot legitimately
    /// be listening, so nothing live matches; anything whose asset is intact — including a
    /// deliberately disabled map — is left alone.</para>
    ///
    /// <para><b>Editor-only, and it changes nothing about the game.</b> The stale monitors exist
    /// only because the Editor keeps one InputSystem across many play sessions; a player build
    /// starts once and cannot accumulate them.</para>
    ///
    /// <para>Discovery is by reflection because <c>InputManager</c>'s listener list is internal;
    /// removal is the public <see cref="InputState.RemoveChangeMonitor"/>. If the package moves
    /// those internals the sweep finds nothing and logs nothing, which is the right way for a
    /// diagnostic aid to fail.</para>
    /// </remarks>
    [InitializeOnLoad]
    public static class StaleInputMonitorSweeper {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static StaleInputMonitorSweeper() {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode) {
                    Sweep();
                }
            };
        }

        [MenuItem("BaK/Diagnostics/Sweep stale input monitors")]
        private static void SweepFromMenu() {
            int removed = Sweep();
            Debug.Log($"Stale input monitors removed: {removed}");
        }

        /// <summary>Removes every monitor whose asset is gone. Returns how many.</summary>
        private static int Sweep() {
            var doomed = new List<(InputControl Control, IInputStateChangeMonitor Monitor, long Index)>();
            try {
                Collect(doomed);
            } catch {
                // Internals moved: nothing to sweep, and nothing worth reporting.
                return 0;
            }

            foreach ((InputControl control, IInputStateChangeMonitor monitor, long index) in doomed) {
                InputState.RemoveChangeMonitor(control, monitor, index);
            }
            return doomed.Count;
        }

        private static void Collect(
            List<(InputControl, IInputStateChangeMonitor, long)> doomed) {
            object manager = typeof(InputSystem)
                .GetField("s_Manager", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);
            object monitors = manager?.GetType().GetField("m_StateMonitors", Any)?.GetValue(manager);
            if (monitors?.GetType().GetField("m_MonitorsPerDevice", Any)
                    ?.GetValue(monitors) is not System.Array perDevice) {
                return;
            }

            for (var device = 0; device < perDevice.Length; device++) {
                object entry = perDevice.GetValue(device);
                if (entry?.GetType().GetField("listeners", Any)?.GetValue(entry)
                    is not System.Array listeners) {
                    continue;
                }
                for (var i = 0; i < listeners.Length; i++) {
                    object listener = listeners.GetValue(i);
                    if (listener == null) {
                        continue;
                    }
                    System.Type type = listener.GetType();
                    if (type.GetField("control", Any)?.GetValue(listener) is not InputControl control
                        || type.GetField("monitor", Any)?.GetValue(listener)
                            is not IInputStateChangeMonitor monitor
                        || !IsStale(monitor)) {
                        continue;
                    }
                    var index = (long)(type.GetField("monitorIndex", Any)?.GetValue(listener) ?? 0L);
                    doomed.Add((control, monitor, index));
                }
            }
        }

        /// <summary>Whether a monitor's owning asset has been DESTROYED — not merely absent.</summary>
        /// <remarks>
        /// *** "DESTROYED" AND "NEVER HAD ONE" ARE DIFFERENT, AND <c>== null</c> CANNOT TELL THEM
        /// APART. *** Unity's <c>==</c> reports true for a destroyed object and for a genuinely null
        /// reference alike. An action built in code — <c>new InputAction(...)</c>, which is how
        /// <c>SystemInputSource</c> creates movement, look, run and the debug keys — lives in a
        /// synthetic map whose <c>asset</c> is null from birth. Under the old test every one of them
        /// read as stale, so entering play mode tore the monitors off the LIVE gameplay actions:
        /// they stayed enabled and correctly resolved to eight controls, and read zero for ever.
        /// The arrow keys stopped moving the party, and disabling and re-enabling the action by hand
        /// put it straight back.
        ///
        /// <para><c>ReferenceEquals</c> is the discriminator: a destroyed <see cref="Object"/> still
        /// holds a real managed reference, where an action that never had an asset holds none. So a
        /// map is stale only when it HAS a reference AND that reference is dead.</para>
        /// </remarks>
        private static bool IsStale(IInputStateChangeMonitor monitor) {
            if (monitor.GetType().GetField("maps", Any)?.GetValue(monitor)
                is not System.Array maps || maps.Length == 0) {
                return false;
            }
            // The first map is enough: a state's maps all come from one asset.
            var first = maps.GetValue(0) as InputActionMap;
            if (first == null || ReferenceEquals(first.asset, null)) {
                return false;   // no asset was ever involved — an action created in code
            }
            return first.asset == null;   // a reference that is there, and dead
        }
    }
}
#endif
