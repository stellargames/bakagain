namespace BakAgain.Tests.UI {
    using System.Reflection;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.Teleport;
    using GameData.Resources.Location;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The teleport map is a window onto C42.SCX over the intro's parchment, as the original draws it
    /// (TASK-1130, forum p71). Shown full-screen at 0,0 the picture sat 26 VGA px west of the pins,
    /// so every temple's marker read as east of its town.
    /// </summary>
    public class TeleportMapWindowTests {
        [Test]
        public void TheScreensBackdropIsTheParchmentNotTheMap() {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TeleportScreen.prefab");
            var loader = prefab.GetComponent<BackgroundImageLoader>();
            object address = typeof(BackgroundImageLoader)
                .GetField("imageAddress", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(loader);
            Assert.That(address, Is.EqualTo(TeleportMenu.Backdrop));
        }

        [Test]
        public void TheMapWindowShowsC42ShiftedOntoThePins() {
            var texture = new Texture2D(320, 200);
            Sprite map = Sprite.Create(texture, new Rect(0, 0, 320, 200), Vector2.zero);

            VisualElement window = TeleportScreen.MapWindow(map);

            // VGA (126,14) 172x160 is canonical (630,84) 860x960; the picture inside starts at VGA
            // (-100,-10), canonical (-500,-60), so map pixel x lands at screen x+26.
            Assert.That(window.style.left.value.value, Is.EqualTo(630));
            Assert.That(window.style.top.value.value, Is.EqualTo(84));
            Assert.That(window.style.width.value.value, Is.EqualTo(860));
            Assert.That(window.style.height.value.value, Is.EqualTo(960));
            Assert.That(window.style.overflow.value, Is.EqualTo(Overflow.Hidden));
            VisualElement picture = window[0];
            Assert.That(picture.style.left.value.value, Is.EqualTo(-500));
            Assert.That(picture.style.top.value.value, Is.EqualTo(-60));
            Assert.That(picture.style.width.value.value, Is.EqualTo(1600));
            Assert.That(picture.style.height.value.value, Is.EqualTo(1200));
        }

        [Test]
        public void TheFrameIsDrawnInTheOriginalsPensAroundTheWindow() {
            static Color Pen(int pen) => new Color32((byte)pen, 0, 0, 255);

            Texture2D frame = TeleportScreen.FrameTexture(Pen);

            // Texture rows run bottom-up; the frame's origin is VGA (124,12).
            Color32 At(int x, int y) => frame.GetPixel(x - 124, frame.height - 1 - (y - 12));
            Assert.That(At(124, 100).r, Is.EqualTo(0x49), "left edge, light");
            Assert.That(At(299, 100).r, Is.EqualTo(0x36), "right edge, dark");
            Assert.That(At(300, 176).r, Is.EqualTo(0x0f), "the line's last pixel");
            Assert.That(At(300, 100).a, Is.EqualTo(0), "nothing outside the rects");
            Assert.That(frame.filterMode, Is.EqualTo(FilterMode.Point));
            VisualElement element = TeleportScreen.FrameElement(frame);
            Assert.That(element.style.left.value.value, Is.EqualTo(620));
            Assert.That(element.style.top.value.value, Is.EqualTo(72));
            Assert.That(element.style.width.value.value, Is.EqualTo(177 * 5));
            Assert.That(element.style.height.value.value, Is.EqualTo(165 * 6));
        }
    }
}
