namespace BakAgain.Editor {
    using BakAgain.Core;
    using UnityEditor;

    [InitializeOnLoad]
    public class EditorInitialization {
        static EditorInitialization() {
            ResourceManagementInitializer.InitializeResourceManagement();
        }
    }
}