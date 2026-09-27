namespace BakAgain.UI {
    using BakAgain.Core;
    using Unity.Properties;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    [UxmlObject]
    public partial class AddressableBinding : CustomBinding {
        private AsyncOperationHandle<Texture2D> _handle;
        private readonly ILogger _logger; 

        [UxmlAttribute]
        public string Address;

        public AddressableBinding() {
            _logger = LogManager.LoggerFactory.CreateLogger<AddressableBinding>(); 
            updateTrigger = BindingUpdateTrigger.WhenDirty;
        }

        protected override void OnActivated(in BindingActivationContext context) {
            _logger.LogInformation("{MethodName}: {TargetElementType} {BindingId} = {Address}", nameof(OnActivated), context.targetElement.GetType(), context.bindingId, Address);

            VisualElement element = context.targetElement;
            _handle = Addressables.LoadAssetAsync<Texture2D>(Address);
            Texture2D result = _handle.WaitForCompletion();
            if (!ConverterGroups.TrySetValueGlobal(ref element, context.bindingId, result, out VisitReturnCode errorCode)) {
                _logger.LogError("Failed to set value for binding {BindingId} on {TargetElementType} for address {Address}. ErrorCode: {ErrorCode}", context.bindingId, context.targetElement.GetType(), Address, errorCode);
            }
        }

        protected override void OnDeactivated(in BindingActivationContext context) {
            _logger.LogInformation("{MethodName}: {TargetElementType} {BindingId} = {Address}", nameof(OnDeactivated), context.targetElement.GetType(), context.bindingId, Address);
            Addressables.Release(_handle);
        }
    }
}