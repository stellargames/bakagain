namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.InputSystem;

    public class InputContextTests {
        private static InputActionAsset MakeAsset() {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.AddActionMap("UI").AddAction("Navigate");
            asset.AddActionMap("Player").AddAction("Move");
            return asset;
        }

        [Test]
        public void Switch_EnablesExactlyOneMap() {
            var asset = MakeAsset();
            var ctx = new InputContext(asset);

            ctx.Switch(InputContextId.UI);
            Assert.IsTrue(asset.FindActionMap("UI").enabled);
            Assert.IsFalse(asset.FindActionMap("Player").enabled);

            ctx.Switch(InputContextId.Gameplay);
            Assert.IsFalse(asset.FindActionMap("UI").enabled);
            Assert.IsTrue(asset.FindActionMap("Player").enabled);
            Assert.AreEqual(InputContextId.Gameplay, ctx.Current);

            Object.DestroyImmediate(asset);
        }
    }
}
