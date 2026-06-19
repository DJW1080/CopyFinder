using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace CopyFinder.Services;

public sealed record SafeFileOperationResult(
    bool Succeeded,
    string Path,
    string? Message = null,
    string? WorkingPath = null);

public sealed record SafeDirectoryEnumerationResult(
    IReadOnlyList<string> Paths,
    string? ErrorMessage);

public static class SafeFile
{
    public static string WorkingDirectory => OneDriveFileHandler.WorkingDirectory;

    public static string DeploymentLogPath => DeploymentLogger.DefaultLogPath;

    public static ControlledFolderAccessStatus GetControlledFolderAccessStatus()
    {
        return ControlledFolderAccessService.GetStatus();
    }

    public static IReadOnlyList<string> GetOneDriveRoots()
    {
        return OneDriveFileHandler.GetKnownRoots();
    }

    public static bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DeploymentLogger.Log("SafeFile", $"FileExists failed for {path}", ex);
            return false;
        }
    }

    public static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DeploymentLogger.Log("SafeFile", $"DirectoryExists failed for {path}", ex);
            return false;
        }
    }

    public static SafeFileOperationResult EnsureDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            DeploymentLogger.Log("SafeDirectory", $"Ensured directory {fullPath}");
            return new SafeFileOperationResult(true, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("EnsureDirectory", path, ex);
            return new SafeFileOperationResult(false, path, ex.Message);
        }
    }

    public static SafeDirectoryEnumerationResult EnumerateDirectories(string directory)
    {
        try
        {
            return new SafeDirectoryEnumerationResult(Directory.EnumerateDirectories(directory).ToList(), null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ControlledFolderAccessService.LogPossibleBlock("EnumerateDirectories", directory, ex);
            return new SafeDirectoryEnumerationResult([], ex.Message);
        }
    }

    public static SafeDirectoryEnumerationResult EnumerateFiles(string directory)
    {
        try
        {
            return new SafeDirectoryEnumerationResult(Directory.EnumerateFiles(directory).ToList(), null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ControlledFolderAccessService.LogPossibleBlock("EnumerateFiles", directory, ex);
            return new SafeDirectoryEnumerationResult([], ex.Message);
        }
    }

    public static PermissionCheckResult HasPermissions(string path)
    {
        var result = NtfsPermissionService.HasModifyOrDeletePermission(path);
        if (!result.HasModifyOrDelete)
        {
            DeploymentLogger.Log("PermissionCheck", $"{path}: {result.Message}");
        }

        return result;
    }

    public static bool IsOneDriveLocked(string path)
    {
        return OneDriveFileHandler.IsOneDriveLocked(path);
    }

    public static Task<SafeWorkingFile> CopyToWorkingDirAsync(
        string path,
        string reason,
        CancellationToken cancellationToken)
    {
        return OneDriveFileHandler.CopyToWorkingDirectoryAsync(path, reason, cancellationToken);
    }

    public static SafeFileOperationResult CopyToWorkingDir(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var workingFile = CopyToWorkingDirAsync(path, "Manual copy request", cancellation.Token)
                .GetAwaiter()
                .GetResult();

            return new SafeFileOperationResult(true, path, workingFile.OneDriveState.Summary, workingFile.ProcessingPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("CopyToWorkingDir", path, ex);
            return new SafeFileOperationResult(false, path, ex.Message);
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var workingFile = await CopyToWorkingDirAsync(path, "Hash", cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                workingFile.ProcessingPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("Hash", path, ex);
            throw;
        }
    }

    public static async Task<SafeFileOperationResult> WriteAllTextAsync(
        string path,
        string contents,
        Encoding? encoding,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (encoding is null)
            {
                await File.WriteAllTextAsync(fullPath, contents, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, contents, encoding, cancellationToken).ConfigureAwait(false);
            }

            DeploymentLogger.Log("SafeWrite", $"Wrote file {fullPath}");
            return new SafeFileOperationResult(true, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("WriteAllText", path, ex);
            return new SafeFileOperationResult(false, path, ex.Message);
        }
    }

    public static SafeFileOperationResult WriteAllText(
        string path,
        string contents,
        Encoding? encoding = null)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (encoding is null)
            {
                File.WriteAllText(fullPath, contents);
            }
            else
            {
                File.WriteAllText(fullPath, contents, encoding);
            }

            DeploymentLogger.Log("SafeWrite", $"Wrote file {fullPath}");
            return new SafeFileOperationResult(true, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("WriteAllText", path, ex);
            return new SafeFileOperationResult(false, path, ex.Message);
        }
    }

    public static SafeFileOperationResult Delete(string path)
    {
        return DeleteAsync(path, allowPermissionRepair: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<SafeFileOperationResult> DeleteAsync(
        string path,
        bool allowPermissionRepair,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(fullPath))
            {
                return new SafeFileOperationResult(false, fullPath, "File no longer exists.");
            }

            var state = OneDriveFileHandler.GetState(fullPath, checkInUse: true);
            if (state.RequiresWorkingCopy)
            {
                await using var workingFile = await CopyToWorkingDirAsync(fullPath, "Pre-delete OneDrive safety copy", cancellationToken)
                    .ConfigureAwait(false);

                state = OneDriveFileHandler.GetState(fullPath, checkInUse: true);
                if (state.RequiresWorkingCopy)
                {
                    var message = $"OneDrive is actively syncing, cloud-only, or the file is in use. Original was not deleted. {state.Summary}";
                    DeploymentLogger.Log("OneDriveDeleteDeferred", $"{message} Path={fullPath}; WorkingCopy={workingFile.ProcessingPath}");
                    return new SafeFileOperationResult(false, fullPath, message, workingFile.ProcessingPath);
                }
            }

            ClearReadOnlyAttribute(fullPath);
            var permission = HasPermissions(fullPath);
            if (!permission.HasModifyOrDelete)
            {
                if (!allowPermissionRepair)
                {
                    var message = $"{permission.Message} Run CopyFinder elevated or deploy the enterprise SYSTEM helper before repairing ownership.";
                    DeploymentLogger.Log("PermissionDenied", $"{fullPath}: {message}");
                    return new SafeFileOperationResult(false, fullPath, message);
                }

                var repair = await NtfsPermissionService.TryTakeOwnershipAndGrantModifyAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);
                if (!repair.Succeeded)
                {
                    return new SafeFileOperationResult(false, fullPath, repair.Message);
                }
            }

            await Task.Run(() =>
            {
                FileSystem.DeleteFile(
                    fullPath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
            }, cancellationToken).ConfigureAwait(false);

            DeploymentLogger.Log("SafeDelete", $"Moved to Recycle Bin where available: {fullPath}");
            return new SafeFileOperationResult(true, fullPath, "Moved to Recycle Bin where available.");
        }
        catch (UnauthorizedAccessException ex)
        {
            ControlledFolderAccessService.LogPossibleBlock("Delete", fullPath, ex);
            var cfa = ControlledFolderAccessService.GetStatus();
            var message = cfa.RequiresUserAction
                ? $"Access denied. Controlled Folder Access may be blocking CopyFinder. Allow this exact path: {cfa.ExecutablePath}"
                : $"Access denied: {ex.Message}";

            return new SafeFileOperationResult(false, fullPath, message);
        }
        catch (IOException ex)
        {
            DeploymentLogger.Log("SafeDelete", $"Delete failed for {fullPath}", ex);
            return new SafeFileOperationResult(false, fullPath, ex.Message);
        }
    }

    public static SafeFileOperationResult Unlock(string path)
    {
        return UnlockAsync(path, allowPermissionRepair: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<SafeFileOperationResult> UnlockAsync(
        string path,
        bool allowPermissionRepair,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var oneDriveState = OneDriveFileHandler.GetState(fullPath, checkInUse: true);
        if (oneDriveState.IsInUse)
        {
            var message = "File is in use by another process. CopyFinder will not force-close external handles.";
            DeploymentLogger.Log("Unlock", $"{message} Path={fullPath}");
            return new SafeFileOperationResult(false, fullPath, message);
        }

        ClearReadOnlyAttribute(fullPath);
        var permission = HasPermissions(fullPath);
        if (permission.HasModifyOrDelete)
        {
            return new SafeFileOperationResult(true, fullPath, "File is already writable by the current user.");
        }

        if (!allowPermissionRepair)
        {
            var message = "Permission repair was not requested. Enterprise deployments can install a SYSTEM helper service for controlled ownership repair.";
            DeploymentLogger.Log("Unlock", $"{message} Path={fullPath}");
            return new SafeFileOperationResult(false, fullPath, message);
        }

        var repair = await NtfsPermissionService.TryTakeOwnershipAndGrantModifyAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);

        return new SafeFileOperationResult(repair.Succeeded, fullPath, repair.Message);
    }

    private static void ClearReadOnlyAttribute(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return;
            }

            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            DeploymentLogger.Log("PermissionChange", $"Removed read-only attribute before operation: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ControlledFolderAccessService.LogPossibleBlock("PermissionChange", path, ex);
        }
    }
}
