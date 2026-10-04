namespace BakAgain.World {
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Per-zone environment data resolved at build time (palette-dependent) and consumed by
    /// the gameplay state that owns the world camera. Attached to the zone root by
    /// <see cref="ZoneSceneBuilder"/>.
    ///
    /// <para>Mirrors the original's <c>drawHorizonSkyAndGround</c> (ovr138 @0x460c7): the sky is a
    /// flat fill in <c>skyColor</c> above the horizon line, the ground a flat fill / textured strip
    /// in <c>groundColor</c> below it, and (detail&gt;=1) the <c>Z##H.BMX</c> mountain panels are
    /// blitted across the horizon offset by camera heading. <c>skyColor</c>/<c>groundColor</c> are
    /// palette pen indices loaded from <c>Z##DEF.DAT</c>; they look time-of-day driven because the
    /// day/night lighting system mutates the palette while the pen index stays fixed.</para>
    /// </summary>
    public class ZoneEnvironment : MonoBehaviour {
        /// <summary>RGB of the zone DEF's sky pen, resolved through the (current) zone palette.</summary>
        public Color SkyColor { get; set; } = Color.black;

        /// <summary>RGB of the zone DEF's ground pen, resolved through the (current) zone palette.</summary>
        public Color GroundColor { get; set; } = Color.black;

        /// <summary>The horizon mountain renderer, or null if the zone has no Z##H.BMX (e.g. underground).</summary>
        public HorizonRenderer Horizon { get; set; }

        /// <summary>RGB of <c>Z##.DAT</c>'s ground pen — what the overhead map fills its view with.</summary>
        public Color MapFillColor { get; set; } = Color.black;

        /// <summary>
        /// Apply this zone's environment to the world camera: clear to the sky colour (the sky band
        /// behind the world, instead of black) and bind the camera so the mountain backdrop follows
        /// and parallax-scrolls with heading.
        /// </summary>
        public void ApplyToCamera(Camera worldCamera) {
            if (worldCamera == null) return;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = SkyColor;
            Horizon?.SetCamera(worldCamera);
        }

        /// <summary>
        /// Switch the world's backdrop between travel and the overhead map.
        /// </summary>
        /// <remarks>
        /// <b>The overhead map is a different RENDER, not just a different camera angle.</b> The
        /// original does not point the travel renderer downwards: <c>sub_seg021_231</c> (0x21941)
        /// fills the viewport with a flat pen and draws the depth-sorted items over it — no sky, no
        /// horizon strip, no ground band. Reproducing that here is three switches:
        /// <list type="bullet">
        /// <item>clear to <see cref="MapFillColor"/> instead of the sky;</item>
        /// <item>hide the horizon backdrop, which is a first-person device with nothing to say from
        /// above;</item>
        /// <item>push the fog and the far plane out by the camera's height;</item>
        /// <item>widen the camera to the map's own projection — see below.</item>
        /// </list>
        ///
        /// <para><b>The fog shift is what makes anything visible at all.</b> The original fogs and
        /// culls by the distance ACROSS the ground, so an item directly below the camera is "near"
        /// however high the camera is. Unity's fog and far plane measure true distance, so at map
        /// height (Z01 starts at 1330 units, and zooms to 2030) the whole world sits past a 640-unit
        /// fog end and reads as one flat grey — which is exactly what it did before this. Shifting
        /// both by the height restores the original's meaning at the point under the camera and
        /// stays conservative further out.</para>
        ///
        /// <para><b>The map is not the travel camera pointed down; it is a WIDER LENS pointed
        /// down.</b> The original keeps two view descriptors, and the map screens render through the
        /// one whose projection shift is 7 while travel and combat render through START.DAT's 9 —
        /// four times the ground at the same height. Giving the map the travel FOV is why the port
        /// showed a quarter of the zone and the locator dropped every marker past a quarter of its
        /// range (TASK-374). <see cref="WorldProjection"/> carries the derivation; the height of the
        /// rect the map is drawn into is all it needs, because the width cancels.</para>
        /// </remarks>
        /// <param name="worldCamera">The world camera, whose far plane and FOV are adjusted.</param>
        /// <param name="on">True to enter map mode, false to restore travel.</param>
        /// <param name="cameraHeight">The map camera's height above the ground, in Unity units.</param>
        /// <param name="mapViewHeight">
        /// Canonical height of the rectangle the map is drawn into: the travel viewport's for the
        /// overhead map, the inset's for the locator.
        /// </param>
        /// <param name="focalLength">The zone's focal length in canonical units (ZoneDefinition.FocalLength).</param>
        public void SetOverheadMapMode(
            Camera worldCamera, bool on, float cameraHeight, float mapViewHeight, int focalLength) {
            if (!_travelBackdropSaved) {
                _travelFarClip = worldCamera != null ? worldCamera.farClipPlane : 0f;
                _travelFov = worldCamera != null ? worldCamera.fieldOfView : 0f;
                _travelFogStart = RenderSettings.fogStartDistance;
                _travelFogEnd = RenderSettings.fogEndDistance;
                _travelBackdropSaved = true;
            }

            if (Horizon != null) {
                Horizon.gameObject.SetActive(!on);
            }

            if (worldCamera != null) {
                worldCamera.backgroundColor = on ? MapFillColor : SkyColor;
                worldCamera.farClipPlane = on ? _travelFarClip + cameraHeight : _travelFarClip;
                worldCamera.fieldOfView = on
                    ? (float)WorldProjection.VerticalFovDegrees(mapViewHeight, focalLength)
                    : _travelFov;
            }

            RenderSettings.fogStartDistance = on ? _travelFogStart + cameraHeight : _travelFogStart;
            RenderSettings.fogEndDistance = on ? _travelFogEnd + cameraHeight : _travelFogEnd;

            // *** THE FOURTH SWITCH: Z##.DAT's pen remap. *** The original installs the table into
            // the render view for as long as the map is up (sub_ovr180_877, cleared by its partner),
            // so every pen draws as another one and the map reads flatter than the world. Ours is
            // baked per face at mesh build and selected here by one global, which costs nothing
            // while travelling and needs no per-material bookkeeping. A zone with an empty table
            // baked its own colours, so this changes nothing there.
            Shader.SetGlobalFloat(MapModeId, on ? 1f : 0f);
        }

        private static readonly int MapModeId = Shader.PropertyToID("_MapMode");

        private bool _travelBackdropSaved;
        private float _travelFarClip;
        private float _travelFov;
        private float _travelFogStart;
        private float _travelFogEnd;
    }
}
