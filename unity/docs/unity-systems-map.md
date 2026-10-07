# Unity Systems Map — BaK-Again

**What this is:** the authoritative index of the Unity app's systems — what exists, where it lives, its entry seam, and (most importantly) the **overlaps / "single owner" claims** so a new feature reuses the existing system instead of building a parallel one.

**How to use it:** *before building any new system, component, or mechanism in `Assets/Scripts`, read the relevant section here AND the "Duplicate-risk register" below.* If something already covers the need, extend it. Only build new when this map + a fresh Explore confirm nothing does. Cite what you found.

**Maintenance:** curated by hand (not auto-generated) from a thorough Explore sweep (2026-06-24). When you add or significantly change a system, update the relevant entry + the register. Treat it like `docs/INVENTORY.md` — accurate or fix it; never let it drift silently. Verify a claim against the code before relying on it (it was built from excerpts).

---

## Architectural rules (hard constraints)

1. **Plugin-DLL boundary is absolute.** Never re-parse `.SCX/.BMX/.PAL/.BOK/.DDX/.ADS/.TTM` bytes in Unity. Only `ResourceExtraction.dll` extractors parse DOS formats; `GameData.dll` holds the models. Reach the archive **only** via `ResourceProviderFactory.CreateResourceProvider` → `IResourceProvider.GetResource<T>` (Unity bridges it through `BakResourceProvider` by reflection — intentional, don't "fix" to a direct cast).
2. **Aspect correction happens once, in the DLL** (`AspectCorrection`, VGA ×5/×6, EGA ×2). Unity only ever sees canonical **1600×1200** (or **1280×960** book) square-pixel data. `Graphics/Canonical.cs` mirrors those scale constants and is the SSOT on the Unity side — no `320/200/640/350` literals in Unity code.
3. **Canonical methods — DO NOT reimplement** (see the list at the bottom): `ImageConverter.ConvertToTexture` (indexed→RGBA), `PaletteMapping.GetPaletteFor` (image→palette, in GameData.dll), `Canonical.*` constants, `ConverterExtensions.ToUnity()` (palette→Unity Color), `CanonicalStage.GetOrCreate` (the 1600×1200 stage).
4. **Exactly three resource-loading APIs — do not add a fourth.** `IResourceProviderService.LoadAssetAsync(key, owner)` (owner-tracked, DI, for components); `IResourceCache.GetOrLoadAsync(key)` (session cache, cutscene/book/dialog); raw `Addressables.LoadAssetAsync` (loader-managed handle). Overrides win automatically (override locator registered first).
5. **Input has a single owner: the InputCore stack.** `InputAdapter` is the *only* device-touching code; everything routes intents through `InputLayerStack`. The known exceptions are debug-only (`WorldExplorerController`, `ModelDebugInput`) plus the **one production debt: `InGameMenu.Update()`** (arrow poller, not yet cut over). Do not add a new `Update()` keyboard/mouse poller for menu/UI input.
6. **UI Toolkit's built-in nav is suppressed deliberately** (`NavigableLayer` stops `NavigationMove/Submit/Cancel`; `InputAdapter` nulls the module's `move/submit/cancel`). Both are needed. If you make widgets focusable, the built-in focus ring will fight the adapter unless suppressed.
7. **`Enter Play Mode = full domain+scene reload`** (`EnterPlayModeOptionsEnabled: 0`). MidiPlayerTK needs the domain reload; never enable DisableDomainReload. (See the `feedback_enterplaymode_domain_reload_cursor` rule.)

---

## Systems index

### A. Input / navigation / focus / hover / cursor  (`Assets/Scripts/UI/InputCore/`, `UI/Cursor/`)
- **InputLayerStack** (`InputLayerStack.cs`) — ordered `IInputLayer` stack; `DispatchIntent` routes to topmost Exclusive, else topmost focus-wanting Passive. Singleton.
- **IUiCommands / UiCommands** (`IUiCommands.cs`, `UiCommands.cs`) — the intent seam (`MoveFocus/Activate/Cancel/Accelerator/Skip`). Wraps the stack.
- **UiDriver** (`UiDriver.cs`) — static `Commands`/`Stack` set at container build; **the agent/test seam** (drive nav from `execute_code`/tests with no synthetic device).
- **InputAdapter** (`InputAdapter.cs`) — only device code; UI map + Tab/numpad/letters → intents; click→Skip on release; strips the scene `InputSystemUIInputModule` nav to pointer-only. EntryPoint.
- **InputContext** (`InputContext.cs`) — enables one action map (`UI`/`Player`). **Registered but `Switch` has no caller yet** (Player map dead).
- **NavigableLayer / NavWidget** (`NavigableLayer.cs`, `NavWidget.cs`) — REQ-menu focus (cyclic + `Spatial`), cursor warp on focus, **suppresses UITK built-in nav**, and **dispatches synthetic PointerEnter/Leave on focus change so hover follows keyboard nav**. Passive (Exclusive for dialog choices).
- **ActionLayer** (`ActionLayer.cs`) — full-frame Exclusive (cutscene/credits/book/narrative dialog); `anyIntentActivates` = attract mode.
- **MenuLayerHost** (`MenuLayerHost.cs`) — bridges `UserInterfaceLoader.Built` → push a Passive `NavigableLayer`; pull-reconciles against `IsBuilt`. On all REQ prefabs **except InGameMenu**.
- **CursorManager / CursorArbiter / CursorMap / NullCursorManager** (`UI/Cursor/`) — software cursor overlay in canonical space; `WarpTo` (keyboard), `SetByIndex` (hover shape). Arbiter = last-active-wins (mouse vs warp).
- **InputInjector** (`Testing/InputInjector.cs`) — Editor-only synthetic device events (`TapKey/ClickAt`); goes *through* the adapter (vs `UiDriver` which bypasses it).
- Hover/nav build wiring lives in **`UserInterfaceLoader`** (below): `RegisterNav` (focusable + NavWidget), `RegisterCursorHover` (PointerEnter→cursor shape), per-element PointerEnter/Leave icon swap for toggles/ImageButtons.

### B. UI surfaces, menus, dialogs, layout  (`Assets/Scripts/UI/`, `Graphics/`)
- **CanonicalStage** (`UI/CanonicalStage.cs`) + **Canonical/CanonicalConversion** (`Graphics/`) — the shared 1600×1200 pillarboxed stage + coord math. `PanelSettings.asset` (1600×1200) shared by all game screens; `MetaPanelSettings.asset` (1200×540) for MetaMenu only.
- **ScreenNavigator** (`UI/Navigation/`) — **the single owner of "which screen is visible":** one stack of `IScreen` (`ShowAsync`/`HideAsync` + `ScreenPresentation` Opaque/Composited); `ResetTo/Push/Pop/Replace/Clear/PushAndAwaitClose`, serialized. Makes the ONLY `SetActive`/show/hide calls on screens — **screens never activate each other** (no `Open(returnTo)`, no `_returnTo`, no cross-screen refs). Opaque hides the screen beneath; Composited leaves it visible (in-game menu over the HUD). `ScreenBase` is the MonoBehaviour base (`OnBeforeShowAsync`/`OnAfterShow`). Flows call `ResetTo`; a screen's OK/Cancel/Exit calls `Pop`.
- **REQ screen pattern** — prefab = `BackgroundImageLoader` (SCX→stage bg) + `UserInterfaceLoader` (REQ widgets) + `MenuLayerHost` + a `<Screen>Menu : ScreenBase, IActionHandler` (+ `IMenuStateProvider`/`IFilePickerSource`). Screens: `MainMenu`, `InGameMenu`, `PreferencesMenu`, `LoadGameMenu`, `SaveGameMenu`, `ContentsMenu`, `InventoryMenu`, plus the non-REQ `IScreen`s (`InGameScreen`, `FullMapView`, `BookView`, `CreditsView`, `CutSceneView`). Registered via `RegisterComponentInNewPrefab`.
- **UserInterfaceLoader** (`ResourceManagement/Loaders/UserInterfaceLoader.cs`) — builds TextButton/ImageButton/Toggle/Hotspot/FilePicker from REQ_*.DAT; labels from LBL_*.DAT; BICONS icon scheme; fires `Built(NavWidget[])`/`Cleared`. The renderer, **not** the nav owner.
- **DialogManager / IDialogManager / DialogOpenWipe / DialogTextFormatter** (`UI/`) — DDX dialogs (narrative / confirm / fire-and-forget), chrome, modal scrim, choice buttons (push Exclusive NavigableLayer), open-wipe animation, CP437 control codes. `DialogOverlay.prefab` (SortingOrder 10).
- **MetaMenu** (`UI/MetaMenu.cs`) — bootstrap path-config screen; native UXML, **outside** InputCore/REQ; `WaitForDialogAsync` gates `GameInitializationService`.
- **InGame travel system** (`UI/InGame/` + `World/`) — the in-game world view + navigation + compass:
  - **InGameState** (`Core/States/`) owns a dedicated **WorldCamera** GameObject (not `Camera.main`); camera pose comes from the **zone definition** (`Z##DEF.DAT`: `DefaultCameraZ` = eye altitude, `DefaultCameraPitch` = pitch — `resource_loadZoneDataFiles` @ 0x7313b), with party X/Y + heading from `GameSession`. Builds zone (`ZoneSceneBuilder`) + HUD + menu.
  - **InGameHud** (UIDocument, FRAME.SCR chrome + world RT cutout) hosts **CompassView** and refreshes it each frame.
  - **InGameMenu.prefab** (real, wired to `RootLifetimeScope.inGameMenuPrefab`) = UIDocument(sortOrder 1, over the HUD) + `UserInterfaceLoader`(REQ_MAIN.DAT) + `InGameMenu : IActionHandler`. Drives **PartyMovement** on the 4 nav ids. `InGameMenu.Update()` is the lone production input poller (held-repeat for arrow keys + mouse-held-on-button) — the known InputCore debt; deliberately **no** `MenuLayerHost`. (A `NullInGameMenu` stub still exists for when the prefab is unassigned.)
  - **PartyMovement** (`World/PartyMovement.cs`) — applies MOVEMENT.DAT step/turn to `GameSession` (source of truth) + syncs the camera; pure math in **MovementMath** / **CompassMath** (tested). Since 2026-08-10 it is also the **single owner of the walkability gate**: probe destination → step → terrain-follow eye Z → hotspot pass (which can revert the step) → blocked-forward ±90° pivot → bump SFX 0x31. See `docs/specs/collision-system.md`.
  - **World collision** (`World/Collision/`) — **`ProximityWorld`** is the single owner of "may the party stand here / how high is the ground here": the zone's GID polygons per placement, the FILTER.DAT candidate gate, the `vislist_sort` ordering (nearest wins, priority models last) and the walkable-kind set {0,1,2,14,15,23}. Pure math in **`ProximityMath`** (point-in-polygon, sloped-region height, octagonal distance, Q14 rotate) — no UnityEngine, fully unit-tested. Built by `ZoneSceneBuilder` in the same loop that spawns the renderables (`ZoneSceneBuilder.Collision`); consumed only by `PartyMovement`. **Do not add a party radius, capsule, swept test or sliding** — the party is a point and blocked-forward *turns* you (spec §6).
  - **Hotspots / trigger volumes** (`World/Hotspots/`) — **`HotspotActivator`** (pure) runs the activate pass over `Tzzxxyy.DAT` triggers at the party's sub-tile: inclusive bbox match in table order, availability gates, ambush latch, and the interaction→**revert the step** rule that makes `Bloc` a data-driven invisible wall. **`HotspotService`** is the live host (per-chunk tables, `DEF_BLOC.DAT`, `GameSession` globals, `IDialogManager`) and supplies the `Func<x,y,bool>` PartyMovement calls. **`HotspotDispatcher`** owns the dispatch pass and is reached from `HotspotService.ActivateAtPartyPosition` after every successful step, with live arms for `Dial` (→ `IDialogManager.ShowById`), `Disa`/`Enab`, `Zone` (ask-then-cross via `ZoneTriggerRules`), and `Bkgr`/`Town` (GDS scene entry). **Do not write a second dispatcher.** Only two kinds are still unhandled: `Comb` (waiting on TASK-94) and `Trap` (TASK-100); `Soun` has no data shipped, so there is nothing to drive it.
  - **CompassView** (`UI/InGame/CompassView.cs`) — scrolling COMPASS.BMX strip (single 1280×66 image = full 360°, palette **OPTIONS.PAL**) windowed to the chrome compass slot, offset by heading (two copies wrap, mirroring `drawCompass` @ 0x4691f).
  - **This is bare-bones travel only — lots deferred, do NOT treat as finished.** Walkability, terrain-follow eye-Z and hotspot trigger volumes landed 2026-08-10 (above); still not built: road-following/travel mode, the hotspot dispatch pass, world click/interaction & building entry, the non-movement action buttons (cast/encamp/map/book/party — `InGameMenu.PrimaryAction` stubs/logs them), party-portrait slots, sky/fog/time-of-day & zone-def fog params, world items/sprites/NPCs/encounters, the InputCore cutover (`InGameMenu.Update()` is still a private poller), and visual calibration of the camera pitch sign + compass scroll direction.
- **FullMapView** (`UI/FullMap/`) — chapter map overlay; **uGUI (not UITK)** — the only uGUI surface; bridges to UITK overlays via `GameViewportRegistry`.
- **CreditsView** (`UI/CreditsView.cs`) — credits scroll in CanonicalStage; pushes Exclusive ActionLayer.
- **GameViewportRegistry / IGameViewport** (`UI/`) — mutable screen-rect provider (cutscene/HUD/map are producers; DialogManager consumer — currently dead injection).

### C. Cutscene engine, books, animation  (`Assets/Scripts/CutScenes/`, `Book/`, `Shaders/`)
- **CutscenePresenter** + **CutscenePlayer** + **CutscenePlayerFactory** + **CutsceneInstaller** — orchestrate ADS/TTM: load→preprocess→`ScriptProcessor`→frame loop. `PlayCutsceneAsync(name, view, anim, attractMode)`.
- **CutsceneState (+Extensions)** — 4×(indexed+direct) RenderTexture buffers (A/B/C/X) + OutputBuffer, palette slots/cycling, materials. `ScreenBuffer.cs` is **vestigial**.
- **CutsceneFrameProcessor / ICutsceneFrameProcessor** + **AnimationCommandMap** — per-frame command dispatch (~40 handlers in `AnimationCommands/`) + timing/palette-cycle hold.
- **AnimationCommands/** — the ~40 frame-command handlers (buffer ops, DrawImage variants, palette, fade, dialog, audio). `LoadFontResource`/`SelectFontSlot` **throw NotImplementedException**.
- **CutSceneView / ICutsceneView** — 4:3 RawImage host; provides `Canvas` to CutsceneState; registers `GameViewportRegistry`.
- **Drawing / DrawingUtils** (`Graphics/`) — GL blit helpers; indexed-vs-direct split + cutout-hole punch.
- **Shaders** (`Shaders/Custom/CutScenes/`) — `IndexedTexture` (palette lookup), `CutOut` (hole punch), `TextureWithZeroClip` (pass-through). Found by `Shader.Find` → must be in Always-Included.
- **IndexedTexture** (`ResourceManagement/Models/IndexedTexture.cs`) — unified indexed (R-channel) vs direct (RGBA mod) image; `IsIndexed` picks the draw path.
- **ResourceCache / ResourceSet** (`CutScenes/`) — session cache + per-cutscene asset bag. **ResourceCache never releases handles.**
- **Book**: **BookPresenter / BookView / BakTextWrapper / BakFontData** — BOK pagination, wrap (`sub_ovr148_51F`), EGA font width tables; pushes ActionLayer("book"). **uGUI/TMP rendering** (separate from cutscene pipeline).

### D. Resource pipeline & rendering  (`Assets/Scripts/ResourceManagement/`)
- **Plugin DLLs** (`Assets/Plugins/netstandard2.1/GameData.dll`, `ResourceExtraction.dll`) — models + parsers. Sources in `DotNetProjects/`.
- **BakResourceProvider** — extracts from KRONDOR.001, two-level cache (parsed `IResource` + converted Unity objects), converter dispatch (`UnityConverterMap`: IndexedTexture, Sprite). `ClearCaches()` must run on chapter/scene change.
- **OverrideResourceProvider** — PNG/JSON from `Overrides/`; no cache; mod path (`IsIndexed=false`).
- **Locators**: `BakResourceLocator` (archive index + `.SCR/.bmp` aliasing + standalone files), `OverrideResourceLocator` (`Overrides/` tree). Registered by **ResourceManagementInitializer**; settings in **BakResourceSettings** (PlayerPrefs).
- **Converters** (`Converters/`): **ImageConverter** (canonical indexed→RGBA — DO NOT reimplement), `SpriteUnityConverter` (Sprite, palette baked, `ParseSubImageIndex`), `IndexedTextureConverter`, `ImageSetConverter`, `UnityConverterBase` (`LoadPalette` via `PaletteMapping`, sync `WaitForCompletion` — load-time only), `ConverterExtensions` (`ToUnity`, `ToTexture2D`).
- **Loading services**: `IResourceProviderService/AddressableResourceProviderService` (owner-tracked), `AddressableLoader` (thin static), `BackgroundImageLoader` (SCX→stage bg style).
- **Palette pipeline**: static bake (Sprite) vs runtime GPU lookup (IndexedTexture shader + `CutsceneState.SetPalette`/cycling) vs world fog (RGBA). `PaletteMapping.GetPaletteFor` is the only image→palette map.

### E. Core flow, DI, world, audio  (`Assets/Scripts/Core/`, `World/`, `Audio/`)
- **GameInitializationService** (`IAsyncStartable`) — real bootstrap: loading screen → MetaMenu path prompt → `ResourceManagementInitializer` → `GameManager.StartGame`.
- **NO state machine** (deleted 2026-07-12, see `docs/superpowers/specs/2026-07-12-screen-navigation-architecture-design.md`). The former `StateMachine`/`IState`/6 states are gone. In their place three owners:
  - **`GameSession`** = the state, as data (no session vs. session loaded; later "in combat").
  - **`WorldRuntime`** (`Core/Services/`, file under `World/`) = the single owner of the zone-scene + world-camera + party-movement build/teardown (`BuildAsync`/`TeardownAsync`). Nothing else may build/destroy the world.
  - **`GameFlow` / `IGameFlow`** (`Core/Services/`) = the transitions: `Boot` (attract loop → menu), `ShowMainMenu`, `StartNewGame`, `LoadSave`, `EnterWorld`. Serialized (one flow at a time), the only mutators of "what is loaded", the only place a screen-tree + world lifecycle are sequenced. Screens trigger flows; only flows change configuration.
- **GameManager.StartGame** runs a `StartStateSelector` entry (each entry is a flow call, or an Editor-only debug script). `DebugStart.Select("InGameState")` etc. still work (entry ids kept the historical *State type names).
- **ChapterScenesPlayer** (`Core/Services/`) — awaited script that plays a chapter's cutscene/book scenes by pushing the cutscene/book screens (replaces `ChapterScenesState` + its `OnComplete` relay). `ChapterScenes.PartCount/IsReplayable` (in `States/ChapterScenesRequest.cs`) are the pure selection rules, still shared with tests.
- **DialogExecutor** (`Core/Services/`) — UI-free dialog game-logic: id→leaf-entry resolution (the effect-applying branch walk), `ApplyEffect`, `RunChapterSetupAsync` (party order). DialogManager renders resolved entries only. **Owner for "apply a dialog effect / resolve a dialog".**
- **Debug entries** (`Core/States/Debug/`, `#if UNITY_EDITOR`): BookTest/WorldTest/ModelDebug/TestCutscene — now plain scripts (`RunAsync`), not states.
- **DI: RootLifetimeScope** (`Core/DI/`) — the *only* `LifetimeScope`; all registrations. `InputCoreInstaller.RegisterInputCore(builder)` (static) + `new CutsceneInstaller().Install(builder)` (instance) — two installer patterns. Null-guarded prefab registration for HUD/Menu/Cursor.
- **GameSession** — live state (party/time/zone/pos); `Initialize`/`ApplyChapterStart`/**`Clear` (defined, not yet called)**. Plain singleton, no change events.
- **GameStateLoader / Services** — SaveGame load (chapter 1 only), `PreferencesService` (**`Changed` has no subscribers yet**), `SaveGameDirectoryService`, `GlobalDiagnosticService`, `LogManager` (static; its own ILoggerFactory over Debug.Log since TASK-840).
- **World** (`World/`): `ZoneSceneBuilder` (TBL/WLD/PAL/RMP→scene), `WorldEntityClassifier`, converters (`BakCoordinateConverter`, `TblMeshConverter`, `FogRampBuilder`, …), `WorldRenderModeService` (Classic/Enhanced, `SetMode` never called), `WorldViewport/IWorldViewport` (RE rect), `HorizonRenderer` (**dead — passed null**), `WorldExplorerController` (debug WASD poller), `BillboardSprite`, `FogController`.
- **Arena creature animation** (`World/Encounters/`) — added 2026-09-02, and the **single owner** for
  what a combatant sprite does. `DirectionalSprite` picks the octant per frame and holds the walk
  frame (`GaitFrame`/`AdvanceGait`/`SeedGait`/`CurrentOctant`/`SetTintRung`); the per-actor state it
  resumes from lives on **`Combatant`**, not on the GameObject, because `WorldRuntime`'s
  `DrawCombatantsAsync` destroys and rebuilds the whole arena on every combat redraw. Four behaviours
  hang off that redraw and none of them should be reimplemented elsewhere:
  **the slide** (`SlideThenRedrawAsync` lerps every sprite that changed cell, then rebuilds — enemy
  turns slide for free because the seam is the redraw, not the click), **the idle**
  (`ArenaIdleAnimator`, `CreatureAnimationStep`'s re-rolled 8..15-tick delay so gaits drift apart),
  **the death collapse** (`DeathCollapse`, four frames ending on the corpse the rebuild draws anyway),
  and **the swing** (`AttackSwing`, four frames from `SpriteKeys[1]`, then restores the standing
  sprite). The hit reaction is a **palette re-tint** through the zone's own RMP fade ramp
  (`WorldEntityRenderContext.FadeRamp` + `LoadSpriteTextureAsync(..., fadeRung)`), **not** a
  displacement — the original's `hitReactionDir` is a remap index. `ArenaCombatant`/`ArenaCorpse`
  carry `(RosterSlot, PartyMember)` identity; both fields are needed, since slot 0 names two
  combatants. See `docs/FileFormats/General resources.md` and TASK-103/TASK-285 for the RE.
- **Spell & world VFX** (`World/Encounters/SpellVfx.cs`, TASK-117, 2026-09-25) — **single owner** for
  a combat spell's picture. The rules name it (`GameData` `SpellVisuals.OneShot/Lingering`) and step
  its particles (`SpellParticles`, the WORLDFX.C rules on the ~59 ms combat frame); `CombatRuntime.
  PlaySpellVisual` raises it, `HotspotService.SpellVisualSink` forwards it, and `SpellVfx` plays one
  sequence at a time, anchored to the COMBATANT (looked up each frame, since the arena is rebuilt per
  redraw). Lingering looks (Hocho box, sparkles, halos, held-tile shake) are re-attached per redraw by
  `WorldRuntime.AttachLingeringSpellVisuals`; spell tiles by `DrawSpellTilesAsync`. The **world palette
  flash** is the `_BakFlash` term in `BakLighting.hlsl` (`SpellVfx.SetWorldFlash`) — HUD untouched, sky
  follows via `WorldLightingService.Apply`; a per-sprite flash is ClassicSprite's `_FlashColor`. Glows
  draw with `Resources/Shaders/SpellGlow.shader`. Also here: floating damage numbers
  (`InGameScreen.FloatText`, fed by `Combatant.DamageFloat`), the trapped-chest BOOM
  (`WorldRuntime.PlayChestExplosionAsync`), and the cast-ring sigil morph (`CastScreen.MorphSigilAsync`).
  The projectile flight stays `ProjectileFlight`; the hit recoil stays `SetTintRung`.
- **Audio**: `MidiPlaybackManager` (MidiPlayerTK wrapper; `PlaySong/PlaySfx/StopSound`; static `Instance` AND DI — dual access; pool size 2; not subscribed to Preferences). **`MenuSoundService`** (`Audio/MenuSoundService.cs`) — plays UI select/toggle SFX by sound id (default `sound_pound` 83; `ClickSound` override; silent only when `SoundFlags==3`). Composes `MidiPlaybackManager.PlaySfx` + `IResourceCache`; called from `UserInterfaceLoader.Select` (which resolves it from the `LifetimeScope` as a sibling-injection fallback) and `DialogManager` choice buttons. The owner for "make a UI sound" — don't call `PlaySfx` for UI directly.
- **Platform** (`Platform/Android,Windows/`) — **empty stubs**.

---

## Duplicate-risk register  (check before building)

> The point of this map. If your task touches one of these, you're in territory where a parallel implementation is easy to add by mistake — reuse/extend the owner instead.

| Concern | Owner / where | Watch |
|---|---|---|
| **Show / hide / navigate between screens** | `ScreenNavigator` (`UI/Navigation/`) | The ONLY caller of `SetActive`/show-hide on screens. Never add `Open(returnTo)`/`_returnTo`/cross-screen refs; a screen pushes/pops, it never activates a sibling. |
| **Change what's loaded (new game / load / quit / enter world)** | `GameFlow` (`Core/Services/`) | The only mutator of loaded configuration + the only place a screen-tree and `WorldRuntime` are sequenced. Screens *trigger* flows; they don't inline hydration/teardown. There is NO state machine. |
| **Build / tear down the world (zone scene + camera)** | `WorldRuntime` (`Core/Services/`) | Single owner; only `GameFlow` calls it. Don't rebuild the world for combat — retarget the camera + swap the HUD screen. |
| **Resolve / apply a dialog (branch walk, effects, chapter setup)** | `DialogExecutor` (`Core/Services/`) | UI-free. DialogManager renders resolved entries only; don't put dialog game-logic back in the UI. |
| **Move focus / handle Tab-arrows-Enter-Esc** | `InputAdapter` → `InputLayerStack` → layers | Don't poll keys in `Update()`. UITK built-in nav is a 2nd mover — `NavigableLayer` suppresses it. `InGameMenu.Update()` is the one (known) debt. |
| **Hover / highlight a widget** | `UserInterfaceLoader` PointerEnter/Leave (cursor shape + icon swap); `NavigableLayer.OnWidgetFocused` dispatches synthetic PointerEnter/Leave so keyboard focus drives it | Hover is now focus-driven AND mouse-driven through the **same** handlers — reuse them, don't add new hover logic. |
| **Indexed→RGBA image conversion** | `ImageConverter.ConvertToTexture` / `ToTexture2D` (canonical, "DO NOT reimplement") | Scan-order logic also in `IndexedTexture.CreateIndexTexture` (different output) — keep consistent. |
| **Image→palette mapping** | `PaletteMapping.GetPaletteFor` (GameData.dll) via `UnityConverterBase.LoadPalette` | Fallback `key→.PAL` is the only secondary strategy; don't add a third. |
| **Load a resource** | the 3 APIs (rule 4) | Don't add a 4th loader. |
| **Pen index → Color** (`ResolvePen`) | **3 copies**: `UserInterfaceLoader`, `DialogManager`, `DialogTextFormatter` | Extract a shared helper before adding a 4th. |
| **8-VGA-px menu/dialog font size (48)** | duplicated const in `UserInterfaceLoader` (`LabelFontSize`) and `DialogManager` (`BodyFontSizePx`) | Change both together (no shared def). |
| **1/60s frame duration** | duplicated in `CutsceneFrameProcessor`, `FadeInExtensions`, `FadeOutExtensions` | Change all three. |
| **Canonical/4:3 letterbox** | `CanonicalStage` (UITK) vs `CutSceneView` RawImage vs `BookView` `FitPageToCanvas` vs `FullMapView` (uGUI) | Four parallel letterbox implementations; share only `Canonical.*`. |
| **Pillarbox offset for cursor** | `CursorManager.StageOrigin()` vs `CanonicalConversion.ScreenToCanonical` | Two routes to the same offset; diverge if fit policy changes. |
| **Screen-rect for dialog anchoring** | `GameViewportRegistry` (single active provider) | Producers must `SetProvider(null)` on teardown; last-writer-wins, no notify. |
| **Software cursor map** | `CursorManager.SemanticMap` (live) vs `CursorMap.cs` (parsed but **unused**) | Two representations; SemanticMap is the runtime one. |
| **Sibling-component cursor injection** | `UserInterfaceLoader` + `MenuLayerHost` both `FindFirstObjectByType<CursorManager>()` (DI doesn't inject prefab siblings) | Expected pattern; null-safe; fragile if 2 CursorManagers. |
| **Two test seams** | `UiDriver.Commands` (intent, bypasses adapter) vs `InputInjector` (device, through adapter) | Pick deliberately. |
| **MidiPlaybackManager access** | static `Instance` (cutscene cmds) AND DI (others) | Same object, dual access. |
| **"Can the party walk here / how high is the ground"** | `ProximityWorld` (`World/Collision/`) | The only walkability + terrain-height query. Never add Unity Physics colliders, a party radius or a swept test for movement — `WorldPicker`'s colliders are for *mouse picking only* (DETECT.DAT-gated) and are a different system. |
| **"Did stepping here trigger something"** | `HotspotActivator` + `HotspotService` (`World/Hotspots/`) | The only consumer of `Tzzxxyy.DAT`. A trigger that interacts reverts the step — that rule lives in `PartyMovement`, not in the caller. |
| **Camera ownership** | `InGameState` creates a dedicated `WorldCamera` GameObject (pose from zone def); HUD renders it into the cutout RT; debug states make their own `"MainCamera"`; `BillboardSprite` still uses `Camera.main` | Production travel camera is owned by `InGameState`; debug states + `BillboardSprite` remain on `Camera.main` — no global ownership model. |
| **Two viewport interfaces** | `IWorldViewport` (RE rect math) vs `IGameViewport`/`GameViewportRegistry` (overlay rect) | Bridged only by real `InGameHud`. |

**Known dead / stub / unfinished (don't build *on* these assuming they work):** `InputContext.Switch` (no caller, Player map dead), `HorizonRenderer` (null texture), `CursorMap.cs` (unused), `ScreenBuffer.cs` (vestigial), `UiImage`/`AddressableBinding` (TODO-remove / orphaned), `GameSession.Clear` (uncalled), `PreferencesService.Changed` (no subscribers), `WorldRenderModeService.SetMode` (uncalled), `LoadFontResource`/`SelectFontSlot` (throw), `Platform/*` (empty), `ModelDebugState` writes `debug.json` to CWD on every zone load, `ResourceCache`/cutscene handles never released.

---

## Canonical "DO NOT reimplement" list
- `ScreenNavigator` (`IScreenNavigator`) — the one screen stack; show/hide a screen through it, never `SetActive` a screen directly or reference one screen from another.
- `GameFlow` (`IGameFlow`) — the transition scripts; changing "what's loaded" goes through a flow, not a new ad-hoc SetActive/`ChangeState` path (there is no state machine).
- `WorldRuntime` — the world build/teardown; the only owner of the zone scene + world camera.
- `SpellVfx` + `SpellVisuals` — combat spell visuals; raise one through `CombatRuntime.PlaySpellVisual`, never draw a spell effect ad hoc.
- `DialogExecutor` — dialog resolution/effects; the UI never re-implements the branch walk.
- `ImageConverter.ConvertToTexture(image, colors, transparentIndex0)` (or `.ToTexture2D`) — indexed→RGBA.
- `PaletteMapping.GetPaletteFor(key, subImage)` — image→palette (GameData.dll).
- `Canonical.*` (Width/Height/VgaScaleX/Y/BookWidth/Height/EgaScaleX) — coord constants.
- `ConverterExtensions.ToUnity()` — `GameData` palette `Color[]` → `UnityEngine.Color[]`.
- `CanonicalStage.GetOrCreate(root)` — the shared 1600×1200 stage.
- `ResourceProviderFactory.CreateResourceProvider` → `IResourceProvider.GetResource<T>` — the archive seam.
- The InputCore stack (`IUiCommands`/`InputLayerStack`/`NavigableLayer`/`ActionLayer`) — all UI/menu input.
