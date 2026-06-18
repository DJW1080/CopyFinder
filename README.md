# CopyFinder

Version: 2.0.3

CopyFinder is a WinUI 3 desktop app for finding duplicate files in a selected directory.

See `REPO_LAYOUT.md` for the repository map, source folder purpose, generated folder rules, and GitHub upload guidance.

## Behavior

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
- Lets you manually choose the kept file inside a duplicate group.
- Opens a file location from the review grid.
- Hashes candidate files with configurable worker count.
- Remembers last folder, keep rule, scan limit, hash workers, and scan filters.

## Upgrade Groups

- Review safety: grouped review, scan-stop limits, delete confirmation, and post-delete summaries.
- Keep rules: original name, shortest name, oldest file, newest file, preferred folder, or highest image resolution.
- Ignore rules: minimum file size, excluded extensions, hidden files, and system files.
- Review controls: manual keep override and open file location.
- Performance: throttled parallel hashing.
- Reporting: CSV and JSON exports.
- Planned next: richer image metadata, duplicate scan continuation, and safer network-drive workflows.

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
Run `CopyFinder.exe` from that folder, or send the generated standalone zip to a test machine.
The publish script clears the output folder first so stale files are not carried forward.
Debug symbol files are excluded from the normal standalone zip. Use `.\publish.ps1 -IncludeDebugSymbols` for internal diagnostic builds.
