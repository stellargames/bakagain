namespace BakAgain.World.Scenes {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.GameState;
    using GameData.Resources.Scene;
    using System;
    using System.Collections.Generic;

    /// <summary>One hotspot that is live right now, paired with the action id the panel gives it.</summary>
    public readonly struct LiveHotspot {
        public LiveHotspot(int index, int actionId, GdsHotspot hotspot) {
            Index = index;
            ActionId = actionId;
            Hotspot = hotspot;
        }

        /// <summary>Position in the scene's own hotspot list — the id the original keys on.</summary>
        public int Index { get; }

        /// <summary>The panel action id: <c>0x80 + Index</c>.</summary>
        public int ActionId { get; }

        public GdsHotspot Hotspot { get; }
    }

    /// <summary>
    /// Loads an interactive location and works out what is actually clickable in it — the Unity half
    /// of <c>townscene_load</c>, with the rules themselves in
    /// <see cref="GdsSceneRules"/> so they stay testable without an Editor.
    ///
    /// <para>Rendering is not here. A scene's picture comes from its <b>ADS animation</b>
    /// (<see cref="GdsScene.AnimationResource"/> + the entry tag), not from a static SCX, so drawing
    /// one means driving the cutscene engine and holding on its last frame — see the task notes.</para>
    /// </summary>
    public sealed class GdsSceneLoader {
        private readonly IResourceCache _resources;
        private readonly GameSession _session;

        public GdsSceneLoader(IResourceCache resources, GameSession session) {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>
        /// The resource name for a location: chapter number then a letter for the sub-scene, so
        /// (10, 1) is <c>GDS10A.DAT</c>. "Chapter" here is the scene's own number, which is not the
        /// story chapter — the story chapter only gates hotspots.
        /// </summary>
        public static string ResourceName(int sceneNumber, int sub) =>
            $"GDS{sceneNumber}{(char)('@' + sub)}.DAT";

        /// <summary>
        /// Loads a location, honouring the two story-flag redirects that can substitute a different
        /// sub-scene for the one asked for.
        /// </summary>
        /// <returns>The scene, or null when it does not resolve.</returns>
        public async UniTask<GdsScene> LoadAsync(int sceneNumber, int sub) {
            int resolved = GdsSceneRules.ResolveSubScene(sceneNumber, sub, ReadFlag);
            return await _resources.GetOrLoadAsync<GdsScene>(ResourceName(sceneNumber, resolved));
        }

        /// <summary>
        /// The hotspots a player can currently interact with, in scene order.
        /// </summary>
        /// <param name="preserve">
        /// True when re-entering a scene rather than loading it fresh, which bypasses the chapter
        /// gate entirely — see <see cref="GdsSceneRules.IsHotspotVisible"/>.
        /// </param>
        public IReadOnlyList<LiveHotspot> VisibleHotspots(GdsScene scene, bool preserve = false) {
            var live = new List<LiveHotspot>();
            if (scene?.Hotspots == null) {
                return live;
            }
            int chapter = Math.Max(_session.Chapter, 1);
            for (var i = 0; i < scene.Hotspots.Length; i++) {
                GdsHotspot hotspot = scene.Hotspots[i];
                if (GdsSceneRules.IsHotspotVisible(hotspot, chapter, preserve, GatePasses)) {
                    live.Add(new LiveHotspot(i, GdsSceneRules.ActionIdFor(i), hotspot));
                }
            }
            return live;
        }

        // An unset global reads as 0, which is what the original's gstate_event_read returns.
        private int ReadFlag(int key) => _session.GetGlobalValue(key) ?? 0;

        /// <summary>
        /// Evaluates a hotspot's visibility gate. It is a <b>range</b> test on a global, so a plain
        /// "is the flag set" reading would let through hotspots meant for a later part of the story.
        /// </summary>
        private bool GatePasses(Condition gate) {
            switch (gate) {
                case null:
                    return true;
                case VarCondition v: {
                    int value = ReadFlag(30000 + v.Var);
                    return value >= v.Min && (v.Max == null || value <= v.Max.Value);
                }
                case FlagCondition f:
                    return (ReadFlag(f.Flag) != 0) == f.Set;
                default:
                    // Only Var and Flag gates appear on shipped hotspots; anything else would be new
                    // data, so let it through rather than silently hiding an interaction.
                    return true;
            }
        }
    }
}
