namespace BakAgain.World.Encounters {
    using UnityEngine;

    /// <summary>
    /// Marks an arena sprite as a body that can be looted, and carries the one number needed to
    /// find its container.
    /// </summary>
    /// <remarks>
    /// <b>The roster slot, not the actor-table slot.</b> A corpse's container is keyed
    /// <c>(zone 100, x = the enemy's index in the encounter's roster, y = the encounter number)</c>
    /// — measured live: encounter 5 has containers at x = 0 and x = 1 for its two enemies, holding
    /// three and five items. The 1730-entry actor slot the fight was built from is a different
    /// number and finds nothing.
    ///
    /// <para>A component rather than a name parse: the sprite's GameObject name is for humans
    /// reading the hierarchy, and deriving behaviour from it would break the first time someone
    /// renames it.</para>
    /// </remarks>
    public sealed class ArenaCorpse : MonoBehaviour {
        /// <summary>The enemy's index in the encounter's roster, 0..6.</summary>
        public int RosterSlot { get; set; } = -1;

        /// <summary>The encounter the body belongs to, or -1 for one drawn in the arena.</summary>
        public long EncounterNumber { get; set; } = -1;

        /// <summary>Whether <see cref="RosterSlot"/> indexes the party rather than the roster.</summary>
        /// <remarks>
        /// Added for the same reason <c>ArenaCombatant</c> carries it: a party member who falls is
        /// drawn as a body too, and slot 0 then names two different combatants. The loot path never
        /// needed the distinction because only enemies have containers; the death collapse does,
        /// because it has to find the combatant to know whether it has already played.
        /// </remarks>
        public bool PartyMember { get; set; }

        /// <summary>The bitmap index this body is drawn from — the death run's LAST frame.</summary>
        /// <remarks>
        /// The collapse counts back three from here. -1 when the sprite was not built from a
        /// creature set, in which case there is nothing to play.
        /// </remarks>
        public int CollapseLastFrame { get; set; } = -1;

        /// <summary>The creature's sprite set, for loading the three frames before the last.</summary>
        public string SetName { get; set; }

        /// <summary>The quad this body was built from — reused for each collapse frame.</summary>
        public GameData.Resources.World.SpriteBMeshFace Face { get; set; }

        /// <summary>The model's extent, for sizing each frame's billboard.</summary>
        public int Extent { get; set; }
    }
}
