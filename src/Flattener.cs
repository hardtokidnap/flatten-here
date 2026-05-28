namespace FlattenHere;

internal readonly record struct MovePlanEntry(string Source, string Destination, bool Renamed);

internal enum FlattenMode { Move, DryRun }

internal sealed class FlattenSummary
{
    public int Moved;
    public int Renamed;
    public int Failed;
    public int RemovedDirs;
    public int SkippedReparse;
    public int PlannedCount => Moved + Renamed + Failed;
}

internal static class Flattener
{
    private const int ERROR_NOT_SAME_DEVICE = 0x11;

    private static readonly EnumerationOptions ShallowEnumOpts = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public static List<MovePlanEntry> BuildPlan(string target, Logger log, FlattenSummary summary)
    {
        if (!Directory.Exists(target))
            throw new DirectoryNotFoundException(target);

        var plan = new List<MovePlanEntry>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reserve existing top-level file names so plan-renames don't collide with them.
        SafeEnumerate(() => Directory.EnumerateFiles(target, "*", ShallowEnumOpts),
            files =>
            {
                foreach (var f in files) taken.Add(f);
            },
            ex => log.Write("SKIP-DIR", $"{target}  {ex.GetType().Name}: {ex.Message}"));

        var stack = new Stack<string>();
        SafeEnumerate(() => Directory.EnumerateDirectories(target, "*", ShallowEnumOpts),
            subs =>
            {
                foreach (var s in subs) stack.Push(s);
            },
            ex => log.Write("SKIP-DIR", $"{target}  {ex.GetType().Name}: {ex.Message}"));

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(dir);
            }
            catch (Exception ex)
            {
                log.Write("SKIP-ATTR", $"{dir}  {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                log.Write("SKIP-REPARSE", dir);
                summary.SkippedReparse++;
                continue;
            }

            SafeEnumerate(() => Directory.EnumerateFiles(dir, "*", ShallowEnumOpts),
                files =>
                {
                    foreach (var src in files)
                    {
                        var entry = ResolveDestination(target, src, taken);
                        taken.Add(entry.Destination);
                        plan.Add(entry);
                    }
                },
                ex => log.Write("SKIP-DIR", $"{dir}  {ex.GetType().Name}: {ex.Message}"));

            SafeEnumerate(() => Directory.EnumerateDirectories(dir, "*", ShallowEnumOpts),
                subs =>
                {
                    foreach (var s in subs) stack.Push(s);
                },
                ex => log.Write("SKIP-DIR", $"{dir}  {ex.GetType().Name}: {ex.Message}"));
        }

        return plan;
    }

    public static void Execute(IReadOnlyList<MovePlanEntry> plan, FlattenMode mode, Logger log, FlattenSummary summary)
    {
        foreach (var entry in plan)
        {
            if (mode == FlattenMode.DryRun)
            {
                log.Write(entry.Renamed ? "DRY-RENAME" : "DRY-MOVE",
                    $"{entry.Source} -> {entry.Destination}");
                if (entry.Renamed) summary.Renamed++;
                else summary.Moved++;
                continue;
            }

            try
            {
                File.Move(entry.Source, entry.Destination, overwrite: false);
                log.Write(entry.Renamed ? "RENAME" : "MOVE",
                    $"{entry.Source} -> {entry.Destination}");
                if (entry.Renamed) summary.Renamed++;
                else summary.Moved++;
            }
            catch (IOException ioex) when ((ioex.HResult & 0xFFFF) == ERROR_NOT_SAME_DEVICE)
            {
                TryCrossVolumeMove(entry, log, summary);
            }
            catch (Exception ex)
            {
                log.WriteFail("MOVE", entry.Source, entry.Destination, ex);
                summary.Failed++;
            }
        }
    }

    private static void TryCrossVolumeMove(MovePlanEntry entry, Logger log, FlattenSummary summary)
    {
        try
        {
            File.Copy(entry.Source, entry.Destination, overwrite: false);
            var srcLen = new FileInfo(entry.Source).Length;
            var dstLen = new FileInfo(entry.Destination).Length;
            if (srcLen != dstLen)
            {
                try { File.Delete(entry.Destination); } catch { /* best effort */ }
                log.WriteFail("MOVE-XVOL-VERIFY", entry.Source, entry.Destination,
                    new IOException($"Length mismatch after cross-volume copy ({srcLen} vs {dstLen})."));
                summary.Failed++;
                return;
            }
            File.Delete(entry.Source);
            log.Write("MOVE-XVOL", $"{entry.Source} -> {entry.Destination}");
            if (entry.Renamed) summary.Renamed++;
            else summary.Moved++;
        }
        catch (Exception ex)
        {
            log.WriteFail("MOVE-XVOL", entry.Source, entry.Destination, ex);
            summary.Failed++;
        }
    }

    public static void CleanupEmpty(string target, FlattenMode mode, Logger log, FlattenSummary summary)
    {
        var all = new List<string>();
        var stack = new Stack<string>();

        SafeEnumerate(() => Directory.EnumerateDirectories(target, "*", ShallowEnumOpts),
            subs =>
            {
                foreach (var s in subs) stack.Push(s);
            },
            ex => log.Write("SKIP-DIR", $"{target}  {ex.GetType().Name}: {ex.Message}"));

        while (stack.Count > 0)
        {
            var d = stack.Pop();
            FileAttributes attrs;
            try { attrs = File.GetAttributes(d); }
            catch { continue; }
            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;

            all.Add(d);
            SafeEnumerate(() => Directory.EnumerateDirectories(d, "*", ShallowEnumOpts),
                subs =>
                {
                    foreach (var s in subs) stack.Push(s);
                },
                ex => log.Write("SKIP-DIR", $"{d}  {ex.GetType().Name}: {ex.Message}"));
        }

        // Deepest first.
        all.Sort((a, b) => b.Length.CompareTo(a.Length));

        foreach (var d in all)
        {
            bool empty;
            try
            {
                empty = !Directory.EnumerateFileSystemEntries(d).Any();
            }
            catch
            {
                continue;
            }
            if (!empty) continue;

            if (mode == FlattenMode.DryRun)
            {
                log.Write("DRY-RMDIR", d);
                summary.RemovedDirs++;
                continue;
            }

            try
            {
                Directory.Delete(d, recursive: false);
                log.Write("RMDIR", d);
                summary.RemovedDirs++;
            }
            catch (Exception ex)
            {
                log.WriteFail("RMDIR", d, string.Empty, ex);
            }
        }
    }

    public static int CountSubfoldersDeep(string target)
    {
        if (!Directory.Exists(target)) return 0;
        int count = 0;
        var stack = new Stack<string>();
        try
        {
            foreach (var s in Directory.EnumerateDirectories(target, "*", ShallowEnumOpts))
                stack.Push(s);
        }
        catch { return 0; }

        while (stack.Count > 0)
        {
            var d = stack.Pop();
            try
            {
                var attrs = File.GetAttributes(d);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }
            count++;
            try
            {
                foreach (var s in Directory.EnumerateDirectories(d, "*", ShallowEnumOpts))
                    stack.Push(s);
            }
            catch { /* skip */ }
        }
        return count;
    }

    private static MovePlanEntry ResolveDestination(string target, string src, HashSet<string> taken)
    {
        var name = Path.GetFileName(src);
        var direct = Path.Combine(target, name);
        if (!taken.Contains(direct) && !File.Exists(direct))
            return new MovePlanEntry(src, direct, false);

        var nameNoExt = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; i < int.MaxValue; i++)
        {
            var candidate = Path.Combine(target, $"{nameNoExt} ({i}){ext}");
            if (!taken.Contains(candidate) && !File.Exists(candidate))
                return new MovePlanEntry(src, candidate, true);
        }
        throw new InvalidOperationException($"Could not find a free destination name for {src}");
    }

    private static void SafeEnumerate<T>(Func<IEnumerable<T>> source, Action<IEnumerable<T>> consume, Action<Exception> onError)
    {
        try
        {
            consume(source());
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
