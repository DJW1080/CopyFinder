namespace CopyFinder.Services;

public static class DeploymentLogger
{
    private static readonly object SyncRoot = new();
    private static string? _overrideLogPath;

    public static string DefaultLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CopyFinder",
            "Logs",
            "deployment.log");

    public static string ActiveLogPath { get; private set; } = DefaultLogPath;

    public static void UseLogPathForTests(string logPath)
    {
        lock (SyncRoot)
        {
            _overrideLogPath = logPath;
            ActiveLogPath = logPath;
        }
    }

    public static void ResetLogPathForTests()
    {
        lock (SyncRoot)
        {
            _overrideLogPath = null;
            ActiveLogPath = DefaultLogPath;
        }
    }

    public static bool Log(string category, string message, Exception? exception = null)
    {
        var line = FormatLine(category, message, exception);
        lock (SyncRoot)
        {
            var primaryPath = _overrideLogPath ?? DefaultLogPath;
            if (TryAppend(primaryPath, line))
            {
                ActiveLogPath = primaryPath;
                return true;
            }

            var fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CopyFinder",
                "Logs",
                "deployment.log");

            var fallbackLine = FormatLine(
                "LogFallback",
                $"ProgramData deployment log could not be written. Primary path: {primaryPath}. Original entry: {line}",
                null);

            if (TryAppend(fallbackPath, fallbackLine))
            {
                ActiveLogPath = fallbackPath;
                return false;
            }
        }

        return false;
    }

    private static string FormatLine(string category, string message, Exception? exception)
    {
        var safeCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        var exceptionText = exception is null
            ? string.Empty
            : $" Exception={exception.GetType().Name}: {exception.Message}";

        return $"{DateTimeOffset.Now:O} [{safeCategory}] {message}{exceptionText}{Environment.NewLine}";
    }

    private static bool TryAppend(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, line);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
