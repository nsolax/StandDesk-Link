# StandDesk C# Windows controller — design and construction plan

Status: implemented and packaged in `dist/StandDesk`; 29 tests pass, Release build has zero warnings/errors. Read-only connection, short automatic arrival, interrupted travel/Stop, return to sitting, local height editing, shortcut registration/reconfiguration and tray reopening were verified. Full standing-height travel and forced hardware interruption scenarios remain untested; see `docs/HARDWARE_VALIDATION.md`. Requested approach: C# with WPF, replacing the earlier C++ selection. Planning workflow: ECC Blueprint; architecture and tests follow ECC .NET Patterns and C# Testing. No Git repository exists here; work was done in place without branches, PRs or remote publication.

## Outcome

A small C# WPF `StandDesk.exe` runs in the user's Windows session. It provides Sit, Stand, and Stop from a window, notification-area menu, and configurable global keyboard shortcuts. One action initiates travel to a saved height. The app controls Bluetooth movement from the PC; it does not add native double-tap functionality to the desk paddle. The PC must be awake and Bluetooth available.

User requirement added: sitting and standing target heights are manually adjustable in the app. The recorded heights are initial defaults, not permanent preset values. Support both typing a height in centimeters and capturing the desk's current position as either preset.

The first release includes the tray and controls; hotkeys are added after the movement controller is verified. Startup at sign-in is an explicit option, off by default. No automatic movement at launch, reconnect, login, resume, or configuration load.

## Existing evidence and constraints

- Workspace: `C:\Users\nxnine\devx\playground\StandDesk`.
- Host: Windows 11 Pro, x64. Visual Studio Community 2022 is registered. `dotnet --list-sdks` returned no SDKs. Installed runtimes include .NET/Windows Desktop 6, 7, and 8; these are not the SDK needed to build. A vswhere query did not report the .NET desktop workload. No installs or machine changes were made during planning.
- Desk: LINAK panel reported by user as DPG1I002-00; Bluetooth name `Desk 2079`, address `D8:81:2F:0B:F2:7F`. Windows has this device registered, and direct BLE connection was demonstrated.
- Baseline snapshot: `backups/20261029T122441Z/desk-2079-settings.json`, SHA256 alongside it. This is a partial settings snapshot, NOT firmware or a tested restore package. Setup query returned `0b00` rather than successful data.
- Verified preset writes: `backups/20261029T122740Z/preset-change.json`. Slots 1/2 persisted after reconnect; slot 3 was preserved. Owner-mode session setup was required for those writes. V1 of the app need not write any desk presets or user identifiers.
- `desired_positions.json` holds the two measured presets. Sitting: 74.12 cm, extension raw 1162. Standing: 116.87 cm, extension raw 5437. Configured base offset: raw 6250 = 625.0 mm. These are controller-calibrated readings, not tape measurements.
- Capabilities payload `2301`: three memories, automatic up/down flags false. Local software movement is separate from native Automatic drive support.
- SteelSeries GG is running. Exact keyboard model and final key combinations are not established. Ordinary Windows global hotkeys work regardless of keyboard vendor.
- Python scripts and downloaded reference repos are investigation evidence, not a production runtime dependency. Preserve all backups and scripts.

## Approaches considered

1. **C# with WPF and Windows WinRT BLE — selected.** Good fit for a compact Windows utility with editable settings, async Bluetooth operations, and tray/hotkey integration. Managed memory simplifies implementation; idle efficiency still depends on event-driven behavior and must be measured.
2. **C++ Win32/C++/WinRT — previous plan, superseded.** Lower potential runtime overhead, with more UI and lifecycle implementation work. No C++ app code was built, so no product migration is required.
3. **C# with WinUI 3 or the existing Python approach.** WinUI 3 offers modern controls but adds Windows App SDK setup for a small utility; Python has proven connectivity but was not selected for the permanent app. Neither is needed for the agreed WPF approach.

The user selected C# after the WPF recommendation. This document replaces C++ toolchain, UI, build, test, and packaging details while preserving the verified protocol evidence and feature requirements.

## Software to install

- **Required: .NET 10 SDK, stable Windows x64 release.** The SDK includes the build tools and runtime needed for local development; a runtime-only installer is insufficient. Use https://dotnet.microsoft.com/en-us/download/dotnet/10.0 and choose SDK -> Windows -> x64. Target .NET 10 LTS rather than .NET 8/9, whose support ends November 10, 2026, shortly after this plan's date.
- **Optional IDE: Visual Studio Community 2026**, stable release, with **.NET desktop development** selected. Include the .NET 10 SDK and its recommended desktop components; this can install the SDK instead of using the standalone installer. Visual Studio 2022 is installed, but targeting .NET 10 in that IDE is unsupported. It may stay installed alongside the newer IDE.
- A standalone .NET 10 SDK is sufficient for the Codex/CLI workflow; a full Visual Studio upgrade is not required just to build this app. Do not require C++ compilers, CMake, Qt, Python, or a separate Windows App SDK install.
- Windows API targeting references are restored through the Windows-specific .NET target framework/NuGet packages. Start without a separate native Windows SDK installation; add one only if a concrete build dependency proves necessary.
- Verify after installation in a new terminal: `dotnet --info`, `dotnet --list-sdks`, `dotnet new list wpf`. Confirm a 10.0 SDK, x64 architecture, and WPF template. Once code exists, verify Windows API targeting restore with `dotnet restore`.

## User experience

- Window: `Desk 2079`, connection/status text, current or last-read height with a freshness indication, Sit and Stand buttons showing their saved heights, prominent Stop, and Settings. Initial values are 74.12 cm and 116.87 cm; labels follow subsequent edits.
- First launch: visible window, controls disabled until a successful fresh read validates the selected desk. No movement caused by connecting.
- After that initial validation, an idle Sit/Stand click may perform connect -> fresh identity/calibration/height validation -> requested move. Disabling controls during connection must not make them unusable after the idle disconnect. Stop cancels this pending connection/action; late connection success cannot trigger travel.
- Tray: Open, Sit, Stand, Stop, Exit. Close-window action hides to tray only while idle; closing during movement cancels first. Exit always cancels and attempts stop, then unregisters resources.
- Settings: separate **Sitting height (cm)** and **Standing height (cm)** numeric fields with 0.1 cm adjustment steps, direct decimal entry, and **Use current height** beside each field. Preserve the original 74.12/116.87 cm values until deliberately changed; do not round unchanged values merely by opening Settings. Also include editable hotkeys, enable/disable hotkeys, and startup toggle.
- **Save** validates and atomically stores both target heights locally; **Cancel** discards edits. Saving does not move the desk or overwrite its onboard memory slots. Every window/tray/hotkey action resolves the latest saved value through the same settings model. Saved values persist across restarts, and the preset button labels update immediately.
- **Use current height** connects if needed, obtains a fresh stationary read with validated calibration, then copies that value to the selected field. It does not move the desk or save automatically. If disconnected/unreachable, moving, or calibration-invalid, show the reason and do not copy a cached height.
- Preset fields require finite numeric values, sitting strictly below standing, representable raw extension, and targets within the desk's verified operating bounds. The two initial presets are not mechanical limits and must not become a permanent editing restriction. Provide a local operating-range setup: read trustworthy controller limits if the protocol is established, otherwise let the user physically position the desk with its paddle and capture lower/upper allowed positions while stationary. Never infer unobserved limits or automatically drive to end stops. This allows new targets below 74.12 cm or above 116.87 cm once that range has been established. Explain out-of-range entries beside the fields and retain existing presets until valid settings are saved.
- Disable preset/range editing during movement or a pending movement connection. Snapshot the target at operation start; a settings change can never retarget an in-flight move. No preset edit triggers motion.
- Proposed keys, pending user choice: Ctrl+Alt+Shift+Up for Stand, Ctrl+Alt+Shift+Down for Sit, Ctrl+Alt+Shift+Space for Stop. Register with `MOD_NOREPEAT`; do not assume these are conflict-free. Registration detects many system-wide conflicts, but cannot prove the combination has no application-local use. Keep buttons usable on registration failure and show the failed combination.
- Global hotkeys are optional until verified. Register Stop before movement hotkeys; if Stop registration fails, do not enable movement hotkeys. Changing bindings is transactional and retains the previous set on failure.
- A second instance opens the existing instance rather than creating a competing movement controller. No administrator privileges should be needed for everyday operation.

## Architecture and data

- .NET 10 LTS, C#, WPF/XAML, SDK-style projects. Windows app target `net10.0-windows10.0.19041.0`, x64 release. Use supported language features shipped with the pinned stable SDK; enable nullable reference types and .NET analyzers. Pin an installed stable 10.0 SDK feature band in `global.json` with patch roll-forward; never pin an unavailable SDK.
- WPF UI uses a small view model with commands/bindings and Dispatcher marshaling for updates. WPF owns the UI thread/message loop. Use `HwndSource` hooks with Win32 `RegisterHotKey`/`UnregisterHotKey` through narrowly scoped interop, and a disposable `System.Windows.Forms.NotifyIcon` for the tray (`UseWPF` and `UseWindowsForms` enabled in the app project). Avoid a separate tray framework. Explicitly handle WPF shutdown so hiding a window does not end the process or leave an invisible unexitable app.
- `IBleTransport` abstracts connect, discover, read, subscribe, write, and disconnect. Production implementation uses Windows.Devices.Bluetooth/GenericAttributeProfile via Windows-targeted .NET; deterministic fake implementation supports tests. Dispose WinRT sessions/services/device objects, native registrations, timers, and cancellation sources, and unsubscribe events on teardown. GC alone does not release subscriptions or hotkeys deterministically.
- `DeskProtocol` validates buffers and converts raw heights. Represent raw extension, base offset, and absolute height distinctly. Absolute cm = `(base_raw + extension_raw) / 100`; never send absolute cm as an extension value.
- `MotionController` owns the serialized state machine and one motion operation. Use async/await with explicit CancellationToken propagation and injected time for tests, not `.Result`, `.Wait()`, busy loops, detached Tasks, or async void except guarded UI event handlers. WinRT operations start on the appropriate thread for device access; awaited I/O must not block the Dispatcher. An already-requested Stop cannot wait behind an unbounded command queue. Use simple constructor injection without requiring a hosted server/container.
- States: disconnected, connecting, idle, moving, stopping, fault. One accepted target at a time. Repeated same-target requests are ignored during travel. Opposite-direction requests stop current travel; require a fresh action afterward instead of queueing a reversal.
- Cancellation uses generation/operation identifiers, including for connecting. Late connect/read/write completions cannot revive a canceled move. Stop cancels future writes immediately and attempts protocol stop as soon as the transport permits. Windows GATT calls may outlive app deadlines; closing a session or canceling an operation does not prove a queued radio write was withdrawn.
- Small versioned `settings.json` under `%LOCALAPPDATA%\StandDesk`, with validated device address, editable raw presets, calibration, verified operating bounds and their source, hotkeys, and startup preference. Use System.Text.Json and immutable settings snapshots; no third-party JSON dependency. Keep initial defaults separate from saved overrides; migration/default seeding never replaces an existing user's edits. Import `desired_positions.json` once through documented migration. Configuration writes are atomic. Invalid settings cannot enable travel. If persistence fails, report it and retain the previous effective targets and labels.
- Seed `config/desk-2079.defaults.json` from the recorded evidence. No reads or mutations of SteelSeries databases. No server, network listener, telemetry, web UI, or Windows service.
- Store bounded local diagnostic logs for connection/error transitions, actions, and hardware-test results. Preserve the original backup independently.

## BLE and movement contract

The existing code/reference implementations establish the following candidate protocol; automatic travel has NOT yet been tested on this desk from this workspace. Keep it disabled until the staged hardware checks establish behavior.

| Purpose | Characteristic / encoding |
| --- | --- |
| Movement control | `99fa0002-338a-1024-8a49-009c0215f78a` |
| Wake / Stop | `fe00` / `ff00` |
| Error/status notification | `99fa0003-338a-1024-8a49-009c0215f78a` |
| Height/speed | `99fa0021-338a-1024-8a49-009c0215f78a`; uint16 LE extension in 0.1 mm, int16 LE speed |
| Target reference input | `99fa0031-338a-1024-8a49-009c0215f78a`; uint16 LE raw extension, refreshed during movement |
| DPG configuration | `99fa0011-338a-1024-8a49-009c0215f78a`; use only necessary validated read queries in V1 |

- Establish device access and identity, discover required services/characteristics, validate access/status results, subscribe to errors and fresh height updates, and obtain a valid baseline before sending a target. Handle access prompts from the UI thread as required by Windows; do not block it waiting for GATT.
- Check the configured base offset against a read DPG offset where available. Calibration mismatch disables motion until reconciled. A device-name match alone is insufficient identity.
- Only target the verified operating span. Initially the captured sitting/standing span is known; the in-app operating-range setup may extend it using validated controller limits or user-captured stationary physical positions. Do not infer the desk's mechanical limits from preset values. Invalid, malformed, stale, or implausible readings block movement.
- Candidate target refresh interval: 100–200 ms, adjusted from hardware observations. No overlapping target writes. Send only while the operation is active and fresh measurements support continued movement.
- Initial tuning candidates: feedback age at most 1 s during motion; start/progress timeout 2 s; overall travel deadline 45 s; arrival tolerance 3 mm. These are software defaults to validate, not measured desk guarantees.
- Freshness means the receipt time of an actual successful uncached read or valid notification, never the time a cached UI value was displayed. Some controllers notify only when values change; perform bounded explicit reads when necessary to validate stationary arrival or startup. A quiet notification stream alone is neither proof of disconnection nor permission to continue blind travel. If reads cannot refresh within the configured motion deadline, stop future target refresh and enter a fault.
- Speed zero alone is NOT arrival: allow bounded startup, then use distance/progress plus stable readings. Confirm arrival within tolerance and stationary after Stop. Wrong direction, overshoot outside tolerance, no progress, collision/error status, failed write, or lost feedback ends the operation without automatic retry.
- Stop button/hotkey, application exit, session lock, suspend, device removal, and disconnect invalidate the operation. Best-effort protocol stop where connected. Resume/reconnect never replays motion. Don't claim the app can physically stop a desk after the radio is lost.
- A blocked/in-flight GATT request can delay the Stop write; the physical paddle remains the independent local control. Verify controller watchdog behavior when refresh ceases before describing crash/disconnect stopping behavior. Do not keep issuing commands after a physical-paddle interruption or collision.
- During movement keep the connection and feedback subscriptions active. Idle policy: no background scanning or rapid polling; release the connection after a short grace period (proposed 30 s). A visible window may refresh height slowly while connected; hidden idle mode waits on OS events. Label stale height rather than showing it as live. No idle Bluetooth reconnect loop.

## File layout

```text
StandDesk.sln
global.json
Directory.Build.props
src/StandDesk.App/StandDesk.App.csproj
src/StandDesk.App/App.xaml[.cs]
src/StandDesk.App/MainWindow.xaml[.cs]
src/StandDesk.App/SettingsWindow.xaml[.cs]
src/StandDesk.App/ViewModels/MainViewModel.cs
src/StandDesk.App/Services/TrayService.cs
src/StandDesk.App/Services/HotkeyService.cs
src/StandDesk.App/Services/SettingsStore.cs
src/StandDesk.App/Services/WinRtBleTransport.cs
src/StandDesk.Core/StandDesk.Core.csproj
src/StandDesk.Core/DeskProtocol.cs
src/StandDesk.Core/MotionController.cs
src/StandDesk.Core/IBleTransport.cs
src/StandDesk.Core/DeskSettings.cs
config/desk-2079.defaults.json
tests/StandDesk.Tests/StandDesk.Tests.csproj
tests/StandDesk.Tests/FakeBleTransport.cs
tests/StandDesk.Tests/ProtocolTests.cs
tests/StandDesk.Tests/MotionControllerTests.cs
tests/StandDesk.Tests/SettingsTests.cs
docs/BUILD.md
docs/USER_GUIDE.md
docs/HARDWARE_VALIDATION.md
```

## Construction steps

Work in dependency order `1 -> 2 -> 3 -> 4 -> 5`. Avoid parallel file edits for this small app. Preserve existing evidence at every step. Each step can be completed and verified independently of later UI additions.

### 1. Establish .NET toolchain and buildable WPF shell

Context: workspace contains Python investigation and backups, not an existing C# application. The PC has .NET runtimes but no detected SDK. Visual Studio 2022 is registered; using it does not supply supported .NET 10 IDE integration.

- Verify stable .NET 10 SDK availability and WPF templates after the user's install. IDE installation is optional. Restore Windows targeting references through NuGet; report proxy/restore errors accurately rather than installing unrelated workloads.
- Add SDK-style solution, app and core projects, shared compiler settings, SDK pin, minimal WPF window, resources and build instructions. Core targets `net10.0` to keep protocol/motion tests independent of the Windows UI and BLE implementation. No BLE movement or hotkeys.
- Enable nullable reference types and analyzers, and address warnings in authored code. Use System.Text.Json and constructor-injected service boundaries.
- Verify: `dotnet restore StandDesk.sln`, `dotnet build StandDesk.sln -c Debug`, `dotnet run --project src/StandDesk.App`. Launch the shell and close it; no console window in published use.
- Exit: WPF app builds and opens on this PC without Python or a C++ compiler. Rollback: remove only new build output/app files; desk remains unchanged.

### 2. Implement protocol, read-only transport, settings, and fake transport

Context: address and protocol table above; saved presets raw 1162/5437, base raw 6250. The original JSON files are evidence to preserve.

- Add strong units, bounded packet parsing, versioned local settings/import, serialized async BLE ownership, status/error propagation, and read-only connection UI. Implement numeric Sit/Stand editing, Save/Cancel, current-height capture and operating-range setup without movement commands.
- Discover the exact device and read height plus calibration through Windows WinRT Bluetooth APIs from C#. Do not write owner mode, presets, or move commands.
- Tests: known byte sequences decode to 74.12/116.87 cm; reject truncated packets, invalid settings, swapped/overflowing targets; handle denied access, missing services, delayed completion, disconnect, canceled connect, and stale data. Verify decimal parsing (including configured Windows decimal separator), unit/raw round trips, Save versus Cancel, unchanged-value precision, persisted overrides surviving migration/restart, failed writes retaining prior state, bounds/capture validation and fresh stationary capture. Numeric editing and current-height capture must issue no motion commands.
- Use xUnit with native assertions and a handwritten deterministic fake transport; introduce other test libraries only if they solve a concrete need. No network services/containers are needed. Inject TimeProvider so timing/cancellation tests do not depend on real sleeps or BLE hardware.
- Verify: `dotnet build StandDesk.sln -c Release`, `dotnet test tests/StandDesk.Tests -c Release`; real read-only C# connection agrees with current desk position and releases resources afterward.
- Exit: validated telemetry and settings, no movement. Rollback: disable transport and use fake mode; no desk changes.

### 3. Implement and validate serialized automatic movement

Context: native Automatic drive flags are false; software target-reference control is a separate protocol that still needs real-desk validation. Always start from validated telemetry.

- Implement the motion state machine, refresh deadlines, progress/arrival logic, Stop path and late-completion fencing; expose window Sit/Stand/Stop using current saved settings. Keep global movement hotkeys disabled. Verify all action surfaces share the latest saved presets, editing is unavailable during pending/active motion, and an operation retains its initial target.
- Deterministic tests: initial zero speed; target already reached; increasing/decreasing travel; arrival/overshoot; stale/invalid feedback; stall; protocol error; failed or blocked write; Stop during connect and motion; opposite request; double trigger; cancellation followed by late callback; disconnect and resume; no queued/replayed movement.
- Verify fake transport records no further target writes after cancellation, except an already-in-flight write modeled explicitly. Stop attempts must never start a new move. Ensure error callbacks cannot deadlock the GATT worker.
- Hardware testing is interactive and attended: tell the user movement is about to be tested and obtain current readiness/clearance before the first live motion test. Begin with a small bounded move, verify Stop from window and physical paddle, then test both full saved journeys. Never run hardware movement as unattended CI or on app startup. A readiness check is for physical clearance, not a new design-approval workflow.
- Record observed refresh behavior, error semantics, stopping distance, accuracy and watchdog behavior when refresh ends using a bounded test. Do not deliberately collide the desk or disable physical protections.
- Exit: both positions and Stop work on real desk with measured results; failures leave motion disabled. Rollback: disable motion, preserve read-only UI and logs; never flash firmware.

### 4. Add tray lifecycle, hotkeys, and low-idle behavior

Context: movement passed Step 3. SteelSeries model-specific remapping isn't needed for Windows combinations. Final keys remain a user preference.

- Add tray controls and a settings window for hotkeys, RegisterHotKey with MOD_NOREPEAT, transactional registration/failure feedback, a single-instance guard, Explorer restart recovery, and clear hide versus exit behavior.
- Implement lock/suspend cancellation, delayed session release, no reconnect-driven movement, and bounded shutdown. Configure sign-in startup only when the user turns it on; no silent machine-wide changes.
- Verify hotkey dispatch in fake mode first; register conflicts with a helper window to test failures. Then perform an attended physical Stop-hotkey test and Sit/Stand hotkey test. Confirm no keyboard-repeat motion queue.
- Measure idle CPU-time delta and working set for 60 s after warmup in hidden/disconnected mode; record results with measurement context. Treat low CPU as an objective; do not invent RAM/CPU guarantees. Check repeated connect/move/stop cycles for resource growth.
- Exit: buttons and chosen hotkeys work with no foreground window, conflicts are visible, Stop remains responsive, idle results are recorded. Rollback: unregister hotkeys/remove startup entry, keep window controls.

### 5. Package, review, and hand over

Context: Steps 1–4 verified on the real desk. Existing settings backup remains separate from app settings.

- Publish a self-contained Release x64 app into `dist/StandDesk` using `dotnet publish src/StandDesk.App -c Release -r win-x64 --self-contained true -o dist/StandDesk`. Include the .NET runtime so running the distributed app does not require a separate .NET install. Prefer reliable folder deployment first; single-file packaging is optional after verification. Do not enable trimming or Native AOT for WPF in V1. Include config template and user guide, and document startup disable/removal and physical-paddle fallback.
- Review asynchronous lifetimes, cancellation races, IDisposable/event cleanup, exceptions at UI boundaries and raw/absolute height conversion against ECC .NET patterns. Do not broaden scope to updater, installer service, firmware patches, or SteelSeries integration without need.
- Verify clean launch from the distribution folder, one running instance, clean exit, removed startup entry when toggled off, preserved local settings, readable errors with Bluetooth off, and no movement on reopen/reconnect.
- Deliver executable location, build/test results, live hardware results and remaining limitations. If live movement has not been verified, state that and ship with movement disabled instead of claiming completion.
- Rollback: exit app, disable startup, remove distribution folder and optionally local app settings; desk presets/firmware remain as before app installation.

## Plan review and mutation

Implementation notes: the approved Window preview uses Desk/Heights/Settings tabs in one WPF window rather than a separate SettingsWindow. SettingsStore is in the core library for filesystem tests. Recorded prototype values are seeded in typed defaults and the config template rather than read from workspace-only JSON at runtime. New local user edits are preserved. Final single-instance tray recovery was tested after correcting the WPF visibility path. Hotkeys and sign-in startup are left off for the user to enable.

Before finalizing, obtain an adversarial plan review focused on GATT timing/cancellation, hotkey conflicts, motion validation, missing prerequisites and scope creep. Resolve critical findings in this file. Record review outcome below. No product code is authorized by the act of writing this plan; the next implementation turn can use it directly.

Review completed: independent ECC Blueprint plan review found no critical/high gaps. Incorporated its clarifications for idle reconnection actions, the V1 editable-height envelope, and read/notification freshness. Retained the explicit distinction between cancellation of future software writes and confirmed physical stopping when a radio write is already in flight.

Plan revision after review: user explicitly requested manual adjustment of sitting/standing heights. Added direct numeric editing, current-position capture, persistent local overrides, consistent button/tray/hotkey values, and a verified operating-range setup rather than permanently restricting edits to the initial two positions. Extended settings and motion validation criteria accordingly. This revision changes the proposed editable-height envelope described in the earlier review; no implementation or desk changes were made.

Language revision: user selected C# with WPF. Replaced the C++/CMake architecture with .NET 10 SDK-style projects, Windows WinRT Bluetooth access, WPF binding/Dispatcher rules, disposable tray/hotkey services, System.Text.Json settings, xUnit/fake-time tests and self-contained publishing. SDK inspection found runtimes only. The minimum prerequisite is the .NET 10 x64 SDK; Visual Studio 2026 with .NET desktop development is optional. The earlier C++ plan review does not constitute review of the revised managed architecture.

C# revision review completed: independent reviewer found no critical/high contradictions or managed lifecycle gaps. Verified the standalone SDK/CLI path, Visual Studio 18.0+ requirement for .NET 10 IDE targeting, and Windows-specific TFM for WinRT access. No product code or desk settings were changed during this revision.

If observed protocol behavior differs, pause live testing and revise transport/motion assumptions, tests, and validation criteria before continuing. Record reason and affected steps here. Do not silently substitute blind timed movement or repeat up/down commands when target-reference behavior fails.

## Sources

- Microsoft Windows desktop/WinRT interop: https://learn.microsoft.com/en-us/windows/apps/develop/interop/
- Windows-specific .NET target framework setup: https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/desktop/modernize/winrt-apis-desktop-apps.md
- WPF prerequisites: https://learn.microsoft.com/en-us/visualstudio/get-started/csharp/tutorial-wpf
- .NET SDK and Visual Studio compatibility: https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs
- .NET 10 SDK downloads: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Windows GATT lifecycle/timing: https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client
- RegisterHotKey behavior/conflicts: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- Notification-area lifecycle: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- LINAK manual: https://cdn.linak.com/-/media/files/user-manual-source/en/deskline-dpg-desk-panels-and-desk-control-app-user-manual-eng.ashx
- Community protocol and existing source reviewed locally: `reference/linak-desk-client/protocol.md`, `reference/linak-controller/linak_controller/desk.py`, `reference/linak-controller/linak_controller/gatt.py`. Use community descriptions as hypotheses validated against this desk, not manufacturer guarantees.
