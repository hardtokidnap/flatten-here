using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace FlattenHere;

internal enum InstallScope { User, Machine }

internal readonly record struct InstallOptions(
    InstallScope Scope,
    bool EnableLongPaths);

internal enum InstallStatus
{
    Success,
    Cancelled,
    NeedsAdmin,
    Failed,
}

internal readonly record struct InstallResult(InstallStatus Status, string? Detail = null);

internal static class Installer
{
    private const string UserShellRoot       = @"Software\Classes\Directory\shell\FlattenHere";
    private const string UserBackgroundRoot  = @"Software\Classes\Directory\Background\shell\FlattenHere";
    private const string LongPathsKey        = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    // Custom TaskDialog button IDs (start at 100 to avoid colliding with stock IDs).
    private const int BtnInstall   = 100;
    private const int BtnUninstall = 101;
    private const int BtnReinstall = 102;

    public static int RunInteractive(string? forceAction)
    {
        // forceAction = "install" or "uninstall" routes past the "what's installed?" detection.
        var installedScope = DetectInstalledScope();

        if (forceAction == "install" || (forceAction is null && installedScope is null))
            return ShowInstallDialog(InstallScope.User);

        if (forceAction == "uninstall" || installedScope is not null)
            return ShowUninstallDialog(installedScope ?? InstallScope.User);

        return ShowInstallDialog(InstallScope.User);
    }

    public static int RunQuiet(string action, InstallOptions opts)
    {
        InstallResult result = action == "install"
            ? Install(opts)
            : Uninstall(opts.Scope);

        return result.Status switch
        {
            InstallStatus.Success     => 0,
            InstallStatus.Cancelled   => 10,
            InstallStatus.NeedsAdmin  => 11,
            _                         => 12,
        };
    }

    // ---------- Install flow ----------

    private static int ShowInstallDialog(InstallScope defaultScope)
    {
        var dstExe = GetInstallExePath(defaultScope);
        var details =
            "What gets written\n" +
            "\n" +
            $"• Install folder: {GetInstallDir(defaultScope)}\n" +
            "• Executable: this exe is copied into the install folder.\n" +
            "• Two registry keys under HKCU\\Software\\Classes\\Directory — one for folder clicks (shell\\FlattenHere) and one for background clicks (Background\\shell\\FlattenHere). Each stores the menu text, an Icon value pointing at the installed exe, and a Position of Bottom.\n" +
            "• A \"command\" subkey under each, holding the actual invocation: \"<installed-exe>\" --path \"%1\" (or %V for the background variant).\n" +
            "\n" +
            "Modifier keys at click time\n" +
            "\n" +
            "• (none): confirm, move every file from subfolders into the clicked folder, show a summary.\n" +
            "• Ctrl: dry-run preview. No files are moved; a log is written.\n" +
            "• Shift: move, then delete subfolders that are now empty.\n" +
            "• Ctrl + Shift: dry-run preview of the Shift behavior.\n" +
            "\n" +
            "Collisions get a numeric rename (name (2).ext, name (3).ext, …).\n" +
            "A flatten-here.log is written next to the target folder. If the target isn't writable, the log falls back to %TEMP%\\flatten-here-<pid>.log.\n" +
            "\n" +
            "Uninstall by double-clicking this exe again — the installer will detect the existing install and offer Uninstall or Reinstall.";

        using var dlg = new TaskDialogBuilder()
            .WithTitle("flatten-here installer")
            .WithMainInstruction("Install flatten-here?")
            .WithContent(
                "Adds a 'Flatten files here' option to the Windows right-click menu on folders. " +
                "Per-user, no admin needed. Nothing is started in the background; the tool only " +
                "runs when you pick the verb from the menu.")
            .WithStockIcon(Native.TD_INFORMATION_ICON)
            .WithExpandedInfo(details)
            .WithExpandLabels(collapsed: "Show details", expanded: "Hide details")
            .WithVerification("Enable long path support system-wide (>260 chars). Requires admin.")
            .WithFooter("MIT licensed. github.com/hardtokidnap/flatten-here")
            .AddCommandLink(BtnInstall, "Install for current user",
                "Writes per-user registry keys under HKCU. No admin prompt.")
            .AddButton(Native.IDCANCEL, "Cancel");

        var r = dlg.Show();

        if (r.ButtonId != BtnInstall)
            return 10;

        var opts = new InstallOptions(InstallScope.User, EnableLongPaths: r.VerificationChecked);
        var result = Install(opts);
        ShowResult(result, action: "install", opts);
        return result.Status == InstallStatus.Success ? 0 : 12;
    }

    private static int ShowUninstallDialog(InstallScope scope)
    {
        var exePath = GetInstallExePath(scope);
        var verbRoot = scope == InstallScope.Machine ? "HKLM" : "HKCU";

        using var dlg = new TaskDialogBuilder()
            .WithTitle("flatten-here installer")
            .WithMainInstruction("flatten-here is already installed.")
            .WithContent($"Currently installed for: {(scope == InstallScope.Machine ? "all users" : "current user")}.")
            .WithStockIcon(Native.TD_INFORMATION_ICON)
            .WithExpandedInfo(
                $"Install folder: {GetInstallDir(scope)}\n\n" +
                $"Registry verbs registered under {verbRoot}\\Software\\Classes\\Directory:\n" +
                $"• shell\\FlattenHere (folder right-click)\n" +
                $"• Background\\shell\\FlattenHere (background right-click inside a folder)\n\n" +
                "Uninstall removes both keys and deletes the install folder. " +
                "Reinstall overwrites the exe in place and rewrites the keys.")
            .WithExpandLabels("Show details", "Hide details")
            .AddCommandLink(BtnUninstall, "Uninstall",
                "Removes the registry keys and deletes the install folder.")
            .AddCommandLink(BtnReinstall, "Reinstall",
                "Overwrites the current exe with this one and re-writes the registry keys.")
            .AddButton(Native.IDCLOSE, "Close")
            .WithDefaultButton(Native.IDCLOSE);

        var r = dlg.Show();

        if (r.ButtonId == BtnUninstall)
        {
            var result = Uninstall(scope);
            ShowResult(result, action: "uninstall", new InstallOptions(scope, false));
            return result.Status == InstallStatus.Success ? 0 : 12;
        }
        if (r.ButtonId == BtnReinstall)
        {
            var result = Install(new InstallOptions(scope, EnableLongPaths: false));
            ShowResult(result, action: "reinstall", new InstallOptions(scope, false));
            return result.Status == InstallStatus.Success ? 0 : 12;
        }
        return 0;
    }

    private static void ShowResult(InstallResult result, string action, InstallOptions opts)
    {
        if (result.Status == InstallStatus.Success)
        {
            var msg = action switch
            {
                "install"   => "Installed. Right-click any folder and pick 'Flatten files here'.",
                "reinstall" => "Reinstalled.",
                "uninstall" => "Uninstalled. The right-click verb is gone.",
                _ => "Done.",
            };
            using var dlg = new TaskDialogBuilder()
                .WithTitle("flatten-here installer")
                .WithMainInstruction(msg)
                .WithStockIcon(Native.TD_INFORMATION_ICON)
                .WithCommonButtons(Native.TaskDialogCommonButtons.Ok);
            dlg.Show();
            return;
        }

        var detail = result.Detail ?? "(no detail)";
        using var fail = new TaskDialogBuilder()
            .WithTitle("flatten-here installer")
            .WithMainInstruction($"{Capitalize(action)} failed.")
            .WithContent(detail)
            .WithStockIcon(Native.TD_ERROR_ICON)
            .WithCommonButtons(Native.TaskDialogCommonButtons.Close);
        fail.Show();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------- Core install / uninstall ----------

    public static InstallResult Install(InstallOptions opts)
    {
        try
        {
            if (opts.Scope == InstallScope.Machine && !IsAdmin())
                return new InstallResult(InstallStatus.NeedsAdmin, "Machine-wide install needs an elevated process.");

            var installDir = GetInstallDir(opts.Scope);
            var dstExe = GetInstallExePath(opts.Scope);

            Directory.CreateDirectory(installDir);

            var srcExe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the current exe path.");
            if (!string.Equals(Path.GetFullPath(srcExe), Path.GetFullPath(dstExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(srcExe, dstExe, overwrite: true);

            using var root = OpenRoot(opts.Scope, writable: true);
            WriteVerb(root, UserShellRoot,      dstExe, $"\"{dstExe}\" --path \"%1\"");
            WriteVerb(root, UserBackgroundRoot, dstExe, $"\"{dstExe}\" --path \"%V\" --from-background");

            if (opts.EnableLongPaths)
            {
                if (!IsAdmin())
                    return new InstallResult(InstallStatus.Success,
                        "Install OK. Long-path support was requested but skipped: requires admin.");
                EnableLongPaths();
            }

            return new InstallResult(InstallStatus.Success);
        }
        catch (Exception ex)
        {
            return new InstallResult(InstallStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static InstallResult Uninstall(InstallScope scope)
    {
        try
        {
            if (scope == InstallScope.Machine && !IsAdmin())
                return new InstallResult(InstallStatus.NeedsAdmin, "Machine-wide uninstall needs an elevated process.");

            using (var root = OpenRoot(scope, writable: true))
            {
                root.DeleteSubKeyTree(UserShellRoot,      throwOnMissingSubKey: false);
                root.DeleteSubKeyTree(UserBackgroundRoot, throwOnMissingSubKey: false);
            }

            var installDir = GetInstallDir(scope);
            if (Directory.Exists(installDir))
            {
                var self = Environment.ProcessPath;
                if (self != null && IsUnderOrEqual(Path.GetFullPath(self), Path.GetFullPath(installDir)))
                    ScheduleSelfDelete(installDir);
                else
                    SafeDeleteTree(installDir);
            }

            return new InstallResult(InstallStatus.Success);
        }
        catch (Exception ex)
        {
            return new InstallResult(InstallStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---------- Helpers ----------

    public static InstallScope? DetectInstalledScope()
    {
        using (var userKey = Registry.CurrentUser.OpenSubKey(UserShellRoot))
            if (userKey != null) return InstallScope.User;

        using (var machineKey = Registry.LocalMachine.OpenSubKey(UserShellRoot))
            if (machineKey != null) return InstallScope.Machine;

        return null;
    }

    private static RegistryKey OpenRoot(InstallScope scope, bool writable)
        => scope == InstallScope.Machine ? Registry.LocalMachine : Registry.CurrentUser;

    private static string GetInstallDir(InstallScope scope)
        => scope == InstallScope.Machine
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FlattenHere")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlattenHere");

    private static string GetInstallExePath(InstallScope scope)
        => Path.Combine(GetInstallDir(scope), "FlattenHere.exe");

    private static void WriteVerb(RegistryKey root, string subPath, string exePath, string commandLine)
    {
        using var shell = root.CreateSubKey(subPath, writable: true);
        shell.SetValue(string.Empty, "Flatten files here");
        shell.SetValue("MUIVerb",    "Flatten files here");
        shell.SetValue("Position",   "Bottom");
        shell.SetValue("Icon",       exePath);
        using var cmd = shell.CreateSubKey("command", writable: true);
        cmd.SetValue(string.Empty, commandLine);
    }

    private static void EnableLongPaths()
    {
        using var fs = Registry.LocalMachine.OpenSubKey(LongPathsKey, writable: true);
        // Schema is fixed by Windows; if this key vanishes Windows itself is in trouble.
        fs?.SetValue("LongPathsEnabled", 1, RegistryValueKind.DWord);
    }

    private static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsUnderOrEqual(string path, string ancestor)
        => string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Cannot delete the exe we're running from. Defer the rmdir to a detached cmd.exe
    // that waits two seconds (Windows releases the file handle on process exit), then
    // recursively deletes the install folder.
    private static void ScheduleSelfDelete(string installDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "cmd.exe",
            Arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{installDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    private static void SafeDeleteTree(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best effort: a stray file lock is not worth aborting uninstall over */ }
    }
}
