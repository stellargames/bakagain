namespace BakAgain.Core.Services {
    using System;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// The owner's play-test saves in a development player: the Android build stages them as
    /// Resources/<see cref="ResourceFolder"/>/SAVE##.bytes, and every start copies them into
    /// <see cref="DirectoryName"/> under the save root (before the first scene), so slots 1-4 there are fresh each launch.
    /// </summary>
    /// <remarks>.G20 keeps the set out of the way of a player's SAVES.G01: Classic lists one directory
    /// per number, and two .G01s would hide one of them.</remarks>
    public static class TestSaves {
        public const string ResourceFolder = "TestSaves";
        public const string DirectoryName = "TESTKIT.G20";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Seed() {
            if (Application.isEditor || !Debug.isDebugBuild) {
                return;
            }
            TextAsset[] saves = Resources.LoadAll<TextAsset>(ResourceFolder);
            if (saves.Length == 0) {
                return;
            }
            string saveRoot = Path.Combine(Application.persistentDataPath, SaveGameDirectoryService.DefaultRootDirectoryName);
            try {
                string dir = Path.Combine(saveRoot, DirectoryName);
                Directory.CreateDirectory(dir);
                foreach (TextAsset save in saves) {
                    File.WriteAllBytes(Path.Combine(dir, save.name + ".GAM"), save.bytes);
                }
                Debug.Log($"Seeded {saves.Length} test saves into {dir}.");
            } catch (Exception e) {
                Debug.LogError($"Failed to seed the test saves under {saveRoot}: {e}");
            }
        }
    }
}
