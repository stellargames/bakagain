namespace BetrayalAtKrondor.Tests.Data;
using GameData.Resources.Data;
using ResourceExtraction;
using System;
using Xunit;

public class ContainerGeometryTests {
    // TASK-1102: the lock sub-record (flags, difficulty, puzzle chest, trap damage) comes first
    // after the items, and the writer patches it so a spent trap stays spent.
    [Fact] public void LockOffset_IsRightAfterTheItems_OrMinusOneWithoutALock() {
        Assert.Equal(16 + 4 * 4, ContainerGeometry.LockOffset(4, SaveGameContainerDataType.Lock));
        Assert.Equal(-1, ContainerGeometry.LockOffset(4, SaveGameContainerDataType.Dialog));
    }

    [Fact] public void PackLock_WritesTheFourBytesInTheOrderTheExtractorReads() =>
        Assert.Equal(new byte[] { 1, 2, 3, 4 },
            ContainerGeometry.PackLock(new SaveGameContainerLockData(1, 2, 3, 4)));

    [Fact] public void SerializedSize_HeaderPlusItems_NoBlocks() =>
        Assert.Equal(16 + 4 * 4, ContainerGeometry.SerializedSize(4, (SaveGameContainerDataType)0)); // cap 4, no flags

    [Fact] public void SerializedSize_AddsGatedBlocks() {
        // Timestamp (0x10, 4B) + Dialog (0x02, 6B)
        var dt = (SaveGameContainerDataType)(0x10 | 0x02);
        Assert.Equal(16 + 4 * 4 + 4 + 6, ContainerGeometry.SerializedSize(4, dt));
    }

    [Fact] public void ContainerBodyOffset_SumsPrecedingSizes() {
        var zc = new[] {
            Cont(2, (SaveGameContainerDataType)0),      // size 16+8 = 24
            Cont(4, (SaveGameContainerDataType)0x10),   // size 16+16+4 = 36
        };
        // zoneLocalOffset 100, index 1 => 239505 + 100 + 2 + 24 = 239631
        Assert.Equal(239505 + 100 + 2 + 24, ContainerGeometry.ContainerBodyOffset(100, zc, 1));
    }

    private static SaveGameContainerData Cont(int cap, SaveGameContainerDataType dt) =>
        new SaveGameContainerData(new SaveGameContainerLocationData(0,0,0,0,0,0,0),
            (SaveGameContainerType)0, 0, (byte)cap, dt, Array.Empty<SaveGameInventoryItemData>(),
            null, null, null, null, null, null);
}
