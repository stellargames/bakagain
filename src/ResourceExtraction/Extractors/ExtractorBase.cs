namespace ResourceExtraction.Extractors;

using GameData.Resources;
using ResourceExtraction.Compression;
using ResourceExtraction.Extensions;
using ResourceExtractor.Compression;
using System;
using System.Collections.Generic;
using System.IO;

public abstract class ExtractorBase<T> where T : IResource {
    /// <summary>The field-by-field read trace, written to the console. Opt-in (BAK_EXTRACT_TRACE=1):
    /// Unity sends the console into Player.log, which it grew to 7 MB in a few days (TASK-1127).</summary>
    internal static readonly bool Debug = Environment.GetEnvironmentVariable("BAK_EXTRACT_TRACE") == "1";
    protected static string Indent = string.Empty;

    protected static void Log(string message) {
        if (Debug)
            Console.WriteLine(Indent + message);
    }

    /// <summary>Reads one compressed block — see <see cref="CompressedBlock"/>.</summary>
    protected static byte[] DecompressToByteArray(BinaryReader resourceReader, long length = 0) =>
        CompressedBlock.Read(resourceReader, length);

    public abstract T Extract(string id, Stream resourceStream);

    protected static string ReadAlignedString(BinaryReader reader) {
        string text = reader.ReadZeroTerminatedString();
        if ((text.Length & 1) == 0) {
            reader.ReadByte();
        }

        return text;
    }
}