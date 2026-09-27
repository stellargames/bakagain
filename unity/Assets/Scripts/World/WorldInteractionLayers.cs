namespace BakAgain.World {
    /// <summary>
    /// Names/values shared by the world-interaction (click-picking) system. The layer is a
    /// dedicated raycast target so world-object picking never hits terrain/UI. EntityType values
    /// are the DOS entityDatHeader+1 interactable classes (KRONDOR.EXE HandleEnvironmentInteraction
    /// switch); see <see cref="GameData.Resources.World.WorldEntityType"/> (Corpse = 16, handle_Corpse @0x76a0a).
    /// </summary>
    public static class WorldInteractionLayers {
        public const string WorldInteractableLayerName = "WorldInteractable";
    }
}
