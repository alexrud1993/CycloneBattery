# CYCLONE BATTERY — TECHNICAL SPECIFICATION

**Project type:** lightweight Windows 11 tray utility + optional mini-widget  
**Working name:** `Cyclone Battery`  
**Target hardware:** GameSir Cyclone 2  
**Primary connection:** 2.4 GHz USB dongle  
**Primary controller mode:** XInput / Xbox mode  
**Target OS:** Windows 11 x64  
**Primary goal:** show the real controller battery level without launching GameSir Connect.

> The project name is provisional and can be changed later without changing architecture.

---

## 1. Product goal

Create a small, reliable Windows utility that runs in the notification area, reads the GameSir Cyclone 2 battery directly through its vendor HID interface, and shows:

- battery percentage;
- charging / cable-connected state;
- controller connected / disconnected state;
- low-battery notifications;
- an optional compact always-on-top battery widget.

The utility must not require GameSir Connect to run.

The utility must not modify controller configuration, firmware, calibration, profiles, RGB, mappings, dead zones, or any other controller settings.

---

## 2. Core principle

The app is a **read-only interoperability utility**.

It communicates only with the HID interface needed to request/read controller status.

No cloud.
No account.
No paid API.
No telemetry.
No internet connection required during normal use.
No administrator rights for normal operation.

---

## 3. Recommended technology stack

### Runtime / language

- **C#**
- **.NET 10 LTS**
- target framework: `net10.0-windows`
- x64 primary build

Reason: native Windows integration, straightforward WPF/tray behavior, clean self-contained deployment, long support window, no Python installation required.

### UI

- **WPF**
- `System.Windows.Forms.NotifyIcon` for tray behavior
- WPF window for the optional mini-widget and settings

### HID

Preferred dependency:

- **HidSharp 2.6.4**
- NuGet: https://www.nuget.org/packages/HidSharp

HidSharp must be wrapped behind our own interface so the HID implementation can later be replaced without rewriting the UI.

### Tests

- xUnit
- pure parser/state tests must not require the controller

---

## 4. Verified Cyclone 2 protocol facts

Primary research references:

1. cyclone2-linux:
   https://github.com/vdemonchy/cyclone2-linux

2. Battery protocol:
   https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md

3. Independent Cyclone 2 reverse-engineering notes:
   https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md

4. Earlier public notes:
   https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9

### XInput identity

Primary XInput USB identity:

- Vendor ID: `0x3537`
- Product ID: `0x100B`

The controller exposes XInput plus a vendor HID interface.

### Relevant reports

- Output report ID: `0x0F`
- Controller status input report ID: `0x12`
- Command response input report ID: `0x10`

Only `0x12` is valid for the battery parser.

### Keep-alive / report activation

Official GameSir Connect behavior has been observed sending a heartbeat approximately once per second:

- report ID `0x0F`
- opcode `0xF2`

Conceptually:

`0F F2 00 00 ...`

A second independently verified wake method is a zero-padded `0x0F` output report using opcode `0x03`.

The implementation should:

1. try the heartbeat method first;
2. wait for valid `0x12` input;
3. if no valid report arrives, try the verified wake fallback;
4. never send profile/configuration/RGB writes.

### Battery fields

For a valid `0x12` report:

- `byte[35]` = cable/charging-state flag
  - `0x00` = on battery
  - `0x01` = cable/power connected
- `byte[36]` = battery percentage, expected raw range `0..100`
- `byte[37]` must **not** be used as the charging flag

Important:
The parser must validate how the library exposes the report ID byte and preserve raw report indexing exactly. Unit tests must use known captured `0x12` frames so an off-by-one indexing error cannot silently ship.

---

## 5. HID discovery strategy

Do **not** assume that the first HID path matching VID/PID is the correct interface.

The implementation must:

1. enumerate all HID devices matching `VID 0x3537 / PID 0x100B`;
2. collect their:
   - device path;
   - max input report length;
   - max output report length;
   - product/manufacturer text when available;
3. attempt to open candidate interfaces one at a time;
4. dynamically allocate the output report using the reported HID output report length;
5. send only the safe status activation command;
6. listen briefly for an input report whose first report ID is `0x12`;
7. validate:
   - report length is sufficient;
   - battery byte is in `0..100`;
   - cable flag is plausible;
8. select and cache the working device path for the current connection session;
9. rediscover after disconnect/reconnect.

If an interface is inaccessible or busy, continue testing other matching interfaces.

If GameSir Connect has locked the interface, the app should show a nonfatal status such as:

`Controller interface is busy. Close GameSir Connect and retry.`

---

## 6. Runtime architecture

Suggested solution structure:

```text
CycloneBattery.sln
src/
  CycloneBattery.App/
    App.xaml
    App.xaml.cs
    UI/
    Tray/
    Settings/
    Notifications/
  CycloneBattery.Core/
    Hid/
    Protocol/
    State/
    Services/
    Models/
tests/
  CycloneBattery.Tests/
docs/
  HARDWARE_TEST.md
  PROTOCOL_NOTES.md
scripts/
  publish-win-x64.ps1
README.md
LICENSE
```

### Core interfaces

Recommended abstractions:

- `IHidDeviceDiscovery`
- `ICycloneHidTransport`
- `ICycloneBatteryReader`
- `IControllerStateService`
- `IAppSettingsService`
- `IAutostartService`
- `INotificationService`

UI code must not parse raw HID packets directly.

---

## 7. State model

Use an explicit state model rather than a pile of booleans.

Minimum states:

### `Disconnected`

Dongle/controller status does not provide a usable active XInput controller.

### `Connecting`

Matching interface found; app is trying to validate it.

### `Connected`

Valid `0x12` report received.

Fields:

- `BatteryPercent`
- `CableConnected`
- `LastUpdated`
- `DevicePathId` — sanitized/internal only

### `UnsupportedMode`

Cyclone 2 appears present but not in supported XInput mode.

Do not invent a battery value.

### `Busy`

Device exists but cannot be opened, most likely another program owns it.

### `Error`

Unexpected nonfatal transport/protocol problem.

The service must recover automatically without restarting the app.

---

## 8. Background service behavior

### Connection detection

- scan immediately at startup;
- rescan roughly every 2 seconds while disconnected;
- reconnect automatically after dongle/controller reconnect;
- UI must never freeze during HID I/O.

### Heartbeat

While connected to the correct XInput HID interface:

- send the safe `0x0F / 0xF2` heartbeat approximately every 1 second if required to keep reports active;
- do not redraw the UI every second unless values changed;
- if heartbeat stops producing reports, attempt one safe reinitialization and then rediscover the interface.

### Reads

- asynchronous/background reading;
- accept only report ID `0x12`;
- reject too-short reports;
- reject battery values outside `0..100`;
- do not convert invalid values into fake percentages.

### Resource usage target

When idle/connected:

- effectively 0% CPU in normal Task Manager observation;
- low memory footprint;
- no busy loop;
- no high-frequency logging.

---

## 9. Tray UI

The tray icon is the primary interface.

### Tray tooltip

When connected:

`Cyclone 2 — 73%`

When charging/powered:

`Cyclone 2 — 73% — Charging`

When disconnected:

`Cyclone 2 — Disconnected`

### Tray icon

Use an original generic battery/gamepad-style icon. Do not copy GameSir artwork.

Dynamic states:

- connected / healthy;
- medium battery;
- low battery;
- charging;
- disconnected.

Exact visual colors may follow Windows theme but must remain readable at 16–32 px.

### Tray context menu

Minimum items:

```text
Cyclone 2
73% · On battery

Show / Hide widget
Refresh
-----------------
Start with Windows   [✓]
Low battery alert    >
Settings
Diagnostics
-----------------
Exit
```

The first status rows may be disabled informational menu items.

---

## 10. Mini-widget

The widget is optional.

### Default appearance

Approximate size:

- width: 180–220 px
- height: 60–80 px

Display:

- generic gamepad icon;
- `Cyclone 2`;
- battery progress bar;
- numeric percentage;
- charging/power indicator when applicable.

Example:

```text
┌────────────────────────┐
│ 🎮 Cyclone 2      ⚡    │
│ ███████████░░  73%     │
└────────────────────────┘
```

### Behavior

- borderless;
- modern neutral Windows 11 style;
- small corner radius;
- draggable;
- remembers last position;
- optional always-on-top;
- `ShowInTaskbar = false`;
- hide/show from tray;
- persists user preference;
- no focus stealing on background battery updates.

When disconnected:

- default behavior: widget may automatically hide;
- setting can allow showing a `Disconnected` state.

---

## 11. Settings

Keep settings intentionally small.

Minimum:

### General

- Start with Windows: on/off
- Show widget on startup: on/off
- Always on top: on/off
- Hide widget when controller disconnects: on/off

### Battery alert

- enable low-battery alert
- threshold: `10 / 15 / 20 / 25 / 30%`
- default: `20%`

### Diagnostics

- Open log folder
- Copy diagnostic summary
- Force reconnect
- Test notification

Do not expose protocol timings to normal users unless an Advanced section is later added.

---

## 12. Low-battery notifications

Notify when battery first crosses from above threshold to at-or-below threshold.

Avoid spam.

Rules:

- one alert per discharge event;
- re-arm only after battery rises by a hysteresis margin or controller begins charging;
- default threshold 20%;
- ignore unknown battery state.

Suggested hysteresis:

- threshold = 20
- re-arm above 25

Use a Windows notification mechanism that works reliably for an unpackaged WPF application.

A legacy tray balloon is acceptable for MVP if modern toast registration would add disproportionate complexity.

---

## 13. Autostart

Use current-user autostart only.

Recommended MVP mechanism:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

Value name:

`CycloneBattery`

Value:

properly quoted path to the executable plus an optional silent-start argument.

No admin rights.

Autostart must be removable cleanly when the user disables the setting.

Reference:
https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

---

## 14. Local data

Store application data under:

`%LOCALAPPDATA%\CycloneBattery\`

Suggested:

```text
settings.json
logs/
  app-YYYYMMDD.log
```

### Logging

Default log level: Info.

Log:

- app start/version;
- controller discovery;
- sanitized HID candidate information;
- selected interface;
- connect/disconnect;
- battery changes;
- cable-state changes;
- protocol validation failures;
- exceptions.

Do not continuously dump all controller input reports during normal use.

A temporary verbose diagnostic mode may dump short, bounded samples.

### Privacy

No telemetry.
No upload.
No analytics.
No account.
No machine fingerprinting.

Sanitize or hash raw HID device paths in normal logs.

---

## 15. Diagnostics mode

This is required because Arena cannot physically access the user's controller.

Provide a Diagnostics window or command-line mode.

Recommended command:

```powershell
CycloneBattery.exe --diagnostics
```

Diagnostics must report:

- application version;
- OS / architecture;
- matching HID devices;
- VID/PID;
- input/output report lengths;
- whether each candidate could be opened;
- whether safe heartbeat/wake succeeded;
- whether a `0x12` report arrived;
- parsed battery value if valid;
- parsed cable flag;
- sanitized errors.

Useful optional command:

```powershell
CycloneBattery.exe --diagnostics --save
```

Result:

`%LOCALAPPDATA%\CycloneBattery\logs\diagnostic-<timestamp>.txt`

The diagnostic output must be easy to paste back into Arena.

---

## 16. Safety / protocol restrictions

The application must never:

- flash firmware;
- enter firmware loader mode;
- write profile registers;
- modify controller configuration;
- alter calibration;
- modify dead zones;
- modify mappings;
- modify RGB;
- trigger rumble;
- send undocumented experimental writes merely to "see what happens".

Allowed writes are limited to the minimum safe report activation/heartbeat required to read status.

This is a hard acceptance requirement.

---

## 17. GameSir Connect coexistence

GameSir Connect is not a dependency.

Expected normal workflow:

1. configure controller in GameSir Connect if desired;
2. close GameSir Connect;
3. Cyclone Battery runs independently.

If both are open and the HID interface cannot be shared:

- do not crash;
- transition to `Busy`;
- explain that GameSir Connect should be closed;
- retry periodically.

---

## 18. Supported modes

### MVP

Officially support:

- GameSir Cyclone 2
- XInput/Xbox mode
- 2.4 GHz dongle
- Windows 11 x64

### Non-MVP

Do not fake support for DS4 mode.

Existing research indicates the Cyclone 2 dongle's DS4 emulation does not expose a trustworthy live battery value through the candidate fields that were tested.

Switch mode support can be considered later if there is a reliable Windows-side source.

---

## 19. Error handling

All HID errors must be recoverable.

Examples:

### Dongle unplugged

State -> `Disconnected`.

### Controller powers off while dongle stays connected

State -> `Disconnected` or inactive dongle state after validation timeout.

### Controller powers back on

Rediscover and reconnect automatically.

### HID read timeout

Retry safely.

### Device path changes

Rediscover.

### GameSir Connect grabs interface

State -> `Busy`; retry.

### Invalid battery > 100

Reject frame; do not clamp silently.

### App already running

Only one instance should remain active.

Second instance should focus/show the first app's widget or exit cleanly.

---

## 20. Single-instance behavior

Implement a named mutex.

Example name:

`Local\CycloneBattery.SingleInstance`

When a second process starts:

- avoid creating a second tray icon/service;
- ideally signal the existing instance to show the widget;
- MVP fallback: show a short message and exit.

---

## 21. Packaging

Primary deliverable:

`CycloneBattery.exe`

Prefer self-contained Windows x64 deployment.

Target:

- .NET 10 LTS;
- no separate .NET runtime installation required;
- no installer required for MVP.

Recommended release publish command:

```powershell
dotnet publish .\src\CycloneBattery.App\CycloneBattery.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

Reference:
https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

If single-file packaging causes an HID/native-loading issue, ship `onedir` temporarily and record the reason. Do not hide packaging failures.

---

## 22. Build scripts

Repository must contain:

```text
scripts/
  build.ps1
  test.ps1
  publish-win-x64.ps1
```

Each script must:

- fail on error;
- print a clear final result;
- work from repository root.

---

## 23. README requirements

README must contain:

1. what the program does;
2. supported hardware/mode;
3. screenshot placeholder;
4. how to build;
5. how to run;
6. how to create the release EXE;
7. troubleshooting;
8. how to run diagnostics;
9. protocol references;
10. disclaimer that the project is unofficial and not affiliated with GameSir.

Do not use official GameSir logos.

The GameSir/Cyclone 2 names may be used descriptively to identify compatible hardware.

---

## 24. Unit tests

Minimum tests:

### Protocol parser

- ignores non-`0x12` report;
- rejects too-short report;
- reads 0%;
- reads 50%;
- reads 100%;
- reads cable flag false;
- reads cable flag true;
- ensures charging is not read from byte 37;
- verifies captured raw frame indexing.

### State logic

- disconnected -> connecting -> connected;
- connected -> disconnected;
- busy -> retry;
- invalid packet does not erase last good reading immediately;
- stale reading eventually becomes unknown/disconnected according to timeout.

### Low battery

- no alert above threshold;
- one alert crossing threshold;
- no repeated spam;
- charging/recovery rearms correctly.

### Settings

- defaults;
- serialization round-trip;
- corrupt settings file falls back safely.

### Autostart

- correct current-user registry key;
- correct quoting for paths containing spaces.

---

## 25. Hardware acceptance test

A build is not accepted only because tests pass in Arena.

Required real-hardware test on Windows 11 with the user's Cyclone 2:

1. GameSir Connect closed.
2. Controller in XInput mode.
3. Connect through 2.4 GHz dongle.
4. App detects controller.
5. Battery is shown as a plausible `0..100%`.
6. Compare against GameSir Connect at a non-100% value if possible.
7. Unplug charging cable -> status changes to on-battery.
8. Plug cable -> powered/charging state changes.
9. Turn controller off -> app becomes disconnected without crashing.
10. Turn controller on -> app reconnects automatically.
11. Unplug dongle -> disconnected.
12. Reinsert dongle -> reconnects.
13. Restart app -> settings and widget position persist.
14. Enable autostart -> reboot/logon -> app starts in tray.
15. GameSir Connect conflict -> graceful `Busy` state, not crash.

---

## 26. Performance acceptance

Normal background use:

- no busy polling;
- no continuous console;
- no perceptible gaming input lag;
- no controller disconnects caused by the app;
- no profile/config changes;
- no high CPU spikes every second;
- UI remains responsive.

The app must prioritize non-interference with gameplay over instant cosmetic updates.

---

## 27. UX acceptance

The user should be able to:

1. launch `CycloneBattery.exe`;
2. see one tray icon;
3. hover/click and know controller battery;
4. optionally show the mini-widget;
5. never open GameSir Connect just to check battery;
6. close the app from the tray.

No setup wizard is required for MVP.

---

## 28. Definition of Done

MVP is DONE only when all are true:

- [ ] repository builds on Windows 11;
- [ ] unit tests pass;
- [ ] real Cyclone 2 via 2.4 GHz dongle is detected;
- [ ] valid battery percentage is shown;
- [ ] cable/charging state is shown;
- [ ] reconnect works;
- [ ] tray works;
- [ ] widget works;
- [ ] low-battery notification works;
- [ ] autostart works;
- [ ] diagnostics are usable;
- [ ] self-contained release build is produced;
- [ ] no GameSir Connect process is needed;
- [ ] no controller settings are modified;
- [ ] README is complete.

---

## 29. Explicitly out of scope for MVP

Do not expand scope unless requested:

- RGB control;
- firmware updates;
- controller calibration;
- remapping;
- dead-zone settings;
- macro management;
- multiple controller models;
- cloud sync;
- online accounts;
- automatic update service;
- Microsoft Store release;
- battery history charts;
- mobile app.

---

## 30. Possible later versions

Only after the core reader is proven on real hardware:

### v1.1

- better modern Windows toast notifications;
- optional battery history;
- optional multiple controllers;
- signed installer / MSIX;
- theme options;
- update checker.

### v1.2+

- additional GameSir models, only after real hardware/protocol verification;
- optional extra controller status fields.

---

## 31. Primary external references

### Arena

- Agent Mode:
  https://arena.ai/agent
- Coding with GitHub in Agent Mode:
  https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode
- Agent Mode guide:
  https://help.arena.ai/articles/5432423882-how-to-use-agent-mode
- File uploads:
  https://help.arena.ai/articles/5595418316-arena-how-to-file-upload

### GameSir Cyclone 2 protocol research

- https://github.com/vdemonchy/cyclone2-linux
- https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md
- https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md
- https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9

### Windows / .NET

- .NET 10:
  https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- .NET support policy:
  https://dotnet.microsoft.com/en-us/platform/support/policy
- Single-file deployment:
  https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Windows Run / RunOnce:
  https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

### HID library

- HidSharp:
  https://www.nuget.org/packages/HidSharp
