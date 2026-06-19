using System.Diagnostics;
using Microsoft.Win32;

namespace CopyFinder.Services;

public enum ControlledFolderAccessMode
{
    Unknown,
    Disabled,
    Enabled,
    AuditMode,
    BlockDiskModificationOnly,
    AuditDiskModificationOnly
}

public sealed record ControlledFolderAccessStatus(
    ControlledFolderAccessMode Mode,
    string ExecutablePath,
    string AllowApplicationCommand,
    string UserGuidance,
    bool RequiresUserAction);

public static class ControlledFolderAccessService
{
    private static readonly object SyncRoot = new();
    private static ControlledFolderAccessStatus? _cachedStatus;

    public static ControlledFolderAccessStatus GetStatus(bool refresh = false)
    {
        lock (SyncRoot)
        {
            if (!refresh && _cachedStatus is not null)
            {
                return _cachedStatus;
            }

            var executablePath = GetExecutablePath();
            var mode = TryReadFromPowerShell() ?? TryReadFromRegistry() ?? ControlledFolderAccessMode.Unknown;
            var command = $"Add-MpPreference -ControlledFolderAccessAllowedApplications \"{executablePath}\"";
            var requiresUserAction = mode is ControlledFolderAccessMode.Enabled or ControlledFolderAccessMode.BlockDiskModificationOnly;
            var guidance = requiresUserAction
                ? $"Controlled Folder Access is active. Allow this exact app path in Windows Security or elevated PowerShell: {executablePath}"
                : mode == ControlledFolderAccessMode.AuditMode || mode == ControlledFolderAccessMode.AuditDiskModificationOnly
                    ? "Controlled Folder Access is in audit mode. CopyFinder can run, but audit events should be reviewed before enterprise deployment."
                    : mode == ControlledFolderAccessMode.Disabled
                        ? "Controlled Folder Access is disabled."
                        : "Controlled Folder Access status could not be confirmed on this machine.";

            _cachedStatus = new ControlledFolderAccessStatus(mode, executablePath, command, guidance, requiresUserAction);
            DeploymentLogger.Log("CFA", $"Status={mode}; AllowPath={executablePath}");
            return _cachedStatus;
        }
    }

    public static string GetExecutablePath()
    {
        return Environment.ProcessPath
               ?? Path.Combine(AppContext.BaseDirectory, "CopyFinder.exe");
    }

    public static void LogPossibleBlock(string operation, string path, Exception exception)
    {
        var status = GetStatus();
        var cfaText = status.RequiresUserAction
            ? $" Controlled Folder Access is active. AllowPath={status.ExecutablePath}. Command={status.AllowApplicationCommand}."
            : $" Controlled Folder Access status={status.Mode}.";

        DeploymentLogger.Log("CFABlock", $"{operation} failed for {path}.{cfaText}", exception);
    }

    private static ControlledFolderAccessMode? TryReadFromPowerShell()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-MpPreference).EnableControlledFolderAccess\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!process.Start())
            {
                return null;
            }

            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return ParseMode(output);
        }
        catch
        {
            return null;
        }
    }

    private static ControlledFolderAccessMode? TryReadFromRegistry()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access",
            @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access",
            @"SOFTWARE\Microsoft\Windows Defender\Exploit Guard\Controlled Folder Access"
        };

        foreach (var keyPath in keyPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                var value = key?.GetValue("EnableControlledFolderAccess");
                if (value is null)
                {
                    continue;
                }

                return ParseMode(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static ControlledFolderAccessMode? ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (int.TryParse(normalized, out var number))
        {
            return number switch
            {
                0 => ControlledFolderAccessMode.Disabled,
                1 => ControlledFolderAccessMode.Enabled,
                2 => ControlledFolderAccessMode.AuditMode,
                3 => ControlledFolderAccessMode.BlockDiskModificationOnly,
                4 => ControlledFolderAccessMode.AuditDiskModificationOnly,
                _ => ControlledFolderAccessMode.Unknown
            };
        }

        return normalized.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant() switch
        {
            "disabled" => ControlledFolderAccessMode.Disabled,
            "enabled" => ControlledFolderAccessMode.Enabled,
            "auditmode" => ControlledFolderAccessMode.AuditMode,
            "blockdiskmodificationonly" => ControlledFolderAccessMode.BlockDiskModificationOnly,
            "auditdiskmodificationonly" => ControlledFolderAccessMode.AuditDiskModificationOnly,
            _ => ControlledFolderAccessMode.Unknown
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort only; CFA detection must not block app startup.
        }
    }
}
