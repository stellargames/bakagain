namespace BakAgain.World.Interaction {
    using Cysharp.Threading.Tasks;

    /// <summary>One entry in the world-interaction dispatch (the Unity analog of a DOS
    /// HandleEnvironmentInteraction switch case). Keyed by the entity's semantic
    /// <see cref="WorldEntity.Behavior"/>.</summary>
    public interface IWorldInteractionHandler {
        string Behavior { get; }
        UniTask HandleAsync(WorldEntity entity, bool isPrimary);
    }
}
