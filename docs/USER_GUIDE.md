# Using StandDesk

Open `StandDesk.exe`. It reads the desk on startup without moving it. Click **Sit** or **Stand** once to move to the locally saved height. **Stop movement** interrupts a move. If Bluetooth is unavailable, no movement is replayed when it returns; press the action again deliberately.

## Adjust heights

Open **Heights**, enter cm values, and click **Save heights**. Sitting must be below standing. The buttons, tray actions and shortcuts all use the saved values. Cancel restores saved settings. Saving never moves the desk and does not overwrite its onboard memories.

To capture a comfortable position, first use the physical paddle to place the desk, release it, then select **Use current height** for sitting or standing. This obtains a fresh stationary read and can expand the known operating span to include that physically reached position. Click Save heights to keep it.

For a numeric target beyond the known span, first establish the relevant position with the physical paddle, then use **Operating range → Capture lower/upper bound**. These are local allowed-travel bounds, not a measurement of the desk's mechanical limits. Saving validates both bounds and presets together. A calibration mismatch disables travel until the configured offset is reconciled; the app does not change calibration automatically.

## Keyboard

In Settings, review/edit the proposed shortcuts, select **Enable keyboard shortcuts**, and save. Ctrl, Alt, Shift plus a letter, digit, arrow, Space, Home, End, or supported F-key can be used; F12 and Windows-key combinations are excluded. Registration checks common conflicts and retains the previous bindings if any new binding fails. This cannot detect every shortcut handled privately by another application.

The Stop shortcut must register before Sit/Stand are enabled. Keyboard repeat does not queue journeys. Pressing the opposite preset during movement stops the current move; press it again after stopping to move in the new direction. Ordinary key combinations work on the SteelSeries keyboard without modifying SteelSeries GG.

## Background operation

Closing the window stops pending movement and hides the window to the tray. Double-click the icon to reopen it, or right-click for Sit, Stand, Stop and Exit. Idle operation has no continuous scan or fast polling. Use **Start when I sign in to Windows** only if desired; startup itself does not move the desk.

Settings and bounded logs live in `%USERPROFILE%\.standdesk`. This folder is shared by ordinary Windows launches and launches from Codex, avoiding packaged-app AppData redirection. The previous validated settings were migrated during this update; the original copy was preserved. Uninstall by exiting, disabling startup first if enabled, and removing the distribution folder. Removing local settings is optional. Original desk backups remain in this workspace's `backups` folder.

Auto-start explicitly loads the saved file from the absolute Windows profile path. It does not silently substitute fresh-install defaults if the profile/file is temporarily unavailable. Restoring the tray window reloads saved settings when idle. `startup-diagnostic.json` in the settings folder records the last startup's effective path, mode and validation/hotkey/startup flags for troubleshooting; no automatic movement is performed by this diagnostic.

## Interruptions

Errors, stale feedback, stalls, direction changes, session lock and suspend cancel future movement commands. Stop sends both the target-reference abort and the ordinary command-channel Stop, then active movement cleanup waits for stationary feedback. A radio failure or queued Windows BLE operation can delay delivery; the app never promises physical stopping when communication is lost. Use the physical paddle when a stop cannot be confirmed.

No firmware is modified. Native double-tap paddle travel remains unsupported by this panel; automatic movement is provided by the PC.
