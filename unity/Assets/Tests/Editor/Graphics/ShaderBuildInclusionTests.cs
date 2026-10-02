namespace BakAgain.Tests.Graphics {
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Every project shader the scripts look up by NAME must be built into players.
    /// </summary>
    /// <remarks>
    /// <b><c>Shader.Find</c> only finds what the build kept.</b> In the Editor every shader is
    /// there, so a missing one shows up only on a player: TASK-700's magenta door corners on Android
    /// were <c>BakAgain/HiddenFrame</c> missing from the build. No material references it (it is
    /// built in code), so the build stripped it; WorldMeshFrames then hid a door's inactive frame
    /// with a null material, which Unity draws with the magenta error material. A shader is kept
    /// when it is in Always Included Shaders or lives under a <c>Resources</c> folder.
    /// </remarks>
    public class ShaderBuildInclusionTests {
        // Shader.Find("Name") literals, and `const string XShaderName = "Name"` handed to it.
        private static readonly Regex FindLiteral = new Regex(@"Shader\.Find\(""([^""]+)""\)");
        private static readonly Regex NameConst = new Regex(@"const string \w*Shader\w*\s*=\s*""([^""]+)""");

        [Test]
        public void EveryShaderLookedUpByNameIsInPlayerBuilds() {
            var names = new HashSet<string>();
            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)) {
                string text = File.ReadAllText(file);
                foreach (Match m in FindLiteral.Matches(text)) names.Add(m.Groups[1].Value);
                foreach (Match m in NameConst.Matches(text)) names.Add(m.Groups[1].Value);
            }
            Assert.That(names, Does.Contain("BakAgain/HiddenFrame"), "the scan found WorldMeshFrames' constant");

            var settings = new SerializedObject(
                UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings());
            SerializedProperty always = settings.FindProperty("m_AlwaysIncludedShaders");
            var included = new HashSet<Shader>();
            for (var i = 0; i < always.arraySize; i++) {
                if (always.GetArrayElementAtIndex(i).objectReferenceValue is Shader s) included.Add(s);
            }

            var missing = new List<string>();
            foreach (string name in names) {
                Shader shader = Shader.Find(name);
                string path = shader == null ? null : AssetDatabase.GetAssetPath(shader);
                if (path == null || !path.StartsWith("Assets/")) {
                    continue;   // built-in or package shaders: not this project's to include
                }
                if (!included.Contains(shader) && !path.Contains("/Resources/")) {
                    missing.Add($"{name} ({path})");
                }
            }
            Assert.IsEmpty(missing, "looked up by name but stripped from player builds: "
                + string.Join(", ", missing.OrderBy(n => n)));
        }
    }
}
