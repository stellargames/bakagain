namespace BakAgain.UI.InGame {
    // Swappable movement-input seam. Movement is a gameplay concern kept OUT of the InputCore intent
    // stack; the stack only GATES it via the `active` flag. A future WasdMouseLookMovementDriver (its
    // own Input System Gameplay action map + camera look + mouse capture, all toggled off `active`)
    // is a drop-in behind this — the existing InputContext (InputContextId.Gameplay) is the map-switch
    // it would use. Design: docs/superpowers/specs/2026-07-02-ingame-inputcore-cutover-design.md.
    public interface IMovementDriver {
        // Called once per frame. `active` == the travel surface is the stack's resolved input target
        // (no menu/modal above). When false the driver must produce NO movement and release any
        // device capture it holds.
        void Tick(bool active);
    }
}
