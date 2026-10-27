namespace GameData.Setup;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameData.Mods;
using ZipLimits = GameData.Mods.ModArchive.ZipLimits;

/// <param name="Error">Null, or a short reason for the UI to map to text: "corrupt-zip", "too-large" or "io".</param>
public sealed record ImportResult(GameFilesReport Report, string? Error);

/// <summary>Copies the game files from a zip or a folder into the game folder. A zip is untrusted: output names are always the fixed upper-case ones, never derived from an entry path.</summary>
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

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.Ordinal);

    private static ImportResult Run(string destination, ZipLimits limits, Func<(List<(string Name, Func<Stream> Open)>, ZipArchive?)> select) {
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
            error = "corrupt-zip";
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
