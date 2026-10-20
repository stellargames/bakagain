using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GameData.Mods;

/// <summary>The outcome of checking a mod zip or folder; <see cref="Manifest"/> is null when mod.json is missing or invalid.</summary>
public sealed record ModValidation(ModManifest? Manifest, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {
    public bool Ok => Errors.Count == 0;
}

/// <summary>Validates, installs and packs mod archives (mod system spec 4.3). A downloaded zip is untrusted input.</summary>
public static class ModArchive {
    // Zip caps: a downloaded archive is untrusted, and its headers can lie about sizes.
    internal const int MaxEntries = 20_000;
    internal const long MaxEntryBytes = 256L * 1024 * 1024;
    internal const long MaxTotalBytes = 1024L * 1024 * 1024;
    // mod.json and aliases.json are read into memory to validate: a few KiB in practice.
    internal const int MaxTextBytes = 1024 * 1024;

    internal readonly record struct ZipLimits(int Entries, long EntryBytes, long TotalBytes, int TextBytes = MaxTextBytes) {
        public static readonly ZipLimits Default = new(MaxEntries, MaxEntryBytes, MaxTotalBytes);
    }

    private static readonly string[] FixedFolders = { "SCX", "BMX", "Lang", "Resources", "ObjectInfo" };
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".json", ".po", ".ttf", ".otf", ".bdf", ".sf2" };
    private static readonly Regex SunVariant = new(@"^BMX/([^/]+)/(\d+)\.(front|left|right|back)\.png\z");
    private static readonly Regex DataFolder = new("^[A-Z0-9]+\\z");
    private static readonly Regex DriveLetter = new("^[A-Za-z]:");

    public static ModValidation ValidateZip(string zipPath) => ValidateZip(zipPath, ZipLimits.Default);

    internal static ModValidation ValidateZip(string zipPath, ZipLimits limits) {
        try {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            List<string> tooBig = CheckDeclaredSizes(zip.Entries.Select(e => (e.FullName, e.Length)).ToList(), limits);
            if (tooBig.Count > 0) return new ModValidation(null, tooBig, Array.Empty<string>());
            // Bounded by bytes read, not by the header: Unity's Mono inflates past the declared size.
            return Validate(zip.Entries.Select(e => e.FullName).ToList(), name => {
                using Stream s = zip.GetEntry(name)!.Open();
                return ReadCappedText(s, name, limits.TextBytes);
            });
        } catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) {
            return new ModValidation(null, new[] { "cannot read zip: " + e.Message }, Array.Empty<string>());
        }
    }

    // The sizes in the central directory: a fast refusal. Install counts what it really writes.
    internal static List<string> CheckDeclaredSizes(IReadOnlyList<(string Name, long Length)> entries, ZipLimits limits) {
        var errors = new List<string>();
        if (entries.Count > limits.Entries) errors.Add($"archive has {entries.Count} entries; the limit is {limits.Entries}");
        (string Name, long Length) big = entries.FirstOrDefault(e => e.Length > limits.EntryBytes);
        if (big.Name != null) errors.Add($"entry '{big.Name}' is {big.Length} bytes uncompressed; the limit is {limits.EntryBytes} per entry");
        // Stops once over the limit, so declared sizes near long.MaxValue cannot overflow the sum.
        long total = 0;
        foreach ((string _, long length) in entries) {
            total += Math.Max(0, Math.Min(length, limits.TotalBytes + 1));
            if (total > limits.TotalBytes) {
                errors.Add($"archive is at least {total} bytes uncompressed; the limit is {limits.TotalBytes}");
                break;
            }
        }
        return errors;
    }

    /// <summary>
    /// A text entry read whole, refused past <paramref name="cap"/> bytes: at most cap + 1 bytes are
    /// read, whatever the archive's header claims.
    /// </summary>
    internal static string ReadCappedText(Stream stream, string name, int cap) {
        var buffer = new byte[cap + 1];
        int read = 0, n;
        while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0) read += n;
        if (read > cap) throw new InvalidDataException($"{name} is larger than {cap} bytes");
        using var reader = new StreamReader(new MemoryStream(buffer, 0, read));
        return reader.ReadToEnd();
    }

    public static ModValidation ValidateFolder(string folder) {
        try {
            List<string> names = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/')).ToList();
            return Validate(names, name => {
                using FileStream s = File.OpenRead(Path.Combine(folder, name));
                return ReadCappedText(s, name, MaxTextBytes);
            });
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return new ModValidation(null, new[] { "cannot read folder: " + e.Message }, Array.Empty<string>());
        }
    }

    /// <summary>Extracts into <c>modsRoot/&lt;id&gt;</c>, replacing a previous version; a failure leaves the old version in place.</summary>
    public static InstalledMod Install(string zipPath, string modsRoot) => Install(zipPath, modsRoot, ZipLimits.Default);

    internal static InstalledMod Install(string zipPath, string modsRoot, ZipLimits limits) {
        ModValidation v = ValidateZip(zipPath, limits);
        if (!v.Ok) throw new InvalidDataException(string.Join("; ", v.Errors));
        return Extract(zipPath, v.Manifest!.Id, modsRoot, limits);
    }

    // Separate from Install so a test can reach the containment and counting checks without validation refusing first.
    internal static InstalledMod Extract(string zipPath, string id, string modsRoot, ZipLimits limits) {
        string finalDir = Path.Combine(modsRoot, id);
        string tmp = finalDir + ".tmp";
        string old = finalDir + ".old";
        DeleteIfExists(tmp);
        DeleteIfExists(old);
        try {
            Directory.CreateDirectory(tmp);
            string tmpFull = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                long total = 0;
                var buffer = new byte[81920];
                foreach (ZipArchiveEntry e in zip.Entries.Where(e => !e.FullName.EndsWith("/"))) {
                    string dest = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!dest.StartsWith(tmpFull, StringComparison.Ordinal)) throw new InvalidDataException("entry escapes the mod folder: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using Stream src = e.Open();
                    using FileStream dst = File.Create(dest);
                    long entryBytes = 0;
                    int n;
                    while ((n = src.Read(buffer, 0, buffer.Length)) > 0) {
                        entryBytes += n;
                        total += n;
                        if (entryBytes > limits.EntryBytes) throw new InvalidDataException($"entry '{e.FullName}' is larger than its header says; the limit is {limits.EntryBytes} bytes per entry");
                        if (total > limits.TotalBytes) throw new InvalidDataException($"archive is larger than its headers say; the limit is {limits.TotalBytes} bytes");
                        dst.Write(buffer, 0, n);
                    }
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
        } catch {
            // A cleanup that fails must not replace the error that stopped the install.
            try {
                DeleteIfExists(tmp);
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            }
            throw;
        }
        // The new version is in place. An .old that cannot be deleted now stays behind: the scan skips it, the
        // next install or an uninstall (DeleteInstalled) deletes it, and RecoverStrandedInstalls leaves it alone.
        try {
            DeleteIfExists(old);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
        }
        return ReadInstalled(finalDir) ?? throw new InvalidDataException("installed mod has no valid mod.json");
    }

    /// <summary>Writes <c>&lt;outDir&gt;/&lt;id&gt;-&lt;version&gt;.zip</c> and returns its path.</summary>
    public static string Pack(string folder, string outDir, ModValidation? validated = null) {
        ModValidation v = validated ?? ValidateFolder(folder);
        if (!v.Ok) throw new InvalidDataException(string.Join("; ", v.Errors));
        if (IsInside(folder, outDir)) throw new InvalidDataException($"output folder '{outDir}' is inside the mod folder; the zip would be packed into itself");
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

    private static bool IsInside(string folder, string dir) {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return target.StartsWith(root, comparison);
    }

    /// <summary>
    /// Run at startup before scanning: a crash between the two moves of an install can leave the previous
    /// version only as <c>&lt;id&gt;.old</c>, so put it back when <c>&lt;id&gt;</c> is gone and the id is
    /// still in <paramref name="listedIds"/> (<c>mods.json</c>): an unlisted id was uninstalled, and its
    /// <c>.old</c> is a leftover, not a stranded install. Returns the moves that failed, one line each; the
    /// others still happen.
    /// </summary>
    public static IReadOnlyList<string> RecoverStrandedInstalls(string modsRoot, IEnumerable<string> listedIds) {
        var failures = new List<string>();
        if (!Directory.Exists(modsRoot)) return failures;
        var listed = new HashSet<string>(listedIds);
        foreach (string old in Directory.GetDirectories(modsRoot, "*.old")) {
            string original = old.Substring(0, old.Length - ".old".Length);
            if (Directory.Exists(original) || !listed.Contains(Path.GetFileName(original))) continue;
            try {
                Directory.Move(old, original);
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                failures.Add($"{old} could not be restored: {e.Message}");
            }
        }
        return failures;
    }

    /// <summary>
    /// Uninstalls: deletes the mod's folder and any <c>.old</c> or <c>.tmp</c> an install left beside it.
    /// The leftovers go first, so a failure keeps the mod itself, never a stray previous version.
    /// </summary>
    public static void DeleteInstalled(string modFolder) {
        DeleteIfExists(modFolder + ".old");
        DeleteIfExists(modFolder + ".tmp");
        DeleteIfExists(modFolder);
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

    // An empty segment ("SCX//X.png") would pass the duplicate check as a different name; one trailing '/' marks a folder entry.
    private static bool UnsafePath(string name)
        => name.Contains('\\') || name.StartsWith("/") || DriveLetter.IsMatch(name)
            || (name.EndsWith("/") ? name.Substring(0, name.Length - 1) : name).Split('/').Any(s => s.Length == 0 || s == ".." || s == ".");

    private static ModValidation Validate(IReadOnlyList<string> allNames, Func<string, string> readText) {
        var errors = new List<string>();
        var warnings = new List<string>();
        // A text over the cap is an error of the mod, not an unreadable archive.
        string? Read(string name) {
            try {
                return readText(name);
            } catch (InvalidDataException e) {
                errors.Add(e.Message);
                return null;
            }
        }

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
        } else if (Read("mod.json") is { } manifestText) {
            manifest = ModManifest.Parse(manifestText, out IReadOnlyList<string> manifestErrors);
            errors.AddRange(manifestErrors.Select(e => "mod.json: " + e));
            if (manifest != null) warnings.AddRange(manifest.Warnings.Select(w => "mod.json: " + w));
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

        if (files.Contains("aliases.json") && Read("aliases.json") is { } aliasesText) {
            Dictionary<string, string>? aliases = ModAliases.Parse(aliasesText);
            if (aliases == null) {
                errors.Add("aliases.json must be an object of strings");
            } else {
                // A target is an image under SCX/ or BMX/ (a sun variant is not one), named without its extension.
                var withoutExt = new HashSet<string>(files
                    .Where(f => (f.StartsWith("SCX/") || f.StartsWith("BMX/")) && !SunVariant.IsMatch(f) && (f.EndsWith(".png") || f.EndsWith(".jpg")))
                    .Select(f => f.Substring(0, f.Length - Path.GetExtension(f).Length)));
                foreach (var a in aliases) {
                    if (UnsafePath(a.Key) || UnsafePath(a.Value)) errors.Add($"alias '{a.Key}' has an unsafe path");
                    else if (!withoutExt.Contains(a.Value)) errors.Add($"alias '{a.Key}' target is not an SCX/ or BMX/ image of this mod: {a.Value}");
                }
            }
        }

        if (manifest != null && manifest.IsLanguage && !files.Any(f => f.StartsWith("Lang/"))) {
            errors.Add("a language mod needs a Lang/ folder");
        }

        return new ModValidation(manifest, errors, warnings);
    }
}
