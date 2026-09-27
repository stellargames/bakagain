namespace BakAgain.UI.InputCore {
    using System;
    using UnityEngine.InputSystem;

    /// <summary>
    /// Owns the one <see cref="DefaultInputActions"/> the input core shares, and switches it OFF
    /// before it is destroyed.
    /// </summary>
    /// <remarks>
    /// <b>The generated wrapper's <c>Dispose()</c> is <c>Object.Destroy(asset)</c> and nothing
    /// else.</b> Destroying an action asset whose maps are still ENABLED does not unregister the
    /// state-change monitors those maps hold on their controls — <c>&lt;Mouse&gt;/position</c> among
    /// them. The monitor survives, its <c>InputActionState</c> does not, and from then on every
    /// mouse movement is dispatched into a dead state: <i>"Map index out of range in
    /// ProcessControlStateChange"</i>, once per orphan, for as long as the Editor lives.
    ///
    /// <para><b>Measured, not inferred.</b> Enabling a fresh instance adds one monitor on
    /// <c>&lt;Mouse&gt;/position</c>; calling <c>Dispose()</c> alone leaves the count unchanged,
    /// while <c>Disable()</c> followed by <c>Dispose()</c> takes it back down. Six of these had
    /// accumulated in one Editor, and six was exactly the number of errors a SINGLE mouse move
    /// produced.</para>
    ///
    /// <para><b>Why a wrapper rather than a disposal ordering.</b> Registering the wrapper directly
    /// with the container would leave the container holding an <see cref="IDisposable"/> whose
    /// <c>Dispose</c> destroys the asset, and nothing constrains it to run after whatever else
    /// disables it. Owning the pair here makes the order impossible to get wrong: the only object
    /// the container disposes is this one, and it always disables first.</para>
    ///
    /// <para><b>Disabling is not a substitute for disposing.</b> Skipping the destroy leaks a live
    /// asset per container build — no errors, because a live state can be dispatched into, but the
    /// monitors still pile up. Both halves are needed, in this order.</para>
    /// </remarks>
    public sealed class SharedInputActions : IDisposable {
        private DefaultInputActions _actions;

        public SharedInputActions() {
            _actions = new DefaultInputActions();
        }

        /// <summary>The shared actions. Null once disposed.</summary>
        public DefaultInputActions Actions => _actions;

        /// <summary>The asset the maps live in, for consumers that switch maps by name.</summary>
        public InputActionAsset Asset => _actions?.asset;

        public void Dispose() {
            if (_actions == null) {
                return;
            }
            // *** ORDER IS THE WHOLE POINT. *** Disable unregisters the monitors; Dispose then
            // destroys an asset nothing is listening through. Reversed, the monitors outlive it.
            _actions.Disable();
            _actions.Dispose();
            _actions = null;
        }
    }
}
