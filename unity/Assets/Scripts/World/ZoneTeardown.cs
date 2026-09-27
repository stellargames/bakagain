namespace BakAgain.World {
    using System;
    using UnityEngine;
    /// <summary>Fires a callback in OnDestroy — used to dispose the zone's WorldModelLoader
    /// (its cached model templates) when the zone root is torn down.</summary>
    public sealed class ZoneTeardown : MonoBehaviour {
        public Action OnDestroyed;
        private void OnDestroy() => OnDestroyed?.Invoke();
    }
}
