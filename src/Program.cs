namespace FlattenHere;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var parsed = ParseArgs(args);

            // No --path argument => installer mode. Either the user double-clicked the exe
            // from a download folder, or invoked it from a terminal with --install / --uninstall.
            if (parsed.Target is null)
            {
                if (parsed.Quiet && (parsed.Install || parsed.Uninstall))
                {
                    var scope = parsed.Machine ? InstallScope.Machine : InstallScope.User;
                    var opts = new InstallOptions(scope, parsed.EnableLongPaths);
                    return Installer.RunQuiet(parsed.Install ? "install" : "uninstall", opts);
                }

                string? forced = parsed.Install ? "install"
                              : parsed.Uninstall ? "uninstall"
                              : null;
                return Installer.RunInteractive(forced);
            }

            // Modifier keys feed the default mode (Explorer cannot pass them as args).
            // Explicit --dry-run / --cleanup flags override the modifier state, which is
            // what makes the exe scriptable and testable from a terminal.
            var mode = (parsed.DryRunFlag ?? Native.IsKeyDown(Native.VK_CONTROL))
                ? FlattenMode.DryRun
                : FlattenMode.Move;
            var deleteEmpty = parsed.CleanupFlag ?? Native.IsKeyDown(Native.VK_SHIFT);

            string normalized;
            try
            {
                normalized = Guards.Normalize(parsed.Target);
            }
            catch (Exception ex)
            {
                Ui.ShowGuardBlocked($"Invalid target path:\n{parsed.Target}\n\n{ex.Message}");
                return 2;
            }

            var guard = Guards.Check(normalized);
            if (!guard.Allowed)
            {
                Ui.ShowGuardBlocked(guard.Reason ?? "Target folder is protected.");
                return 3;
            }

            if (!Directory.Exists(normalized))
            {
                Ui.ShowGuardBlocked($"Target folder does not exist:\n{normalized}");
                return 4;
            }

            using var log = Logger.Open(normalized);
            log.Write("START", $"target={normalized}  mode={mode}  deleteEmpty={deleteEmpty}");

            var summary = new FlattenSummary();
            var plan = Flattener.BuildPlan(normalized, log, summary);

            if (plan.Count == 0)
            {
                // Cleanup-only path: shift-click on a folder that's already flat. Still useful
                // for removing leftover empty subfolders from a previous flatten run.
                if (deleteEmpty)
                {
                    var subfolderCount = Flattener.CountSubfoldersDeep(normalized);
                    if (subfolderCount > 0)
                    {
                        if (mode == FlattenMode.Move
                            && Ui.ConfirmCleanupOnly(subfolderCount, normalized) == ConfirmResult.Cancel)
                        {
                            log.Write("CANCEL", "user cancelled cleanup-only confirm");
                            return 0;
                        }
                        Flattener.CleanupEmpty(normalized, mode, log, summary);
                    }
                }
                Ui.ShowSummary(summary, normalized, mode, deleteEmpty, log.Path, log.UsingFallback);
                log.Write("END", $"plan empty  rmdir={summary.RemovedDirs}");
                return 0;
            }

            if (mode == FlattenMode.Move)
            {
                var subfolderCount = Flattener.CountSubfoldersDeep(normalized);
                if (Ui.Confirm(plan.Count, subfolderCount, normalized, mode, deleteEmpty) == ConfirmResult.Cancel)
                {
                    log.Write("CANCEL", "user cancelled at confirm");
                    return 0;
                }
            }

            Flattener.Execute(plan, mode, log, summary);
            if (deleteEmpty)
                Flattener.CleanupEmpty(normalized, mode, log, summary);

            Ui.ShowSummary(summary, normalized, mode, deleteEmpty, log.Path, log.UsingFallback);
            log.Write("END",
                $"moved={summary.Moved} renamed={summary.Renamed} failed={summary.Failed} " +
                $"rmdir={summary.RemovedDirs} skip-reparse={summary.SkippedReparse}");
            return summary.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Ui.ShowFatal($"{ex.GetType().Name}: {ex.Message}", null);
            return 99;
        }
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        string? target = null;
        bool fromBg = false, install = false, uninstall = false, machine = false,
             quiet = false, enableLongPaths = false;
        bool? dryRun = null, cleanup = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (Eq(a, "--path") && i + 1 < args.Length)        target = args[++i];
            else if (Eq(a, "--from-background"))               fromBg = true;
            else if (Eq(a, "--dry-run"))                       dryRun = true;
            else if (Eq(a, "--cleanup"))                       cleanup = true;
            else if (Eq(a, "--install"))                       install = true;
            else if (Eq(a, "--uninstall"))                     uninstall = true;
            else if (Eq(a, "--machine"))                       machine = true;
            else if (Eq(a, "--quiet"))                         quiet = true;
            else if (Eq(a, "--enable-long-paths"))             enableLongPaths = true;
        }
        return new ParsedArgs(target, fromBg, dryRun, cleanup, install, uninstall, machine, quiet, enableLongPaths);
    }

    private static bool Eq(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ParsedArgs(
        string? Target,
        bool FromBackground,
        bool? DryRunFlag,
        bool? CleanupFlag,
        bool Install,
        bool Uninstall,
        bool Machine,
        bool Quiet,
        bool EnableLongPaths);
}
