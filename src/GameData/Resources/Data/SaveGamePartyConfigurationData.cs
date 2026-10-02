namespace GameData.Resources.Data;

using System;

public class SaveGamePartyConfigurationData {
    public SaveGamePartyConfigurationData(
        byte numberOfActivePartyCharacters,
        byte[] activePartyCharacters,
        uint sharedInventoryPointer1,
        uint sharedInventoryPointer2,
        byte attributeIncreasedFlag,
        short rewardMoneyCounter,
        short[] initialAttributeGainModifiers,
        SaveGameActorStatusEffectsData[] actorStatusEffects
    ) {
        NumberOfActivePartyCharacters = numberOfActivePartyCharacters;
        ActivePartyCharacters = activePartyCharacters ?? Array.Empty<byte>();
        SharedInventoryPointer1 = sharedInventoryPointer1;
        SharedInventoryPointer2 = sharedInventoryPointer2;
        AttributeIncreasedFlag = attributeIncreasedFlag;
        RewardMoneyCounter = rewardMoneyCounter;
        InitialAttributeGainModifiers = initialAttributeGainModifiers ?? Array.Empty<short>();
        ActorStatusEffects = actorStatusEffects ?? Array.Empty<SaveGameActorStatusEffectsData>();
    }

    public byte NumberOfActivePartyCharacters { get; }
    public byte[] ActivePartyCharacters { get; }
    public uint SharedInventoryPointer1 { get; }
    public uint SharedInventoryPointer2 { get; }
    public byte AttributeIncreasedFlag { get; }
    public short RewardMoneyCounter { get; }
    /// <summary>
    /// Six int16 — canassa's <c>aSkillTrainRate</c>: each member's study rate, <c>26 / skills
    /// marked</c>, rewritten at boot and by the character screen (CHARSCRN.C:366, misnamed
    /// <c>charscreen_recalc_condition_tick</c>). The port derives the same value from the flags —
    /// <see cref="Character.SkillEmphasis.TrainRate"/> — so nothing reads this copy.
    /// </summary>
    /// <remarks>
    /// It held TWO entries and a stray padding byte until 2026-08-24, which left the whole rest of
    /// this section — the condition ranks above all — seven bytes early. See TASK-203.
    /// </remarks>
    public short[] InitialAttributeGainModifiers { get; }
    public SaveGameActorStatusEffectsData[] ActorStatusEffects { get; }
}
