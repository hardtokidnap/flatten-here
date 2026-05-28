namespace FlattenHere;

internal readonly record struct GuardResult(bool Allowed, string? Reason)
{
    public static GuardResult Allow() => new(true, null);
    public static GuardResult Block(string reason) => new(false, reason);
}

internal sealed record GuardEntry(string Path, string Label, bool BlockSubfolders);

internal static class Guards
{
    public static GuardResult Check(string targetIn)
        => Check(targetIn, BuildDefaultGuards(), DefaultSystemDrive());

    internal static GuardResult Check(string targetIn, IReadOnlyList<GuardEntry> guards, string systemDrive)
    {
        if (string.IsNullOrWhiteSpace(targetIn))
            return GuardResult.Block("Empty target path.");

        string target;
        try
        {
            target = Normalize(targetIn);
        }
        catch (Exception ex)
        {
            return GuardResult.Block($"Invalid path: {ex.Message}");
        }
        if (string.IsNullOrEmpty(target))
            return GuardResult.Block("Empty target path.");

        if (IsDriveRoot(target))
            return GuardResult.Block($"Refusing to flatten the root of a drive ({target}\\).");

        foreach (var g in guards)
        {
            if (string.IsNullOrEmpty(g.Path)) continue;
            if (PathEquals(target, g.Path))
                return GuardResult.Block($"Refusing to flatten {g.Label}.");
            if (g.BlockSubfolders && IsStrictlyUnder(target, g.Path))
                return GuardResult.Block($"Refusing to flatten under {g.Label}.");
        }

        if (!string.IsNullOrEmpty(systemDrive)
            && target.StartsWith(systemDrive + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var rest = target[(systemDrive.Length + 1)..];
            var segs = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length < 2)
                return GuardResult.Block(
                    $"Refusing to flatten a top-level system-drive folder ({target}). Pick a deeper folder.");
        }

        return GuardResult.Allow();
    }

    internal static IReadOnlyList<GuardEntry> BuildDefaultGuards()
    {
        var list = new List<GuardEntry>();
        void AddEnv(string env, string label, bool blockSub)
        {
            var v = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(v))
                list.Add(new GuardEntry(Normalize(v), label, blockSub));
        }

        AddEnv("SystemRoot",       "the Windows directory",   blockSub: true);
        AddEnv("ProgramFiles",     "Program Files",           blockSub: true);
        AddEnv("ProgramFiles(x86)","Program Files (x86)",     blockSub: true);
        AddEnv("ProgramData",      "ProgramData",             blockSub: true);
        AddEnv("UserProfile",      "your user profile root",  blockSub: false);
        AddEnv("AppData",          "the Roaming AppData root",blockSub: false);
        AddEnv("LocalAppData",     "the Local AppData root",  blockSub: false);

        var sysDrive = DefaultSystemDrive();
        if (!string.IsNullOrEmpty(sysDrive))
        {
            list.Add(new GuardEntry(Normalize($"{sysDrive}\\$Recycle.Bin"),             "the Recycle Bin",           BlockSubfolders: true));
            list.Add(new GuardEntry(Normalize($"{sysDrive}\\System Volume Information"), "System Volume Information", BlockSubfolders: true));
        }
        return list;
    }

    internal static string DefaultSystemDrive()
        => Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

    internal static string Normalize(string path)
        => string.IsNullOrEmpty(path)
            ? path
            : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool IsDriveRoot(string trimmed)
        => trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';

    private static bool IsStrictlyUnder(string target, string ancestor)
        => !PathEquals(target, ancestor)
           && target.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
