# Cyclone 2 protocol notes

Everything Cyclone Battery sends or reads, with the evidence behind it.

**This application is read-only.** The only bytes it ever writes are the two status activation
reports described in [Status activation](#status-activation). No profile, register-with-data,
calibration, dead-zone, mapping, RGB, rumble or firmware command exists anywhere in the code.

---

## 1. Device identity

| Mode | USB ID | Battery source | Supported |
|---|---|---|---|
| XInput / Xbox | `3537:100B` | vendor HID input report `0x12`, `byte[36]` | **yes (MVP)** |
| HID (controller off / dongle idle) | `3537:0575` | none | no |
| DS4 emulation | `054C:09CC` | none reliable | no |
| Switch Pro emulation | `057E:2009` | kernel coarse level (Linux only) | no |

The MVP targets the first row only: **2.4 GHz dongle, XInput mode, Windows 11 x64.**

DS4 mode is reported as unsupported rather than guessed. Upstream research found the standard DS4
battery byte dead, the kernel `power_supply` capacity pinned at ~5%, and the DS4 feature report
`0x12` byte 10 frozen at 100 while the real charge fell 96% → 90%.

Sources:

- <https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md>
- <https://github.com/vdemonchy/cyclone2-linux>
- <https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md>
- <https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9>

---

## 2. Reports

| Report ID | Direction | Meaning |
|---|---|---|
| `0x0F` | OUTPUT | command report (the only one this app writes) |
| `0x12` | INPUT | controller status — **the battery source** |
| `0x10` | INPUT | command/event reply — **not** battery (its battery position is `0x00`) |

The vendor HID interface is silent while idle: passive reads return nothing. `GET_FEATURE` on
`0x10`/`0x12` fails with `EPIPE` because they are input reports, not feature reports. Something
must be written on `0x0F` before status frames appear.

---

## 3. Status activation

Two safe methods, tried in this order:

1. **Heartbeat** — `0F F2 00 00 …`
   This is what GameSir Connect itself sends about once per second to keep the extended `0x12`
   stream enabled.
2. **Wake fallback** — `0F 03 00 00 …`
   A zero-padded command with no payload. Independently confirmed to start the `0x12` stream
   (opcodes `0x01`/`0x03` work, `0x00` gets no reply).

Implementation (`CycloneInterfaceProber`):

1. open a candidate interface;
2. send the heartbeat, wait for a valid `0x12`;
3. only if nothing arrived, send the wake fallback and wait again;
4. first interface that yields a decodable reading wins and stays open;
5. while connected, the heartbeat is repeated about once per second.

Report buffers are sized from the HID-reported lengths
(`HidDevice.GetMaxOutputReportLength()`), never from a hard-coded guess. If the reported length is
unusable (< 2 bytes) the observed default of 64 bytes is used instead.

Note on opcode `0x03`: in the vendor command grammar `0x03` is "write register", so
`0F 03 00 00 00 00 …` is a write with profile `0`, address `0x0000` and **length 0** — no data
bytes at all. That is why it is safe, and why it is only used as a fallback.

---

## 4. Battery field layout

For a `0x12` frame, using **absolute indices that include the report id byte at index 0**:

| Index | Meaning |
|---|---|
| `0` | report id (`0x12`) |
| `35` | cable / power flag: `0x00` on battery, `0x01` cable or external power |
| `36` | battery percent, raw `0..100` (`0x64` = 100) |
| `37` | **not** the charging flag — it stays `0x00` across plug/unplug cycles |

Validation rules in `BatteryFrameParser`:

- frame must be non-empty;
- `frame[0]` must be `0x12` (`0x10` event replies are rejected);
- length must be at least `37` so index 36 exists;
- `frame[35]` must be `0x00` or `0x01`;
- `frame[36]` must be `0..100`.

A rejected frame produces a reason, never a clamped or invented value. The last good reading is
kept until it goes stale (12 s by default).

### Why the indices are absolute (report id included)

HidSharp documents this explicitly (`HidDevice.cs`, branch `vine`):

> `GetMaxInputReportLength()` — "Returns the maximum input report length, **including the Report ID
> byte**. If the device does not use Report IDs, use 0 for the first byte."

and `HidStream.Write(byte[])`:

> "The buffer containing the report. **Place the Report ID in the first byte.**"

The upstream captured frames use the same convention. Both published captures start with `12` and
carry `0x64` at index 36; they are reproduced verbatim in
`tests/CycloneBattery.Tests/Testing/CapturedFrames.cs` and asserted by the parser tests, so an
off-by-one cannot ship silently.

> **Open item.** In both published captures `byte[35]` is `0x00`, and those captures predate the
> 2026-06-05 confirmation of the cable flag. The cable behaviour is therefore covered by synthetic
> frames in unit tests and must additionally be confirmed on real hardware by plugging and
> unplugging the cable (steps 7–8 of `HARDWARE_TEST.md`). Diagnostics prints the raw frame prefix,
> so a mismatch is immediately visible in a pasted log.

---

## 5. Interface selection

The Cyclone 2 exposes several HID interfaces with the same VID/PID, and only one of them streams
status. The first matching device path is therefore **never** trusted. `CycloneInterfaceProber`:

1. enumerates every `3537:100B` interface (path, input/output report lengths, product/manufacturer);
2. opens them one at a time;
3. validates each by actually producing a decodable `0x12`;
4. caches the winner for the session and rediscovers after a disconnect.

If an interface cannot be opened, the failure is classified:

- access denied / sharing violation → **Busy** ("Close GameSir Connect and retry");
- anything else → transport failure, retry with rediscovery.

---

## 6. Safety rules encoded in the design

- `StatusActivationCommand` is the only type that can build an output report, and it can only build
  the heartbeat or the payload-free wake. There is no API for anything else.
- Output frames are asserted to be all-zero after the opcode byte by unit tests.
- Raw Windows device paths are hashed before they reach the log or diagnostics
  (`HidDevicePathSanitizer`).
- No telemetry, no network access, no registry writes outside the optional HKCU `Run` value.
