using System.Diagnostics;
using CopyFinder.Models;
using CopyFinder.Services;
using System.Security.Cryptography;

var tests = new (string Name, Func<Task> Test)[]
{
    ("scanner skips junction folders by default", ScannerSkipsJunctionFoldersByDefault),
    ("scanner skips junction root by default", ScannerSkipsJunctionRootByDefault),
    ("scanner ignores invalid preferred folder", ScannerIgnoresInvalidPreferredFolder),
    ("scanner stops hashing current group after duplicate limit", ScannerStopsHashingCurrentGroupAfterDuplicateLimit),
    ("scanner stress keeps duplicate limit bounded", ScannerStressKeepsDuplicateLimitBounded),
    ("publish script removes pdb files before zip", PublishScriptRemovesPdbFilesBeforeZip),
    ("version stamp is 2.0.3", VersionStampIsTwoPointZeroThree),
    ("technification assets are complete", TechnificationAssetsAreComplete),
    ("delete validator accepts unchanged duplicate", DeleteValidatorAcceptsUnchangedDuplicate),
    ("delete validator rejects changed duplicate", DeleteValidatorRejectsChangedDuplicate)
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

Console.WriteLine("All tests passed.");

static async Task ScannerSkipsJunctionFoldersByDefault()
{
    var workspace = FindWorkspaceRoot();
    var artifactRoot = Path.Combine(workspace, "TestArtifacts", Guid.NewGuid().ToString("N"));
    var scanRoot = Path.Combine(artifactRoot, "scan-root");
    var outsideRoot = Path.Combine(artifactRoot, "outside-target");
    var junctionPath = Path.Combine(scanRoot, "linked-outside");

    Directory.CreateDirectory(scanRoot);
    Directory.CreateDirectory(outsideRoot);

    try
    {
        await File.WriteAllTextAsync(Path.Combine(scanRoot, "unique.txt"), "inside");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "b.txt"), "same duplicate content");
        CreateJunction(junctionPath, outsideRoot);

        var scanner = new DuplicateScanner();
        var result = await scanner.FindDuplicatesAsync(
            scanRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress: null,
            CancellationToken.None);

        AssertEqual(0, result.Files.Count, "Junction target files should not be included in scan results.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task ScannerSkipsJunctionRootByDefault()
{
    var workspace = FindWorkspaceRoot();
    var artifactRoot = Path.Combine(workspace, "TestArtifacts", Guid.NewGuid().ToString("N"));
    var outsideRoot = Path.Combine(artifactRoot, "outside-target");
    var junctionRoot = Path.Combine(artifactRoot, "scan-root-link");

    Directory.CreateDirectory(artifactRoot);
    Directory.CreateDirectory(outsideRoot);

    try
    {
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "b.txt"), "same duplicate content");
        CreateJunction(junctionRoot, outsideRoot);

        var scanner = new DuplicateScanner();
        var result = await scanner.FindDuplicatesAsync(
            junctionRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress: null,
            CancellationToken.None);

        AssertEqual(0, result.Files.Count, "A linked scan root should not include target files by default.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task DeleteValidatorAcceptsUnchangedDuplicate()
{
    var artifactRoot = CreateArtifactRoot();
    try
    {
        var keepPath = Path.Combine(artifactRoot, "keep.txt");
        var duplicatePath = Path.Combine(artifactRoot, "duplicate.txt");
        await File.WriteAllTextAsync(keepPath, "same duplicate content");
        await File.WriteAllTextAsync(duplicatePath, "same duplicate content");

        var expectedHash = ComputeSha256(duplicatePath);
        var candidate = new DuplicateDeleteCandidate(
            GroupId: 1,
            DuplicatePath: duplicatePath,
            KeptPath: keepPath,
            ExpectedSize: new FileInfo(duplicatePath).Length,
            ExpectedHash: expectedHash);

        var result = await DuplicateDeleteValidator.ValidateAsync(candidate, CancellationToken.None);

        AssertTrue(result.CanDelete, result.FailureMessage ?? "Expected duplicate to be eligible for deletion.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static Task PublishScriptRemovesPdbFilesBeforeZip()
{
    var scriptPath = Path.Combine(FindWorkspaceRoot(), "publish.ps1");
    var script = File.ReadAllText(scriptPath);
    var removePdbIndex = script.IndexOf("-Filter '*.pdb'", StringComparison.OrdinalIgnoreCase);
    var zipIndex = script.IndexOf("Compress-Archive", StringComparison.OrdinalIgnoreCase);

    AssertTrue(removePdbIndex >= 0, "publish.ps1 should remove PDB files from normal standalone output.");
    AssertTrue(zipIndex >= 0, "publish.ps1 should create a zip archive.");
    AssertTrue(removePdbIndex < zipIndex, "PDB removal should happen before Compress-Archive.");

    return Task.CompletedTask;
}

static Task VersionStampIsTwoPointZeroThree()
{
    var workspace = FindWorkspaceRoot();
    var project = File.ReadAllText(Path.Combine(workspace, "CopyFinder.csproj"));
    var manifest = File.ReadAllText(Path.Combine(workspace, "app.manifest"));
    var mainWindow = File.ReadAllText(Path.Combine(workspace, "MainWindow.xaml"));
    var readme = File.ReadAllText(Path.Combine(workspace, "README.md"));

    AssertContains("<Version>2.0.3</Version>", project);
    AssertContains("<AssemblyVersion>2.0.3.0</AssemblyVersion>", project);
    AssertContains("<FileVersion>2.0.3.0</FileVersion>", project);
    AssertContains("<InformationalVersion>2.0.3</InformationalVersion>", project);
    AssertContains("assemblyIdentity version=\"2.0.3.0\"", manifest);
    AssertContains("Text=\"2.0.3\"", mainWindow);
    AssertContains("Version: 2.0.3", readme);

    return Task.CompletedTask;
}

static Task TechnificationAssetsAreComplete()
{
    var workspace = FindWorkspaceRoot();
    var project = File.ReadAllText(Path.Combine(workspace, "CopyFinder.csproj"));
    var fileViewModel = File.ReadAllText(Path.Combine(workspace, "ViewModels", "DuplicateFileViewModel.cs"));

    var requiredAssets = new[]
    {
        Path.Combine("Technification", "Logo", "favicon", "favicon.ico"),
        Path.Combine("Technification", "Logo", "logo-Master.png"),
        Path.Combine("Technification", "FileIcons", "file-archive.png"),
        Path.Combine("Technification", "FileIcons", "file-audio.png"),
        Path.Combine("Technification", "FileIcons", "file-code.png"),
        Path.Combine("Technification", "FileIcons", "file-generic.png"),
        Path.Combine("Technification", "FileIcons", "file-image.png"),
        Path.Combine("Technification", "FileIcons", "file-pdf.png"),
        Path.Combine("Technification", "FileIcons", "file-presentation.png"),
        Path.Combine("Technification", "FileIcons", "file-spreadsheet.png"),
        Path.Combine("Technification", "FileIcons", "file-text.png"),
        Path.Combine("Technification", "FileIcons", "file-video.png"),
        Path.Combine("Technification", "FileIcons", "file-word.png")
    };

    foreach (var relativePath in requiredAssets)
    {
        var fullPath = Path.Combine(workspace, relativePath);
        AssertTrue(File.Exists(fullPath), $"Required Technification asset is missing: {relativePath}");
    }

    AssertContains(@"<Content Include=""Technification\FileIcons\*.png""", project);
    AssertContains("ms-appx:///Technification/FileIcons/", fileViewModel);
    AssertFalse(project.Contains("Old.Logo", StringComparison.OrdinalIgnoreCase), "CopyFinder.csproj should not reference the retired Old.Logo folder.");
    AssertFalse(fileViewModel.Contains("Old.Logo", StringComparison.OrdinalIgnoreCase), "ViewModel icon URI should not reference the retired Old.Logo folder.");

    return Task.CompletedTask;
}

static async Task ScannerStopsHashingCurrentGroupAfterDuplicateLimit()
{
    var artifactRoot = CreateArtifactRoot();
    try
    {
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "b.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "c.txt"), "same duplicate content");

        var hashingMessages = new List<string>();
        var progress = new DelegateProgress<ScanProgress>(message =>
        {
            if (message.Message.StartsWith("Hashing ", StringComparison.Ordinal))
            {
                hashingMessages.Add(message.Message);
            }
        });

        var scanner = new DuplicateScanner();
        var result = await scanner.FindDuplicatesAsync(
            artifactRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 1,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress,
            CancellationToken.None);

        AssertEqual(2, result.Files.Count, "The limited result should contain one kept file and one duplicate.");
        AssertEqual(1, result.DuplicateFileCount, "The duplicate count should stop at the configured limit.");
        AssertTrue(result.LimitReached, "The scanner should report that the duplicate limit was reached.");
        AssertTrue(hashingMessages.Count <= 2, $"Expected at most 2 hashing operations, got {hashingMessages.Count}.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task ScannerStressKeepsDuplicateLimitBounded()
{
    var artifactRoot = CreateArtifactRoot();
    try
    {
        for (var index = 0; index < 256; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(artifactRoot, $"duplicate-{index:000}.txt"),
                "same duplicate content");
        }

        var hashingMessages = new List<string>();
        var progress = new DelegateProgress<ScanProgress>(message =>
        {
            if (message.Message.StartsWith("Hashing ", StringComparison.Ordinal))
            {
                hashingMessages.Add(message.Message);
            }
        });

        var scanner = new DuplicateScanner();
        var result = await scanner.FindDuplicatesAsync(
            artifactRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 25,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress,
            CancellationToken.None);

        AssertEqual(26, result.Files.Count, "The limited stress result should contain one kept file and 25 duplicates.");
        AssertEqual(25, result.DuplicateFileCount, "The duplicate count should stop at the configured stress limit.");
        AssertTrue(result.LimitReached, "The scanner should report that the stress duplicate limit was reached.");
        AssertTrue(hashingMessages.Count <= 26, $"Expected at most 26 hashing operations, got {hashingMessages.Count}.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task ScannerIgnoresInvalidPreferredFolder()
{
    var artifactRoot = CreateArtifactRoot();
    try
    {
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "b.txt"), "same duplicate content");

        var scanner = new DuplicateScanner();
        var result = await scanner.FindDuplicatesAsync(
            artifactRoot,
            new ScanOptions
            {
                KeepRule = KeepRule.PreferFolder,
                PreferredFolder = "C:\\bad\0folder",
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress: null,
            CancellationToken.None);

        AssertEqual(2, result.Files.Count, "Invalid preferred folder should not abort duplicate detection.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task DeleteValidatorRejectsChangedDuplicate()
{
    var artifactRoot = CreateArtifactRoot();
    try
    {
        var keepPath = Path.Combine(artifactRoot, "keep.txt");
        var duplicatePath = Path.Combine(artifactRoot, "duplicate.txt");
        await File.WriteAllTextAsync(keepPath, "same duplicate content");
        await File.WriteAllTextAsync(duplicatePath, "same duplicate content");

        var expectedHash = ComputeSha256(duplicatePath);
        var candidate = new DuplicateDeleteCandidate(
            GroupId: 1,
            DuplicatePath: duplicatePath,
            KeptPath: keepPath,
            ExpectedSize: new FileInfo(duplicatePath).Length,
            ExpectedHash: expectedHash);

        await File.WriteAllTextAsync(duplicatePath, "changed content");

        var result = await DuplicateDeleteValidator.ValidateAsync(candidate, CancellationToken.None);

        AssertFalse(result.CanDelete, "Changed duplicate should not be eligible for deletion.");
        AssertContains("changed", result.FailureMessage ?? string.Empty);
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static string FindWorkspaceRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (File.Exists(Path.Combine(current, "CopyFinder.csproj")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName ?? string.Empty;
    }

    throw new InvalidOperationException("Could not locate CopyFinder.csproj from test output path.");
}

static string CreateArtifactRoot()
{
    var path = Path.Combine(FindWorkspaceRoot(), "TestArtifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void CreateJunction(string junctionPath, string targetPath)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    });

    if (process is null)
    {
        throw new InvalidOperationException("Could not start mklink process.");
    }

    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"mklink failed with exit code {process.ExitCode}: {output}{error}");
    }
}

static void DeleteTestDirectory(string artifactRoot)
{
    var workspace = FindWorkspaceRoot();
    var fullArtifactRoot = Path.GetFullPath(artifactRoot);
    var allowedRoot = Path.GetFullPath(Path.Combine(workspace, "TestArtifacts"));
    if (!fullArtifactRoot.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Refusing to delete outside test artifacts: {fullArtifactRoot}");
    }

    if (Directory.Exists(fullArtifactRoot))
    {
        foreach (var reparsePoint in Directory
                     .EnumerateDirectories(fullArtifactRoot, "*", SearchOption.AllDirectories)
                     .Where(IsReparsePoint)
                     .OrderByDescending(path => path.Length))
        {
            Directory.Delete(reparsePoint);
        }

        Directory.Delete(fullArtifactRoot, recursive: true);
    }
}

static bool IsReparsePoint(string path)
{
    try
    {
        return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
    catch (IOException)
    {
        return false;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
    {
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertContains(string expectedSubstring, string actual)
{
    if (!actual.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expectedSubstring}'.");
    }
}

static string ComputeSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

internal sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value)
    {
        report(value);
    }
}
