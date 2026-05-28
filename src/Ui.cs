namespace FlattenHere;

internal enum ConfirmResult { Continue, Cancel }

internal static class Ui
{
    private const string Caption = "flatten-here";

    public static ConfirmResult ConfirmCleanupOnly(int subfolderCount, string target)
    {
        var body = $"No files to flatten in:\n{target}\n\n" +
                   $"{subfolderCount:N0} subfolder{Plural(subfolderCount)} found. " +
                   "Empty ones will be removed (deepest first).\n\n" +
                   "Continue?";
        var r = Native.MessageBox(IntPtr.Zero, body, Caption,
            Native.MB_OKCANCEL | Native.MB_SETFOREGROUND | Native.MB_TOPMOST);
        return r == Native.IDOK ? ConfirmResult.Continue : ConfirmResult.Cancel;
    }

    public static ConfirmResult Confirm(int fileCount, int subfolderCount, string target, FlattenMode mode, bool deleteEmpty)
    {
        var body = $"Flatten {fileCount:N0} file{Plural(fileCount)} from {subfolderCount:N0} subfolder{Plural(subfolderCount)} into:\n{target}";

        if (mode == FlattenMode.DryRun)
            body += "\n\nDRY RUN. No files will be moved.";
        if (deleteEmpty)
            body += "\n\nEmpty subfolders will be removed afterwards.";

        body += "\n\nContinue?";

        // No MB_ICON* flag -> Win32 does NOT play a system sound. Icons and sounds
        // are coupled in MessageBox; this tool is user-invoked, so no chime needed.
        var r = Native.MessageBox(IntPtr.Zero, body, Caption,
            Native.MB_OKCANCEL | Native.MB_SETFOREGROUND | Native.MB_TOPMOST);
        return r == Native.IDOK ? ConfirmResult.Continue : ConfirmResult.Cancel;
    }

    public static void ShowSummary(FlattenSummary summary, string target, FlattenMode mode, bool deleteEmpty, string logPath, bool usingFallback)
    {
        var lines = new List<string>();
        lines.Add(mode == FlattenMode.DryRun ? "DRY RUN summary" : "Done.");
        lines.Add(string.Empty);
        lines.Add($"Moved:    {summary.Moved:N0}");
        lines.Add($"Renamed:  {summary.Renamed:N0}");
        if (summary.Failed > 0)
            lines.Add($"Failed:   {summary.Failed:N0}");
        if (deleteEmpty)
            lines.Add($"Removed:  {summary.RemovedDirs:N0} empty subfolder{Plural(summary.RemovedDirs)}");
        if (summary.SkippedReparse > 0)
            lines.Add($"Skipped:  {summary.SkippedReparse:N0} reparse point{Plural(summary.SkippedReparse)} (junctions / symlinks)");
        lines.Add(string.Empty);
        lines.Add($"Target: {target}");
        lines.Add($"Log:    {logPath}{(usingFallback ? "  (fallback location)" : string.Empty)}");

        Native.MessageBox(IntPtr.Zero, string.Join('\n', lines), Caption,
            Native.MB_OK | Native.MB_SETFOREGROUND);
    }

    public static void ShowGuardBlocked(string reason)
    {
        Native.MessageBox(IntPtr.Zero,
            reason + "\n\nNo files were moved.",
            Caption,
            Native.MB_OK | Native.MB_SETFOREGROUND | Native.MB_TOPMOST);
    }

    public static void ShowFatal(string details, string? logPath)
    {
        var body = "flatten-here failed unexpectedly.\n\n" + details;
        if (!string.IsNullOrEmpty(logPath))
            body += "\n\nPartial log: " + logPath;
        Native.MessageBox(IntPtr.Zero, body, Caption,
            Native.MB_OK | Native.MB_SETFOREGROUND | Native.MB_TOPMOST);
    }

    private static string Plural(int n) => n == 1 ? string.Empty : "s";
}
