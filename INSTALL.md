# CopyFinder Install Instructions

Version: 2.1.8

CopyFinder is published as an unpackaged, standalone Windows desktop app. There is no MSI installer in this release. Install by extracting the release zip to a stable local folder and running `CopyFinder.exe`.

## Requirements

- Windows 11 build 26100 or newer.
- x64 Windows.
- Permission to extract files to the chosen install folder.
- For managed or locked-down computers, permission to allow `CopyFinder.exe` through Controlled Folder Access if Windows blocks protected-folder writes.

The release is self-contained and includes the runtime files it needs.

## Files To Download

Download both files from the release:

```text
CopyFinder-v2.1.8-win-x64-Standalone.zip
CopyFinder-v2.1.8-win-x64-Standalone.zip.sha256.txt
```

## Verify The Zip

Run this from the folder containing the downloaded files:

```powershell
$zip = ".\CopyFinder-v2.1.8-win-x64-Standalone.zip"
$expected = (Get-Content ".\CopyFinder-v2.1.8-win-x64-Standalone.zip.sha256.txt").Split(" ")[0]
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
if ($actual -ne $expected) { throw "Checksum mismatch. Expected $expected but got $actual." }
"Checksum OK: $actual"
```

Do not run the app if the checksum does not match.

## Install

1. Create a stable local install folder, for example:

```powershell
New-Item -ItemType Directory -Force -Path "C:\Tools\CopyFinder" | Out-Null
```

2. Extract the zip into that folder:

```powershell
Expand-Archive -LiteralPath ".\CopyFinder-v2.1.8-win-x64-Standalone.zip" -DestinationPath "C:\Tools\CopyFinder" -Force
```

3. Run the app:

```powershell
Start-Process "C:\Tools\CopyFinder\CopyFinder.exe"
```

You can also run `Start-CopyFinder.cmd` from the extracted folder.

## First Launch

On first launch for a new version, CopyFinder shows a compatibility report covering:

- Controlled Folder Access status.
- Known OneDrive roots for the current user.
- Working directory creation at `%LOCALAPPDATA%\CopyFinder\Temp`.
- NTFS write/delete readiness for the working directory.
- Deployment logging status.

Deployment logs are written to:

```text
%ProgramData%\CopyFinder\Logs\deployment.log
```

If `%ProgramData%` cannot be written by the current user, CopyFinder logs a fallback entry under `%LOCALAPPDATA%\CopyFinder\Logs\deployment.log`.

## Controlled Folder Access

If Windows blocks the app from changing files in protected folders, allow the exact installed executable path.

For the example install folder:

```powershell
Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -Command "Add-MpPreference -ControlledFolderAccessAllowedApplications ''C:\Tools\CopyFinder\CopyFinder.exe''"'
```

If you install somewhere else, replace the path with the exact `CopyFinder.exe` path shown in the app compatibility report.

## OneDrive Folders

CopyFinder detects files inside known OneDrive folders. When a file is cloud-only, syncing, or in use, CopyFinder copies it to:

```text
%LOCALAPPDATA%\CopyFinder\Temp
```

It processes the temporary copy and does not delete the original while OneDrive still reports an active lock or sync risk.

## Updates

1. Close CopyFinder.
2. Download and verify the new release zip and SHA-256 sidecar.
3. Extract the new zip over the existing install folder.
4. Start `CopyFinder.exe`.

If files are locked during update, close CopyFinder from Task Manager and repeat the extract step.

## Uninstall

1. Close CopyFinder.
2. Delete the install folder, for example `C:\Tools\CopyFinder`.
3. Optional user data cleanup:

```powershell
Remove-Item -LiteralPath "$env:LOCALAPPDATA\CopyFinder" -Recurse -Force
Remove-Item -LiteralPath "$env:ProgramData\CopyFinder" -Recurse -Force
```

The optional cleanup removes settings, temp files, and deployment logs.

## Enterprise Notes

- Normal UI deletes do not silently take ownership of protected files.
- Ownership repair requires an elevated process or an approved enterprise SYSTEM helper service.
- SmartScreen warnings on unsigned builds are reputation/signing warnings. They are not the same as a Defender malware detection.
- If an antivirus product reports a detection, record the exact detection name and submit the zip or hash to the vendor for false-positive analysis.
