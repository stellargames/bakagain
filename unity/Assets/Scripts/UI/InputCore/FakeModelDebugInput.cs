namespace BakAgain.UI.InputCore {
    /// <summary>Settable IModelDebugInput for tests.</summary>
    public sealed class FakeModelDebugInput : IModelDebugInput {
        public bool PrevModel { get; set; }
        public bool NextModel { get; set; }
        public bool PrevZone { get; set; }
        public bool NextZone { get; set; }
        public bool RotateLeft { get; set; }
        public bool RotateRight { get; set; }
    }
}
