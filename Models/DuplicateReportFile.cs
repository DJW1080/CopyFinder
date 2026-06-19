namespace CopyFinder.Models;

public sealed record DuplicateReportFile(
    int GroupId,
    string Role,
    bool IsSelected,
    bool IsOriginal,
    long Size,
    string Hash,
    int? ImageWidth,
    int? ImageHeight,
    DateTime LastWriteTime,
    string Path);
