namespace GameData.Setup;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameData.Mods;
using ZipLimits = GameData.Mods.ModArchive.ZipLimits;

/// <param name="Error">Null, or a short reason for the UI to map to text: "corrupt-zip", "too-large" or "io";
/// from <see cref="GameFilesImport.FromGogInstaller"/> also "not-installer" (not a supported installer) or "corrupt".</param>
public sealed record ImportResult(GameFilesReport Report, string? Error);

/// <summary>Copies the game files from a zip, a folder or GOG's installer into the game folder. A zip or installer is untrusted: output names are always the fixed upper-case ones, never derived from an entry path.</summary>
public static class GameFilesImport {
    private const string ResourceConfig = "RESOURCE.CFG";

    private sealed class TooLargeException : Exception { }

    public static ImportResult FromZip(string zipPath, string destination) => FromZip(zipPath, destination, ZipLimits.Default);

    internal static ImportResult FromZip(string zipPath, string destination, ZipLimits limits) =>
        Run(destination, limits, () => {
            ZipArchive zip = ZipFile.OpenRead(zipPath);
            try {
                return (Pick(zip, limits), zip);
            } catch {
                zip.Dispose();
                throw;
            }
        });

    /// <summary><paramref name="folder"/> is the already-located game folder. Copying a folder onto itself only checks it.</summary>
    public static ImportResult FromFolder(string folder, string destination) {
        if (SameFolder(folder, destination)) {
            return new ImportResult(GameFilesCheck.Check(destination), null);
        }
        return Run(destination, ZipLimits.Default, () => {
            var files = new List<(string, Func<Stream>)>();
            foreach (string name in GameFolder.Required.Append(ResourceConfig)) {
                string? path = name == ResourceConfig ? GameFolder.FindResourceConfig(folder) : GameFolder.Find(folder, name);
                if (path != null) {
                    files.Add((name, () => File.OpenRead(path)));
                }
            }
            return (files, null);
        });
    }

    /// <summary>Extracts the game files from GOG's installer (see <see cref="GogInstaller"/>); RESOURCE.CFG comes from <c>{app}\__support\save</c>.</summary>
    /// <param name="progress">0..1 over the bytes of the files extracted.</param>
    public static ImportResult FromGogInstaller(string exePath, string destination, IProgress<double>? progress) {
        FileStream? exe = null;
        try {
            exe = File.OpenRead(exePath);
            GogInstaller? installer = GogInstaller.TryOpen(exe);
            if (installer == null) {
                return new ImportResult(GameFilesCheck.Check(destination), "not-installer");
            }
            // The output names are the fixed ones; an installer's own paths only select.
            var picked = new List<(string Name, InstallerFile File)>();
            foreach (string name in GameFolder.Required.Append(ResourceConfig)) {
                string wanted = name == ResourceConfig ? @"{app}\__support\save\" + name : @"{app}\" + name;
                InstallerFile? file = installer.Files.LastOrDefault(f => string.Equals(f.Destination, wanted, StringComparison.OrdinalIgnoreCase));
                if (file != null) {
                    picked.Add((name, file));
                }
            }
            double total = Math.Max(1, picked.Sum(p => (double)p.File.Size));
            long done = 0;
            var files = picked.Select(p => (p.Name, (Func<Stream>)(() => {
                var bytes = new MemoryStream();
                long before = done;
                installer.Extract(p.File, bytes, progress == null ? null : new Progress(n => progress.Report((before + n) / total)));
                done += p.File.Size;
                bytes.Position = 0;
                return bytes;
            }))).ToList();
            return Run(destination, ZipLimits.Default, () => (files, null), "corrupt");
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return new ImportResult(GameFilesCheck.Check(destination), "io");
        } finally {
            exe?.Dispose();
        }
    }

    private sealed class Progress : IProgress<long> {
        private readonly Action<long> _report;
        public Progress(Action<long> report) => _report = report;
        public void Report(long value) => _report(value);
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.Ordinal);

    private static ImportResult Run(string destination, ZipLimits limits, Func<(List<(string Name, Func<Stream> Open)>, ZipArchive?)> select, string corrupt = "corrupt-zip") {
        string? error = null;
        var parts = new List<(string Part, string Final)>();
        try {
            (List<(string Name, Func<Stream> Open)> files, ZipArchive? zip) = select();
            using (zip) {
                Directory.CreateDirectory(destination);
                long total = 0;
                foreach ((string name, Func<Stream> open) in files) {
                    string final = Path.Combine(destination, name);
                    parts.Add((final + ".part", final));
                    using Stream src = open();
                    using FileStream dst = File.Create(final + ".part");
                    CopyCapped(src, dst, limits, ref total);
                }
            }
            // All copied before any is replaced, so a failure keeps the old files.
            foreach ((string part, string final) in parts) {
                File.Delete(final);
                File.Move(part, final);
            }
        } catch (InvalidDataException) {
            error = corrupt;
        } catch (TooLargeException) {
            error = "too-large";
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            error = "io";
        } finally {
            foreach ((string part, string _) in parts) {
                try { File.Delete(part); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return new ImportResult(GameFilesCheck.Check(destination), error);
    }

    // Counts the bytes really read: a header can claim less than the stream inflates to.
    private static void CopyCapped(Stream src, Stream dst, ZipLimits limits, ref long total) {
        var buffer = new byte[81920];
        long entry = 0;
        int n;
        while ((n = src.Read(buffer, 0, buffer.Length)) > 0) {
            entry += n;
            total += n;
            if (entry > limits.EntryBytes || total > limits.TotalBytes) {
                throw new TooLargeException();
            }
            dst.Write(buffer, 0, n);
        }
    }

    // The shallowest folder of the zip holding all three files; RESOURCE.CFG from it, else from its __support/save.
    private static List<(string Name, Func<Stream> Open)> Pick(ZipArchive zip, ZipLimits limits) {
        if (zip.Entries.Count > limits.Entries) {
            throw new TooLargeException();
        }
        var groups = new Dictionary<string, Dictionary<string, ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry e in zip.Entries) {
            string path = e.FullName.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            string file = path.Substring(slash + 1);
            string dir = slash < 0 ? "" : path.Substring(0, slash).Trim('/');
            if (file.Length == 0) {
                continue;
            }
            if (!groups.TryGetValue(dir, out var g)) {
                groups[dir] = g = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            }
            g.TryAdd(file, e);
        }
        string? best = groups.Keys
            .Where(d => GameFolder.Required.All(groups[d].ContainsKey))
            .OrderBy(d => d.Length == 0 ? 0 : d.Count(c => c == '/') + 1)
            .ThenBy(d => d, StringComparer.Ordinal)
            .FirstOrDefault();
        var result = new List<(string, Func<Stream>)>();
        if (best == null) {
            return result;
        }
        foreach (string name in GameFolder.Required) {
            result.Add((name, groups[best][name].Open));
        }
        string support = best.Length == 0 ? "__support/save" : best + "/__support/save";
        if (groups[best].TryGetValue(ResourceConfig, out var cfg) || (groups.TryGetValue(support, out var sg) && sg.TryGetValue(ResourceConfig, out cfg))) {
            result.Add((ResourceConfig, cfg.Open));
        }
        return result;
    }
}
