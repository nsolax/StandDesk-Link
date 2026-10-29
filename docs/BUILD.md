# Build

Install stable **.NET SDK 10.0.403 x64**, or a compatible servicing patch. Windows 10/11 build host is required for WPF. `global.json` pins the installed SDK feature band. Visual Studio 2026 with .NET desktop development is optional. No C++ compiler or separate native SDK is required.

From the project root:

```powershell
dotnet restore StandDesk.sln
dotnet build StandDesk.sln -c Release
dotnet test tests/StandDesk.Tests -c Release
dotnet publish src/StandDesk.App -c Release -r win-x64 --self-contained true -o dist/StandDesk
```

The Windows target framework brings in Windows API reference assemblies through NuGet. First restore/publish needs network access. The application is not trimmed or Native AOT compiled. Keep all published DLLs and runtime files with StandDesk.exe.

`StandDesk.Core` contains protocol conversion, settings, hotkey parsing and movement state machine. `StandDesk.App` contains WPF UI, Windows BLE, tray and native hotkey integration. Tests use a deterministic fake Bluetooth transport and never move a real desk.

## Diagnostics

`StandDesk.exe --probe=C:\absolute\path\probe.json` performs a read-only connection and records height and calibration, then exits. No movement commands are sent.

`--test-motion=<absolute file>` is an attended hardware validation mode: it deliberately moves the desk by a small amount, proves automatic arrival, then interrupts a separate short move. Do not invoke it as part of ordinary builds, startup, CI, or unattended tests. It can enable local movement controls only when both checks pass. It never changes onboard presets or firmware. Run diagnostics with the ordinary app exited; only one instance is permitted.
