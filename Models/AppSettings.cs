namespace CopyFinder.Models;

public sealed class AppSettings
{
    public string LastFolder { get; set; } = string.Empty;
    public int ScanDuplicateLimit { get; set; } = 500;
    public ScanOptions ScanOptions { get; set; } = new();
    public string LastCompatibilityReportVersion { get; set; } = string.Empty;
}
