using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameData.Mods;

public sealed record InstalledMod(ModManifest Manifest, string Folder);

public enum ModProblemKind { MissingDependency, DependencyVersion, Conflict, GameVersion }

/// <summary>Why a mod is not active. <see cref="Other"/> is what it names: the other mod's id, or the game range it needs.</summary>
public sealed record ModProblem(string ModId, ModProblemKind Kind, string Detail, string Other);

/// <summary>The mods in force for one mode. <see cref="Active"/> is listed highest priority first.</summary>
public sealed record ModResolution(IReadOnlyList<InstalledMod> Active, IReadOnlyList<InstalledMod> Languages, IReadOnlyList<ModProblem> Problems);

/// <summary>Works out which installed mods are active in a mode (mod system spec 4.3 to 4.6).</summary>
public static class ModResolver {
    public static ModResolution Resolve(IReadOnlyList<InstalledMod> installed, IReadOnlyList<ModStateEntry> state, ModMode mode, SemVersion? gameVersion) {
        var problems = new List<ModProblem>();
        var byId = new Dictionary<string, InstalledMod>();
        foreach (InstalledMod m in installed) {
            // First copy wins; the others are reported, under Conflict with the other copy's folder name.
            if (!byId.TryAdd(m.Manifest.Id, m)) {
                string other = Path.GetFileName(m.Folder);
                problems.Add(new ModProblem(m.Manifest.Id, ModProblemKind.Conflict, $"installed twice; '{other}' is ignored in favour of '{Path.GetFileName(byId[m.Manifest.Id].Folder)}'", other));
            }
        }
        var enabled = new Dictionary<string, bool>();
        var ordered = new List<InstalledMod>();
        foreach (ModStateEntry e in state) {
            if (enabled.ContainsKey(e.Id)) continue;
            enabled[e.Id] = e.Enabled;
            if (byId.TryGetValue(e.Id, out InstalledMod? m)) ordered.Add(m);
        }
        // Installed but not in the state file: enabled, at the top.
        ordered.InsertRange(0, byId.Values.Where(m => !enabled.ContainsKey(m.Manifest.Id)));

        var inMode = ordered.Where(m => m.Manifest.Modes.Contains(mode)).ToList();
        var languages = inMode.Where(m => m.Manifest.IsLanguage).ToList();
        var active = new List<InstalledMod>();
        foreach (InstalledMod m in inMode.Where(m => !m.Manifest.IsLanguage && enabled.GetValueOrDefault(m.Manifest.Id, true))) {
            if (gameVersion is SemVersion gv && m.Manifest.Game is { } range && !range.Satisfies(gv))
                problems.Add(new ModProblem(m.Manifest.Id, ModProblemKind.GameVersion, $"needs game {range}, this is {gv}", range.ToString()));
            else active.Add(m);
        }

        // Drop one violator at a time and look again, so exclusions cascade to dependents.
        while (FirstViolation(active) is var (victim, problem)) {
            active.Remove(victim);
            problems.Add(problem);
        }
        return new ModResolution(active, languages, problems);
    }

    private static (InstalledMod, ModProblem)? FirstViolation(List<InstalledMod> active) {
        foreach (InstalledMod m in active) {
            foreach (var (depId, range) in m.Manifest.Dependencies) {
                InstalledMod? dep = active.FirstOrDefault(a => a.Manifest.Id == depId);
                if (dep == null)
                    return (m, new ModProblem(m.Manifest.Id, ModProblemKind.MissingDependency, $"needs '{depId}', which is not active", depId));
                if (!range.Satisfies(dep.Manifest.Version))
                    return (m, new ModProblem(m.Manifest.Id, ModProblemKind.DependencyVersion, $"needs '{depId}' {range}, found {dep.Manifest.Version}", depId));
            }
        }
        for (int hi = 0; hi < active.Count; hi++) {
            for (int lo = hi + 1; lo < active.Count; lo++) {
                ModManifest a = active[hi].Manifest, b = active[lo].Manifest;
                if (a.Conflicts.Contains(b.Id) || b.Conflicts.Contains(a.Id))
                    return (active[lo], new ModProblem(b.Id, ModProblemKind.Conflict, $"conflicts with higher-priority '{a.Id}'", a.Id));
            }
        }
        return null;
    }
}
