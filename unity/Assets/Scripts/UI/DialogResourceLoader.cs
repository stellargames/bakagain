namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using System;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using PaletteResource = GameData.Resources.Palette.PaletteResource;

    /// <summary>
    /// Loads and caches the resources the dialog renderer needs: the DDX
    /// <see cref="Dialog"/> for an id, the KEYWORD.DAT label table, and the default
    /// OPTIONS.PAL palette used when no active palette is set. Split out of
    /// <see cref="DialogManager"/> (its former resource-loading responsibility);
    /// the keyword/palette caches live for the loader's lifetime.
    /// </summary>
    /// <summary>
    /// The two loads <see cref="BakAgain.Core.Services.DialogExecutor"/> makes — the seam that lets
    /// a test drive a dialog without the addressables catalogue behind it.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately only the two the executor uses.</b> The renderer's loads (keywords, styles,
    /// sprites, palettes) stay off it: widening this to the whole loader would make every fake carry
    /// five members it does not care about, and the executor is the half that has branch logic worth
    /// testing.
    /// </remarks>
    public interface IDialogResourceLoader {
        UniTask<Dialog> LoadDialogAsync(int dialogId);

        UniTask<GameData.Resources.Location.TeleportDestinationSet> GetTeleportDestinationsAsync();
    }

    internal sealed class DialogResourceLoader : IDialogResourceLoader {
        private const string KeywordsKey = "KEYWORD.DAT";

        // Default palette for dialogs shown without a caller-supplied one (the
        // in-game / menu path). The original game renders standard dialogs
        // against OPTIONS.PAL (PaletteMapping: DIALOG → OPTIONS.PAL), whose
        // pen 0 is black, pen 0x0A the bright cream the name bubble uses, etc.
        // Loaded once and cached for the loader's lifetime.
        internal const string DefaultDialogPaletteKey = "OPTIONS.PAL";

        private readonly ILogger _logger;
        private KeywordList _keywords;
        private Color[] _defaultPalette;
        private DialogStyleTable _styleTable;

        public DialogResourceLoader(ILogger logger) {
            _logger = logger;
        }

        private const string TeleportKey = "TELEPORT.DAT";
        private GameData.Resources.Location.TeleportDestinationSet _teleportDestinations;

        public async UniTask<Dialog> LoadDialogAsync(int dialogId) {
            // Already a resolved global id — DdxKeyFor takes it as-is. Adding the 1600000 base
            // again here would land in DIAL_Z32 and find nothing.
            string key = GameData.Resources.Animation.CutsceneDialogCommand.DdxKeyFor(dialogId);

            AsyncOperationHandle<Dialog> handle = Addressables.LoadAssetAsync<Dialog>(key);
            Dialog dialog = await handle;

            if (handle.Status == AsyncOperationStatus.Succeeded) {
                // Already in run-time codes: the resource providers convert as the Dialog loads.
                return dialog;
            }
            _logger.LogError("Failed to load Dialog with filename {Filename}", key);
            return null;
        }

        // Keyword table (KEYWORD.DAT) used to label choice buttons from each
        // branch's keyword index. Cached for the loader's lifetime.
        /// <summary>
        /// The teleport destination table (TELEPORT.DAT) — the forty places the game can put the
        /// party, which a dialog <c>Teleport</c> action names by id. Cached like the keyword table.
        /// </summary>
        public async UniTask<GameData.Resources.Location.TeleportDestinationSet> GetTeleportDestinationsAsync() {
            if (_teleportDestinations != null) {
                return _teleportDestinations;
            }
            AsyncOperationHandle<GameData.Resources.Location.TeleportDestinationSet> handle =
                Addressables.LoadAssetAsync<GameData.Resources.Location.TeleportDestinationSet>(TeleportKey);
            GameData.Resources.Location.TeleportDestinationSet result = await handle;
            if (handle.Status == AsyncOperationStatus.Succeeded && result != null) {
                _teleportDestinations = result;
                _held.Add(handle);
            } else {
                _logger.LogError("Failed to load {Key}; dialog teleports cannot be applied", TeleportKey);
            }
            return _teleportDestinations;
        }

        public async UniTask<KeywordList> GetKeywordsAsync() {
            if (_keywords != null) {
                return _keywords;
            }
            AsyncOperationHandle<KeywordList> handle = Addressables.LoadAssetAsync<KeywordList>(KeywordsKey);
            KeywordList result = await handle;
            if (handle.Status == AsyncOperationStatus.Succeeded && result != null) {
                _keywords = result;
                _held.Add(handle);
            } else {
                _logger.LogError("Failed to load keyword table {Key}; confirmation buttons will show numeric keys", KeywordsKey);
            }
            return _keywords;
        }

        /// <summary>
        /// The dialog style table (DIALSTYL.DAT) — the seven <c>dialogTypeData</c> rows that say
        /// where each kind of dialog box sits and what chrome it wears. Loaded through the
        /// resource system rather than read off a static array, which is what lets a mod author
        /// move or resize a dialog box by dropping a <c>DAT/DIALSTYL.json</c> into their override
        /// directory. Cached for the loader's lifetime, like the keyword table and palette.
        ///
        /// <para>The table has no archive member — it is synthesized from
        /// <see cref="DialogStyleTable"/>'s own rows by <c>BakResourceProvider</c> (and resolved
        /// by <c>BakResourceLocator</c>'s DIALSTYL special case), exactly as CHAPTERS.DAT is. If
        /// even that fails, fall back to a locally constructed shipped table: a dialog rendering
        /// at the faithful position beats no dialog at all.</para>
        /// </summary>
        public async UniTask<DialogStyleTable> GetStyleTableAsync() {
            if (_styleTable != null) {
                return _styleTable;
            }
            AsyncOperationHandle<DialogStyleTable> handle =
                Addressables.LoadAssetAsync<DialogStyleTable>(DialogStyleTable.ResourceId);
            DialogStyleTable table = await handle;
            if (handle.Status == AsyncOperationStatus.Succeeded && table != null) {
                _styleTable = table;
                _held.Add(handle);
            } else {
                _logger.LogError(
                    "Failed to load dialog style table {Key}; falling back to the shipped rows compiled into GameData",
                    DialogStyleTable.ResourceId);
                // Through the resource layer's own factory, not CreateShipped: Frame is not part
                // of CreateShipped (GameData has no canonical dimensions of its own — see the
                // property's remarks) and a fallback table whose frame was 0x0 would take the
                // dialog stage down the warn-and-collapse path on top of whatever already failed.
                // ShippedDialogStyleTable is the one place that knows this, and it lives behind
                // the ResourceManagement boundary so no UI class has to reach into
                // ResourceExtraction for it.
                _styleTable = OverrideResourceProvider.ShippedDialogStyleTable();
            }
            return _styleTable;
        }

        /// <summary>
        /// A sprite from an image set, for the dialog surface's own decoration (the vine corners).
        /// </summary>
        /// <remarks>
        /// Cached per address: the vines are rebuilt on every full-screen dialog, and the sprite is
        /// the same one every time. Returns null and says so if the address does not resolve, so
        /// the caller degrades to an undecorated panel rather than throwing on a decoration.
        /// </remarks>
        public async UniTask<UnityEngine.Sprite> GetSpriteAsync(string address) {
            if (_sprites.TryGetValue(address, out UnityEngine.Sprite cached)) {
                return cached;
            }

            AsyncOperationHandle<UnityEngine.Sprite> handle =
                Addressables.LoadAssetAsync<UnityEngine.Sprite>(address);
            UnityEngine.Sprite sprite = await handle;
            if (handle.Status != AsyncOperationStatus.Succeeded || sprite == null) {
                _logger.LogWarning("Dialog decoration sprite {Key} did not load.", address);
                return null;
            }

            _sprites[address] = sprite;
            _held.Add(handle);
            return sprite;
        }

        private readonly System.Collections.Generic.Dictionary<string, UnityEngine.Sprite> _sprites
            = new System.Collections.Generic.Dictionary<string, UnityEngine.Sprite>();

        // The handles behind the lifetime caches above. Held, they pin each asset in Addressables'
        // operation cache for the whole process, so a later load of the same key gets this
        // instance back, already converted — under whatever language pack was current then.
        private readonly System.Collections.Generic.List<AsyncOperationHandle> _held
            = new System.Collections.Generic.List<AsyncOperationHandle>();

        /// <summary>Ends the loader's lifetime: drops its caches and releases what it loaded.</summary>
        public void Release() {
            foreach (AsyncOperationHandle handle in _held) {
                Addressables.Release(handle);
            }
            _held.Clear();
            _keywords = null;
            _teleportDestinations = null;
            _styleTable = null;
            _defaultPalette = null;
            _sprites.Clear();
        }

        public async UniTask<Color[]> GetDefaultPaletteAsync() {
            if (_defaultPalette != null) {
                return _defaultPalette;
            }
            AsyncOperationHandle<PaletteResource> handle =
                Addressables.LoadAssetAsync<PaletteResource>(DefaultDialogPaletteKey);
            PaletteResource palette = await handle;
            if (handle.Status == AsyncOperationStatus.Succeeded && palette != null) {
                _defaultPalette = palette.Colors.ToUnity();
                _held.Add(handle);
            } else {
                _logger.LogError("Failed to load default dialog palette {Key}", DefaultDialogPaletteKey);
                _defaultPalette = Array.Empty<Color>();
            }
            return _defaultPalette;
        }
    }
}
