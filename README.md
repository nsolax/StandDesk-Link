# StandDesk — IKEA / LINAK DPG1I002-00

A C# / WPF Windows app built for an IKEA standing desk equipped with a **LINAK DPG1I002-00** controller. Control sitting and standing heights from the desktop, system tray, or keyboard.

## Supported hardware

This version was developed and tested with one physical desk:

| Detail | Development desk |
| --- | --- |
| Desk brand | IKEA |
| Controller manufacturer | LINAK |
| Controller / item ID | DPG1I002-00 (label supplied as `DPG1i002-00`) |
| Other label markings | “Designed in Denmark”, `DK-6430` |
| Bluetooth name | `DESK 2079` |
| Tested operating system | Windows 11 x64 |

The exact IKEA retail desk model has not been identified. The controller identifier above describes the LINAK panel; it is not an IKEA retail model name. `DESK 2079` is the development desk's Bluetooth name, not a product model.

**This is a desk-specific implementation, not a universal standing-desk controller.** Compatibility with other IKEA desks, other LINAK controllers, or other manufacturers has not been verified. A matching controller label alone does not establish compatibility.

### Adapting it to another desk

The current source includes the development desk's Bluetooth address, calibration, and initial presets. `DeskSettings.Validate()` also restricts the allowed address. This release does not provide automatic desk discovery or a multi-desk setup wizard.

To adapt the project, review `src/StandDesk.Core/DeskSettings.cs`, its address validation, and the Bluetooth protocol in `src/StandDesk.App/Services/WinRtBleTransport.cs`. The JSON file in `config/` documents the initial values; changing that file alone does not reconfigure the compiled app. Verify the new desk's protocol, calibration, travel bounds, and Stop behavior before enabling movement. Do not reuse the development desk's height limits or mark hardware validation successful without an attended test.

This is an independent project and is not affiliated with or endorsed by IKEA or LINAK.

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
