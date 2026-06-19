namespace CopyFinder.Services;

public sealed record OneDriveFileState(
    string Path,
    bool IsInsideOneDrive,
    string? OneDriveRoot,
    bool IsCloudOnlyPlaceholder,
    bool IsSyncPending,
    bool IsInUse,
    FileAttributes Attributes)
{
    public bool RequiresWorkingCopy =>
        IsInsideOneDrive && (IsCloudOnlyPlaceholder || IsSyncPending || IsInUse);

    public string Summary
    {
        get
        {
            if (!IsInsideOneDrive)
            {
                return "Not inside a known OneDrive folder.";
            }

            var states = new List<string>();
            if (IsCloudOnlyPlaceholder)
            {
                states.Add("cloud-only or placeholder");
            }

            if (IsSyncPending)
            {
                states.Add("sync pending risk");
            }

            if (IsInUse)
            {
                states.Add("in use");
            }

            return states.Count == 0
                ? $"Inside OneDrive root {OneDriveRoot}; no lock indicators detected."
                : $"Inside OneDrive root {OneDriveRoot}; {string.Join(", ", states)}.";
        }
    }
}

public sealed record SafeWorkingFile(
    string OriginalPath,
    string ProcessingPath,
    bool IsTemporaryCopy,
    OneDriveFileState OneDriveState) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        if (!IsTemporaryCopy)
        {
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(ProcessingPath))
                {
                    File.SetAttributes(ProcessingPath, File.GetAttributes(ProcessingPath) & ~FileAttributes.ReadOnly);
                    File.Delete(ProcessingPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeploymentLogger.Log("OneDriveTempCleanup", $"Could not remove working copy {ProcessingPath}", ex);
            }
        }).ConfigureAwait(false);
    }
}

public static class OneDriveFileHandler
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes Pinned = (FileAttributes)0x00080000;
    private const FileAttributes Unpinned = (FileAttributes)0x00100000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static string WorkingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopyFinder",
            "Temp");

    public static IReadOnlyList<string> GetKnownRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoot(roots, Environment.GetEnvironmentVariable("OneDrive"));
        AddRoot(roots, Environment.GetEnvironmentVariable("OneDriveConsumer"));
        AddRoot(roots, Environment.GetEnvironmentVariable("OneDriveCommercial"));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(userProfile))
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(userProfile, "OneDrive*"))
                {
                    AddRoot(roots, directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeploymentLogger.Log("OneDrive", $"Could not enumerate OneDrive roots under {userProfile}", ex);
            }
        }

        return roots
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static OneDriveFileState GetState(string path, bool checkInUse)
    {
        var fullPath = Path.GetFullPath(path);
        var root = FindRoot(fullPath);
        var attributes = GetAttributes(fullPath);
        var isInsideOneDrive = root is not null;
        var isCloudOnly = isInsideOneDrive && IsCloudOnlyOrPlaceholder(attributes);
        var isInUse = checkInUse && File.Exists(fullPath) && IsFileInUse(fullPath);
        var syncPending = isInsideOneDrive && (isCloudOnly || isInUse);

        var state = new OneDriveFileState(
            fullPath,
            isInsideOneDrive,
            root,
            isCloudOnly,
            syncPending,
            isInUse,
            attributes);

        if (state.RequiresWorkingCopy)
        {
            DeploymentLogger.Log("OneDriveLock", $"{state.Summary} Path={fullPath}");
        }

        return state;
    }

    public static async Task<SafeWorkingFile> CopyToWorkingDirectoryAsync(
        string path,
        string reason,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var state = GetState(fullPath, checkInUse: true);
        if (!state.RequiresWorkingCopy)
        {
            return new SafeWorkingFile(fullPath, fullPath, IsTemporaryCopy: false, state);
        }

        Directory.CreateDirectory(WorkingDirectory);
        var extension = Path.GetExtension(fullPath);
        var destination = Path.Combine(WorkingDirectory, $"{Guid.NewGuid():N}{extension}");

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(fullPath, destination, overwrite: false);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
        }, cancellationToken).ConfigureAwait(false);

        DeploymentLogger.Log("OneDriveFallback", $"Copied {fullPath} to working file {destination}. Reason={reason}; {state.Summary}");
        return new SafeWorkingFile(fullPath, destination, IsTemporaryCopy: true, state);
    }

    public static bool IsOneDriveLocked(string path)
    {
        return GetState(path, checkInUse: true).RequiresWorkingCopy;
    }

    private static void AddRoot(ISet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                roots.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch
        {
            // Ignore malformed environment paths.
        }
    }

    private static string? FindRoot(string fullPath)
    {
        foreach (var root in GetKnownRoots())
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedRoot;
            }
        }

        return null;
    }

    private static FileAttributes GetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeploymentLogger.Log("OneDrive", $"Could not read attributes for {path}", ex);
            return 0;
        }
    }

    private static bool IsCloudOnlyOrPlaceholder(FileAttributes attributes)
    {
        return attributes.HasFlag(FileAttributes.Offline) ||
               attributes.HasFlag(FileAttributes.ReparsePoint) ||
               attributes.HasFlag(RecallOnOpen) ||
               attributes.HasFlag(RecallOnDataAccess) ||
               attributes.HasFlag(Unpinned) ||
               attributes.HasFlag(Pinned) && attributes.HasFlag(FileAttributes.Offline);
    }

    private static bool IsFileInUse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
