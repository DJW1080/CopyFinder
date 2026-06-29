using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CopyFinder.Models;
using CopyFinder.Services;
using CopyFinder.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CopyFinder;

public sealed partial class MainWindow : Window
{
    private const string ProductTitle = "Technification CopyFinder";
    private const int InitialWindowWidth = 830;
    private const int InitialWindowHeight = 800;
    private const int DefaultDpi = 96;

    private readonly DuplicateScanner _scanner = new();
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings;
    private CancellationTokenSource? _scanCancellation;
    private int _deleteActionNumber;
    private bool _isLoadingSettings;
    private bool _compatibilityReportStarted;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowTitle();
        SetInitialWindowSize();
        SetWindowIcon();
        _settings = _settingsService.Load();
        DuplicateGroups.CollectionChanged += DuplicateGroups_CollectionChanged;
        LoadSettingsIntoUi();
        Activated += MainWindow_Activated;
    }

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

    private IEnumerable<DuplicateFileViewModel> DuplicateFiles =>
        DuplicateGroups.SelectMany(group => group.Files);

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedPath = ShellFolderPicker.PickFolder(
                WindowNative.GetWindowHandle(this),
                SafeFile.DirectoryExists(FolderPathBox.Text)
                    ? FolderPathBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                FolderPathBox.Text = selectedPath;
                SaveSettingsFromUi();
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Folder browser failed: {ex.Message}{Environment.NewLine}{Environment.NewLine}You can type or paste the folder path into the folder box.");
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderPathBox.Text;
        if (string.IsNullOrWhiteSpace(folder) || !SafeFile.DirectoryExists(folder))
        {
            await ShowMessageAsync("Choose an existing folder before scanning.");
            return;
        }

        SaveSettingsFromUi();
        var scanOptions = BuildScanOptionsFromUi();
        if (scanOptions.KeepRule == KeepRule.PreferFolder && !IsExistingDirectoryPath(scanOptions.PreferredFolder))
        {
            await ShowMessageAsync("Choose an existing preferred folder before scanning with the Prefer folder keep rule.");
            return;
        }

        SetScanningState(true);
        DuplicateGroups.Clear();
        _deleteActionNumber = 0;
        _scanCancellation = new CancellationTokenSource();
        ScanProgressBar.IsIndeterminate = true;
        SummaryText.Text = "Scanning...";
        StatusText.Text = string.Empty;

        var progress = new Progress<ScanProgress>(state =>
        {
            StatusText.Text = state.Message;
        });

        try
        {
            var result = await Task.Run(
                () => _scanner.FindDuplicatesAsync(folder, scanOptions, progress, _scanCancellation.Token),
                _scanCancellation.Token);

            foreach (var group in result.Files.GroupBy(file => file.GroupId))
            {
                var files = group.Select(file => new DuplicateFileViewModel(file));
                DuplicateGroups.Add(new DuplicateGroupViewModel(group.Key, files));
            }

            RefreshSummary(result.LimitReached ? "Scan stopped for review." : "Scan complete.");
            StatusText.Text = result.Files.Count == 0
                ? "No duplicate files found."
                : result.LimitReached
                    ? $"Stopped after finding {result.DuplicateFileCount:N0} duplicate files."
                    : "Scan complete.";
        }
        catch (OperationCanceledException)
        {
            SummaryText.Text = "Scan canceled.";
            StatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Scan failed.";
            await ShowMessageAsync($"Scan failed: {ex.Message}");
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            ScanProgressBar.IsIndeterminate = false;
            SetScanningState(false);
            RefreshActionState();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
    }

    private void SelectAllDuplicatesButton_Click(object sender, RoutedEventArgs e)
    {
        SetDuplicateSelection(file => true);
        StatusText.Text = "All duplicates selected.";
    }

    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetDuplicateSelection(file => false);
        StatusText.Text = "All duplicates deselected.";
    }

    private void SelectGroupButton_Click(object sender, RoutedEventArgs e)
    {
        SetGroupSelection(sender, true);
    }

    private void DeselectGroupButton_Click(object sender, RoutedEventArgs e)
    {
        SetGroupSelection(sender, false);
    }

    private int SetDuplicateSelection(Func<DuplicateFileViewModel, bool> selector)
    {
        var duplicateFiles = DuplicateFiles.Where(file => file.IsDuplicate).ToList();
        foreach (var file in duplicateFiles)
        {
            file.IsSelected = selector(file);
        }

        RefreshActionState();
        RefreshSummary();
        return duplicateFiles.Count;
    }

    private void SetGroupSelection(object sender, bool isSelected)
    {
        if (sender is not FrameworkElement { Tag: int groupId })
        {
            return;
        }

        var group = DuplicateGroups.FirstOrDefault(candidate => candidate.GroupId == groupId);
        if (group is null)
        {
            return;
        }

        foreach (var file in group.Files.Where(file => file.IsDuplicate))
        {
            file.IsSelected = isSelected;
        }

        RefreshActionState();
        RefreshSummary();
        StatusText.Text = isSelected ? $"Group {groupId:N0} selected." : $"Group {groupId:N0} deselected.";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();

        var selectedFiles = DuplicateFiles.Where(file => file.IsSelected && file.IsDuplicate).ToList();
        if (selectedFiles.Count == 0)
        {
            await ShowMessageAsync("No duplicate files are selected.");
            return;
        }

        var candidateBuildFailures = new List<string>();
        var deleteCandidates = BuildDeleteCandidates(selectedFiles, candidateBuildFailures);
        if (deleteCandidates.Count == 0)
        {
            await ShowMessageAsync("No selected duplicate files could be matched to a kept file for validation.");
            return;
        }

        var networkFileCount = selectedFiles.Count(file => FilePathClassifier.IsNetworkPath(file.Path));
        var networkWarning = networkFileCount > 0
            ? $"{Environment.NewLine}{Environment.NewLine}Warning: {networkFileCount:N0} selected file(s) are on network paths or mapped network drives. Network-drive deletion may be final and may not use the Recycle Bin."
            : string.Empty;

        var dialog = CreateDialog(
            "Delete selected duplicates?",
            $"{selectedFiles.Count:N0} selected duplicate file(s) will be validated against the kept file before deletion. The first file in each group is kept.{networkWarning}",
            "Delete Duplicates",
            "Cancel");

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _deleteActionNumber++;

        var deleted = 0;
        var processed = 0;
        var failures = new List<string>(candidateBuildFailures);
        var selectedByPath = selectedFiles.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var progressDialog = CreateDeleteProgressDialog(deleteCandidates.Count, out var currentFileText, out var deleteProgressBar);
        var progressDialogOperation = progressDialog.ShowAsync();
        await Task.Yield();

        foreach (var candidate in deleteCandidates)
        {
            currentFileText.Text = GetFileNameForDisplay(candidate.DuplicatePath);
            await Task.Yield();

            try
            {
                var validation = await DuplicateDeleteValidator.ValidateAsync(candidate, CancellationToken.None);
                if (!validation.CanDelete)
                {
                    failures.Add(validation.FailureMessage ?? $"{candidate.DuplicatePath}: validation failed before deletion.");
                    continue;
                }

                var deleteResult = await SafeFile.DeleteAsync(
                    candidate.DuplicatePath,
                    allowPermissionRepair: false,
                    CancellationToken.None);
                if (!deleteResult.Succeeded)
                {
                    failures.Add($"{candidate.DuplicatePath}: {deleteResult.Message ?? "delete failed"}");
                    continue;
                }

                if (selectedByPath.TryGetValue(candidate.DuplicatePath, out var file))
                {
                    RemoveFileFromGroups(file);
                }

                deleted++;
            }
            catch (Exception ex)
            {
                failures.Add($"{candidate.DuplicatePath}: {ex.Message}");
            }
            finally
            {
                processed++;
                deleteProgressBar.Value = deleteCandidates.Count == 0
                    ? 100
                    : processed * 100d / deleteCandidates.Count;
            }
        }

        currentFileText.Text = "Complete";
        deleteProgressBar.Value = 100;
        await Task.Delay(250);
        progressDialog.Hide();
        await progressDialogOperation;

        RemoveEmptyDuplicateGroups();
        RefreshActionState();
        RefreshSummary();

        SummaryText.Text = $"{deleted:N0} file(s) moved to the Recycle Bin where available.";
        StatusText.Text = failures.Count == 0
            ? "Delete complete."
            : $"{failures.Count:N0} file(s) could not be moved.";

        await ShowPostDeleteSummaryAsync(_deleteActionNumber, deleted, failures.Count, networkFileCount, failures);
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DuplicateFiles.Any())
        {
            await ShowMessageAsync("There are no scan results to export.");
            return;
        }

        try
        {
            var path = ShellFileSavePicker.PickReportFile(
                WindowNative.GetWindowHandle(this),
                $"CopyFinder-report-{DateTime.Now:yyyyMMdd-HHmmss}");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var reportFiles = CreateReportFiles();
            if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                var writeResult = await SafeFile.WriteAllTextAsync(
                    path,
                    DuplicateReportFormatter.BuildJsonReport(reportFiles),
                    encoding: null,
                    CancellationToken.None);
                if (!writeResult.Succeeded)
                {
                    await ShowMessageAsync($"Export failed: {writeResult.Message}");
                    return;
                }
            }
            else
            {
                var writeResult = await SafeFile.WriteAllTextAsync(
                    path,
                    DuplicateReportFormatter.BuildCsvReport(reportFiles),
                    Encoding.UTF8,
                    CancellationToken.None);
                if (!writeResult.Succeeded)
                {
                    await ShowMessageAsync($"Export failed: {writeResult.Message}");
                    return;
                }
            }

            StatusText.Text = $"Report exported: {path}";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Export failed: {ex.Message}");
        }
    }

    private void KeepFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        var group = DuplicateGroups.FirstOrDefault(candidate =>
            candidate.Files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)));
        var file = group?.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));

        if (group is null || file is null)
        {
            return;
        }

        group.SetOriginal(file);
        RefreshActionState();
        RefreshSummary();
        StatusText.Text = $"Keep file changed: {path}";
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!SafeFile.FileExists(path) && string.IsNullOrWhiteSpace(directory))
            {
                await ShowMessageAsync("Could not determine the file location.");
                return;
            }

            var argument = SafeFile.FileExists(path) ? $"/select,\"{path}\"" : $"\"{directory}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = argument,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Could not open file location: {ex.Message}");
        }
    }

    private void KeepRuleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreferredFolderBox is null)
        {
            return;
        }

        PreferredFolderBox.IsEnabled = KeepRuleComboBox.SelectedIndex == (int)KeepRule.PreferFolder;
        if (_isLoadingSettings)
        {
            return;
        }

        SaveSettingsFromUi();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        SaveSettingsFromUi();
    }

    private void ScanLimitBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        SaveSettingsFromUi();
    }

    private void SetScanningState(bool isScanning)
    {
        BrowseButton.IsEnabled = !isScanning;
        ScanButton.IsEnabled = !isScanning;
        CancelButton.IsEnabled = isScanning;
        SettingsPanel.IsEnabled = !isScanning;
        DeleteButton.IsEnabled = !isScanning && DuplicateFiles.Any(file => file.IsSelected && file.IsDuplicate);
        SelectAllDuplicatesButton.IsEnabled = !isScanning && DuplicateFiles.Any(file => file.IsDuplicate);
        DeselectAllButton.IsEnabled = !isScanning && DuplicateFiles.Any(file => file.IsSelected && file.IsDuplicate);
        ExportButton.IsEnabled = !isScanning && DuplicateFiles.Any();
    }

    private void RefreshActionState()
    {
        DeleteButton.IsEnabled = DuplicateFiles.Any(file => file.IsSelected && file.IsDuplicate);
        SelectAllDuplicatesButton.IsEnabled = DuplicateFiles.Any(file => file.IsDuplicate);
        DeselectAllButton.IsEnabled = DuplicateFiles.Any(file => file.IsSelected && file.IsDuplicate);
        ExportButton.IsEnabled = DuplicateFiles.Any();
    }

    private void RefreshSummary(string prefix = "")
    {
        var groups = DuplicateGroups.Count;
        var selectedCount = DuplicateFiles.Count(file => file.IsSelected && file.IsDuplicate);
        var duplicateCount = DuplicateFiles.Count(file => file.IsDuplicate);
        var summary = $"{groups:N0} duplicate groups, {duplicateCount:N0} duplicate files, {selectedCount:N0} selected.";
        SummaryText.Text = string.IsNullOrWhiteSpace(prefix) ? summary : $"{prefix} {summary}";
    }

    private void DuplicateGroups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DuplicateGroupViewModel group in e.OldItems)
            {
                group.PropertyChanged -= DuplicateGroup_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DuplicateGroupViewModel group in e.NewItems)
            {
                group.PropertyChanged += DuplicateGroup_PropertyChanged;
            }
        }

        RefreshActionState();
    }

    private void DuplicateGroup_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateGroupViewModel.SelectedCount))
        {
            RefreshActionState();
            RefreshSummary();
        }
    }

    private void RemoveFileFromGroups(DuplicateFileViewModel file)
    {
        var group = DuplicateGroups.FirstOrDefault(candidate => candidate.GroupId == file.GroupId);
        group?.RemoveFile(file);
    }

    private void RemoveEmptyDuplicateGroups()
    {
        foreach (var group in DuplicateGroups.Where(group => !group.HasDuplicates()).ToList())
        {
            DuplicateGroups.Remove(group);
        }
    }

    private List<DuplicateDeleteCandidate> BuildDeleteCandidates(
        IEnumerable<DuplicateFileViewModel> selectedFiles,
        ICollection<string> failures)
    {
        var candidates = new List<DuplicateDeleteCandidate>();
        foreach (var file in selectedFiles)
        {
            var group = DuplicateGroups.FirstOrDefault(candidate => candidate.GroupId == file.GroupId);
            var keptFile = group?.Files.FirstOrDefault(candidate => candidate.IsOriginal);
            if (keptFile is null)
            {
                failures.Add($"{file.Path}: no kept file was available for validation.");
                continue;
            }

            candidates.Add(new DuplicateDeleteCandidate(
                file.GroupId,
                file.Path,
                keptFile.Path,
                file.Size,
                file.Hash));
        }

        return candidates;
    }

    private List<DuplicateReportFile> CreateReportFiles()
    {
        return DuplicateFiles.Select(file => new DuplicateReportFile(
            file.GroupId,
            file.Role,
            file.IsSelected,
            file.IsOriginal,
            file.Size,
            file.Hash,
            file.ImageWidth,
            file.ImageHeight,
            file.LastWriteTime,
            file.Path)).ToList();
    }

    private ScanOptions BuildScanOptionsFromUi()
    {
        return new ScanOptions
        {
            KeepRule = (KeepRule)Math.Max(0, KeepRuleComboBox.SelectedIndex),
            PreferredFolder = PreferredFolderBox.Text.Trim(),
            MaxDuplicateFiles = Math.Max(1, (int)GetNumberBoxValue(ScanLimitBox, 500)),
            HashParallelism = Math.Clamp((int)GetNumberBoxValue(HashWorkersBox, 2), 1, 16),
            MinimumFileSizeBytes = Math.Max(0, (long)GetNumberBoxValue(MinimumSizeBox, 0) * 1024),
            SkipHiddenFiles = SkipHiddenCheckBox.IsChecked == true,
            SkipSystemFiles = SkipSystemCheckBox.IsChecked == true,
            ExcludedExtensions = ParseExtensions(ExcludedExtensionsBox.Text)
        };
    }

    private void LoadSettingsIntoUi()
    {
        _isLoadingSettings = true;
        try
        {
            FolderPathBox.Text = _settings.LastFolder;
            KeepRuleComboBox.SelectedIndex = Math.Clamp((int)_settings.ScanOptions.KeepRule, 0, KeepRuleComboBox.Items.Count - 1);
            PreferredFolderBox.Text = _settings.ScanOptions.PreferredFolder;
            MinimumSizeBox.Value = _settings.ScanOptions.MinimumFileSizeBytes / 1024d;
            SkipHiddenCheckBox.IsChecked = _settings.ScanOptions.SkipHiddenFiles;
            SkipSystemCheckBox.IsChecked = _settings.ScanOptions.SkipSystemFiles;
            ExcludedExtensionsBox.Text = string.Join(", ", _settings.ScanOptions.ExcludedExtensions);
            ScanLimitBox.Value = Math.Clamp(_settings.ScanDuplicateLimit, 1, 10000);
            HashWorkersBox.Value = Math.Clamp(_settings.ScanOptions.HashParallelism, 1, 16);
            PreferredFolderBox.IsEnabled = KeepRuleComboBox.SelectedIndex == (int)KeepRule.PreferFolder;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveSettingsFromUi()
    {
        if (ScanLimitBox is null ||
            HashWorkersBox is null ||
            MinimumSizeBox is null ||
            KeepRuleComboBox is null ||
            PreferredFolderBox is null ||
            SkipHiddenCheckBox is null ||
            SkipSystemCheckBox is null ||
            ExcludedExtensionsBox is null)
        {
            return;
        }

        _settings.LastFolder = FolderPathBox.Text;
        _settings.ScanDuplicateLimit = Math.Max(1, (int)GetNumberBoxValue(ScanLimitBox, 500));
        _settings.ScanOptions = BuildScanOptionsFromUi();
        _settings.ScanOptions.MaxDuplicateFiles = _settings.ScanDuplicateLimit;
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Settings were not saved: {ex.Message}";
        }
    }

    private static List<string> ParseExtensions(string value)
    {
        return value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double GetNumberBoxValue(NumberBox numberBox, double fallback)
    {
        return double.IsNaN(numberBox.Value) ? fallback : numberBox.Value;
    }

    private static bool IsExistingDirectoryPath(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && SafeFile.DirectoryExists(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ShowPostDeleteSummaryAsync(
        int actionNumber,
        int movedCount,
        int failedCount,
        int networkFileCount,
        IReadOnlyList<string> failures)
    {
        var summary =
            $"Delete action: {actionNumber:N0}{Environment.NewLine}" +
            $"Files moved: {movedCount:N0}{Environment.NewLine}" +
            $"Failed files: {failedCount:N0}{Environment.NewLine}" +
            $"Network-drive files: {networkFileCount:N0}{Environment.NewLine}{Environment.NewLine}" +
            "Local moved files can be restored from the Windows Recycle Bin. Network-drive deletions may be final.";

        if (failures.Count > 0)
        {
            summary += $"{Environment.NewLine}{Environment.NewLine}First failures:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures.Take(10));
        }

        await ShowMessageAsync(summary);
    }

    private ContentDialog CreateDeleteProgressDialog(
        int totalFiles,
        out TextBlock currentFileText,
        out ProgressBar progressBar)
    {
        currentFileText = new TextBlock
        {
            Text = "Preparing...",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 420
        };

        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 8
        };

        var countText = new TextBlock
        {
            Text = $"{totalFiles:N0} selected duplicate file(s)",
            Foreground = App.Current.Resources["AppTextSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 440,
            Children =
            {
                currentFileText,
                progressBar,
                countText
            }
        };

        return new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Deleting duplicates",
            Content = content
        };
    }

    private static string GetFileNameForDisplay(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? "(unknown file)" : fileName;
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = CreateDialog("CopyFinder", message, "OK", null);
        await dialog.ShowAsync();
    }

    private ContentDialog CreateDialog(string title, string content, string primaryText, string? closeText)
    {
        return new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText
        };
    }

    private void SetInitialWindowSize()
    {
        var rasterizationScale = GetWindowRasterizationScale();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Round(InitialWindowWidth * rasterizationScale),
            (int)Math.Round(InitialWindowHeight * rasterizationScale)));
    }

    private void SetWindowTitle()
    {
        Title = $"{ProductTitle} {GetAppVersion()}";
    }

    private double GetWindowRasterizationScale()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        if (windowHandle != IntPtr.Zero)
        {
            var dpi = GetDpiForWindow(windowHandle);
            if (dpi > 0)
            {
                return dpi / (double)DefaultDpi;
            }
        }

        return Content.XamlRoot?.RasterizationScale ?? 1d;
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Technification", "Logo", "favicon", "favicon.ico");
        if (SafeFile.FileExists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_compatibilityReportStarted)
        {
            return;
        }

        _compatibilityReportStarted = true;
        await ShowFirstRunCompatibilityReportAsync();
    }

    private async Task ShowFirstRunCompatibilityReportAsync()
    {
        var version = GetAppVersion();
        if (string.Equals(_settings.LastCompatibilityReportVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var report = await new DeploymentCompatibilityChecker().CheckAsync(CancellationToken.None);
            await ShowMessageAsync(report.ToUserMessage());
            _settings.LastCompatibilityReportVersion = version;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            DeploymentLogger.Log("Compatibility", "First-run compatibility check failed.", ex);
            StatusText.Text = $"Compatibility check failed: {ex.Message}";
        }
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return version is null
            ? "dev"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
