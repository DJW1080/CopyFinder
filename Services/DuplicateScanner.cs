using System.Collections.Concurrent;
using System.Security.Cryptography;
using CopyFinder.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace CopyFinder.Services;

public sealed record ScanProgress(string Message);

public sealed class DuplicateScanner
{
    public async Task<DuplicateScanResult> FindDuplicatesAsync(
        string rootDirectory,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidatesBySize = EnumerateFiles(rootDirectory, options, progress, cancellationToken)
            .GroupBy(file => file.Length)
            .Where(group => group.Key > 0 && group.Count() > 1)
            .ToList();

        var duplicateFiles = new List<DuplicateFile>();
        var groupId = 1;
        var duplicateFileCount = 0;
        var limitReached = false;
        var maxDuplicateFiles = Math.Max(1, options.MaxDuplicateFiles);

        foreach (var sizeGroup in candidatesBySize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (limitReached)
            {
                break;
            }

            var duplicateSlotsRemaining = maxDuplicateFiles - duplicateFileCount;
            var hashGroups = await HashSizeGroupAsync(
                sizeGroup.ToList(),
                duplicateSlotsRemaining,
                options,
                progress,
                cancellationToken);

            foreach (var hashGroup in hashGroups)
            {
                if (duplicateFileCount >= maxDuplicateFiles)
                {
                    limitReached = true;
                    break;
                }

                var imageMetadata = await ReadImageMetadataAsync(hashGroup.Files, cancellationToken);
                var orderedFiles = hashGroup.Files
                    .OrderBy(file => GetPrimaryKeepScore(file, options, imageMetadata))
                    .ThenBy(GetCopyNameScore)
                    .ThenBy(file => Path.GetFileNameWithoutExtension(file.Name).Length)
                    .ThenBy(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                duplicateSlotsRemaining = maxDuplicateFiles - duplicateFileCount;
                var filesToReturn = orderedFiles.Take((int)Math.Min(orderedFiles.Count, duplicateSlotsRemaining + 1)).ToList();

                for (var index = 0; index < filesToReturn.Count; index++)
                {
                    var file = filesToReturn[index];
                    imageMetadata.TryGetValue(file.FullName, out var metadata);
                    duplicateFiles.Add(new DuplicateFile(
                        groupId,
                        file.FullName,
                        file.Length,
                        hashGroup.Hash,
                        file.LastWriteTime,
                        metadata.Width,
                        metadata.Height,
                        index == 0));
                }

                duplicateFileCount += Math.Max(0, filesToReturn.Count - 1);
                limitReached = duplicateFileCount >= maxDuplicateFiles && orderedFiles.Count > filesToReturn.Count;
                if (duplicateFileCount >= maxDuplicateFiles)
                {
                    limitReached = true;
                }

                groupId++;
            }
        }

        return new DuplicateScanResult(duplicateFiles, limitReached, duplicateFileCount);
    }

    private static async Task<IReadOnlyList<HashGroup>> HashSizeGroupAsync(
        IReadOnlyList<FileInfo> files,
        int duplicateSlotsRemaining,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (duplicateSlotsRemaining <= 0)
        {
            return [];
        }

        return files.Count > duplicateSlotsRemaining + 1
            ? await HashSizeGroupUntilLimitAsync(files, duplicateSlotsRemaining, progress, cancellationToken)
            : await HashSizeGroupInParallelAsync(files, options, progress, cancellationToken);
    }

    private static async Task<IReadOnlyList<HashGroup>> HashSizeGroupUntilLimitAsync(
        IEnumerable<FileInfo> files,
        int duplicateSlotsRemaining,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filesByHash = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        var projectedDuplicateFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress($"Hashing {file.FullName}"));

            try
            {
                var hash = await ComputeSha256Async(file.FullName, cancellationToken);
                if (!filesByHash.TryGetValue(hash, out var matchingFiles))
                {
                    matchingFiles = [];
                    filesByHash[hash] = matchingFiles;
                }

                matchingFiles.Add(file);
                if (matchingFiles.Count > 1)
                {
                    projectedDuplicateFiles++;
                    if (projectedDuplicateFiles >= duplicateSlotsRemaining)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                progress?.Report(new ScanProgress($"Skipped file: {file.FullName}"));
            }
        }

        return filesByHash
            .Where(group => group.Value.Count > 1)
            .Select(group => new HashGroup(group.Key, group.Value))
            .ToList();
    }

    private static async Task<IReadOnlyList<HashGroup>> HashSizeGroupInParallelAsync(
        IEnumerable<FileInfo> files,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filesByHash = new ConcurrentDictionary<string, ConcurrentBag<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(options.HashParallelism, 1, 16)
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, token) =>
        {
            progress?.Report(new ScanProgress($"Hashing {file.FullName}"));

            try
            {
                var hash = await ComputeSha256Async(file.FullName, token);
                filesByHash.GetOrAdd(hash, _ => []).Add(file);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                progress?.Report(new ScanProgress($"Skipped file: {file.FullName}"));
            }
        });

        return filesByHash
            .Select(group => new HashGroup(group.Key, group.Value.ToList()))
            .Where(group => group.Files.Count > 1)
            .ToList();
    }

    private static IEnumerable<FileInfo> EnumerateFiles(
        string rootDirectory,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(rootDirectory);
        var seen = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pending.Pop();
            if (ShouldSkipDirectory(currentDirectory, progress))
            {
                continue;
            }

            if (!TryMarkDirectoryVisited(currentDirectory, visitedDirectories))
            {
                continue;
            }

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(currentDirectory).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                progress?.Report(new ScanProgress($"Skipped folder: {currentDirectory}"));
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                if (ShouldSkipDirectory(subdirectory, progress))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                progress?.Report(new ScanProgress($"Skipped folder: {currentDirectory}"));
                continue;
            }

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                    if (!file.Exists || ShouldSkipFile(file, options))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    progress?.Report(new ScanProgress($"Skipped file: {path}"));
                    continue;
                }

                seen++;
                if (seen % 100 == 0)
                {
                    progress?.Report(new ScanProgress($"Found {seen:N0} files"));
                }

                yield return file;
            }
        }
    }

    private static bool TryMarkDirectoryVisited(string directory, ISet<string> visitedDirectories)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return visitedDirectories.Add(normalizedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ShouldSkipDirectory(string directory, IProgress<ScanProgress>? progress)
    {
        try
        {
            var attributes = new DirectoryInfo(directory).Attributes;
            if (!attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
            {
                return false;
            }

            progress?.Report(new ScanProgress($"Skipped linked folder: {directory}"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress?.Report(new ScanProgress($"Skipped folder: {directory}"));
            return true;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static long GetPrimaryKeepScore(
        FileInfo file,
        ScanOptions options,
        IReadOnlyDictionary<string, ImageMetadata> imageMetadata)
    {
        return options.KeepRule switch
        {
            KeepRule.PreferOriginalName => GetCopyNameScore(file),
            KeepRule.PreferShortestName => Path.GetFileNameWithoutExtension(file.Name).Length,
            KeepRule.PreferOldestFile => file.LastWriteTimeUtc.Ticks,
            KeepRule.PreferNewestFile => -file.LastWriteTimeUtc.Ticks,
            KeepRule.PreferFolder => IsInPreferredFolder(file, options.PreferredFolder) ? 0 : 1,
            KeepRule.PreferHighestResolution => imageMetadata.TryGetValue(file.FullName, out var metadata)
                ? -metadata.PixelCount
                : long.MaxValue,
            _ => GetCopyNameScore(file)
        };
    }

    private static int GetCopyNameScore(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var normalizedName = name.Trim().ToLowerInvariant();

        if (normalizedName.EndsWith(" - copy", StringComparison.Ordinal) ||
            normalizedName.EndsWith(" copy", StringComparison.Ordinal) ||
            normalizedName.EndsWith("_copy", StringComparison.Ordinal))
        {
            return 10;
        }

        if (EndsWithParenthesizedNumber(normalizedName))
        {
            return 20;
        }

        return 0;
    }

    private static bool IsInPreferredFolder(FileInfo file, string preferredFolder)
    {
        if (string.IsNullOrWhiteSpace(preferredFolder))
        {
            return false;
        }

        if (!TryNormalizeDirectoryPath(preferredFolder, out var normalizedPreferredFolder) ||
            !TryNormalizeFilePath(file.FullName, out var normalizedFilePath))
        {
            return false;
        }

        return normalizedFilePath.StartsWith(normalizedPreferredFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeDirectoryPath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizeFilePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static bool ShouldSkipFile(FileInfo file, ScanOptions options)
    {
        if (file.Length < options.MinimumFileSizeBytes)
        {
            return true;
        }

        if (options.SkipHiddenFiles && file.Attributes.HasFlag(System.IO.FileAttributes.Hidden))
        {
            return true;
        }

        if (options.SkipSystemFiles && file.Attributes.HasFlag(System.IO.FileAttributes.System))
        {
            return true;
        }

        if (options.ExcludedExtensions.Count == 0)
        {
            return false;
        }

        var extension = file.Extension.TrimStart('.').ToLowerInvariant();
        return options.ExcludedExtensions.Any(excluded =>
            string.Equals(excluded.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Dictionary<string, ImageMetadata>> ReadImageMetadataAsync(
        IEnumerable<FileInfo> files,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metadata[file.FullName] = await TryReadImageMetadataAsync(file.FullName);
        }

        return metadata;
    }

    private static async Task<ImageMetadata> TryReadImageMetadataAsync(string path)
    {
        if (!IsSupportedImageFile(path))
        {
            return ImageMetadata.Empty;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return new ImageMetadata((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return ImageMetadata.Empty;
        }
    }

    private static bool IsSupportedImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" => true,
            _ => false
        };
    }

    private static bool EndsWithParenthesizedNumber(string value)
    {
        if (!value.EndsWith(')'))
        {
            return false;
        }

        var openParenthesis = value.LastIndexOf('(');
        if (openParenthesis < 0 || openParenthesis == value.Length - 2)
        {
            return false;
        }

        for (var index = openParenthesis + 1; index < value.Length - 1; index++)
        {
            if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct ImageMetadata(int? Width, int? Height)
    {
        public static ImageMetadata Empty { get; } = new(null, null);

        public long PixelCount =>
            Width is null || Height is null ? 0 : (long)Width.Value * Height.Value;
    }

    private sealed record HashGroup(string Hash, List<FileInfo> Files);
}
