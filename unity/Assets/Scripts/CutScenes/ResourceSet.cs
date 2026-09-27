namespace BakAgain.CutScenes {
    using BakAgain.Core;
    using System.Collections.Generic;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class ResourceSet {
        private readonly Dictionary<string, object> _resources = new();
        private readonly ILogger _logger;

        public ResourceSet() {
            _logger = LogManager.LoggerFactory.CreateLogger<ResourceSet>();
        }

        public void Add<T>(string key, T resource) {
            _resources[key] = resource;
        }

        public T Get<T>(string key) where T : class {
            if (_resources.TryGetValue(key, out object resource) && resource is T typedResource) {
                return typedResource;
            }
            _logger.LogWarning("Failed to get resource with key: {Key}", key);
            return null;
        }

        public void Add(ResourceSet resourceSet) {
            foreach (KeyValuePair<string, object> resource in resourceSet._resources) {
                _resources[resource.Key] = resource.Value;
            }
        }

        public bool ContainsKey(string resourceKey) {
            return _resources.ContainsKey(resourceKey);
        }
    }
}