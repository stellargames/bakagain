namespace BakAgain.World.Encounters {
    using UnityEngine;

    /// <summary>
    /// Marks an arena sprite as a combatant that is still in the fight, so a click during target
    /// selection can find it.
    /// </summary>
    /// <remarks>
    /// <b>The sibling of <see cref="ArenaCorpse"/>, and deliberately not the same component.</b> A
    /// body is addressed as a container to loot; a live combatant is addressed as a target to shoot
    /// or cast at. They are picked off the same layer by the same ray and share nothing else — the
    /// same line <c>ArenaCorpse</c> already draws against <c>WorldEntity</c>.
    ///
    /// <para><b>Living actors used to carry no collider at all</b>, on the reasoning that they were
    /// not interactable. That was true only while nothing could aim at them: with
    /// <see cref="GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection"/>
    /// armed, clicking a standing enemy IS the interaction.</para>
    /// </remarks>
    public sealed class ArenaCombatant : MonoBehaviour {
        /// <summary>
        /// The combatant's index in its own side's list — the encounter's roster for an enemy, the
        /// marching order for a party member.
        /// </summary>
        /// <remarks>Which list is decided by <see cref="PartyMember"/>; the number alone is
        /// ambiguous between the two.</remarks>
        public int RosterSlot { get; set; } = -1;

        /// <summary>Whether <see cref="RosterSlot"/> indexes the party rather than the enemies.</summary>
        public bool PartyMember { get; set; }

        /// <summary>The quad this sprite was built from — reused by a swing's frames.</summary>
        public GameData.Resources.World.SpriteBMeshFace Face { get; set; }

        /// <summary>The model's extent, for sizing each swing frame's billboard.</summary>
        public int Extent { get; set; }
    }
}
