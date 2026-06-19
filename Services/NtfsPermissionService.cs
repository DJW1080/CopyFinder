using System.Diagnostics;
using System.Security.Principal;

namespace CopyFinder.Services;

public sealed record PermissionCheckResult(
    bool HasModifyOrDelete,
    bool IsLocked,
    string Message);

public sealed record PermissionRepairResult(
    bool Succeeded,
    string Message);

public static class NtfsPermissionService
{
    public static PermissionCheckResult HasModifyOrDeletePermission(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return ProbeDirectory(fullPath);
            }

            if (!File.Exists(fullPath))
            {
                return new PermissionCheckResult(false, false, "Path does not exist.");
            }

            var attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return new PermissionCheckResult(false, false, "File is read-only.");
            }

            try
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                return new PermissionCheckResult(true, false, "Read/write probe succeeded; delete is verified during the delete operation.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return new PermissionCheckResult(false, false, $"Current user lacks modify access: {ex.Message}");
            }
            catch (IOException ex)
            {
                return new PermissionCheckResult(false, true, $"File is locked or unavailable: {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new PermissionCheckResult(false, false, $"Permission check failed: {ex.Message}");
        }
    }

    public static async Task<PermissionRepairResult> TryTakeOwnershipAndGrantModifyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsElevated())
        {
            var message = "Ownership repair requires an elevated CopyFinder process or enterprise helper service.";
            DeploymentLogger.Log("PermissionRepair", $"{message} Path={fullPath}");
            return new PermissionRepairResult(false, message);
        }

        var takeown = await RunProcessAsync(
            "takeown.exe",
            BuildTakeownArguments(fullPath),
            cancellationToken).ConfigureAwait(false);

        if (!takeown.Succeeded)
        {
            DeploymentLogger.Log("OwnershipChange", $"takeown failed for {fullPath}: {takeown.Message}");
            return takeown;
        }

        var identity = WindowsIdentity.GetCurrent().Name;
        var grant = await RunProcessAsync(
            "icacls.exe",
            [fullPath, "/grant", $"{identity}:(M,D)", "/C"],
            cancellationToken).ConfigureAwait(false);

        DeploymentLogger.Log(
            "PermissionEscalation",
            grant.Succeeded
                ? $"Granted Modify/Delete to {identity} on {fullPath}"
                : $"icacls failed for {fullPath}: {grant.Message}");

        return grant;
    }

    public static PermissionCheckResult TestWorkingDirectory(string workingDirectory)
    {
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var testFile = Path.Combine(workingDirectory, $"permission-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "CopyFinder permission test");
            File.Delete(testFile);
            return new PermissionCheckResult(true, false, $"Working directory is writable: {workingDirectory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PermissionCheckResult(false, false, $"Working directory permission test failed: {ex.Message}");
        }
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static PermissionCheckResult ProbeDirectory(string path)
    {
        try
        {
            var testPath = Path.Combine(path, $".copyfinder-permission-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testPath, "CopyFinder permission probe");
            File.Delete(testPath);
            return new PermissionCheckResult(true, false, "Directory create/delete probe succeeded.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PermissionCheckResult(false, false, $"Current user lacks directory modify/delete access: {ex.Message}");
        }
        catch (IOException ex)
        {
            return new PermissionCheckResult(false, true, $"Directory is locked or unavailable: {ex.Message}");
        }
    }

    private static string[] BuildTakeownArguments(string path)
    {
        if (Directory.Exists(path))
        {
            return ["/F", path, "/R", "/D", "Y"];
        }

        return ["/F", path];
    }

    private static async Task<PermissionRepairResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return new PermissionRepairResult(false, $"Could not start {fileName}.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var message = string.Join(" ", new[] { output.Trim(), error.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new PermissionRepairResult(
                process.ExitCode == 0,
                string.IsNullOrWhiteSpace(message) ? $"{fileName} exited with code {process.ExitCode}." : message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new PermissionRepairResult(false, $"{fileName} failed: {ex.Message}");
        }
    }
}
