# CopyFinder

Version: 2.0.7

CopyFinder is a WinUI 3 desktop app for finding duplicate files in a selected directory.

See `INSTALL.md` for end-user install/update/uninstall steps and `REPO_LAYOUT.md` for the repository map, source folder purpose, generated folder rules, and GitHub upload guidance.

## Behaviour

- Scans subfolders recursively.
- Groups possible duplicates by file size first.
- Hashes only same-size files with SHA-256.
- Shows duplicate matches as expandable groups.
- Marks one file in each group as `Keep` using the selected keep rule.
- Selects the remaining duplicate files by default.
- Moves selected duplicates to the Windows Recycle Bin after confirmation.
- Stops each scan after the configured duplicate file count. The default is 500 duplicate files.
- Exports scan results to CSV or JSON.
- Shows image thumbnails and includes image dimensions in exported reports.
- Warns when selected files are on network paths or mapped network drives because deletion may be final.
- Let's you manually choose the kept file inside a duplicate group.
- Opens a file location from the review grid.
- Hashes candidate files with configurable worker count.
- Remembers last folder, keep rule, scan limit, hash workers, and scan filters.
- Runs a first-launch deployment compatibility check for Controlled Folder Access, OneDrive roots, NTFS write/delete readiness, the working directory, and deployment logging.
- Routes scan hashing, image metadata reads, report exports, and duplicate deletes through a deployment-safe file layer.
- Writes deployment logs to `%ProgramData%\CopyFinder\Logs\deployment.log` when ProgramData is writable.

## Upgrade Groups

- Review safety: grouped review, scan-stop limits, delete confirmation, and post-delete summaries.
- Keep rules: original name, shortest name, oldest file, newest file, preferred folder, or highest image resolution.
- Ignore rules: minimum file size, excluded extensions, hidden files, and system files.
- Review controls: manual keep override and open file location.
- Performance: throttled parallel hashing.
- Reporting: CSV and JSON exports with test-backed formatting.
- Deployment hardening: Controlled Folder Access guidance, OneDrive working-copy fallback, NTFS permission checks, and logged delete remediation.
- Planned next: richer image metadata, duplicate scan continuation, and signed installer/reputation hardening.

## Build

Requires Windows 11 build 26100 or newer, Windows SDK 10.0.26100.0 or newer, and the .NET 10 SDK.

```powershell
dotnet build
```

## Test

Run the focused regression harness:

```powershell
dotnet run --project Tests\CopyFinder.Tests.csproj
```

## Run

```powershell
dotnet run
```

## Publish

Create a standalone runnable app folder and zip for testing:

```powershell
.\publish.ps1
```

The app is published to `publish\CopyFinder-Standalone`.
Run `CopyFinder.exe` from that folder or send the generated standalone zip to a test machine.
The publish script clears the output folder first so stale files are not carried forward.
Debug symbol files are excluded from the normal standalone zip. Use `.\publish.ps1 -IncludeDebugSymbols` for internal diagnostic builds.

Install instructions for release users are in `INSTALL.md`.

## Deployment Safety

CopyFinder uses `SafeFile` for protected file operations. The layer logs CFA status, possible CFA blocks, OneDrive locks/fallback copies, permission checks, read-only attribute changes, ownership-repair attempts, and delete failures to `%ProgramData%\CopyFinder\Logs\deployment.log`.

If Controlled Folder Access blocks the app, allow this exact executable path from the published folder:

```powershell
Add-MpPreference -ControlledFolderAccessAllowedApplications "C:\Path\To\CopyFinder.exe"
```

Normal UI deletes do not silently take ownership. Enterprise deployments that need ownership repair should run CopyFinder elevated or add a dedicated SYSTEM helper service for approved remediation.
