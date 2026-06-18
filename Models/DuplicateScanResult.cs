namespace CopyFinder.Models;

public sealed record DuplicateScanResult(
    IReadOnlyList<DuplicateFile> Files,
    bool LimitReached,
    int DuplicateFileCount);
