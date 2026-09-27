namespace BakAgain.Core.Services {
    using Cysharp.Threading.Tasks;

    /// <summary>Writes the current game state to a SAVE##.GAM slot, interchangeable with the DOS game.</summary>
    public interface ISaveGameService {
        UniTask<bool> SaveAsync(string directoryName, int slotIndex, string saveName);
    }
}
