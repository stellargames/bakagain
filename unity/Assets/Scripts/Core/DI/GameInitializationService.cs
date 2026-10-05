namespace BakAgain.Core.DI {
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

        public GameInitializationService(ILogger<GameInitializationService> logger, GameManager gameManager,
            UnityEngine.UIElements.PanelSettings gamePanelSettings) {
            _logger = logger;
            _gameManager = gameManager;
            _gamePanelSettings = gamePanelSettings;
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
        /// </remarks>
        private void ClearBehindUiToBlack() {
            Camera boot = Camera.main;
            if (boot == null) {
                _logger.LogWarning("No main camera at boot; pillarbox bars will show whatever the scene clears to.");
                return;
            }
            boot.clearFlags = CameraClearFlags.SolidColor;
            boot.backgroundColor = Color.black;
        }

        public async UniTask StartAsync(CancellationToken cancellation = default) {
            _logger.LogInformation("StartAsync() initiated.");
            ClearBehindUiToBlack();
            AsyncOperationHandle<GameObject> loadingScreenHandle = Addressables.InstantiateAsync(LoadingPrefabAddress);

            try {
                // Optionally wait for the loading screen to be fully loaded before proceeding
                await loadingScreenHandle.Task.AsUniTask();
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
                    } finally {
                        if (metaMenuHandle.IsValid()) {
                            Addressables.ReleaseInstance(metaMenuHandle);
                        }
                    }
                    _logger.LogInformation("MetaMenu dialog completed. GamePath attempt: '{GamePath}'", BakResourceSettings.GamePath);
                }

                _logger.LogInformation("Using GamePath: {GamePath}", BakResourceSettings.GamePath);

                _logger.LogInformation("Initializing Addressables.");
                await Addressables.InitializeAsync();

                _logger.LogInformation("Initializing ResourceManagement.");
                ResourceManagementInitializer.InitializeResourceManagement();

                GameFonts.Install(_gamePanelSettings);
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