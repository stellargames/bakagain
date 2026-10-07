namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.ResourceManagement.Models;
    using GameData.Resources;
    using GameData.Resources.Image;
    using ResourceExtraction;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceProviders;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using IResourceProvider = ResourceExtraction.IResourceProvider; 

    public class BakResourceProvider : ResourceProviderBase {
        private static readonly Dictionary<Type, Type> UnityConverterMap = new() {
            {typeof(IndexedTexture), typeof(IndexedTextureConverter)},
            {typeof(Sprite), typeof(SpriteUnityConverter)}
        };

        private static readonly Dictionary<string, Type> ResourceTypeMap = new() {
            {".SCX", typeof(BackgroundImage)},
            {".BMX", typeof(ImageSet)}
        };

        private readonly IResourceProvider _resourceProvider;
        private readonly Dictionary<(string path, Type type), object> _resourceCache = new();
        private readonly Dictionary<string, IResource> _extractedResourceCache = new();
        // The pack the two caches were filled under: they hold resources as that pack translated them.
        private GameData.Resources.Text.LanguagePack _cachedUnder;
        private readonly ILogger _logger; 

        public BakResourceProvider() {
            m_ProviderId = nameof(BakResourceProvider);
            _logger = LogManager.LoggerFactory.CreateLogger<BakResourceProvider>(); 
            _resourceProvider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
        }

        /// <inheritdoc />
        public override void Provide(ProvideHandle provideHandle) {
            try {
                string path = provideHandle.Location.InternalId;
                Type resourceType = provideHandle.Location.ResourceType;

                var result = LoadResource(path, resourceType);
                if (result == null) {
                    provideHandle.Complete<object>(null, false, new Exception($"Resource {path} of type {resourceType} not found."));

                    return;
                }

                provideHandle.Complete(result, true, null);
            } catch (Exception e) {
                _logger.LogError(e, "Error in Provide method"); 
                provideHandle.Complete<object>(null, false, e);
            }
        }

        private object LoadResource(string path, Type resourceType) {
            _logger.LogDebug("{ClassName}: Requested {ResourceType} '{Path}'", nameof(BakResourceProvider), resourceType, path);

            // A pack switched since (LanguagePacks.Reload) must not be handed the old pack's text.
            if (!ReferenceEquals(_cachedUnder, LanguagePacks.Current)) {
                _resourceCache.Clear();
                _extractedResourceCache.Clear();
                _cachedUnder = LanguagePacks.Current;
            }

            // Check final resource cache first
            (string path, Type resourceType) cacheKey = (path, resourceType);
            if (_resourceCache.TryGetValue(cacheKey, out object cachedResource) && cachedResource != null) {
                _logger.LogDebug("{ClassName}: Getting {ResourceType} '{Path}' from cache", nameof(BakResourceProvider), resourceType, path);

                return cachedResource;
            }

            // Parse resource path and get required type
            var (baseId, subIndex) = ParseResourcePath(path);
            Type iResourceType = GetRequiredResourceType(resourceType, baseId);

            // Get or extract the base resource
            IResource extractedResource = GetOrExtractResource(baseId, iResourceType);

            if (extractedResource == null) {
                return null;
            }

            // Process and convert the resource
            object resource = ProcessResource(extractedResource, resourceType, subIndex, path);
            if (resource != null) {
                _resourceCache[cacheKey] = resource;
            }

            return resource;
        }

        private IResource GetOrExtractResource(string resourceId, Type resourceType) {
            // Check extracted resource cache
            if (_extractedResourceCache.TryGetValue(resourceId, out IResource cachedResource)) {
                _logger.LogDebug("{ClassName}: Getting {ResourceType} '{ResourceId}' from extracted cache", nameof(BakResourceProvider), resourceType, resourceId);

                return cachedResource;
            }

            // Extract new resource
            try {
                _logger.LogDebug("{ClassName}: Extracting {ResourceType} '{ResourceId}' from archive", nameof(BakResourceProvider), resourceType, resourceId);

                MethodInfo method = _resourceProvider.GetType().GetMethod(nameof(IResourceProvider.GetResource))?.MakeGenericMethod(resourceType);
                var resource = method?.Invoke(_resourceProvider, new object[] {resourceId}) as IResource;

                if (resource != null) {
                    if (resource is GameData.Resources.World.ZoneTable zoneTable) {
                        // Runtime re-extracts ZoneTable from the binary, so stamp texture keys here using the
                        // BASE archive's slot counts (_resourceProvider is the un-overridden KRONDOR.001), keeping
                        // keys frozen against the original bitmaps. See docs/superpowers/specs/2026-07-15-world-model-atlas-texturing-design.md.
                        ResourceExtraction.Extractors.ZoneTableExtractor.StampTextureKeys(
                            zoneTable, resourceId, _resourceProvider);
                    }

                    // The active language pack's translations go in as the resource loads, so every
                    // reader of it sees them (TASK-773). English is a no-op.
                    LanguagePacks.Current.Apply(resource, resourceId);
                    // The format's formatting tags become the renderer's codes here, for every
                    // reader of the Dialog (TASK-774) — the cutscene engine loads it through the
                    // cache and never passes the dialog loader.
                    if (resource is GameData.Resources.Dialog.Dialog dialogResource) {
                        GameData.Resources.Dialog.DialogTextRuns.ToRuntime(dialogResource);
                    }
                    if (resource is GameData.Resources.Font.FontResource font) {
                        LanguagePacks.MergeFont(font);
                    }

                    _extractedResourceCache[resourceId] = resource;
                }

                return resource;
            } catch (Exception e) {
                _logger.LogError(e, "Failed to extract resource '{ResourceId}'", resourceId);

                return null;
            }
        }

        private object ProcessResource(IResource resource, Type targetType, int? subIndex, string resourceId) { 
            // Get sub-resource if requested
            if (subIndex.HasValue) {
                if (resource is not IHaveSubResource<IResource> container) {
                    _logger.LogError("Resource '{ResourceId}' does not support sub-resources", resourceId);

                    return null;
                }

                // GetSubResource's contract is not agreed: IHaveSubResource does not say, and
                // ImageSet THROWS for an out-of-range index while the null check below expects it to
                // return null. Treat both as "not found" here, at Debug, because this layer cannot
                // tell a caller's bug from a caller legitimately probing: the cutscene preload walk
                // pairs each DrawImage index with whatever file the slot holds when the LINEAR walk
                // reaches it, which is not always the slot playback will really have, so an index
                // the paired file lacks is an artefact of walking (CutsceneFrameProcessor says the
                // same, one layer up). Logging that at Error made C21 look broken when it plays
                // correctly, and cost a filed bug (TASK-294) to rediscover.
                try {
                    resource = container.GetSubResource(subIndex.Value);
                } catch (IndexOutOfRangeException) {
                    _logger.LogDebug("Sub-resource #{SubIndex} is out of range for '{ResourceId}'", subIndex, resourceId);

                    return null;
                }
                if (resource == null) {
                    _logger.LogDebug("Sub-resource #{SubIndex} not found in '{ResourceId}'", subIndex, resourceId);

                    return null;
                }
            }

            // Return as-is if types match
            if (targetType == resource.GetType())
                return resource;

            // Convert to target type
            try {
                if (!UnityConverterMap.TryGetValue(targetType, out Type converterType)) {
                    throw new ArgumentException($"No converter found for target type {targetType}");
                }

                _logger.LogDebug("{ClassName}: Converting {ResourceTypeName} to {TargetType}", nameof(BakResourceProvider), resource.GetType().Name, targetType);
                var converter = Activator.CreateInstance(converterType);
                MethodInfo convertMethod = converterType.GetMethod(nameof(UnityConverterBase<object>.Convert));

                return convertMethod?.Invoke(converter, new object[] {resource, resourceId});
            } catch (Exception e) {
                _logger.LogError(e, "Failed to convert resource '{ResourceId}' to {TargetType}", resourceId, targetType);

                return null;
            }
        }

        private static Type GetRequiredResourceType(Type targetType, string resourceId) {
            // Return target type if it's already an IResource
            if (typeof(IResource).IsAssignableFrom(targetType))
                return targetType;

            // Otherwise determine type from file extension
            string extension = Path.GetExtension(resourceId).ToUpperInvariant();
            if (!ResourceTypeMap.TryGetValue(extension, out Type resourceType)) {
                throw new ArgumentException($"Unsupported file extension: {extension}");
            }

            return resourceType;
        }

        private static (string baseId, int? subIndex) ParseResourcePath(string path) {
            // A '@host.PAL' suffix names the palette the sprite is composited against and is not part
            // of what the archive is asked for — see PaletteMapping.HostPaletteSeparator. It stays in
            // the cache key (the caller's full path), so one portrait caches once per host screen.
            string[] parts = GameData.PaletteMapping.StripHostPalette(path).Split('#');

            if (parts.Length == 1)
                return (parts[0], null);

            if (int.TryParse(parts[1], out int index))
                return (parts[0], index);

            throw new ArgumentException($"Invalid sub-resource index in path: {path}");
        }

        public void ClearCaches() {
            // Converted results own GPU textures (IndexedTexture and IndexedTexture[]),
            // which are not garbage-collected — dispose them before dropping the entries.
            foreach (object cached in _resourceCache.Values) {
                DisposeCached(cached);
            }
            _resourceCache.Clear();
            _extractedResourceCache.Clear();
        }

        private static void DisposeCached(object cached) {
            switch (cached) {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IEnumerable<IDisposable> disposables:
                    foreach (IDisposable item in disposables) {
                        item?.Dispose();
                    }
                    break;
            }
        }
    }
}