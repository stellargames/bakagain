namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Dialog;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using UnityEngine.ResourceManagement.ResourceProviders;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class OverrideResourceProvider : ResourceProviderBase {
        private static readonly JsonSerializerSettings JsonSettings = new() {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new FrameCommandSerializationBinder()
        };

        /// <summary>
        /// Resource types whose shipped content is synthesized in code rather than read from the
        /// archive, AND whose type defaults are not the shipped values. An override document for
        /// one of these is merged onto a fresh shipped baseline instead of being deserialized
        /// standalone — see <see cref="OverrideJsonMerge"/> for why that is not optional.
        ///
        /// <para>Most resources need no entry here. A resource read from the archive has no
        /// in-code baseline to merge onto in the first place, and a resource whose type defaults
        /// ARE its shipped values (CreditsLayout, whose geometry is baked in as property
        /// initialisers) already survives a partial document unaided.</para>
        /// </summary>
        private static readonly Dictionary<Type, Func<string, object>> ShippedBaselines =
            new Dictionary<Type, Func<string, object>> {
                {typeof(DialogStyleTable), id => ShippedDialogStyleTable(id)}
            };

        /// <summary>
        /// The resource types the merge-onto-baseline path (<see cref="OverrideJsonMerge"/>) can
        /// reach — i.e. the keys of <see cref="ShippedBaselines"/>, exposed read-only for tests that
        /// police the whole family rather than one type by name (see
        /// <c>DialogStyleTableOverrideTests.EveryMergeBaselineResource_HasNoUnfencedNoSetterProperties</c>).
        /// A type added to <see cref="ShippedBaselines"/> is automatically in scope for that check —
        /// nothing here needs updating when a new resource joins the merge path.
        /// </summary>
        internal static IReadOnlyCollection<Type> ShippedBaselineTypes => ShippedBaselines.Keys;

        /// <summary>
        /// The shipped dialog style table, identical to the one <c>BakResourceProvider</c> hands
        /// out when nothing is overridden — <b>design frame included</b>. <b>The single Unity-side
        /// factory for a shipped table</b>: the merge baseline above and
        /// <c>DialogResourceLoader</c>'s load-failure fallback both come through here, so there is
        /// one place that knows a shipped table has to be stamped and one place a fourth caller
        /// has to find.
        ///
        /// <para><c>DialogStyleTable.CreateShipped</c> deliberately does not stamp
        /// <c>Frame</c>: the canonical dimensions are derived from <c>AspectCorrection</c>, which
        /// lives in ResourceExtraction, so GameData cannot state them without becoming a second
        /// source of truth for the same numbers. Every path that produces a shipped table
        /// therefore runs it through <c>CanonicalSpace.Apply</c>, and the merge baseline is one of
        /// those paths: without this, an override document that never mentions <c>Frame</c> would
        /// merge onto a 0x0 frame and the dialog stage would silently collapse to
        /// <c>CanonicalStage</c>'s warn-and-fall-back path.</para>
        ///
        /// <para>It also keeps the <c>ResourceExtraction</c> reference on this side of the
        /// ResourceManagement boundary. <c>DialogResourceLoader</c> is a UI class, and no other
        /// file under <c>Assets/Scripts/UI/</c> reaches into the extraction layer; calling this
        /// instead of stamping for itself is what keeps that true.</para>
        /// </summary>
        internal static DialogStyleTable ShippedDialogStyleTable(string id = DialogStyleTable.ResourceId) {
            DialogStyleTable table = DialogStyleTable.CreateShipped(id);
            ResourceExtraction.Imaging.CanonicalSpace.Apply(table);
            return table;
        }

        /// <summary>
        /// Read an override document from disk and turn it into the requested resource — the
        /// exact step <see cref="Provide"/> performs for every non-image override, factored out
        /// so a test can drive it with a real path from
        /// <see cref="OverrideResourceLocator"/> instead of having to fabricate a
        /// <c>ProvideHandle</c>.
        /// </summary>
        internal static object LoadOverrideObject(string path, Type resourceType, string resourceId) {
            string json = File.ReadAllText(path);

            if (resourceType == typeof(TextAsset)) {
                // Served as the file's raw text, not run through JsonConvert like every other
                // branch here: the consumer (UiStringLoader, for uistrings.json) parses the
                // document itself. That's not a style choice — nothing on this path COULD
                // populate a TextAsset by deserializing into it (TextAsset.text has no public
                // setter, only the string constructor used below), and the document a mod author
                // writes is a flat key/value string map, not a serialized TextAsset to begin
                // with. Registering the catalog type in ShippedBaselines instead is also not an
                // option: UiStringCatalog is deliberately immutable (private constructor,
                // read-only Entries), so it can't be the target of a property-by-property merge
                // either — see EveryMergeBaselineResource_HasNoUnfencedNoSetterProperties, which
                // polices exactly that family for get-only properties a merge can't reach.
                return new TextAsset(json) { name = resourceId };
            }

            if (ShippedBaselines.TryGetValue(resourceType, out Func<string, object> createBaseline)) {
                return OverrideJsonMerge.OntoBaseline(
                    json, createBaseline(resourceId), resourceType, JsonSettings);
            }

            object loaded = JsonConvert.DeserializeObject(json, resourceType, JsonSettings);
            // A mod's DDX JSON carries the same tags as the extracted one (TASK-774).
            if (loaded is GameData.Resources.Dialog.Dialog modDialog) {
                GameData.Resources.Dialog.DialogTextRuns.ToRuntime(modDialog);
            }
            return loaded;
        }

        public OverrideResourceProvider() {
            m_ProviderId = nameof(OverrideResourceProvider);
        }

        /// <inheritdoc />
        public override void Provide(ProvideHandle provideHandle) {
            InternalOp.Start(provideHandle);
        }

        /// <summary>
        /// Destroys the textures this provider created when Addressables releases the
        /// asset. Every <see cref="Provide"/> allocates a fresh Texture2D (no caching or
        /// aliasing), so destroying on release is safe and stops override textures from
        /// accumulating in native memory.
        /// </summary>
        public override void Release(IResourceLocation location, object asset) {
            switch (asset) {
                case IndexedTexture indexedTexture:
                    UnityObjectUtil.Destroy(indexedTexture.DirectTexture);
                    indexedTexture.Dispose();
                    break;
                case Sprite sprite:
                    Texture2D spriteTexture = sprite.texture;
                    UnityObjectUtil.Destroy(sprite);
                    UnityObjectUtil.Destroy(spriteTexture);
                    break;
                case Texture2D texture:
                    UnityObjectUtil.Destroy(texture);
                    break;
            }
        }

        private static void LoadTexture(ProvideHandle provideHandle) {
            Texture2D texture = LoadTextureInternal(provideHandle);

            provideHandle.Complete(texture, texture != null, texture != null ? null : new Exception($"Texture failed to load for location {provideHandle.Location.PrimaryKey}."));
        }

        private static void LoadIndexedTexture(ProvideHandle provideHandle) {
            Texture2D texture = LoadTextureInternal(provideHandle);
            Vector2 scale = LoadScaleInternal(provideHandle);
            var image = texture ? new IndexedTexture(texture, scale) : null;

            provideHandle.Complete(image, image != null, image != null ? null : new Exception($"Texture failed to load for location {provideHandle.Location.PrimaryKey}."));
        }

        private static Vector2 LoadScaleInternal(ProvideHandle provideHandle) {
            string path = provideHandle.ResourceManager.TransformInternalId(provideHandle.Location);

            string jsonPath = path.Replace(".png", ".json");
            if (!File.Exists(jsonPath)) {
                return Vector2.one;
            }
            string json = File.ReadAllText(jsonPath);
            var metaData = JsonConvert.DeserializeObject<MetaData>(json);

            return new Vector2((float)metaData.ScaleX, (float)metaData.ScaleY);
        }

        private struct MetaData {
            public double ScaleX { get; set; }
            public double ScaleY { get; set; }
        }

        private static Texture2D LoadTextureInternal(ProvideHandle provideHandle) {
            string path = provideHandle.ResourceManager.TransformInternalId(provideHandle.Location);
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(0, 0) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = provideHandle.Location.PrimaryKey
            };
            if (!texture.LoadImage(bytes)) {
                UnityObjectUtil.Destroy(texture);
                throw new InvalidDataException($"Override image '{path}' is not a decodable PNG/JPG (LoadImage failed).");
            }

            return texture;
        }

        private static void LoadSprite(ProvideHandle provideHandle) {
            Texture2D texture = LoadTextureInternal(provideHandle);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            provideHandle.Complete(sprite, sprite != null, sprite != null ? null : new Exception($"Sprite failed to load for location {provideHandle.Location.PrimaryKey}."));
        }

        private abstract class InternalOp {
            private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<OverrideResourceProvider>();

            public static void Start(ProvideHandle provideHandle) {
                if (provideHandle.Location == null) {
                    return;
                }
                string path = provideHandle.ResourceManager.TransformInternalId(provideHandle.Location);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                    Logger.LogWarning("Override image '{Path}' not found.", path);
                    return;
                }

                if (provideHandle.Location != null && provideHandle.Location.ResourceType !=
                    typeof(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle)) {
                    // Log only for actual resources, not for internal Addressables operations
                    // Check if the path is not null or empty before logging
                    if (!string.IsNullOrEmpty(path)) {
                        Logger.LogInformation("{ProviderName}: Loading override {ResourceType} '{Path}'",
                            nameof(OverrideResourceProvider), provideHandle.Location.ResourceType, path);
                    }
                }

                if (provideHandle.Location.ResourceType == typeof(Sprite)) {
                    LoadSprite(provideHandle);

                    return;
                }

                if (provideHandle.Location.ResourceType == typeof(IndexedTexture)) {
                    LoadIndexedTexture(provideHandle);

                    return;
                }

                if (provideHandle.Location.ResourceType == typeof(Texture2D)) {
                    LoadTexture(provideHandle);

                    return;
                }

                object result = LoadOverrideObject(
                    path, provideHandle.Location.ResourceType, provideHandle.Location.PrimaryKey);
                provideHandle.Complete(result, result != null,
                    result != null
                        ? null
                        : new Exception(
                            $"Object failed to load for location {provideHandle.Location.PrimaryKey}."));
            }
        }
    }

    internal class FrameCommandSerializationBinder : ISerializationBinder {
        public Type BindToType(string assemblyName, string typeName) {
            var type = Type.GetType($"GameData.Resources.Animation.FrameCommands.{typeName}, {typeof(FrameCommand).Assembly.FullName}");
            if (type != null && typeof(FrameCommand).IsAssignableFrom(type)) {
                return type;
            }

            throw new NotImplementedException($"Type binding not implemented for {typeName}");
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName) {
            if (typeof(FrameCommand).IsAssignableFrom(serializedType)) {
                assemblyName = serializedType.Assembly.FullName;
                typeName = serializedType.FullName;
            } else {
                throw new NotImplementedException($"Type binding not implemented for {serializedType}");
            }
        }
    }
}