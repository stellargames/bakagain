namespace BakAgain.Utility {
    using UnityEngine;

    public static class UnityObjectUtil {
        /// <summary>
        /// Destroys a Unity object using the correct call for the current context:
        /// <see cref="Object.Destroy(Object)"/> at runtime (deferred to end of frame,
        /// the only legal call during play) and <see cref="Object.DestroyImmediate(Object)"/>
        /// in the editor outside play mode. A null/already-destroyed object is a no-op.
        /// </summary>
        public static void Destroy(Object obj) {
            if (!obj) {
                return;
            }
            if (Application.isPlaying) {
                Object.Destroy(obj);
            } else {
                Object.DestroyImmediate(obj);
            }
        }
    }
}
