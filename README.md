# Cyclone Battery

A small Windows 11 tray utility that shows the **real battery level of a GameSir Cyclone 2**
controller — without opening GameSir Connect.

It reads the battery straight from the controller's vendor HID interface over the 2.4 GHz dongle,
shows it in the notification area (and optionally in a compact always-on-top widget), and warns you
when the charge gets low.

It is strictly **read-only**: it never touches controller profiles, mappings, calibration, dead
zones, RGB or firmware, and it needs no account, no cloud and no administrator rights.

> Screenshot placeholder: tray tooltip `Cyclone 2 — 73% — Charging` plus the mini widget.
> Replace this block with a screenshot before publishing.

---

## Features

- Tray icon with live percentage and colour-coded state (healthy / medium / low / charging /
  disconnected).
- Optional compact widget: draggable, remembers its position, optional always-on-top, no taskbar
  button, never steals focus during a game.
- Cable / charging state from the controller itself.
- Low-battery notification with a configurable threshold (10/15/20/25/30%, default **20%**) and
  hysteresis, so it alerts once per discharge instead of spamming.
- Automatic reconnect after the controller powers off/on, the dongle is replugged, or the device
  path changes.
- `Start with Windows` via the current-user `Run` key (no admin rights).
- Built-in `--diagnostics` mode for troubleshooting.
- Single-instance protection.

---

## Supported hardware

| Supported | Not supported (MVP) |
|---|---|
| GameSir Cyclone 2 | DS4 mode — no reliable battery source |
| XInput / Xbox mode | Switch mode |
| 2.4 GHz USB dongle | Bluetooth / wired-only setups |
| Windows 11 x64 | other controller models |

If the controller is in another mode the app says so instead of inventing a number.

---

## Requirements

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (to build; the published
  exe is self-contained and needs no runtime installed)

---

## Build

```powershell
git clone <your-fork-url> cyclone-battery
cd cyclone-battery

dotnet restore
dotnet build -c Release
```

or with the provided scripts (they run from any directory and fail loudly):

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

---

## Run

```powershell
dotnet run --project .\src\CycloneBattery.App\CycloneBattery.App.csproj
```

The app starts in the notification area. Right-click the tray icon for the menu, or double-click it
to toggle the widget.

---

## Create the release EXE

```powershell
.\scripts\publish-win-x64.ps1
```

Equivalent manual command:

```powershell
dotnet publish .\src\CycloneBattery.App\CycloneBattery.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

Output:

```text
src\CycloneBattery.App\bin\Release\net10.0-windows\win-x64\publish\CycloneBattery.exe
```

If single-file packaging ever misbehaves on your machine, use `.\scripts\publish-win-x64.ps1
-FolderMode` for a self-contained folder release.

---

## Diagnostics

```powershell
CycloneBattery.exe --diagnostics
CycloneBattery.exe --diagnostics --save
```

`--save` writes `%LOCALAPPDATA%\CycloneBattery\logs\diagnostic-<timestamp>.txt`.

The report includes the OS/architecture, every matching HID interface (with raw device paths
hashed), report lengths, whether the heartbeat or the wake fallback produced a `0x12` status
report, the parsed battery and cable flag, a short hex prefix of the first frame, and any running
process known to compete for the interface.

The full hardware acceptance checklist is in [`docs/HARDWARE_TEST.md`](docs/HARDWARE_TEST.md).
Protocol details and their sources are in [`docs/PROTOCOL_NOTES.md`](docs/PROTOCOL_NOTES.md).

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cyclone 2 — Disconnected` | Controller off, or not in **XInput/Xbox** mode. Check the dongle is plugged in. |
| `Cyclone 2 — Interface busy` | GameSir Connect (or another controller tool) owns the HID interface. Close it completely, including its tray icon. |
| `Cyclone 2 — Connecting` forever | The interface opened but the controller is not streaming status. Keep the controller awake and press a button; then run `--diagnostics` and check the `Interface validation` section. |
| Battery shows but the value looks wrong | Run `--diagnostics` and compare `Battery percent` with GameSir Connect at a non-100% charge. Report the log rather than trusting a guessed offset. |
| No tray icon at all | A second instance may already be running (single-instance mutex `Local\CycloneBattery.SingleInstance`). Check Task Manager for `CycloneBattery.exe`. |
| No notification appeared | Use `Settings → Test notification`. Windows may have notifications disabled for the app. |
| Autostart checkbox keeps resetting | The Run value could not be written; the app reverts the setting rather than lying. See the log in `%LOCALAPPDATA%\CycloneBattery\logs\`. |
| Build fails on `net10.0-windows` | Install the **.NET 10 SDK** (not just the runtime). Verify with `dotnet --info`. |

Logs: `%LOCALAPPDATA%\CycloneBattery\logs\app-YYYYMMDD.log` (device paths are hashed, no telemetry,
nothing leaves the machine).

---

## Project layout

```text
CycloneBattery.sln
src/
  CycloneBattery.App/     WPF shell: tray, widget, settings, diagnostics, autostart, notifications
  CycloneBattery.Core/    protocol parser, HID transport/probing, state machine, settings, logging
tests/
  CycloneBattery.Tests/   hardware-free xUnit tests
docs/
  HARDWARE_TEST.md        real-controller acceptance checklist
  PROTOCOL_NOTES.md       protocol facts and their sources
scripts/
  build.ps1  test.ps1  publish-win-x64.ps1
TECH_SPEC.md              product specification (source of truth)
```

`CycloneBattery.Core` targets plain `net10.0` with no Windows-only dependency, so the protocol and
state logic is testable anywhere. HidSharp sits behind `IHidDeviceDiscovery` /
`ICycloneHidTransport` and can be swapped without touching the UI.

---

## Protocol references

- <https://github.com/vdemonchy/cyclone2-linux>
- <https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md>
- <https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md>
- <https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9>
- HidSharp: <https://www.nuget.org/packages/HidSharp>

Used only for interoperability with hardware you own.

---

## Status

Automated checks cover the parser, interface probing, state machine, alert hysteresis, settings,
autostart entry generation and diagnostics formatting.

**Real-hardware validation is a separate, required step** — see
[`docs/HARDWARE_TEST.md`](docs/HARDWARE_TEST.md). Until those boxes are ticked on a physical
Cyclone 2, the battery reading is not yet accepted.

---

## Disclaimer

This project is **unofficial** and is **not affiliated with, endorsed by, or sponsored by GameSir**.
"GameSir" and "Cyclone 2" are used descriptively to identify compatible hardware. No GameSir logo,
artwork or proprietary asset is used; the tray and widget icons are generated at runtime.

Licensed under the [MIT License](LICENSE).
