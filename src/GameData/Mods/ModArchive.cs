using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameData.Mods;

/// <summary>The outcome of checking a mod zip or folder; <see cref="Manifest"/> is null when mod.json is missing or invalid.</summary>
public sealed record ModValidation(ModManifest? Manifest, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {
    public bool Ok => Errors.Count == 0;
}

/// <summary>Validates, installs and packs mod archives (mod system spec 4.3). A downloaded zip is untrusted input.</summary>
public static class ModArchive {
    private static readonly string[] FixedFolders = { "SCX", "BMX", "Lang", "Resources", "ObjectInfo" };
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".json", ".po", ".ttf", ".otf", ".sf2" };
    private static readonly Regex SunVariant = new(@"^BMX/([^/]+)/(\d+)\.(front|left|right|back)\.png\z");
    private static readonly Regex DataFolder = new("^[A-Z0-9]+\\z");
    private static readonly Regex DriveLetter = new("^[A-Za-z]:");

    public static ModValidation ValidateZip(string zipPath) {
        try {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            return Validate(zip.Entries.Select(e => e.FullName).ToList(), name => {
                using var r = new StreamReader(zip.GetEntry(name)!.Open());
                return r.ReadToEnd();
            });
        } catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) {
            return new ModValidation(null, new[] { "cannot read zip: " + e.Message }, Array.Empty<string>());
        }
    }

    public static ModValidation ValidateFolder(string folder) {
        try {
            List<string> names = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/')).ToList();
            return Validate(names, name => File.ReadAllText(Path.Combine(folder, name)));
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return new ModValidation(null, new[] { "cannot read folder: " + e.Message }, Array.Empty<string>());
        }
    }

    /// <summary>Extracts into <c>modsRoot/&lt;id&gt;</c>, replacing a previous version; a failure leaves the old version in place.</summary>
    public static InstalledMod Install(string zipPath, string modsRoot) {
        ModValidation v = ValidateZip(zipPath);
        if (!v.Ok) throw new InvalidDataException(string.Join("; ", v.Errors));
        string id = v.Manifest!.Id;
        string finalDir = Path.Combine(modsRoot, id);
        string tmp = finalDir + ".tmp";
        string old = finalDir + ".old";
        DeleteIfExists(tmp);
        DeleteIfExists(old);
        try {
            Directory.CreateDirectory(tmp);
            string tmpFull = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry e in zip.Entries.Where(e => !e.FullName.EndsWith("/"))) {
                    string dest = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!dest.StartsWith(tmpFull, StringComparison.Ordinal)) throw new InvalidDataException("entry escapes the mod folder: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using Stream src = e.Open();
                    using FileStream dst = File.Create(dest);
                    src.CopyTo(dst);
                }
            }
            if (ReadInstalled(tmp)?.Manifest.Id != id) throw new InvalidDataException("extracted mod.json does not match the validated manifest");
            if (Directory.Exists(finalDir)) Directory.Move(finalDir, old);
            try {
                Directory.Move(tmp, finalDir);
            } catch {
                if (Directory.Exists(old) && !Directory.Exists(finalDir)) Directory.Move(old, finalDir);
                throw;
            }
            DeleteIfExists(old);
        } catch {
            DeleteIfExists(tmp);
            throw;
        }
        return ReadInstalled(finalDir) ?? throw new InvalidDataException("installed mod has no valid mod.json");
    }

    /// <summary>Writes <c>&lt;outDir&gt;/&lt;id&gt;-&lt;version&gt;.zip</c> and returns its path.</summary>
    public static string Pack(string folder, string outDir) {
        ModValidation v = ValidateFolder(folder);
        if (!v.Ok) throw new InvalidDataException(string.Join("; ", v.Errors));
        // Listed before the zip exists, so packing into the mod's own folder cannot include or lock the output.
        List<string> files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        Directory.CreateDirectory(outDir);
        string zipPath = Path.Combine(outDir, $"{v.Manifest!.Id}-{v.Manifest.Version}.zip");
        File.Delete(zipPath);
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string file in files) {
            // Entry names are built by hand so they use '/' on every platform.
            zip.CreateEntryFromFile(file, Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'));
        }
        return zipPath;
    }

    /// <summary>Null when <c>mod.json</c> is missing or invalid.</summary>
    public static InstalledMod? ReadInstalled(string modFolder) {
        string path = Path.Combine(modFolder, "mod.json");
        if (!File.Exists(path)) return null;
        ModManifest? m = ModManifest.Parse(File.ReadAllText(path), out _);
        return m == null ? null : new InstalledMod(m, modFolder);
    }

    private static void DeleteIfExists(string dir) {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static bool UnsafePath(string name)
        => name.Contains('\\') || name.StartsWith("/") || DriveLetter.IsMatch(name) || name.Split('/').Any(s => s == ".." || s == ".");

    private static ModValidation Validate(IReadOnlyList<string> allNames, Func<string, string> readText) {
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (string n in allNames.Where(UnsafePath)) errors.Add("unsafe path: " + n);
        // Duplicates would let validation read one entry while extraction keeps another.
        foreach (var g in allNames.GroupBy(n => n.ToLowerInvariant()).Where(g => g.Count() > 1)) errors.Add("duplicate entry: " + g.First());
        List<string> files = allNames.Where(n => !n.EndsWith("/") && !UnsafePath(n)).ToList();

        foreach (string n in files) {
            string[] parts = n.Split('/');
            string top = parts[0];
            bool allowedTop = parts.Length == 1
                ? top == "mod.json" || top == "aliases.json"
                : FixedFolders.Contains(top) || DataFolder.IsMatch(top);
            if (!allowedTop) errors.Add("not allowed at this location: " + n);
            else if (!AllowedExtensions.Contains(Path.GetExtension(n).ToLowerInvariant())) errors.Add("file type not allowed: " + n);
        }

        ModManifest? manifest = null;
        if (!files.Contains("mod.json")) {
            errors.Add("mod.json is missing at the root");
        } else {
            manifest = ModManifest.Parse(readText("mod.json"), out IReadOnlyList<string> manifestErrors);
            errors.AddRange(manifestErrors.Select(e => "mod.json: " + e));
        }

        var present = new HashSet<string>(files);
        foreach (string n in files) {
            Match m = SunVariant.Match(n);
            if (m.Success && !present.Contains($"BMX/{m.Groups[1].Value}/{m.Groups[2].Value}.png")) {
                errors.Add($"sun variant has no {m.Groups[2].Value}.png next to it: {n}");
            }
        }
        if (manifest != null && manifest.Modes.Contains(ModMode.Classic) && files.Any(n => SunVariant.IsMatch(n))) {
            warnings.Add("sun variants only apply in Enhanced; this mod also lists classic");
        }

        if (files.Contains("aliases.json")) {
            Dictionary<string, string>? aliases = ModAliases.Parse(readText("aliases.json"));
            if (aliases == null) {
                errors.Add("aliases.json must be an object of strings");
            } else {
                var withoutExt = new HashSet<string>(files.Select(f => f.Substring(0, f.Length - Path.GetExtension(f).Length)));
                foreach (var a in aliases) {
                    if (UnsafePath(a.Key) || UnsafePath(a.Value)) errors.Add($"alias '{a.Key}' has an unsafe path");
                    else if (!withoutExt.Contains(a.Value)) errors.Add($"alias '{a.Key}' target does not exist: {a.Value}");
                }
            }
        }

        if (manifest != null && manifest.IsLanguage && !files.Any(f => f.StartsWith("Lang/"))) {
            errors.Add("a language mod needs a Lang/ folder");
        }

        return new ModValidation(manifest, errors, warnings);
    }
}
