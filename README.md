# CopyFinder

![Windows OS](https://img.shields.io/badge/OS-Windows-0079d4?logo=windows "Runs on Windows")
![Intel](https://img.shields.io/badge/CPU-Intel-31c5f3?logo=intel "Intel Compatible")
![AMD](https://img.shields.io/badge/CPU-AMD-00a774?logo=amd "AMD Compatible")
![Made in Melbourne](https://img.shields.io/badge/🌏%20Made%20in-Melbourne%20Australia-FFB6C1?style=flat "Made in Melbourne")
![Licence](https://img.shields.io/badge/📜%20Licence-CC0%201.0-lightgrey.svg?style=flat "Licence")
![Version \2.0.3](https://img.shields.io/badge/Version-2.0.3-yellow?logo=version "Version 2.0.3")

A WinUI 3 desktop app for finding duplicate files in a selected directory.

See [REPO_LAYOUT.md](REPO_LAYOUT.md) for the repository map, source folder purpose, generated folder rules, and GitHub upload guidance.

## 📜 Behaviour

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

## 📦 Build

Requires Windows 11 build 26100 or newer, Windows SDK 10.0.26100.0 or newer, and the .NET 10 SDK.

```powershell
dotnet build
```

## 📦 Test

Run the focused regression harness:

```powershell
dotnet run --project Tests\CopyFinder.Tests.csproj
```

## 🕹️ Run

```powershell
dotnet run
```

## ⚡ Publish

Create a standalone runnable app folder and zip for testing:

```powershell
.\publish.ps1
```

The app is published to `publish\CopyFinder-Standalone`.
Run `CopyFinder.exe` from that folder or send the generated standalone zip to a test machine.
The publish script clears the output folder first so stale files are not carried forward.
Debug symbol files are excluded from the normal standalone zip. Use `.\publish.ps1 -IncludeDebugSymbols` for internal diagnostic builds.

## 📝Credits

Created by **Dean John Weiniger**.  

## 📜 Licence

This work is dedicated to the public domain under the **Creative Commons CC0 1.0 Universal License**.  
[![CC0 1.0](https://img.shields.io/badge/License-CC0%201.0-lightgrey?logo=creativecommons&logoColor=white)](https://creativecommons.org/publicdomain/zero/1.0/)  

**You are free to:**  
✅ **Share** – Copy and redistribute the material in any medium or format.  
✅ **Adapt** – Remix, transform, and build upon the material for any purpose, even commercially.  
✅ **Use without attribution** – No credit required, though it’s appreciated.

**No conditions apply:**  
🚫 No attribution required.  
🚫 No restrictions on use.  
**Full licence text:** [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)  

---

### *updated: 18-06-2026*
