using CopyFinder.Models;

namespace CopyFinder.Services;

public sealed class DeploymentCompatibilityChecker
{
    public Task<DeploymentCompatibilityReport> CheckAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workingDirectory = SafeFile.EnsureDirectory(SafeFile.WorkingDirectory);
            if (!workingDirectory.Succeeded)
            {
                DeploymentLogger.Log("Compatibility", $"Working directory creation failed: {workingDirectory.Message}");
            }

            var cfa = ControlledFolderAccessService.GetStatus(refresh: true);
            var oneDriveRoots = OneDriveFileHandler.GetKnownRoots();
            var permission = NtfsPermissionService.TestWorkingDirectory(SafeFile.WorkingDirectory);
            var logAvailable = DeploymentLogger.Log(
                "Compatibility",
                $"First-run compatibility check. CFA={cfa.Mode}; OneDriveRoots={oneDriveRoots.Count}; WorkingDirectory={SafeFile.WorkingDirectory}; Permission={permission.Message}");

            return new DeploymentCompatibilityReport(
                cfa,
                oneDriveRoots,
                permission,
                SafeFile.WorkingDirectory,
                DeploymentLogger.ActiveLogPath,
                logAvailable,
                NtfsPermissionService.IsElevated(),
                "Not installed. Normal mode uses current-user operations; enterprise deployments can add a SYSTEM helper for approved ownership repair.");
        }, cancellationToken);
    }
}
