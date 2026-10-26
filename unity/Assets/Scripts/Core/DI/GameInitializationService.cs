namespace BakAgain.Core.DI {
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using System.IO;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using VContainer.Unity;

    public class GameInitializationService : IAsyncStartable {
        private const string MetaMenuPrefabAddress = "Assets/Prefabs/MetaMenu.prefab";
        private const string LoadingPrefabAddress = "Assets/Prefabs/BackgroundLoading.prefab";
        private readonly ILogger<GameInitializationService> _logger;
        private readonly GameManager _gameManager;
        private readonly UnityEngine.UIElements.PanelSettings _gamePanelSettings;
        private readonly BakAgain.UI.Cursor.ICursorManager _cursor;

        public GameInitializationService(ILogger<GameInitializationService> logger, GameManager gameManager,
            UnityEngine.UIElements.PanelSettings gamePanelSettings, BakAgain.UI.Cursor.ICursorManager cursor) {
            _logger = logger;
            _gameManager = gameManager;
            _gamePanelSettings = gamePanelSettings;
            _cursor = cursor;
        }

        /// <summary>
        /// The pillarbox must be black, not the scene's sky.
        /// </summary>
        /// <remarks>
        /// <b>The bars are simply wherever the UI does not draw</b>, so they show whatever the camera
        /// last cleared to. A canonical 4:3 stage pillarboxes at 1920x1080 by design
        /// (<c>Canonical.cs</c>) — but <c>Main.unity</c>'s camera clears to <c>Skybox</c>, which put a
        /// blue gradient and a visible horizon line down both sides of the main menu. The original
        /// game fills the display edge to edge. Found by an original-vs-port screenshot comparison;
        /// both captures are in <c>docs/re-notes/2026-09-04-*-mainmenu.png</c>.
        ///
        /// <para><b>This governs UI screens only.</b> The world never reaches it — <c>ZoneEnvironment</c>
        /// gives the world camera its own <c>SolidColor</c> sky colour, and the debug states build
        /// cameras of their own. There is deliberately no global camera owner
        /// (<c>unity-systems-map.md</c> says so), so this sets the default the UI inherits rather
        /// than claiming ownership of anything.</para>
        ///
        /// <para>Set in code rather than on the scene camera so it is greppable and survives a scene
        /// edit.</para>
        ///
        /// <para><b>Every camera drawing to the screen, not <c>Camera.main</c>.</b> Main.unity's camera is
        /// untagged, so <c>Camera.main</c> was null, this returned with a warning on every boot and the
        /// bars showed the scene's brown. A camera with a target texture is the world's and keeps its
        /// sky. With no screen camera at all (the PlayMode test scene) nothing cleared the screen, and
        /// the bars kept whatever was drawn there last: the combat pack's fade-out stayed in them after
        /// it closed (TASK-952). Then one is made that only clears. Every screen camera draws nothing
        /// (culling mask 0): only the clear is wanted (TASK-956).</para>
        /// </remarks>
        internal static void ClearBehindUiToBlack() {
            var screens = new System.Collections.Generic.List<Camera>();
            foreach (Camera cam in Camera.allCameras) {
                if (cam.targetTexture == null) {
                    screens.Add(cam);
                }
            }
            if (screens.Count == 0) {
                var go = new GameObject("ScreenClearCamera");
                Object.DontDestroyOnLoad(go);
                Camera clear = go.AddComponent<Camera>();
                clear.depth = -100;      // under any screen camera a scene adds later
                screens.Add(clear);
            }
            foreach (Camera cam in screens) {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                // Draws nothing: the world has its own camera and the UI is UI Toolkit. Main.unity's
                // camera was Everything and culled the whole scene from the origin every frame (TASK-956).
                cam.cullingMask = 0;
            }
        }

        /// <summary>
        /// Ends a boot that Play has stopped under: called after every await, so nothing past it runs
        /// in a torn-down session.
        /// </summary>
        /// <remarks>
        /// <b>Stopping Play resumes the boot instead of ending it.</b> On exiting Play the Editor
        /// destroys Addressables' <c>DelayedActionManager</c> first, and its <c>OnDestroy</c> completes
        /// every load still in flight so that nothing waits forever. That runs this method's pending
        /// continuation synchronously inside the teardown, with the catalog already gone: GAME.FNT
        /// and BOOK.FNT had "No Location", <c>GameFonts</c> got null and <c>FntTrueType.Build</c>
        /// threw, logged as "Error during initialization" (TASK-932). Any PlayMode test run shorter
        /// than the boot ended that way, since the root scope boots the game in the test scene too.
        ///
        /// <para>Measured at that moment: the VContainer token and
        /// <c>Application.exitCancellationToken</c> are not cancelled yet and
        /// <c>Application.isPlaying</c> is still true; only
        /// <c>EditorApplication.isPlayingOrWillChangePlaymode</c> has turned false. The token still
        /// covers the scope going away in a player.</para>
        /// </remarks>
        private static void StopIfPlayEnded(CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
                throw new System.OperationCanceledException("Play stopped during the boot.");
            }
#endif
        }

        public async UniTask StartAsync(CancellationToken cancellation = default) {
            _logger.LogInformation("StartAsync() initiated.");
            BakResourceSettings.ApplyPending(); // folders the Enhanced settings saved for this start
            // Before the first boot text: a language installed as a mod is found through the mods.
            ModLayers.Initialize();
            LanguagePacks.Reload();
            ClearBehindUiToBlack();
            AsyncOperationHandle<GameObject> loadingScreenHandle = Addressables.InstantiateAsync(LoadingPrefabAddress);

            try {
                // Optionally wait for the loading screen to be fully loaded before proceeding
                await loadingScreenHandle.Task.AsUniTask();
                StopIfPlayEnded(cancellation);
                UnityEngine.UIElements.VisualElement loadingRoot = loadingScreenHandle.Result?
                    .GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement;
                UnityEngine.UIElements.Label loadingLabel = loadingRoot == null ? null
                    : UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(loadingRoot, "Loading");
                if (loadingLabel != null) {
                    loadingLabel.text = "\n" + BakAgain.ResourceManagement.LanguagePacks.BootText(
                        GameData.Resources.Text.UiTemplates.BootLoading);
                }

                // Check and prompt for game path if necessary
                while (string.IsNullOrEmpty(BakResourceSettings.GamePath) || !Directory.Exists(BakResourceSettings.GamePath)) {
                    _logger.LogInformation("Game path not set or invalid. Loading MetaMenu to prompt user.");

                    AsyncOperationHandle<GameObject> metaMenuHandle = default;
                    try {
                        metaMenuHandle = Addressables.InstantiateAsync(MetaMenuPrefabAddress);
                        GameObject metaMenuGo = await metaMenuHandle.Task.AsUniTask();
                        MetaMenu metaMenu = metaMenuGo.GetComponent<MetaMenu>();

                        if (metaMenu == null) {
                            _logger.LogError("Failed to get MetaMenu component from instantiated prefab. Halting initialization.");

                            throw new System.InvalidOperationException("MetaMenu component not found on prefab.");
                        }

                        await metaMenu.WaitForDialogAsync();
                        StopIfPlayEnded(cancellation);
                    } finally {
                        if (metaMenuHandle.IsValid()) {
                            Addressables.ReleaseInstance(metaMenuHandle);
                        }
                    }
                    _logger.LogInformation("MetaMenu dialog completed. GamePath attempt: '{GamePath}'", BakResourceSettings.GamePath);
                }

                _logger.LogInformation("Using GamePath: {GamePath}", BakResourceSettings.GamePath);

                _logger.LogInformation("Initializing Addressables.");
                await Addressables.InitializeAsync().ToUniTask();
                StopIfPlayEnded(cancellation);

                _logger.LogInformation("Initializing ResourceManagement.");
                ResourceManagementInitializer.InitializeResourceManagement();
                await MidiSynthPlayer.PreloadSoundFontAsync();
                StopIfPlayEnded(cancellation);
                await BookSpreadBackdrop.PreloadAsync();
                StopIfPlayEnded(cancellation);
                // Always: Enhanced can be switched on in this session, and its screens then lay out
                // inside load callbacks, where loading the art synchronously throws.
                await EnhancedArt.PreloadAsync();
                StopIfPlayEnded(cancellation);

                GameFonts.Install(_gamePanelSettings);
                // The arrow comes from the player's POINTER.BMX, which only loads from here on.
                _cursor.SetByIndex(0);
                // The menus before any game is loaded read the catalog too (the V banner, the
                // language button, the restore screen's lines), and the installer otherwise runs
                // only with a loaded game. A load re-installs it with any mod override on top.
                GameData.Resources.Text.UiStrings.Catalog =
                    GameData.Resources.Text.UiStringCatalog.Embedded.TranslatedBy(LanguagePacks.Current);
            } catch (System.OperationCanceledException) {
                _logger.LogInformation("Play stopped before initialization finished; the boot goes no further.");
                return;
            } catch (System.Exception e) {
                _logger.LogError(e, "Error during initialization");

                // In case of an error, the finally block will still clean up the loading screen.
                return; // Stop execution if initialization fails.
            } finally {
                // Correctly release the Addressable instance, which also destroys the GameObject.
                if (loadingScreenHandle.IsValid()) {
                    Addressables.ReleaseInstance(loadingScreenHandle);
                }
            }

            // This code is only reached if initialization was successful.
            _logger.LogInformation("Initialization complete. Starting game.");
            await _gameManager.StartGame();
        }
    }
}