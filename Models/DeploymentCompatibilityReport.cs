using CopyFinder.Services;

namespace CopyFinder.Models;

public sealed record DeploymentCompatibilityReport(
    ControlledFolderAccessStatus ControlledFolderAccess,
    IReadOnlyList<string> OneDriveRoots,
    PermissionCheckResult WorkingDirectoryPermission,
    string WorkingDirectory,
    string DeploymentLogPath,
    bool DeploymentLogAvailable,
    bool IsElevated,
    string EnterpriseHelperServiceStatus)
{
    public bool RequiresAttention =>
        ControlledFolderAccess.RequiresUserAction ||
        !WorkingDirectoryPermission.HasModifyOrDelete ||
        !DeploymentLogAvailable;

    public string ToUserMessage()
    {
        var oneDriveText = OneDriveRoots.Count == 0
            ? "No OneDrive roots were detected for the current user."
            : string.Join(Environment.NewLine, OneDriveRoots.Select(root => $"  - {root}"));

        return
            "CopyFinder deployment compatibility report" + Environment.NewLine + Environment.NewLine +
            $"Controlled Folder Access: {ControlledFolderAccess.Mode}" + Environment.NewLine +
            $"{ControlledFolderAccess.UserGuidance}" + Environment.NewLine +
            $"Allow path: {ControlledFolderAccess.ExecutablePath}" + Environment.NewLine +
            $"Elevated allow command: {ControlledFolderAccess.AllowApplicationCommand}" + Environment.NewLine + Environment.NewLine +
            "OneDrive roots:" + Environment.NewLine +
            $"{oneDriveText}" + Environment.NewLine + Environment.NewLine +
            $"Working directory: {WorkingDirectory}" + Environment.NewLine +
            $"Working directory permission: {WorkingDirectoryPermission.Message}" + Environment.NewLine +
            $"Deployment log: {DeploymentLogPath}" + Environment.NewLine +
            $"Deployment log write test: {(DeploymentLogAvailable ? "OK" : "Failed")}" + Environment.NewLine +
            $"Process elevated: {(IsElevated ? "Yes" : "No")}" + Environment.NewLine +
            $"Enterprise helper: {EnterpriseHelperServiceStatus}";
    }
}
