# ARENA BUILD PROMPT — CYCLONE BATTERY

## Role

Act as a senior Windows desktop engineer specializing in:

- C# / modern .NET;
- WPF;
- USB HID;
- Windows tray utilities;
- robust hardware discovery/reconnect logic;
- testable architecture;
- release packaging.

You are responsible for taking this repository from its current state to a complete working MVP, not merely producing snippets or an implementation plan.

---

## Project

Build **Cyclone Battery**, a lightweight Windows 11 tray utility for **GameSir Cyclone 2**.

The app must read the controller's real battery percentage directly through the controller's vendor HID interface when connected through the **2.4 GHz dongle in XInput/Xbox mode**.

The user specifically does **not** want to launch GameSir Connect merely to check battery level.

Read the attached `TECH_SPEC.md` completely before changing code.

`TECH_SPEC.md` is the product source of truth.

If this prompt and the spec differ, follow the safer/more explicit requirement and record the discrepancy in your final report.

---

## Hard constraints

1. Target Windows 11 x64.
2. Use **C# + .NET 10 LTS + WPF**.
3. Use a Windows tray icon as the primary UI.
4. Add an optional compact draggable always-on-top battery widget.
5. Use **HidSharp** behind an internal abstraction unless a clearly superior no-risk implementation already exists in the repo.
6. No paid API.
7. No cloud.
8. No telemetry.
9. No GameSir Connect dependency.
10. No administrator rights during normal operation.
11. Do not modify controller profiles, mappings, calibration, dead zones, RGB, firmware, or other controller settings.
12. Do not send experimental undocumented writes.
13. Only use the minimum safe status heartbeat/wake writes required to read controller status.
14. Do not use GameSir logos or proprietary visual assets.
15. Do not fake battery data when the protocol is unavailable.
16. Do not claim real-hardware success unless evidence from a real Windows/Cyclone 2 run is supplied.

---

## Required protocol implementation

### Primary XInput identity

- VID `0x3537`
- PID `0x100B`

Do not assume the first matching HID interface is the correct one.

Enumerate candidate HID interfaces and actively validate the interface by safely requesting status and receiving a valid report.

### Reports

- `0x0F`: output command report
- `0x12`: controller status input report
- `0x10`: command/event response — NOT the battery report

### Status activation

Research indicates GameSir Connect sends a heartbeat:

- report ID `0x0F`
- opcode `0xF2`
- approximately once per second while extended reports are needed

A separate verified implementation also shows a zero-padded `0x0F` / opcode `0x03` wake produces `0x12` status streaming.

Implement this conservatively:

1. heartbeat first;
2. wait for a valid `0x12`;
3. safe wake fallback only if necessary;
4. never send configuration/RGB/profile writes.

Use HID-reported report lengths rather than assuming a fixed host-library buffer size.

### Battery parser

For valid `0x12` input:

- `byte[35]`: cable/power flag
  - 0 = on battery
  - 1 = cable/power connected
- `byte[36]`: battery percentage, expected `0..100`
- do not use `byte[37]` as charging state

Important: verify how HidSharp includes the report ID in the byte array and lock the raw indexing down with parser unit tests based on known captured frames.

Reject:

- non-0x12 report IDs;
- too-short packets;
- battery >100;
- impossible cable-state values if observed.

Do not silently clamp malformed data.

---

## Required repository structure

Prefer:

```text
CycloneBattery.sln
src/
  CycloneBattery.App/
  CycloneBattery.Core/
tests/
  CycloneBattery.Tests/
docs/
  HARDWARE_TEST.md
  PROTOCOL_NOTES.md
scripts/
  build.ps1
  test.ps1
  publish-win-x64.ps1
README.md
TECH_SPEC.md
```

Keep hardware/protocol code out of WPF view code.

Use interfaces so transport, parser, state service, settings, notifications, and autostart can be tested independently.

---

## Required features

### Tray

Show:

- connected/disconnected;
- battery percentage;
- cable/charging state;
- show/hide widget;
- refresh;
- start with Windows toggle;
- settings;
- diagnostics;
- exit.

Tooltip should contain current battery where known.

### Widget

Compact Windows 11-style widget:

- generic controller glyph;
- `Cyclone 2`;
- progress bar;
- numeric battery %;
- charging/power indicator;
- draggable;
- optional always-on-top;
- remembers position;
- no taskbar button;
- no focus stealing during background updates.

### Notifications

Configurable low-battery alert.

Default:

- enabled;
- 20% threshold;
- hysteresis to prevent repeated notifications.

### Autostart

Current user only.

Use:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

Correctly quote paths.

No admin.

### Settings

Persist under:

`%LOCALAPPDATA%\CycloneBattery\`

### Diagnostics

Provide:

```powershell
CycloneBattery.exe --diagnostics
```

and ideally:

```powershell
CycloneBattery.exe --diagnostics --save
```

Output must include sanitized candidate HID information and enough detail to diagnose interface/report problems from a pasted log.

This is critical because your Arena sandbox cannot physically access the user's Cyclone 2.

### Single instance

Prevent duplicate tray instances using a named mutex.

---

## Resilience requirements

The application must recover automatically from:

- controller power off/on;
- dongle unplug/replug;
- device-path changes;
- short HID read failures;
- GameSir Connect temporarily owning the interface;
- malformed packets.

The UI thread must never block on HID I/O.

Use cancellation tokens and clean disposal.

No busy polling.

---

## Tests

Create meaningful automated tests.

At minimum test:

### Parser

- non-0x12 ignored;
- packet too short;
- 0%;
- 50%;
- 100%;
- >100 rejected;
- cable false;
- cable true;
- byte 37 is not treated as charging;
- captured-frame indexing.

### State

- disconnected -> connecting -> connected;
- reconnect;
- busy -> retry;
- stale data behavior.

### Notifications

- threshold crossing;
- no spam;
- hysteresis/re-arm.

### Settings

- defaults;
- round trip;
- corrupt config fallback.

### Autostart

- key/path generation and quoting.

Tests must not require physical hardware.

---

## Hardware-validation design

Because the real device is not available to the Arena sandbox:

1. implement a robust diagnostics path;
2. clearly separate:
   - automated/sandbox validation;
   - real-hardware validation;
3. create `docs/HARDWARE_TEST.md`;
4. do not mark hardware acceptance complete before the user supplies a successful real-world log/test.

If HID behavior is uncertain, instrument it safely rather than guessing.

---

## External references you should inspect

Use current upstream information as needed.

### Protocol

- https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md
- https://github.com/vdemonchy/cyclone2-linux
- https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md
- https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9

### HID

- https://www.nuget.org/packages/HidSharp

### .NET

- https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

Use reverse-engineered information only for interoperability with hardware the user owns.
Do not copy proprietary GameSir source code or assets.

---

## Build / release requirements

Provide scripts:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\publish-win-x64.ps1
```

Release target:

- Windows x64;
- Release;
- self-contained;
- single-file if compatible;
- no .NET install required for the end user.

Recommended publish properties:

- `SelfContained=true`
- `PublishSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- `PublishTrimmed=false`

The final executable should be named:

`CycloneBattery.exe`

If single-file causes a reproducible runtime problem, first prove it and then fall back to a clean self-contained folder release rather than hiding the problem.

---

## Workflow

Work autonomously.

### Phase 1 — inspect and plan

- inspect repository;
- read `TECH_SPEC.md`;
- inspect relevant protocol references;
- identify implementation risks;
- create a concise execution plan;
- then begin implementation without waiting for approval unless a hard blocker exists.

### Phase 2 — implement core first

Prioritize:

1. protocol parser;
2. HID enumeration/transport;
3. controller state service;
4. diagnostics;
5. tests.

Do not spend most of the session polishing UI before the reader architecture exists.

### Phase 3 — UI

Implement tray, widget, settings, alerts, autostart.

### Phase 4 — validate

Run all checks available in your environment.

If WPF cannot execute in the Arena host OS, still run every platform-independent test/build check possible and explicitly state the remaining Windows-only validation.

### Phase 5 — delivery

Commit all work to the session branch.

Use a focused PR.

Do not merge it automatically unless Arena's workflow absolutely requires it.

---

## Final response format

At completion give a concise engineering report containing:

1. **Implemented**
2. **Automated checks run + results**
3. **What could not be validated in Arena**
4. **Exact local Windows commands for the user**
5. **Expected output path**
6. **Real-hardware test steps**
7. **Known risks**
8. **Files changed**
9. **Commit / branch / PR status**

Do not finish with generic statements like "it should work."

Clearly distinguish verified facts from assumptions.

---

## Definition of Done for your Arena session

Your Arena work is complete when:

- solution/repository is coherent;
- core reader architecture exists;
- tray/widget/settings/alerts/autostart are implemented;
- diagnostics are implemented;
- tests are implemented and run where possible;
- scripts are implemented;
- README/docs are implemented;
- code is committed to the working branch / PR;
- the only remaining acceptance item is real Windows + real Cyclone 2 validation, if physical hardware was unavailable.

Start now.
