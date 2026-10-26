namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A mirrored encounter sprite's pick box (TASK-1004): no negative-scale BoxCollider, and the box
    /// still on the drawn quad whichever way the sprite faces.
    /// </summary>
    public class PickColliderTests {
        private GameObject _sprite;

        [TearDown]
        public void TearDown() {
            if (_sprite != null) { Object.DestroyImmediate(_sprite); }
        }

        [UnityTest]
        public IEnumerator AMirroredSpritesBoxIsPositiveAndOnTheQuad() {
            int negativeScaleWarnings = 0;
            void Count(string message, string _, LogType __) {
                if (message.Contains("BoxCollider does not support negative scale")) { negativeScaleWarnings++; }
            }
            Application.logMessageReceived += Count;
            _sprite = new GameObject("Encounter mordel #0");
            _sprite.transform.localScale = new Vector3(-2f, 3f, 1f);
            var center = new Vector3(0.25f, 0.5f, 0f);

            PickCollider.Add(_sprite, center, new Vector3(1f, 1f, 0.1f));
            yield return null;

            var box = _sprite.GetComponentInChildren<BoxCollider>();
            Assert.IsNull(_sprite.GetComponent<BoxCollider>(), "nothing on the mirrored object itself");
            Assert.Greater(box.transform.lossyScale.x, 0f, "the box's own scale is positive");
            Assert.That(Vector3.Distance(_sprite.transform.TransformPoint(center),
                box.transform.TransformPoint(box.center)), Is.LessThan(1e-4f), "and it sits on the quad");

            _sprite.transform.localScale = new Vector3(2f, 3f, 1f);   // the sprite turns to face right
            yield return null;
            Assert.Greater(box.transform.lossyScale.x, 0f);
            Assert.That(Vector3.Distance(_sprite.transform.TransformPoint(center),
                box.transform.TransformPoint(box.center)), Is.LessThan(1e-4f), "the box follows the turn");

            _sprite.transform.localScale = new Vector3(-2f, 3f, 1f);   // and back to the left
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.Greater(box.transform.lossyScale.x, 0f);
            Application.logMessageReceived -= Count;
            Assert.AreEqual(0, negativeScaleWarnings, "PhysX never saw a negative box");
        }
    }
}
