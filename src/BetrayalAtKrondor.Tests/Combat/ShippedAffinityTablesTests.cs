namespace BetrayalAtKrondor.Tests.Combat;

using BetrayalAtKrondor.Tests.Content;
using GameData.Resources.Combat;
using ResourceExtraction.Extractors.Exe;
using System.IO;
using System.Text.Json;
using Xunit;

/// <summary>
/// The combat affinity tables ship with the game (TASK-1143) instead of being read from the
/// player's KRONDOR.EXE, which differs between releases (forum p89). These keep the shipped copy
/// equal to the 1.02 CD build's.
/// </summary>
public class ShippedAffinityTablesTests {
    private static string Shape(CombatAffinityTables t) => JsonSerializer.Serialize(new {
        t.ClassGroupModifier, t.ClassCombatGroup, t.Creatures, t.StatCheckThresholds, t.AiFleeThresholds,
    });

    [Fact]
    public void TheShippedTablesAreNotEmpty() {
        CombatAffinityTables t = CombatAffinityTables.CreateShipped();
        Assert.Equal(CombatAffinityTables.ResourceId, t.Id);
        Assert.Equal(new[] { 0, -1, -1, -2 }, t.ClassGroupModifier[0]);
        Assert.Equal(CombatAffinityTables.CreatureClassCount, t.Creatures.Count);
        Assert.NotEmpty(t.AiFleeThresholds);
        Assert.NotEmpty(t.StatCheckThresholds);
    }

    [Fact]
    public void TheShippedTablesEqualTheCommittedGeneratedJson() {
        string? generated = GeneratedCorpus.FindDir(Path.Combine("EXE", "combat-affinity.json"));
        if (generated == null) {
            return;
        }
        CombatAffinityTables onDisk = CombatAffinityTables.FromJson(
            File.ReadAllText(Path.Combine(generated, "EXE", "combat-affinity.json")));
        Assert.True(Shape(onDisk) == Shape(CombatAffinityTables.CreateShipped()),
            "GameData/Content/combat-affinity.json differs from generated/EXE/combat-affinity.json: "
            + "copy the generated file over it and rebuild the Unity plugin DLLs (-c Unity).");
    }

    [Fact]
    public void TheShippedTablesEqualA102Extraction() {
        string? exe = FindOriginalExe();
        if (exe == null) {
            return;
        }
        CombatAffinityTables read = CombatAffinityReader.Read(File.ReadAllBytes(exe));
        Assert.Equal(Shape(read), Shape(CombatAffinityTables.CreateShipped()));
    }

    private static string? FindOriginalExe() {
        string? dir = System.AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            string candidate = Path.Combine(dir, "OriginalGame", "KRONDOR.EXE");
            if (File.Exists(candidate)) {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
