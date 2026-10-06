using BakAgain.UI.InputCore;
using BakAgain.World;
using BakAgain.World.Converters;
using BakAgain.World.Interaction;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BakAgain.UI.InGame {
    /// <summary>
    /// Owns click-to-interact on the 3D world view. Pure plumbing (the Unity analog of DOS
    /// HandleEnvironmentInteraction_impl @0x76573): pick the entity under the pointer, apply the
    /// profile-driven proximity gate, and dispatch by the entity's semantic Behavior key to a
    /// handler. An entity with no behavior, or a behavior with no registered handler, is a no-op
    /// (the faithful DOS default case). Owned by InGameScreen (new-ed, not injected), invoked from
    /// its PrimaryAction/SecondaryAction(192).
    /// </summary>
    public sealed class WorldInteractionController {
        private readonly Camera _camera;
        private readonly IWorldViewport _viewport;
        private readonly IPointer _pointer;
        private readonly VisualElement _panelRoot;
        private readonly Dictionary<string, IWorldInteractionHandler> _handlers = new();
        private VisualElement _stage;
        private bool _loggedStageFallback;

        /// <param name="panelRoot">The travel screen's UIDocument root. The CanonicalStage is
        /// resolved from it lazily and re-tried until found (see <see cref="ResolveStage"/>) rather
        /// than captured here, so a stage that does not exist yet at construction time cannot pin
        /// picking to the pre-layout fallback box for the rest of the session. This parameter used
        /// to be the stage itself and kept its type when it changed meaning, so passing a stage here
        /// still compiles: <see cref="CanonicalStage.Find"/> resolves either, and an element from
        /// inside the stage too (loudly) — see its "What callers may hand in".</param>
        public WorldInteractionController(Camera camera, IWorldViewport viewport, IPointer pointer,
            VisualElement panelRoot, IReadOnlyList<IWorldInteractionHandler> handlers,
            System.Action<BakAgain.World.Encounters.ArenaCorpse, bool> lootCorpse = null,
            System.Action<int, bool, bool> pickCombatant = null,
            System.Action<UnityEngine.Vector3> pickGround = null,
            System.Func<UnityEngine.Vector3, (int RosterSlot, bool PartyMember)?>
                combatantAtPoint = null,
            System.Func<UnityEngine.Vector2?> hoverPointOverride = null,
            System.Action<BakAgain.World.Encounters.EncounterGroupMember, bool> hintEncounter = null,
            System.Func<bool> underground = null) {
            _underground = underground;
            _hintEncounter = hintEncounter;
            _camera = camera;
            _viewport = viewport;
            _pointer = pointer;
            _panelRoot = panelRoot;
            _lootCorpse = lootCorpse;
            _pickCombatant = pickCombatant;
            _pickGround = pickGround;
            _combatantAtPoint = combatantAtPoint;
            _hoverPointOverride = hoverPointOverride;
            if (handlers != null) {
                foreach (IWorldInteractionHandler h in handlers) {
                    if (h != null) { _handlers[h.Behavior] = h; }
                }
            }
        }

        // What to do with a body the pointer found. Optional: a harness with no fight never picks
        // one, and the world click path is unchanged for everything else.
        private readonly System.Action<BakAgain.World.Encounters.ArenaCorpse, bool> _lootCorpse;

        // A live encounter group in the world: the click asks about it (wcursor_encounter_hint).
        private readonly System.Action<BakAgain.World.Encounters.EncounterGroupMember, bool> _hintEncounter;

        // What to do with a LIVE combatant the pointer found, by roster identity. Optional for the
        // same reason the corpse seam is: a harness with no fight never picks one.
        private readonly System.Action<int, bool, bool> _pickCombatant;

        // Who stands on the cell under a world point. *** THIS IS THE COMBAT PICK. *** The original
        // reads the grid's occupant at the cursor's cell (combat_actor_terr_under_cur, CACTOR.C:741)
        // and never hit-tests a figure, so neither do we -- see HotspotService.CombatantAtPoint.
        private readonly System.Func<UnityEngine.Vector3, (int RosterSlot, bool PartyMember)?>
            _combatantAtPoint;

        // What to do with a click that hit NOTHING while a fight is running — the arena floor. Last
        // of the four, because a tile is what a click means only once every object pick has missed.
        private readonly System.Action<UnityEngine.Vector3> _pickGround;

        /// <summary>
        /// The combatant under the cursor right now, or null — the pick without the acting.
        /// </summary>
        /// <remarks>
        /// <b>For the readouts that follow the cursor rather than a click</b>, currently the shoot
        /// menu's target panel, which the original redraws whenever the cursor moves to another
        /// TILE — and a tile is exactly what this resolves. It runs the same floor pick and the
        /// same cell lookup the click path runs, so what the panel reports on and what a click
        /// would hit cannot disagree.
        ///
        /// <para><b>Safe to call every frame, and only worth calling while something wants it</b>:
        /// one raycast, no allocation, no side effect. The caller gates it, not this method.</para>
        /// </remarks>
        public (int RosterSlot, bool PartyMember)? HoverCombatant() =>
            HoverScreenPoint() is UnityEngine.Vector2 p ? CombatantAtScreenPoint(p) : null;

        /// <summary>The combatant on the arena cell under a screen point (Input System coords), or null.</summary>
        public (int RosterSlot, bool PartyMember)? CombatantAtScreenPoint(UnityEngine.Vector2 screenPoint) {
            if (_camera == null || _combatantAtPoint == null) {
                return null;
            }
            UnityEngine.Vector3? floor = WorldPicker.PickGroundPoint(
                _camera, _viewport, screenPoint,
                CanonicalStage.ScreenRect(ResolveStage(), out bool _));
            return floor.HasValue ? _combatantAtPoint(floor.Value) : null;
        }

        // Touch has no hovering pointer (IsPresent is false), so the touch aids supply the point a
        // mouse would hover — the selected target (C1) or a point above the finger (C2). With no
        // override value this is exactly the mouse's position, as before.
        private readonly System.Func<UnityEngine.Vector2?> _hoverPointOverride;

        private UnityEngine.Vector2? HoverScreenPoint() =>
            _hoverPointOverride?.Invoke()
            ?? (_pointer != null && _pointer.IsPresent ? _pointer.ScreenPosition : (UnityEngine.Vector2?)null);

        /// <summary>
        /// The arena floor point under the cursor right now, or null — the ground pick without the
        /// click.
        /// </summary>
        /// <remarks>
        /// <b>So the acting combatant can turn to face the cursor</b>, which is what the original
        /// does: <c>combat_actor_face_cursor</c> @0x5EBBB asks
        /// <c>combat_actor_direction_to_cursor</c> for a direction and replays the walk animation
        /// with it, so a combatant faces where the pointer is rather than where it last moved.
        ///
        /// <para>Runs the same <see cref="WorldPicker.PickGroundPoint"/> the click path runs, for the
        /// same reason <see cref="HoverCombatant"/> shares its pick: what the actor turns toward and
        /// what a click would land on cannot disagree.</para>
        ///
        /// <para><b>Deliberately NOT gated on the combatant pick.</b> The click path asks for the
        /// floor only after every object pick has missed, because a click means one thing; turning
        /// is not a click, and an actor should face a cursor resting on another combatant just as
        /// much as one resting on bare ground.</para>
        /// </remarks>
        public UnityEngine.Vector3? HoverGroundPoint() =>
            HoverScreenPoint() is UnityEngine.Vector2 p ? GroundPointAtScreenPoint(p) : null;

        /// <summary>Where a floor point appears on screen (Input System coords), or null.</summary>
        public UnityEngine.Vector2? ScreenPointOfGround(UnityEngine.Vector3 floorPoint) =>
            _camera == null ? null : WorldPicker.ScreenPointOfGround(_camera, _viewport, floorPoint,
                CanonicalStage.ScreenRect(ResolveStage(), out bool _));

        /// <summary>The arena floor point under a screen point (Input System coords), or null.</summary>
        public UnityEngine.Vector3? GroundPointAtScreenPoint(UnityEngine.Vector2 screenPoint) {
            if (_camera == null) {
                return null;
            }
            return WorldPicker.PickGroundPoint(_camera, _viewport, screenPoint,
                CanonicalStage.ScreenRect(ResolveStage(), out bool _));
        }

        public async UniTask HandleClick(bool isPrimary) {
            // CanPoint, not IsPresent: the click has already happened and carries its own position.
            // Asking whether a cursor was hovering refuses every touch (TASK-67).
            if (_camera == null || _pointer == null || !_pointer.CanPoint) {
                return;
            }
            Vector2 pointer = _pointer.ScreenPosition;
            Rect stageScreenRect = CanonicalStage.ScreenRect(ResolveStage(), out bool isFallback);
            if (isFallback && !_loggedStageFallback) {
                UnityEngine.Debug.LogWarning(
                    "WorldInteractionController: stage not resolved (missing or pre-layout); falling "
                    + "back to the canonical Contain box for world picking.");
                _loggedStageFallback = true;
            }
            // *** ASKED BEFORE THE WORLD PICK, NOT AFTER IT. *** A combatant on the arena stands
            // among the zone's scenery, so a click meant for an enemy can land on a bush behind it
            // — and during a fight the scenery is not what the player is aiming at. The seam is null
            // outside combat, so this costs one extra raycast per click and changes nothing else.
            UnityEngine.Vector3? arenaFloor = _pickCombatant == null || _combatantAtPoint == null
                ? null
                : WorldPicker.PickGroundPoint(_camera, _viewport, pointer, stageScreenRect);
            (int RosterSlot, bool PartyMember)? combatant =
                arenaFloor.HasValue ? _combatantAtPoint(arenaFloor.Value) : null;
            if (combatant != null) {
                // *** THE BUTTON IS PART OF THE CLICK. *** The two melee attacks are on the two
                // mouse buttons, so a combatant pick that dropped isPrimary made them
                // indistinguishable -- both arrived as the same event.
                _pickCombatant(combatant.Value.RosterSlot, combatant.Value.PartyMember, isPrimary);
                return;
            }

            WorldEntity entity = WorldPicker.Pick(_camera, _viewport, pointer, stageScreenRect);
            if (entity == null && _hintEncounter != null) {
                BakAgain.World.Encounters.EncounterGroupMember member =
                    WorldPicker.PickEncounterGroupMember(_camera, _viewport, pointer, stageScreenRect);
                if (member != null) {
                    // Out of DETECT range the original never entered it in the click table, so the
                    // click finds nothing (WORLDHIT.C:306).
                    Vector3 a = _camera.transform.position, b = member.transform.position;
                    float fine = Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z))
                        * BakCoordinateConverter.WorldScale;
                    if (member.ClickRange <= 0 || fine <= member.ClickRange) {
                        _hintEncounter(member, isPrimary);
                    }
                    return;
                }
            }
            if (entity == null) {
                // Nothing placed in the world is there — but a fight may have left a body, which is
                // addressed through the encounter rather than as a world object.
                BakAgain.World.Encounters.ArenaCorpse corpse =
                    WorldPicker.PickCorpse(_camera, _viewport, pointer, stageScreenRect);
                // *** STILL CALLED WITH NULL. *** The loot seam has always been told about a miss
                // and something may rely on being told; only the GROUND pick is gated, because a
                // click that found a body is not also a click on the floor.
                // The button travels with it: a secondary click on a body is refused (0x5f).
                _lootCorpse?.Invoke(corpse, isPrimary);
                if (corpse == null && _pickGround != null) {
                    UnityEngine.Vector3? floor =
                        WorldPicker.PickGroundPoint(_camera, _viewport, pointer, stageScreenRect);
                    if (floor.HasValue) {
                        _pickGround(floor.Value);
                    }
                }
                return;
            }
            if (entity.DetectionRange > 0f) {
                float dist = PlanarDistanceTo(entity);
                if (dist > entity.DetectionRange / BakCoordinateConverter.WorldScale) {
                    UnityEngine.Debug.Log($"WorldInteraction: '{entity.EntityName}' too far — {dist * BakCoordinateConverter.WorldScale:F0} > {entity.DetectionRange:F0} fine.");
                    return;
                }
            }
            if (string.IsNullOrEmpty(entity.Behavior)) {
                ReportMissingBehavior(entity);
                return;
            }
            if (!InProximity(entity)) {
                return; // profile-driven GlobalKey depth gate
            }
            await DispatchAsync(entity, isPrimary);
        }

        /// <summary>Route a picked entity to its behavior handler (testable seam; no camera/pick).</summary>
        public async UniTask DispatchAsync(WorldEntity entity, bool isPrimary) {
            if (entity == null || string.IsNullOrEmpty(entity.Behavior)) {
                return;
            }
            if (_handlers.TryGetValue(entity.Behavior, out IWorldInteractionHandler handler)) {
                await handler.HandleAsync(entity, isPrimary);
            } else {
                float d = PlanarDistanceTo(entity);
                UnityEngine.Debug.Log($"WorldInteraction: picked '{entity.EntityName}' behavior '{entity.Behavior}' — no handler registered (distance {d:F1}u / {d * BakCoordinateConverter.WorldScale:F0} fine).");
            }
        }

        /// <summary>
        /// Why a picked entity carries no behavior. There are two causes and they look identical
        /// on screen — nothing happens — so this separates them.
        ///
        /// <para><b>Expected:</b> the type has no <c>InteractionProfileTable</c> row (Building,
        /// Grave, the traversal types…). Nothing is wrong; the DOS default case is a no-op too.</para>
        ///
        /// <para><b>A defect:</b> the type DOES have a row, so <c>WorldEntityBuilder</c> stamped a
        /// behavior onto this entity when it built it — and the entity in front of us has lost it.
        /// <see cref="WorldEntity.Behavior"/> and <see cref="WorldEntity.Interaction"/> are
        /// <c>[NonSerialized]</c> runtime-only fields, so any scene-serialization round-trip
        /// (entering play mode with a zone left in the edit-mode scene, a scene save, a prefab
        /// extraction) yields an entity that renders, picks and reports its type correctly while
        /// being permanently inert. That is worth a warning: it cost an afternoon to diagnose
        /// once (TASK-82), and the only visible symptom was a world where nothing was clickable.</para>
        /// </summary>
        /// <remarks>Internal rather than private so the two branches can be pinned directly: the
        /// only other way in is <see cref="HandleClick"/>, which needs a camera, a pointer and a
        /// real physics pick to reach one line of diagnostics.</remarks>
        internal void ReportMissingBehavior(WorldEntity entity) {
            float d = PlanarDistanceTo(entity);
            string where = $"'{entity.EntityName}' ({entity.EntityType}) at distance "
                + $"{d:F1}u / {d * BakCoordinateConverter.WorldScale:F0} fine";

            if (ResourceExtraction.InteractionProfileTable.TryGet(entity.EntityType, out _, out _)) {
                UnityEngine.Debug.LogWarning(
                    $"WorldInteraction: picked {where} — this type HAS an interaction profile, so "
                    + "this entity was built without one. Behavior/Interaction are runtime-only "
                    + "([NonSerialized]) and do not survive a scene-serialization round-trip: the "
                    + "usual cause is a stale zone root left in the edit-mode scene, which entering "
                    + "play mode carries over stripped. Delete any leftover 'Zone_*' object and "
                    + "rebuild the zone.");
                return;
            }

            UnityEngine.Debug.Log(
                $"WorldInteraction: picked {where} — no interaction handler for this type yet.");
        }

        // Re-resolved until found, never captured once: this controller is built once per travel
        // session and handles every click in it, so a Find that ran before the stage existed would
        // pin picking to the fallback box permanently. See CanonicalStage.FindOrCached.
        private VisualElement ResolveStage() => _stage = CanonicalStage.FindOrCached(_stage, _panelRoot);

        // Profile-driven proximity gate. Range is in DOS fine units; convert to Unity units by
        // dividing by WorldScale (7000 fine == 70 Unity). Null range = no gate.
        private bool InProximity(WorldEntity entity) {
            GameData.Resources.Data.InteractionRange? range = entity.Interaction?.Range;
            if (range == null) {
                return true;
            }
            float thresholdFine = IsUnderground() ? range.Value.Underground : range.Value.Overground;
            float threshold = thresholdFine / BakCoordinateConverter.WorldScale;
            Vector3 a = _camera.transform.position;
            Vector3 b = entity.transform.position;
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz) <= threshold;
        }

        // The original's game mode 2 (WCURSOR.C:225-229), asked of the live zone on every click.
        private readonly System.Func<bool> _underground;
        private bool IsUnderground() => _underground?.Invoke() ?? false;

        // Planar (XZ) camera→entity distance, Unity units. -1 if no camera (test seam).
        private float PlanarDistanceTo(WorldEntity entity) {
            if (_camera == null || entity == null) {
                return -1f;
            }
            Vector3 a = _camera.transform.position, b = entity.transform.position;
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
