# StandDesk

A C# / WPF Windows app for controlling a LINAK Bluetooth standing desk from the desktop, system tray, or keyboard.

This project is configured for **DESK 2079**, with a LINAK DPG1I002-00 panel. The Bluetooth address, calibration, and initial presets are specific to that desk. Other desks require configuration changes and supervised hardware validation before movement is enabled.

## Features

- Move to saved sitting and standing heights with one action; interrupt travel with **Stop**.
- Edit heights in centimeters or capture the desk's current stationary position.
- Configure global keyboard shortcuts, including shortcuts on a SteelSeries keyboard.
- Keep the app in the system tray and optionally start it at Windows sign-in.
- Read the current height over native Windows Bluetooth LE.
- Disconnect after 30 seconds idle and reconnect on the next action.

The app controls movement from the PC. It does not modify desk firmware, onboard presets, or the physical paddle's double-tap behavior.

## Requirements

- Windows 10/11 x64 with Bluetooth LE support and the desk paired in Windows.
- An awake Windows session within Bluetooth range of the desk.
- For development: **.NET SDK 10.0.403 x64**, or a compatible patch allowed by `global.json`.

Visual Studio is optional. Python and a C++ compiler are not required for the C# app. The self-contained published build includes its .NET runtime.

## Build and run

From the repository root in PowerShell:

```powershell
dotnet restore StandDesk.sln
dotnet build StandDesk.sln -c Release
dotnet test tests/StandDesk.Tests -c Release
dotnet publish src/StandDesk.App -c Release -r win-x64 --self-contained true -o dist/StandDesk
```

Open `dist/StandDesk/StandDesk.exe`. Keep the entire published folder together. Build outputs are excluded from Git and can be generated after cloning.

For development, run:

```powershell
dotnet run --project src/StandDesk.App
```

## Using the app

1. Open **Heights** to set sitting and standing targets. Saving heights does not move the desk. Use the physical paddle and **Use current height** to capture a comfortable position.
2. Click **Sit** or **Stand** to move to a saved target. **Stop movement** interrupts travel.
3. In **Settings**, enable and save keyboard shortcuts or **Start when I sign in to Windows** if desired.

| Action | Suggested shortcut |
| --- | --- |
| Sit | `Ctrl+Alt+Shift+Down` |
| Stand | `Ctrl+Alt+Shift+Up` |
| Stop | `Ctrl+Alt+Shift+Space` |

Shortcuts and startup are off for a fresh configuration. Initial presets are **74.12 cm sitting** and **116.87 cm standing**. Shortcut conflicts are reported when saving settings.

Closing the window stops pending movement and hides the app to the tray. Double-click the tray icon or reopen the executable to restore it. Use **Exit** to quit. After an idle disconnect, the displayed height is the last reading; **Refresh** obtains a fresh one.

## Settings and startup

Preferences, the saved hardware-validation result, and bounded logs are stored in:

```text
%USERPROFILE%\.standdesk\
```

This location is shared by ordinary Windows launches and launches from Codex, avoiding Windows packaged-app AppData redirection. Settings are local to each Windows user and are not included in the repository.

Startup launches the executable with `--background`; it never starts desk movement. Keep the published folder at a stable path. If you move it, disable and re-enable startup in Settings. `startup-diagnostic.json` in the settings folder records the effective settings path and flags for troubleshooting.

## Hardware validation

A fresh configuration keeps movement disabled until supervised hardware validation passes. Unit tests use a fake Bluetooth transport and never move the physical desk. The attended hardware test is a separate diagnostic mode described in [build instructions](docs/BUILD.md); it deliberately moves the desk and must not run unattended or in CI.

Movement cancellation handles errors, stale feedback, stalls, session lock, and suspend. Delivery of Stop depends on Bluetooth communication; use the physical paddle if a stop cannot be confirmed. See [hardware validation results and limitations](docs/HARDWARE_VALIDATION.md).

## Project layout

```text
src/StandDesk.App/       WPF UI, Windows Bluetooth, tray, and hotkeys
src/StandDesk.Core/      Protocol, settings, and movement controller
tests/StandDesk.Tests/   Automated tests without physical desk movement
config/                 Initial desk-specific configuration
docs/                   Build, usage, and hardware documentation
plans/                  Design and implementation notes
```

The root Python scripts are earlier desk investigation and backup utilities. They are not used by the C# app. Local backups, diagnostic captures, cloned reference repositories, virtual environments, and published binaries are excluded by `.gitignore`.

## Documentation

- [User guide](docs/USER_GUIDE.md)
- [Build and diagnostic instructions](docs/BUILD.md)
- [Hardware validation results](docs/HARDWARE_VALIDATION.md)
