namespace CopyFinder.Models;

public sealed class ScanOptions
{
    public int MaxDuplicateFiles { get; set; } = 500;
    public int HashParallelism { get; set; } = 2;
    public KeepRule KeepRule { get; set; } = KeepRule.PreferOriginalName;
    public string PreferredFolder { get; set; } = string.Empty;
    public long MinimumFileSizeBytes { get; set; }
    public bool SkipHiddenFiles { get; set; } = true;
    public bool SkipSystemFiles { get; set; } = true;
    public List<string> ExcludedExtensions { get; set; } = [];
}
