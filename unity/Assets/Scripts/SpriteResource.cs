using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BakAgain {
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteResource : MonoBehaviour {
        [SerializeField]
        private string spriteAddress;

        // Retain handle to release asset and operation
        private AsyncOperationHandle<Sprite> _handle;

        // Start the load operation on start
        private void Start() {
            _handle = Addressables.LoadAssetAsync<Sprite>(spriteAddress);
            _handle.Completed += operationHandle => {
                GetComponent<SpriteRenderer>().sprite = operationHandle.Result;
            };
        }

        // Release asset when parent object is destroyed
        private void OnDestroy() {
            Addressables.Release(_handle);
        }
    }
}