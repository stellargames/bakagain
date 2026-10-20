using GameData.Mods;

const string Usage = "usage:\n  bakmod pack <folder> [-o <outDir>]\n  bakmod validate <zip>";

switch (args) {
    case ["pack", var folder]:
        return Pack(folder, ".");
    case ["pack", var folder, "-o", var outDir]:
        return Pack(folder, outDir);
    case ["validate", var zip]:
        return Report(ModArchive.ValidateZip(zip), zip);
    default:
        Console.Error.WriteLine(Usage);
        return 2;
}

static int Pack(string folder, string outDir) {
    int rc = Report(ModArchive.ValidateFolder(folder), folder);
    if (rc != 0) return rc;
    try {
        Console.WriteLine(ModArchive.Pack(folder, outDir));
        return 0;
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
        Console.Error.WriteLine("error: " + e.Message);
        return 1;
    }
}

static int Report(ModValidation v, string what) {
    foreach (string w in v.Warnings) Console.Error.WriteLine("warning: " + w);
    foreach (string e in v.Errors) Console.Error.WriteLine("error: " + e);
    if (v.Ok) Console.WriteLine($"{what}: OK ({v.Manifest!.Id} {v.Manifest.Version})");
    return v.Ok ? 0 : 1;
}
