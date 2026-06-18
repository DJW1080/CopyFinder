namespace CopyFinder.Models;

public sealed record DuplicateFile(
    int GroupId,
    string Path,
    long Size,
    string Hash,
    DateTime LastWriteTime,
    int? ImageWidth,
    int? ImageHeight,
    bool IsOriginal);
