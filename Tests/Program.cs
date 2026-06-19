using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CopyFinder.Models;
using CopyFinder.Services;

const int PerTestTimeoutMinutes = 2;

var tests = new (string Name, Func<Task> Test)[]
{
    ("scanner skips junction folders by default", ScannerSkipsJunctionFoldersByDefault),
    ("scanner skips junction root by default", ScannerSkipsJunctionRootByDefault),
    ("scanner ignores invalid preferred folder", ScannerIgnoresInvalidPreferredFolder),
    ("scanner uses bounded workflow fixture", ScannerUsesBoundedWorkflowFixture),
    ("scanner stops hashing current group after duplicate limit", ScannerStopsHashingCurrentGroupAfterDuplicateLimit),
    ("scanner stress keeps duplicate limit bounded", ScannerStressKeepsDuplicateLimitBounded),
    ("publish script removes pdb files before zip", PublishScriptRemovesPdbFilesBeforeZip),
    ("project excludes generated release content", ProjectExcludesGeneratedReleaseContent),
    ("version stamp is 2.0.9", VersionStampIsTwoPointZeroNine),
    ("install instructions cover deployment steps", InstallInstructionsCoverDeploymentSteps),
    ("github workflow builds and publishes standalone zip", GitHubWorkflowBuildsAndPublishesStandaloneZip),
    ("technification assets are complete", TechnificationAssetsAreComplete),
    ("report formatter exports csv and json", ReportFormatterExportsCsvAndJson),
    ("delete validator accepts unchanged duplicate", DeleteValidatorAcceptsUnchangedDuplicate),
    ("delete validator rejects changed duplicate", DeleteValidatorRejectsChangedDuplicate),
    ("safe file hashes through wrapper", SafeFileHashesThroughWrapper),
    ("safe file deletes through wrapper", SafeFileDeletesThroughWrapper),
    ("safe file writes deployment log to configured path", SafeFileWritesDeploymentLogToConfiguredPath),
    ("safe file detects configured OneDrive root", SafeFileDetectsConfiguredOneDriveRoot),
    ("main window does not delete directly", MainWindowDoesNotDeleteDirectly),
    ("compatibility report includes CFA allow path", CompatibilityReportIncludesCfaAllowPath)
};

var harnessStopwatch = Stopwatch.StartNew();
var failures = new List<string>();

LogBanner("CopyFinder regression harness starting");
LogEnvironment();

foreach (var (name, test) in tests)
{
    var testStopwatch = Stopwatch.StartNew();
    Log($"START {name}");

    try
    {
        await ExecuteWithTimeoutAsync(test, TimeSpan.FromMinutes(PerTestTimeoutMinutes), name);
        testStopwatch.Stop();
        Log($"PASS {name} ({testStopwatch.Elapsed.TotalSeconds:N2}s)");
    }
    catch (Exception ex)
    {
        testStopwatch.Stop();
        failures.Add($"{name}: {ex.Message}");
        Log($"FAIL {name} ({testStopwatch.Elapsed.TotalSeconds:N2}s)");
        Log(ex.ToString());
    }
}

harnessStopwatch.Stop();

if (failures.Count > 0)
{
    Log(string.Empty);
    Log($"{failures.Count} test(s) failed after {harnessStopwatch.Elapsed.TotalSeconds:N2}s total.");
    Environment.Exit(1);
}

Log($"All tests passed in {harnessStopwatch.Elapsed.TotalSeconds:N2}s.");

static async Task ExecuteWithTimeoutAsync(Func<Task> test, TimeSpan timeout, string testName)
{
    using var cts = new CancellationTokenSource();
    var testTask = test();
    var timeoutTask = Task.Delay(timeout, cts.Token);

    var completed = await Task.WhenAny(testTask, timeoutTask);
    if (completed == timeoutTask)
    {
        throw new TimeoutException($"Test timed out after {timeout.TotalMinutes:N0} minute(s): {testName}");
    }

    cts.Cancel();
    await testTask;
}

static void LogBanner(string message)
{
    Console.WriteLine(new string('=', 80));
    Console.WriteLine(message);
    Console.WriteLine(new string('=', 80));
    Console.Out.Flush();
}

static void Log(string message)
{
    Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}");
    Console.Out.Flush();
}

static void LogEnvironment()
{
    Log($"AppContext.BaseDirectory = {AppContext.BaseDirectory}");
    Log($"CurrentDirectory = {Environment.CurrentDirectory}");
    Log($"OSVersion = {Environment.OSVersion}");
    Log($"ProcessPath = {Environment.ProcessPath ?? "(null)"}");
    Log($"GITHUB_ACTIONS = {Environment.GetEnvironmentVariable("GITHUB_ACTIONS") ?? "(null)"}");
    Log($"CI = {Environment.GetEnvironmentVariable("CI") ?? "(null)"}");
    Log($"RUNNER_TEMP = {Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? "(null)"}");
    Log($"COPYFINDER_WORKFLOW_SCAN_ROOT = {Environment.GetEnvironmentVariable("COPYFINDER_WORKFLOW_SCAN_ROOT") ?? "(null)"}");
    Log($"OneDriveConsumer = {Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "(null)"}");
}

static async Task ScannerSkipsJunctionFoldersByDefault()
{
    Log("  [diag] ScannerSkipsJunctionFoldersByDefault entered");

    var workspace = FindWorkspaceRoot();
    Log($"  [diag] workspace = {workspace}");

    var artifactRoot = Path.Combine(workspace, "TestArtifacts", Guid.NewGuid().ToString("N"));
    var scanRoot = Path.Combine(artifactRoot, "scan-root");
    var outsideRoot = Path.Combine(artifactRoot, "outside-target");
    var junctionPath = Path.Combine(scanRoot, "linked-outside");

    Directory.CreateDirectory(scanRoot);
    Directory.CreateDirectory(outsideRoot);

    Log($"  [diag] artifactRoot = {artifactRoot}");
    Log($"  [diag] scanRoot = {scanRoot}");
    Log($"  [diag] outsideRoot = {outsideRoot}");
    Log($"  [diag] junctionPath = {junctionPath}");

    try
    {
        await File.WriteAllTextAsync(Path.Combine(scanRoot, "unique.txt"), "inside");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "b.txt"), "same duplicate content");
        Log("  [diag] test files created");

        CreateJunction(junctionPath, outsideRoot);
        Log("  [diag] junction created successfully");

        var scanner = new DuplicateScanner();
        Log("  [diag] starting FindDuplicatesAsync for junction-folder test");

        var progress = new DelegateProgress<ScanProgress>(message =>
        {
            Log($"  [scan] {message.Message}");
        });

        var scanStopwatch = Stopwatch.StartNew();

        var result = await scanner.FindDuplicatesAsync(
            scanRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress,
            CancellationToken.None);

        scanStopwatch.Stop();
        Log($"  [diag] FindDuplicatesAsync returned in {scanStopwatch.Elapsed.TotalSeconds:N2}s");
        Log($"  [diag] result.Files.Count = {result.Files.Count}");
        Log($"  [diag] result.DuplicateFileCount = {result.DuplicateFileCount}");
        Log($"  [diag] result.LimitReached = {result.LimitReached}");

        AssertEqual(0, result.Files.Count, "Junction target files should not be included in scan results.");
    }
    finally
    {
        Log("  [diag] cleaning up junction-folder test artifacts");
        DeleteTestDirectory(artifactRoot);
        Log("  [diag] cleanup complete");
    }
}

static async Task ScannerSkipsJunctionRootByDefault()
{
    Log("  [diag] ScannerSkipsJunctionRootByDefault entered");

    var workspace = FindWorkspaceRoot();
    Log($"  [diag] workspace = {workspace}");

    var artifactRoot = Path.Combine(workspace, "TestArtifacts", Guid.NewGuid().ToString("N"));
    var outsideRoot = Path.Combine(artifactRoot, "outside-target");
    var junctionRoot = Path.Combine(artifactRoot, "scan-root-link");

    Directory.CreateDirectory(artifactRoot);
    Directory.CreateDirectory(outsideRoot);

    Log($"  [diag] artifactRoot = {artifactRoot}");
    Log($"  [diag] outsideRoot = {outsideRoot}");
    Log($"  [diag] junctionRoot = {junctionRoot}");

    try
    {
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "a.txt"), "same duplicate content");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "b.txt"), "same duplicate content");
        Log("  [diag] outside files created");

        CreateJunction(junctionRoot, outsideRoot);
        Log("  [diag] root junction created successfully");

        var scanner = new DuplicateScanner();
        Log("  [diag] starting FindDuplicatesAsync for junction-root test");

        var progress = new DelegateProgress<ScanProgress>(message =>
        {
            Log($"  [scan] {message.Message}");
        });

        var scanStopwatch = Stopwatch.StartNew();

        var result = await scanner.FindDuplicatesAsync(
            junctionRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress,
            CancellationToken.None);

        scanStopwatch.Stop();
        Log($"  [diag] FindDuplicatesAsync returned in {scanStopwatch.Elapsed.TotalSeconds:N2}s");
        Log($"  [diag] result.Files.Count = {result.Files.Count}");
        Log($"  [diag] result.DuplicateFileCount = {result.DuplicateFileCount}");
        Log($"  [diag] result.LimitReached = {result.LimitReached}");

        AssertEqual(0, result.Files.Count, "A linked scan root should not include target files by default.");
    }
    finally
    {
        Log("  [diag] cleaning up junction-root test artifacts");
        DeleteTestDirectory(artifactRoot);
        Log("  [diag] cleanup complete");
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
    var hashIndex = script.IndexOf("Get-FileHash -Algorithm SHA256", StringComparison.OrdinalIgnoreCase);
    var staleHashCleanupIndex = script.IndexOf("CopyFinder-v*-$Runtime-Standalone.zip.sha256.txt", StringComparison.OrdinalIgnoreCase);

    AssertTrue(removePdbIndex >= 0, "publish.ps1 should remove PDB files from normal standalone output.");
    AssertTrue(zipIndex >= 0, "publish.ps1 should create a zip archive.");
    AssertTrue(hashIndex >= 0, "publish.ps1 should create a SHA-256 checksum for the zip.");
    AssertTrue(staleHashCleanupIndex >= 0, "publish.ps1 should clean stale checksum sidecar files.");
    AssertTrue(removePdbIndex < zipIndex, "PDB removal should happen before Compress-Archive.");
    AssertTrue(zipIndex < hashIndex, "Checksum creation should happen after Compress-Archive.");

    return Task.CompletedTask;
}

static Task ProjectExcludesGeneratedReleaseContent()
{
    var project = File.ReadAllText(Path.Combine(FindWorkspaceRoot(), "CopyFinder.csproj"));

    AssertContains(@"<Content Remove=""Tests\**;TestArtifacts\**;publish\**""", project);
    AssertContains(@"<None Remove=""Tests\**;TestArtifacts\**;publish\**""", project);
    AssertContains(@"<Compile Remove=""Tests\**\*.cs""", project);
    AssertContains(@"<Content Include=""INSTALL.md"" CopyToOutputDirectory=""PreserveNewest""", project);

    return Task.CompletedTask;
}

static Task VersionStampIsTwoPointZeroNine()
{
    var workspace = FindWorkspaceRoot();
    var project = File.ReadAllText(Path.Combine(workspace, "CopyFinder.csproj"));
    var manifest = File.ReadAllText(Path.Combine(workspace, "app.manifest"));
    var mainWindow = File.ReadAllText(Path.Combine(workspace, "MainWindow.xaml"));
    var readme = File.ReadAllText(Path.Combine(workspace, "README.md"));
    var install = File.ReadAllText(Path.Combine(workspace, "INSTALL.md"));

    AssertContains("<Version>2.0.9</Version>", project);
    AssertContains("<AssemblyVersion>2.0.9.0</AssemblyVersion>", project);
    AssertContains("<FileVersion>2.0.9.0</FileVersion>", project);
    AssertContains("<InformationalVersion>2.0.9</InformationalVersion>", project);
    AssertContains("assemblyIdentity version=\"2.0.9.0\"", manifest);
    AssertContains("Text=\"2.0.9\"", mainWindow);
    AssertContains("Version: 2.0.9", readme);
    AssertContains("Version: 2.0.9", install);

    return Task.CompletedTask;
}

static Task InstallInstructionsCoverDeploymentSteps()
{
    var workspace = FindWorkspaceRoot();
    var install = File.ReadAllText(Path.Combine(workspace, "INSTALL.md"));
    var readme = File.ReadAllText(Path.Combine(workspace, "README.md"));
    var repoLayout = File.ReadAllText(Path.Combine(workspace, "REPO_LAYOUT.md"));

    AssertContains("CopyFinder-v2.0.9-win-x64-Standalone.zip", install);
    AssertContains("CopyFinder-v2.0.9-win-x64-Standalone.zip.sha256.txt", install);
    AssertContains("Get-FileHash -Algorithm SHA256", install);
    AssertContains("Expand-Archive", install);
    AssertContains("Add-MpPreference -ControlledFolderAccessAllowedApplications", install);
    AssertContains("%ProgramData%\\CopyFinder\\Logs\\deployment.log", install);
    AssertContains("%LOCALAPPDATA%\\CopyFinder\\Temp", install);
    AssertContains("INSTALL.md", readme);
    AssertContains("INSTALL.md", repoLayout);

    return Task.CompletedTask;
}

static Task GitHubWorkflowBuildsAndPublishesStandaloneZip()
{
    var workspace = FindWorkspaceRoot();

    var workflowPath = Path.Combine(
        workspace,
        ".github",
        "workflows",
        "copyfinder-windows-desktop.yml");

    var workflow = File.ReadAllText(workflowPath);
    var repoLayout = File.ReadAllText(
        Path.Combine(workspace, "REPO_LAYOUT.md"));

    // Workflow identity
    AssertContains("CopyFinder Windows Desktop", workflow);

    // SDK
    AssertContains("dotnet-version: 10.0.x", workflow);

    // Environment variables
    AssertContains("Solution_Name: CopyFinder.sln", workflow);
    AssertContains(@"Test_Project_Path: Tests\CopyFinder.Tests.csproj", workflow);
    AssertContains(@"Publish_Script: .\publish.ps1", workflow);
    AssertContains("Configuration: Release", workflow);
    AssertContains("Runtime: win-x64", workflow);

    // Job timeout
    AssertContains("timeout-minutes: 60", workflow);

    // Build pipeline
    AssertContains("Restore dependencies", workflow);
    AssertContains("Build solution (Release)", workflow);
    AssertContains("dotnet build $env:Solution_Name", workflow);

    // Bounded fixture
    AssertContains("Create bounded scan fixture", workflow);
    AssertContains("COPYFINDER_WORKFLOW_SCAN_ROOT", workflow);
    AssertContains("CopyFinderScanFixture", workflow);
    AssertContains("bounded duplicate content", workflow);
    AssertContains("unique workflow content", workflow);

    // Diagnostic fixture visibility
    AssertContains("Show test fixture contents", workflow);

    // Regression harness
    AssertContains("Run regression harness", workflow);
    AssertContains("dotnet run --project $env:Test_Project_Path", workflow);
    AssertContains("--no-build", workflow);
    AssertContains("timeout-minutes: 5", workflow);

    // Publish
    AssertContains("Publish standalone zip", workflow);
    AssertContains("& $env:Publish_Script -Configuration Release -Runtime $env:Runtime", workflow);

    // Checksum validation
    AssertContains("Verify standalone zip checksum", workflow);
    AssertContains("Get-FileHash -Algorithm SHA256", workflow);

    // Artifact upload support
    AssertContains("actions/upload-artifact@v4", workflow);

    // Release artifacts
    AssertContains("Upload standalone release zip", workflow);
    AssertContains("name: CopyFinder-Standalone", workflow);
    AssertContains("publish/*.zip", workflow);
    AssertContains("publish/*.zip.sha256.txt", workflow);

    // Ensure Release-only execution
    AssertFalse(
        workflow.Contains("matrix:", StringComparison.OrdinalIgnoreCase),
        "Workflow should not use a Debug/Release matrix.");

    AssertFalse(
        workflow.Contains("configuration: [Debug, Release]", StringComparison.OrdinalIgnoreCase),
        "Workflow should only run the Release configuration.");

    // Repository documentation references
    AssertContains(
        "copyfinder-windows-desktop.yml",
        repoLayout);

    return Task.CompletedTask;
}

static Task ReportFormatterExportsCsvAndJson()
{
    var files = new[]
    {
        new DuplicateReportFile(
            GroupId: 1,
            Role: "Keep",
            IsSelected: false,
            IsOriginal: true,
            Size: 100,
            Hash: "hash-a",
            ImageWidth: 800,
            ImageHeight: 600,
            LastWriteTime: new DateTime(2026, 6, 18, 9, 30, 0, DateTimeKind.Local),
            Path: @"C:\Data\keep,file.txt"),
        new DuplicateReportFile(
            GroupId: 1,
            Role: "Duplicate",
            IsSelected: true,
            IsOriginal: false,
            Size: 100,
            Hash: "hash-a",
            ImageWidth: null,
            ImageHeight: null,
            LastWriteTime: new DateTime(2026, 6, 18, 9, 31, 0, DateTimeKind.Local),
            Path: @"\\server\share\duplicate ""quoted"".txt")
    };

    var csv = DuplicateReportFormatter.BuildCsvReport(files);
    AssertContains("GroupId,Role,Selected,Size,Hash,ImageWidth,ImageHeight,Modified,NetworkPath,DeleteStatus,Path", csv);
    AssertContains(@"""C:\Data\keep,file.txt""", csv);
    AssertContains(@"True,""Selected"",""\\server\share\duplicate """"quoted"""" .txt""".Replace(" .txt", ".txt"), csv);

    using var document = JsonDocument.Parse(DuplicateReportFormatter.BuildJsonReport(files));
    var rows = document.RootElement.EnumerateArray().ToList();

    AssertEqual(2, rows.Count, "JSON report should include both files.");
    AssertEqual("Kept", rows[0].GetProperty("DeleteStatus").GetString() ?? string.Empty, "Original file should be marked kept.");
    AssertFalse(rows[0].GetProperty("IsNetworkPath").GetBoolean(), "Local path should not be marked as network.");
    AssertTrue(rows[1].GetProperty("IsNetworkPath").GetBoolean(), "UNC path should be marked as network.");
    AssertEqual("Selected", rows[1].GetProperty("DeleteStatus").GetString() ?? string.Empty, "Selected duplicate should be marked selected.");

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

static async Task ScannerUsesBoundedWorkflowFixture()
{
    Log("  [diag] ScannerUsesBoundedWorkflowFixture entered");

    var envFixtureRoot = Environment.GetEnvironmentVariable("COPYFINDER_WORKFLOW_SCAN_ROOT");
    string fixtureRoot;
    string? localArtifactRoot = null;

    if (string.IsNullOrWhiteSpace(envFixtureRoot))
    {
        Log("  [diag] no workflow fixture env var supplied; creating local fixture");
        localArtifactRoot = CreateArtifactRoot();
        fixtureRoot = Path.Combine(localArtifactRoot, "CopyFinderScanFixture");
        await CreateBoundedScanFixtureAsync(fixtureRoot);
    }
    else
    {
        fixtureRoot = Path.GetFullPath(envFixtureRoot);
        Log($"  [diag] using workflow fixture from env: {fixtureRoot}");
        AssertTrue(Directory.Exists(fixtureRoot), $"Workflow scan fixture does not exist: {fixtureRoot}");
    }

    AssertSafeWorkflowFixtureRoot(fixtureRoot);
    Log("  [diag] workflow fixture root passed safety checks");

    try
    {
        var scanner = new DuplicateScanner();
        Log("  [diag] starting FindDuplicatesAsync for bounded workflow fixture");

        var progress = new DelegateProgress<ScanProgress>(message =>
        {
            Log($"  [scan] {message.Message}");
        });

        var scanStopwatch = Stopwatch.StartNew();

        var result = await scanner.FindDuplicatesAsync(
            fixtureRoot,
            new ScanOptions
            {
                MaxDuplicateFiles = 10,
                SkipHiddenFiles = false,
                SkipSystemFiles = false
            },
            progress,
            CancellationToken.None);

        scanStopwatch.Stop();
        Log($"  [diag] FindDuplicatesAsync returned in {scanStopwatch.Elapsed.TotalSeconds:N2}s");
        Log($"  [diag] result.Files.Count = {result.Files.Count}");
        Log($"  [diag] result.DuplicateFileCount = {result.DuplicateFileCount}");
        Log($"  [diag] result.LimitReached = {result.LimitReached}");

        foreach (var file in result.Files)
        {
            Log($"  [diag] result file => {file.Path}");
        }

        AssertEqual(2, result.Files.Count, "Workflow fixture scan should return one kept file and one duplicate.");
        AssertEqual(1, result.DuplicateFileCount, "Workflow fixture should contain exactly one duplicate file.");
        AssertFalse(result.LimitReached, "Workflow fixture scan should not hit the duplicate limit.");
        AssertTrue(result.Files.All(file => file.Path.Contains("duplicate-", StringComparison.OrdinalIgnoreCase)), "Workflow fixture scan should only return the duplicate pair.");
        AssertTrue(result.Files.Select(file => file.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1, "Workflow fixture duplicate pair should share one hash.");
    }
    finally
    {
        if (localArtifactRoot is not null)
        {
            Log("  [diag] cleaning up local bounded fixture");
            DeleteTestDirectory(localArtifactRoot);
            Log("  [diag] cleanup complete");
        }
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

static async Task SafeFileHashesThroughWrapper()
{
    var artifactRoot = CreateArtifactRoot();

    try
    {
        var filePath = Path.Combine(artifactRoot, "hash.txt");
        await File.WriteAllTextAsync(filePath, "safe file hash content");

        var expected = ComputeSha256(filePath);
        var actual = await SafeFile.ComputeSha256Async(filePath, CancellationToken.None);

        AssertEqual(expected, actual, "SafeFile hash should match direct SHA-256.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static async Task SafeFileDeletesThroughWrapper()
{
    var artifactRoot = CreateArtifactRoot();

    try
    {
        var filePath = Path.Combine(artifactRoot, "delete-me.txt");
        await File.WriteAllTextAsync(filePath, "delete through safe wrapper");

        var result = await SafeFile.DeleteAsync(filePath, allowPermissionRepair: false, CancellationToken.None);
        AssertTrue(result.Succeeded, result.Message ?? "SafeFile delete should succeed.");
        AssertFalse(File.Exists(filePath), "Deleted file should no longer exist at the original path.");
    }
    finally
    {
        DeleteTestDirectory(artifactRoot);
    }
}

static Task SafeFileWritesDeploymentLogToConfiguredPath()
{
    var artifactRoot = CreateArtifactRoot();
    var logPath = Path.Combine(artifactRoot, "deployment.log");

    try
    {
        DeploymentLogger.UseLogPathForTests(logPath);

        var wrote = DeploymentLogger.Log("Test", "SafeFile deployment logger test.");
        AssertTrue(wrote, "Deployment logger should write to the configured test path.");
        AssertTrue(File.Exists(logPath), "Deployment log should exist.");
        AssertContains("SafeFile deployment logger test.", File.ReadAllText(logPath));
    }
    finally
    {
        DeploymentLogger.ResetLogPathForTests();
        DeleteTestDirectory(artifactRoot);
    }

    return Task.CompletedTask;
}

static async Task SafeFileDetectsConfiguredOneDriveRoot()
{
    var artifactRoot = CreateArtifactRoot();
    var previousConsumer = Environment.GetEnvironmentVariable("OneDriveConsumer");

    try
    {
        Environment.SetEnvironmentVariable("OneDriveConsumer", artifactRoot);

        var filePath = Path.Combine(artifactRoot, "inside-onedrive.txt");
        await File.WriteAllTextAsync(filePath, "onedrive root test");

        var state = OneDriveFileHandler.GetState(filePath, checkInUse: false);

        AssertTrue(state.IsInsideOneDrive, "File should be recognized inside the configured OneDrive root.");
        AssertEqual(
            Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar),
            state.OneDriveRoot ?? string.Empty,
            "OneDrive root should match the configured environment path.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("OneDriveConsumer", previousConsumer);
        DeleteTestDirectory(artifactRoot);
    }
}

static Task MainWindowDoesNotDeleteDirectly()
{
    var workspace = FindWorkspaceRoot();
    var mainWindow = File.ReadAllText(Path.Combine(workspace, "MainWindow.xaml.cs"));
    var safeFile = File.ReadAllText(Path.Combine(workspace, "Services", "SafeFile.cs"));

    AssertFalse(mainWindow.Contains("FileSystem.DeleteFile", StringComparison.Ordinal), "MainWindow should route deletes through SafeFile.");
    AssertContains("FileSystem.DeleteFile", safeFile);
    AssertContains("SafeFile.DeleteAsync", mainWindow);

    return Task.CompletedTask;
}

static Task CompatibilityReportIncludesCfaAllowPath()
{
    var cfa = new ControlledFolderAccessStatus(
        ControlledFolderAccessMode.Enabled,
        @"C:\Apps\CopyFinder\CopyFinder.exe",
        @"Add-MpPreference -ControlledFolderAccessAllowedApplications ""C:\Apps\CopyFinder\CopyFinder.exe""",
        "Controlled Folder Access is active.",
        RequiresUserAction: true);

    var report = new DeploymentCompatibilityReport(
        cfa,
        [@"C:\Users\Test\OneDrive"],
        new PermissionCheckResult(true, false, "Working directory is writable."),
        @"C:\Users\Test\AppData\Local\CopyFinder\Temp",
        @"C:\ProgramData\CopyFinder\Logs\deployment.log",
        DeploymentLogAvailable: true,
        IsElevated: false,
        "Not installed.");

    var message = report.ToUserMessage();
    AssertContains(@"C:\Apps\CopyFinder\CopyFinder.exe", message);
    AssertContains(@"C:\ProgramData\CopyFinder\Logs\deployment.log", message);
    AssertContains("Controlled Folder Access: Enabled", message);

    return Task.CompletedTask;
}

static string FindWorkspaceRoot()
{
    var current = AppContext.BaseDirectory;
    Log($"  [diag] FindWorkspaceRoot starting at {current}");

    while (!string.IsNullOrWhiteSpace(current))
    {
        var candidate = Path.Combine(current, "CopyFinder.csproj");
        if (File.Exists(candidate))
        {
            Log($"  [diag] FindWorkspaceRoot found {current}");
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

static async Task CreateBoundedScanFixtureAsync(string fixtureRoot)
{
    Directory.CreateDirectory(Path.Combine(fixtureRoot, "A"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot, "B"));
    Directory.CreateDirectory(Path.Combine(fixtureRoot, "Unique"));

    await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "A", "duplicate-a.txt"), "bounded duplicate content");
    await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "B", "duplicate-b.txt"), "bounded duplicate content");
    await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "Unique", "unique.txt"), "unique workflow content");
}

static void AssertSafeWorkflowFixtureRoot(string fixtureRoot)
{
    var fullRoot = Path.GetFullPath(fixtureRoot)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    AssertEqual("CopyFinderScanFixture", Path.GetFileName(fullRoot), "Workflow scan fixture must use the expected bounded folder name.");

    if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        var runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if (string.IsNullOrWhiteSpace(runnerTemp))
        {
            throw new InvalidOperationException("RUNNER_TEMP must be set in GitHub Actions.");
        }

        var fullRunnerTemp = Path.GetFullPath(runnerTemp)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        AssertTrue(
            fullRoot.StartsWith(fullRunnerTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Workflow scan fixture must stay under RUNNER_TEMP. Fixture={fullRoot}; RUNNER_TEMP={fullRunnerTemp}");
    }

    var fileCount = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories).Count();
    var directoryCount = Directory.EnumerateDirectories(fullRoot, "*", SearchOption.AllDirectories).Count();

    AssertTrue(fileCount <= 10, $"Workflow scan fixture is too large: {fileCount} files.");
    AssertTrue(directoryCount <= 10, $"Workflow scan fixture has too many directories: {directoryCount} directories.");
}

static void CreateJunction(string junctionPath, string targetPath)
{
    Log("  [diag] CreateJunction requested");
    Log($"  [diag] junctionPath = {junctionPath}");
    Log($"  [diag] targetPath = {targetPath}");

    var arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"";
    Log($"  [diag] cmd.exe {arguments}");

    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    });

    if (process is null)
    {
        throw new InvalidOperationException("Could not start mklink process.");
    }

    if (!process.WaitForExit(30000))
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore best-effort kill failures
        }

        throw new TimeoutException("mklink did not exit within 30 seconds.");
    }

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();

    Log($"  [diag] mklink exit code = {process.ExitCode}");

    if (!string.IsNullOrWhiteSpace(output))
    {
        Log($"  [diag] mklink stdout: {output.Trim()}");
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
        Log($"  [diag] mklink stderr: {error.Trim()}");
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"mklink failed with exit code {process.ExitCode}: {output}{error}");
    }
}

static void DeleteTestDirectory(string artifactRoot)
{
    var workspace = FindWorkspaceRoot();
    var fullArtifactRoot = Path.GetFullPath(artifactRoot);
    var allowedRoot = Path.GetFullPath(Path.Combine(workspace, "TestArtifacts"));

    Log($"  [diag] DeleteTestDirectory requested for {fullArtifactRoot}");
    Log($"  [diag] allowed root = {allowedRoot}");

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
            Log($"  [diag] deleting reparse point {reparsePoint}");
            Directory.Delete(reparsePoint);
        }

        Directory.Delete(fullArtifactRoot, recursive: true);
        Log($"  [diag] deleted {fullArtifactRoot}");
    }
    else
    {
        Log($"  [diag] directory did not exist: {fullArtifactRoot}");
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
