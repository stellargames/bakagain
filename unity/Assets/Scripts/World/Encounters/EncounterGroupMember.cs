namespace BakAgain.World.Encounters {
    using UnityEngine;

    /// <summary>
    /// A live encounter actor standing in the world, marked so a click can find its group —
    /// <c>wcursor_encounter_hint</c> (WCURSOR.C:83-90, :1332), TASK-795. A body gets
    /// <see cref="ArenaCorpse"/> instead.
    /// </summary>
    public sealed class EncounterGroupMember : MonoBehaviour {
        /// <summary>The encounter (combat record) the actor belongs to.</summary>
        public long EncounterNumber { get; set; } = -1;

        /// <summary>The actor's creature type, which "@1" names in the group's lines.</summary>
        public int CreatureNumber { get; set; }

        /// <summary>
        /// How near the party must be for a click to find it, in game units: DETECT.DAT's range for
        /// kind 0x10. The original only enters an encounter actor in the click table within it
        /// (WORLDHIT.C:306) — 16000 above ground, 10000 underground. 0 means no range is known.
        /// </summary>
        public int ClickRange { get; set; }
    }
}
