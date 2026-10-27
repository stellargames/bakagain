namespace GameData.Setup;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public enum GameFileState { Ok, Missing, Empty, Damaged }

public sealed record GameFilesReport(IReadOnlyDictionary<string, GameFileState> Files) {
    public bool AllOk => Files.Values.All(s => s == GameFileState.Ok);

    /// <summary>Names of the files that are not Ok, in <see cref="GameFolder.Required"/> order.</summary>
    public IEnumerable<string> Problems => GameFolder.Required.Where(n => Files[n] != GameFileState.Ok);
}

/// <summary>Cheap damage check on the three required files. No fixed sizes: releases differ.</summary>
public static class GameFilesCheck {
    private const int NameLength = 13;
    private const int HeaderLength = NameLength + sizeof(uint);

    public static GameFilesReport Check(string directory) {
        var files = new Dictionary<string, GameFileState>();
        foreach (string name in GameFolder.Required) {
            files[name] = StateOf(name, GameFolder.Find(directory, name));
        }
        return new GameFilesReport(files);
    }

    private static GameFileState StateOf(string name, string? path) {
        if (path == null) {
            return GameFileState.Missing;
        }
        try {
            using FileStream f = File.OpenRead(path);
            if (f.Length == 0) {
                return GameFileState.Empty;
            }
            return name != "KRONDOR.001" || ArchiveIsIntact(f) ? GameFileState.Ok : GameFileState.Damaged;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return GameFileState.Damaged;
        }
    }

    /// <summary>True when the entry headers (13-byte name, uint size) chain exactly to the end of the file. Reads headers only.</summary>
    public static bool ArchiveIsIntact(Stream krondor001) {
        long length = krondor001.Length;
        long pos = 0;
        byte[] header = new byte[HeaderLength];
        while (pos < length) {
            if (length - pos < HeaderLength) {
                return false;
            }
            krondor001.Position = pos;
            if (krondor001.Read(header, 0, HeaderLength) < HeaderLength) {
                return false;
            }
            pos += HeaderLength + (long)System.BitConverter.ToUInt32(header, NameLength);
        }
        return pos == length;
    }
}
