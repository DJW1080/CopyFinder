# CopyFinder Repository Layout

CopyFinder is an unpackaged WinUI 3 desktop application targeting `net10.0-windows10.0.26100.0` and publishing as a standalone `win-x64` app.

## Source Tree

```text
CopyFinder/
|-- CopyFinder.sln
|-- CopyFinder.csproj
|-- Program.cs
|-- App.xaml
|-- App.xaml.cs
|-- MainWindow.xaml
|-- MainWindow.xaml.cs
|-- app.manifest
|-- publish.ps1
|-- INSTALL.md
|-- README.md
|-- REPO_LAYOUT.md
|-- Models/
|-- Services/
|-- ViewModels/
|-- Tests/
`-- Technification/
```

## Root Files

| Path | Purpose |
| --- | --- |
| `CopyFinder.sln` | Solution entry point for the app and regression test project. |
| `CopyFinder.csproj` | Main WinUI project configuration, target framework, package references, version stamp, assets, and publish settings. |
| `Program.cs` | Custom application entry point used with `DISABLE_XAML_GENERATED_MAIN`. |
| `App.xaml` | Application-wide WinUI resources, theme brushes, and shared styles. |
| `App.xaml.cs` | App startup and main window creation. |
| `MainWindow.xaml` | Main app shell and review UI layout. |
| `MainWindow.xaml.cs` | UI event handling, scan orchestration, export flow, delete flow, and state wiring. |
| `app.manifest` | Desktop app manifest settings including app identity version and DPI awareness. |
| `publish.ps1` | Release script that creates `publish\CopyFinder-Standalone` and a standalone zip. |
| `INSTALL.md` | End-user install, update, uninstall, checksum, CFA, OneDrive, and enterprise deployment notes. |
| `README.md` | User-facing overview, build, test, run, and publish commands. |
| `REPO_LAYOUT.md` | Repository map and source/generated artifact guidance. |
| `.gitignore` | Excludes local build, test, IDE, and release output. |

## Source Folders

| Path | Purpose |
| --- | --- |
| `Models\` | Plain data models and scan options used across scanning, review, and settings. |
| `Services\` | Scanner, deployment-safe file operations, compatibility checks, settings persistence, shell picker integration, and delete-safety validation. |
| `ViewModels\` | UI-facing duplicate group/file state used by the WinUI review grid. |
| `Tests\` | Focused console regression harness for scanner behavior, delete validation, version stamps, and publish policy. |
| `Technification\` | Runtime logo, app icon, and file-type image assets copied into app output. |

## Current Source Files

```text
Models/
|-- AppSettings.cs
|-- DeploymentCompatibilityReport.cs
|-- DuplicateFile.cs
|-- DuplicateReportFile.cs
|-- DuplicateScanResult.cs
|-- KeepRule.cs
`-- ScanOptions.cs

Services/
|-- ControlledFolderAccessService.cs
|-- DeploymentCompatibilityChecker.cs
|-- DeploymentLogger.cs
|-- DuplicateDeleteValidator.cs
|-- DuplicateReportFormatter.cs
|-- DuplicateScanner.cs
|-- FilePathClassifier.cs
|-- NtfsPermissionService.cs
|-- OneDriveFileHandler.cs
|-- SafeFile.cs
|-- SettingsService.cs
|-- ShellFileSavePicker.cs
`-- ShellFolderPicker.cs

ViewModels/
|-- DuplicateFileViewModel.cs
`-- DuplicateGroupViewModel.cs

Tests/
|-- CopyFinder.Tests.csproj
`-- Program.cs

Technification/
|-- FileIcons/
|-- Logo/
    `-- favicon/
```

## Generated Folders

These folders are generated locally and should not be treated as source:

| Path | Source Control | Notes |
| --- | --- | --- |
| `bin\` | Ignore | Build output from the main app. |
| `obj\` | Ignore | Intermediate MSBuild and restore output. |
| `Tests\bin\` | Ignore | Test build output. |
| `Tests\obj\` | Ignore | Test intermediate output. |
| `publish\` | Ignore | Release output and GitHub-uploadable zip files created by `publish.ps1`. |
| `TestArtifacts\` | Ignore | Temporary folders created by the regression harness. |
| `.vs\` | Ignore | Visual Studio local workspace state. |

## Build And Release Commands

```powershell
dotnet restore CopyFinder.sln
dotnet build CopyFinder.sln --no-restore -v:minimal
dotnet run --project Tests\CopyFinder.Tests.csproj --no-restore
.\publish.ps1
```

The normal GitHub release asset is:

```text
publish\CopyFinder-v<version>-win-x64-Standalone.zip
```

## Repo Upload Guidance

Include the source folders and root files listed above. Exclude generated folders, local IDE state, temporary test data, and release output unless you are intentionally uploading a release zip as a GitHub Release asset.
