# Real-hardware acceptance test — GameSir Cyclone 2

The automated test suite proves the parser, state machine, settings, autostart and diagnostics
logic. It **cannot** prove that a real controller is read correctly, because the controller is
attached to your PC, not to the development sandbox.

This document is the missing half of the Definition of Done.

---

## 0. Prerequisites

| Requirement | Detail |
|---|---|
| OS | Windows 11 x64 |
| Controller | GameSir Cyclone 2 |
| Connection | 2.4 GHz GameSir dongle (USB) |
| Controller mode | **XInput / Xbox** |
| GameSir Connect | fully closed, including the tray icon |
| Battery | ideally **not** at 100%, so a cross-check is meaningful |

Build first:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

---

## 1. Run diagnostics before trusting the widget

```powershell
.\src\CycloneBattery.App\bin\Release\net10.0-windows\CycloneBattery.exe --diagnostics
```

Or, without publishing:

```powershell
dotnet run --project .\src\CycloneBattery.App\CycloneBattery.App.csproj -- --diagnostics
```

To also write the report next to the logs:

```powershell
CycloneBattery.exe --diagnostics --save
```

Saved to `%LOCALAPPDATA%\CycloneBattery\logs\diagnostic-<timestamp>.txt`.

### What a healthy report looks like

```text
--- Controller identity ---
Identity             : XInput (Cyclone 2 (XInput mode))
Expected             : vid=0x3537 pid=0x100B (XInput)

--- Matching HID devices ---
[0] vid=0x3537 pid=0x100B in=64B out=64B product="..." manufacturer="..." id=<12 hex chars>

--- Interface validation ---
Outcome              : Success

--- Battery ---
0x12 report received : yes
Battery percent      : 73%
Cable flag (byte 35) : 0 (on battery)
Confirmed frames     : 24 (battery 73..73%)
First frame hex      : 12 80 80 80 ...
```

### Interpreting failures

| Symptom in the report | Likely cause | What to do |
|---|---|---|
| `Identity: DongleIdle` or `(none)` under Matching HID devices | controller off, or not in XInput mode | switch the controller to Xbox mode, power it on |
| `Outcome: AllBusy` | GameSir Connect (or another tool) owns the interface | close it completely, retry |
| `Outcome: OpenedButNoStatus` | interface opened but no `0x12` stream | keep the controller awake, retry; then paste the log |
| `0x12 report received: no`, `parse reject: TooShort` | report length differs from research | paste the log — the offset assumptions need adjusting |
| `parse reject: BatteryOutOfRange` | battery byte is not at index 36 on this firmware | paste the log with the `First frame hex` line |
| `Battery percent: unknown` but frames are seen | offset or scaling differs | paste the log, do **not** accept a guessed value |

---

## 2. Acceptance checklist

Tick every line on the real machine. The MVP is not done until they all pass.

### Detection and battery

- [ ] 1. GameSir Connect is closed.
- [ ] 2. Controller is in XInput mode.
- [ ] 3. Connected through the 2.4 GHz dongle.
- [ ] 4. The app detects the controller (tray tooltip shows a percentage).
- [ ] 5. Battery is shown as a plausible `0..100%`.
- [ ] 6. Cross-check against GameSir Connect at a **non-100%** charge: values agree or are very
      close. If there is a systematic offset, capture diagnostics — do not guess a correction.

### Cable / charging state

- [ ] 7. Unplug the charging cable → status changes to `On battery`.
- [ ] 8. Plug the cable back in → status changes to `Charging` / external power.

### Reconnect

- [ ] 9. Power the controller off → the app becomes disconnected without crashing.
- [ ] 10. Power the controller on → the app reconnects automatically (no manual Refresh).
- [ ] 11. Unplug the dongle → disconnected.
- [ ] 12. Reinsert the dongle → reconnects automatically.
- [ ] 13. Restart the app → settings and widget position persist.

### Autostart

- [ ] 14. Enable `Start with Windows`, then sign out/in or reboot → exactly one tray process
      starts, no console window, widget behaviour follows the saved setting.

### Coexistence

- [ ] 15. Start GameSir Connect while the app runs → the app shows `Interface busy`, does not
      crash, and recovers when GameSir Connect is closed.

### Gameplay

- [ ] 16. Play a controller-heavy game with the app running: input is normal, no added lag, no
      repeated disconnects, no focus stealing, negligible CPU.

### Notifications

- [ ] 17. `Settings → Test notification` shows a Windows notification.
- [ ] 18. (Observed naturally later) a real low-battery event notifies once and does not repeat.

---

## 3. Reporting a problem

Paste the **complete** diagnostics output plus this context. Descriptions like "it doesn't work"
cannot be acted on.

```text
Hardware   : GameSir Cyclone 2, 2.4 GHz dongle, XInput mode
OS         : Windows 11 x64
GameSir Connect: closed
Expected   : Battery 73%
Observed   : Battery unknown

<COMPLETE DIAGNOSTIC OUTPUT>

<BUILD / TEST OUTPUT IF RELEVANT>
```

The `First frame hex` line is the single most useful piece: it lets the byte offsets be re-derived
exactly instead of guessed.
