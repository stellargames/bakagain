namespace BakAgain.World.Hotspots {
    using BakAgain.ResourceManagement;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Debug visualisation of a zone's world triggers — the per-chunk <c>Tzzxxyy.DAT</c> tile-event
    /// rectangles that <see cref="HotspotService"/> runs the party against.
    ///
    /// <para>Draws each trigger's footprint as a coloured outline on the ground, colour-coded by
    /// <see cref="TileEventType"/>. Triggers are invisible in the game by design, so without this
    /// there is no way to see where they are, how large they are, or that they overlap.</para>
    ///
    /// <para>Two things worth knowing when reading the picture:</para>
    /// <list type="bullet">
    ///   <item>Rectangles are <b>chapter-specific</b>. A tile carries a separate trigger block per
    ///   chapter, so the same ground fires different things later in the story — the overlay shows
    ///   one chapter at a time.</item>
    ///   <item>Outlines are drawn at one flat height, not draped over the terrain relief, so on
    ///   sloping ground a rectangle can sit above or below the surface. It marks the footprint
    ///   (triggers test the party's X/Y sub-tile only), not a volume.</item>
    /// </list>
    /// </summary>
    public sealed class WorldTriggerOverlay : MonoBehaviour {
        private const int ChunkSize = GameData.Resources.World.WorldTileCache.TileWorldSize;
        private const int CellSize = 1600;

        /// <summary>Outline width in Unity units. A grid cell is 16 units, so this stays readable
        /// walking around without swamping the small single-cell triggers when zoomed out.</summary>
        private const float LineWidth = 2.5f;

        private readonly List<GameObject> _outlines = new();

        /// <summary>How many trigger rectangles are currently drawn.</summary>
        public int TriggerCount { get; private set; }

        /// <summary>Chunks that carried at least one trigger in the chapter shown.</summary>
        public int ChunkCount { get; private set; }

        /// <summary>The chapter whose triggers are drawn.</summary>
        public int Chapter { get; private set; }

        /// <summary>Whether the outlines are currently shown.</summary>
        public bool Visible { get; private set; } = true;

        /// <summary>Per-type counts, for the legend.</summary>
        public IReadOnlyDictionary<TileEventType, int> CountsByType => _countsByType;

        private readonly Dictionary<TileEventType, int> _countsByType = new();

        /// <summary>
        /// Colour for each event type. Chosen so the ones you usually go looking for stand out:
        /// combat and traps are the warm colours, movement between places is blue/magenta, and the
        /// bookkeeping types (enable/disable) are muted.
        /// </summary>
        public static Color ColorFor(TileEventType type) => type switch {
            TileEventType.Bkgr => new Color(0.55f, 0.55f, 0.55f),
            TileEventType.Comb => new Color(1f, 0.15f, 0.15f),
            TileEventType.Comm => new Color(1f, 0.55f, 0.1f),
            TileEventType.Dial => new Color(0.2f, 0.9f, 1f),
            TileEventType.Heal => new Color(0.2f, 1f, 0.35f),
            TileEventType.Soun => new Color(1f, 0.95f, 0.25f),
            TileEventType.Town => new Color(1f, 0.35f, 0.9f),
            TileEventType.Trap => new Color(0.75f, 0f, 0.25f),
            TileEventType.Zone => new Color(0.3f, 0.5f, 1f),
            TileEventType.Disa => new Color(0.4f, 0.35f, 0.3f),
            TileEventType.Enab => new Color(0.6f, 0.85f, 0.5f),
            TileEventType.Bloc => Color.white,
            _ => Color.white,
        };

        /// <summary>
        /// Loads a zone's trigger tables and draws them.
        /// </summary>
        /// <param name="parent">Zone root; the overlay is added as a child so it dies with the zone.</param>
        /// <param name="chunks">The <c>Tzzxxyy</c> names the zone scene was built from.</param>
        /// <param name="chapter">Which chapter's trigger block to show.</param>
        /// <param name="height">Unity Y to draw the outlines at.</param>
        public static async UniTask<WorldTriggerOverlay> BuildAsync(
            GameObject parent, IEnumerable<string> chunks, int chapter, float height,
            IResourceProviderService resources, object owner, ILogger logger) {
            var go = new GameObject("TriggerOverlay");
            go.transform.SetParent(parent.transform, false);
            var overlay = go.AddComponent<WorldTriggerOverlay>();
            overlay.Chapter = chapter;

            Material material = CreateLineMaterial();

            foreach (string chunk in chunks) {
                string name = System.IO.Path.GetFileNameWithoutExtension(chunk);
                if (!HotspotService.TryParseChunkCoords(name, out int cx, out int cy)) {
                    continue;
                }

                TileEventTile tile = await LoadOrNull<TileEventTile>($"{name}.DAT", resources, owner, logger);
                List<TileEventTrigger> triggers = HotspotService.TriggersFor(tile, chapter);
                if (triggers.Count == 0) {
                    continue;
                }

                overlay.ChunkCount++;
                foreach (TileEventTrigger trigger in triggers) {
                    overlay.DrawTrigger(trigger, cx, cy, height, material);
                }
            }

            logger?.LogInformation(
                "Trigger overlay: zone chapter {Chapter}, {Triggers} trigger(s) across {Chunks} chunk(s).",
                chapter, overlay.TriggerCount, overlay.ChunkCount);
            return overlay;
        }

        /// <summary>Shows or hides the outlines without rebuilding them.</summary>
        public void SetVisible(bool visible) {
            Visible = visible;
            foreach (GameObject outline in _outlines) {
                if (outline != null) {
                    outline.SetActive(visible);
                }
            }
        }

        /// <summary>A one-line summary for the debug UI.</summary>
        public string Summary() =>
            TriggerCount == 0
                ? $"Triggers: none in chapter {Chapter}"
                : $"Triggers: {TriggerCount} in {ChunkCount} chunk(s), chapter {Chapter}";

        private void DrawTrigger(TileEventTrigger trigger, int chunkX, int chunkY, float height, Material material) {
            // Sub-tile bounds are INCLUSIVE at both ends (HotspotActivator.MatchAt tests
            // Start <= sub <= End), so the far edge is End+1 cells out.
            float x0 = ((chunkX * ChunkSize) + (trigger.StartX * CellSize)) / BakCoordinateConverter.WorldScale;
            float x1 = ((chunkX * ChunkSize) + ((trigger.EndX + 1) * CellSize)) / BakCoordinateConverter.WorldScale;
            float z0 = ((chunkY * ChunkSize) + (trigger.StartY * CellSize)) / BakCoordinateConverter.WorldScale;
            float z1 = ((chunkY * ChunkSize) + ((trigger.EndY + 1) * CellSize)) / BakCoordinateConverter.WorldScale;

            var go = new GameObject($"{trigger.Type}_{trigger.EntryNumber}");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.material = material;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.widthMultiplier = LineWidth;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            line.SetPositions(new[] {
                new Vector3(x0, height, z0),
                new Vector3(x1, height, z0),
                new Vector3(x1, height, z1),
                new Vector3(x0, height, z1),
            });

            Color color = ColorFor(trigger.Type);
            // A one-shot trigger is drawn dimmer: it is spent after the first visit, so it is not the
            // same thing as one that fires every time you walk over it.
            if (trigger.FireOnce != 0) {
                color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f);
            }
            line.startColor = color;
            line.endColor = color;

            _outlines.Add(go);
            TriggerCount++;
            _countsByType.TryGetValue(trigger.Type, out int count);
            _countsByType[trigger.Type] = count + 1;
        }

        private static Material CreateLineMaterial() {
            // Hidden/Internal-Colored is the one that actually honours _ZTest, which is what makes
            // the outlines draw THROUGH terrain and scenery. Verified: with Sprites/Default (which
            // silently ignores _ZTest) rectangle edges vanish behind rocks and hillsides, which is
            // exactly when you most want to see where a trigger is. Sprites/Default stays as the
            // fallback in case the internal shader is stripped from a player build — the overlay
            // then still draws, just occluded.
            Shader shader = Shader.Find("Hidden/Internal-Colored")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            return material;
        }

        private static async UniTask<T> LoadOrNull<T>(
            string address, IResourceProviderService resources, object owner, ILogger logger) where T : class {
            try {
                return await resources.LoadAssetAsync<T>(address, owner);
            } catch (System.Exception e) {
                logger?.LogDebug("Trigger overlay: {Address} not available ({Message}).", address, e.Message);
                return null;
            }
        }
    }
}
