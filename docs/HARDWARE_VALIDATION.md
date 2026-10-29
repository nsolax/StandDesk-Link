# Hardware validation — 2026-10-29

Device: Desk 2079, D8:81:2F:0B:F2:7F. Windows 11 x64, .NET 10.0.403, native Windows BLE APIs from C#. User explicitly confirmed attendance, clear desk/cables, and readiness for short movement + Stop testing.

## Verified

- Read-only C# connection returned base offset 6250 and height 74.12 cm, stationary. Evidence: `diagnostics/read-only-probe.json`.
- The initial short diagnostic was incorrectly marked successful from a 0.01 cm displacement despite a desk interruption. That was a diagnostic bug, not a valid movement result. Movement enablement was reset immediately. Hardware validation now requires expected signed travel, no fault, actual bounded automatic arrival and a separate observed Stop case. Regression tests reject wrong direction and faulted arrival.
- Root command-sequence comparison found the reference-input channel needs wake plus stop preflight. Added that sequence; a 1 cm automatic target arrived exactly in the next bounded test.
- Ordinary `ff00` Stop did not cancel active reference-input travel promptly. Verified the distinct `0180` reference abort in the primary community implementation and added it before command-channel Stop. Active operation cleanup polls until stationary rather than assuming one Stop write proves it.
- Final bounded test (`diagnostics/short-motion-test-3.json`): automatic arrival from raw 1461 to 1561, speed 0; separate interrupted move ended at raw 1593, speed 0. Both checks passed, with reference abort and Stop visible in command trace. Total movement in the final interrupted leg was 0.32 cm.
- Actual packaged WPF Sit button then returned the desk from 78.43 cm to **74.12 cm**, with status Ready. No physical movement was caused by editing or saving presets.
- UI tested through Windows computer use: height edit/save (74.22 cm), fresh current-position capture, restoration of sitting to 74.12 cm; custom shortcut registration and reconfiguration; Stop hotkey dispatch; hotkeys disabled again after the test. Startup remains off.
- Native UI smoke caught dark-theme surface/selected-tab accessibility issues and a WPF visibility mismatch when reopening a hidden instance. Corrected the template and window resources, and use a registered show-message to invoke WPF's Show/Activate path for an existing instance. No extra controller process is created.
- Independent source review identified the false-positive hardware test and a cancellation/disposal race. The validation predicate was corrected and tested; CTS cancellation/publication/retirement now share a lock so WinRT callbacks cannot cancel a disposed operation. The concurrent-callback stress test passes.
- Release build: zero warnings/errors. Core test suite: **29 passing tests**, including stale feedback, an in-flight write completing after Stop, failed-write no-retry behavior and wrong-direction feedback. The packaged app follows the active Windows light/dark appearance, including accessible tab content.

## Limits of this verification

The complete 74.12 → 116.87 cm standing journey was not exercised in this session; live testing remained within the authorized short-movement scope. Physical-paddle interruption, forced radio loss during motion, deliberate collision, suspend during motion and abrupt process termination were not live-tested. Collision testing is intentionally not performed. Software tests cover cancellation, protocol fault, stale/invalid packets, stalls and late connect completion; they do not prove radio timing or hardware collision behavior.

Hidden app idle measurement (`diagnostics/idle-resource-use.json`): 60.02 seconds, 15.62 ms CPU time, **0.026% of one CPU core**, **177.5 MiB working set**, 105.6 MiB private memory. This includes the full self-contained WPF/.NET process on this PC, not just controller logic. These are measurements, not universal resource guarantees.
