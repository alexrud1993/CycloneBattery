# CYCLONE BATTERY — FULL IMPLEMENTATION PATH THROUGH ARENA

This guide is the practical route from **zero repository** to a working local Windows `.exe`.

The workflow is intentionally designed so that Arena does almost all software work.

Your manual tasks are reduced to:

1. create/connect a GitHub repository;
2. give Arena the spec and build prompt;
3. pull the code to Windows;
4. run a few commands;
5. test with the real controller;
6. paste the diagnostic result back into the same Arena session if a hardware fix is needed.

---

# 0. Important: which Arena mode to use

Use **Arena Agent Mode + GitHub**.

Direct link:

https://arena.ai/agent

Official guide:

https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode

General Agent Mode guide:

https://help.arena.ai/articles/5432423882-how-to-use-agent-mode

Do **not** make Fullstack Code Arena the main workflow for this project.

Why:

- Fullstack Code Arena is mainly for web/fullstack application generation and previews.
- This project is a native Windows desktop HID utility.
- The important workflow here is repository editing, Git commits, diffs, and PRs.
- Agent Mode supports a GitHub-connected working copy and coding workflow.

A second critical limitation:

Arena cannot physically access your GameSir Cyclone 2 connected to your own PC.

Therefore:

- Arena can implement code and tests;
- Arena can inspect upstream protocol research;
- your Windows PC performs the final real-hardware check;
- diagnostics are pasted back to Arena if refinement is needed.

---

# 1. One-time Windows preparation

## 1.1 Install Git

Download:

https://git-scm.com/download/win

Default installer options are fine.

Verify in PowerShell:

```powershell
git --version
```

---

## 1.2 Install .NET 10 SDK

Download:

https://dotnet.microsoft.com/en-us/download/dotnet/10.0

Install the **.NET 10 SDK x64**, not only the runtime.

Verify:

```powershell
dotnet --info
```

You should see a .NET 10 SDK.

Support policy:

https://dotnet.microsoft.com/en-us/platform/support/policy

.NET 10 is the preferred target for this project because it is the current LTS line.

---

## 1.3 GitHub account

GitHub:

https://github.com/

If you already have an account, use it.

---

## 1.4 Arena account

Arena:

https://arena.ai/

Log in.

Agent Mode:

https://arena.ai/agent

---

# 2. Create the GitHub repository

Open:

https://github.com/new

Recommended values:

```text
Repository name: cyclone-battery
Description: Windows battery tray utility for GameSir Cyclone 2
Visibility: Private
README: optional
.gitignore: VisualStudio
License: optional for a private personal project
```

Create the repository.

Do not add unrelated project files.

---

# 3. Connect GitHub to Arena

Open:

https://arena.ai/agent

Official current coding guide:

https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode

In Arena:

1. choose **Agent Mode**;
2. click **Connect GitHub**;
3. authorize Arena;
4. open **Manage repositories**;
5. allow access to `cyclone-battery`;
6. ensure GitHub is enabled for this Agent session;
7. select the repository for the new session.

Arena will work on a separate working copy/branch and can produce a PR.

Important Arena behavior:

- a GitHub-connected Agent session works through the repository rather than a downloadable session zip;
- the session is designed around one PR;
- review the diff before merging;
- do not merge the PR before you have finished asking Arena for the hardware-related fixes you want in that session.

---

# 4. Give Arena the project files

Use the two supplied files:

- `TECH_SPEC.md`
- `BUILD_PROMPT.md`

Arena Agent Mode supports Markdown uploads.

Upload guide:

https://help.arena.ai/articles/5595418316-arena-how-to-file-upload

Recommended method:

1. upload `TECH_SPEC.md`;
2. paste the complete content of `BUILD_PROMPT.md` into the Agent prompt;
3. alternatively also upload `BUILD_PROMPT.md` and tell Arena to execute it exactly.

Use only one authoritative spec to avoid contradictions.

---

# 5. First Arena message

Paste the contents of `BUILD_PROMPT.md`.

Add only this short line above it if needed:

```text
Implement this project end-to-end in the connected GitHub repository. TECH_SPEC.md is attached and is the product source of truth.
```

Then submit.

Do not break the job into dozens of tiny instructions.

The build prompt already instructs the agent to:

- inspect;
- plan;
- implement;
- test;
- document;
- commit;
- prepare the PR;
- leave real hardware verification for your Windows PC.

---

# 6. What Arena should produce before you touch anything locally

Expected repository shape:

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

Minimum code expected:

- HID discovery;
- safe Cyclone 2 heartbeat/wake;
- battery packet parser;
- controller-state background service;
- tray icon;
- battery widget;
- settings;
- low-battery alert;
- Windows autostart;
- diagnostics mode;
- reconnect logic;
- single-instance logic;
- automated tests.

Do not accept a result containing only UI mockups or placeholder HID code.

---

# 7. Review Arena's result

Before merging anything, inspect:

- **Diff**
- **Checks**
- Agent final report

Arena coding guide:

https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode

Verify that the agent did not:

- add firmware code;
- add RGB writes;
- modify controller profiles;
- add paid APIs;
- add telemetry;
- pretend hardware was tested;
- replace real HID work with mock data in Release mode.

If the agent reports Windows/WPF cannot be executed in its sandbox, that is acceptable.

The real device test happens next.

---

# 8. Get the Arena branch onto your Windows PC

There are two clean options.

## Option A — merge the PR, then clone/pull main

Use this only if the Arena diff looks structurally good and you do not expect more work in the same Agent session.

## Option B — recommended during hardware bring-up

Do **not** merge yet.

Clone the repository and check out Arena's working branch/PR branch locally.

GitHub shows the exact branch and command on the PR page.

Typical flow:

```powershell
cd C:\Projects
git clone https://github.com/YOUR_USER/cyclone-battery.git
cd cyclone-battery
git fetch --all
git branch -a
```

Then check out the Arena branch shown by GitHub:

```powershell
git checkout <arena-branch-name>
```

This keeps the same PR alive while you test.

---

# 9. Local Windows build

Open PowerShell in the repository root.

Run:

```powershell
dotnet --info
```

Then:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Or, if Arena created the requested scripts:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

Expected result:

- build succeeds;
- tests pass;
- no missing package/reference errors.

If build fails, copy the complete error block back into the same Arena Agent session and say:

```text
Local Windows build failed. Fix the repository, not my machine, unless the error clearly proves a missing prerequisite.

Here is the exact output:

<PASTE OUTPUT>
```

Let Arena commit the correction to the same working PR branch.

Pull the update locally:

```powershell
git pull
```

Then repeat build/test.

---

# 10. Prepare the GameSir Cyclone 2 for the first test

Before the test:

1. connect the **2.4 GHz GameSir dongle**;
2. put Cyclone 2 in **XInput/Xbox mode**;
3. confirm the controller works in Windows/game;
4. fully close **GameSir Connect**;
5. make sure GameSir Connect is not sitting in the system tray;
6. keep the controller powered on.

MVP protocol target:

```text
VID: 3537
PID: 100B
Mode: XInput
```

Reference:

https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md

---

# 11. Run diagnostics BEFORE relying on the widget

If Arena followed the spec, run:

```powershell
.\src\CycloneBattery.App\bin\Release\net10.0-windows\CycloneBattery.exe --diagnostics
```

The exact output path may vary.

If the app has not been built into that location, use:

```powershell
dotnet run --project .\src\CycloneBattery.App\CycloneBattery.App.csproj -- --diagnostics
```

Expected diagnostic categories:

```text
OS
App version
Matching HID devices
VID/PID
Input report length
Output report length
Candidate open result
Heartbeat result
0x12 report result
Battery %
Cable flag
Selected interface
```

---

# 12. First real-hardware decision

## Case A — battery appears correctly

Example:

```text
Cyclone 2 detected
Mode: XInput
Battery: 73%
Cable: disconnected
```

Excellent.

Continue to UI tests.

## Case B — controller found but no 0x12 status report

Save diagnostics:

```powershell
CycloneBattery.exe --diagnostics --save
```

Copy the diagnostic text back to Arena.

Prompt:

```text
Real Cyclone 2 hardware test on Windows found the device, but no valid 0x12 status report was received.

Analyze the attached diagnostic log against the protocol references in TECH_SPEC.md.

Fix interface selection / report length / heartbeat/wake handling as needed.

Do not add unsafe configuration writes.

Commit the fix to the existing PR branch.

<PASTE LOG>
```

## Case C — no matching 3537:100B device

Likely possibilities:

- controller is not in XInput mode;
- only idle dongle identity is visible;
- device re-enumerated into another mode;
- HID enumeration logic is wrong.

Paste diagnostic output back to Arena.

Do not ask the agent to invent a battery source for unsupported mode.

## Case D — Access denied / busy

Close:

- GameSir Connect;
- any other controller configuration tool.

Retry.

The app should eventually show `Busy`, not crash.

If not, send the error to Arena.

---

# 13. Cross-check the battery value

This is the strongest practical validation.

1. Run Cyclone Battery.
2. Note the percentage.
3. Close Cyclone Battery.
4. Open GameSir Connect.
5. Note the official displayed percentage.
6. Close GameSir Connect again.
7. Start Cyclone Battery.

Do this preferably when the controller is **not at 100%**.

Expected result:

- values should agree or be very close;
- if there is a systematic offset, do not guess a correction formula;
- capture diagnostics and let Arena inspect the raw report.

---

# 14. Charging-state test

With Cyclone Battery running:

## Test 1

Controller on battery.

Expected:

```text
On battery
```

## Test 2

Connect USB power/charging cable.

Expected:

```text
Charging
```

or:

```text
External power connected
```

depending on UI wording.

Protocol research indicates:

```text
byte[35] = 0 -> battery
byte[35] = 1 -> cable/power connected
```

Do not treat byte 37 as charging.

---

# 15. Reconnect tests

Run all:

### Controller power cycle

1. controller on;
2. app connected;
3. power controller off;
4. app changes to disconnected;
5. power controller on;
6. app reconnects automatically.

### Dongle

1. unplug dongle;
2. app changes to disconnected;
3. reinsert dongle;
4. app reconnects.

### App

1. exit from tray;
2. start again;
3. battery returns.

No manual Refresh should be required for normal reconnect.

---

# 16. UI test

Verify tray:

- one icon only;
- hover shows battery;
- context menu works;
- Exit actually terminates process.

Verify widget:

- show/hide;
- battery progress;
- numeric percent;
- charging indicator;
- drag;
- position persists;
- optional always-on-top;
- does not appear in Alt+Tab/taskbar unnecessarily;
- does not steal focus during a game.

---

# 17. Low-battery notification test

You do not need to drain the controller to 20% just for testing.

The application should provide a diagnostics/settings action such as:

`Test notification`

Use that to verify the Windows notification mechanism.

Then verify threshold logic with automated tests.

Actual real 20% behavior can be observed naturally later.

---

# 18. Autostart test

Enable:

`Start with Windows`

Then inspect Windows startup apps if desired.

Windows autostart reference:

https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

The MVP uses current-user:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

No administrator privilege should be requested.

Sign out/in or reboot.

Expected:

- one Cyclone Battery tray process starts;
- no visible console window;
- widget behavior follows saved setting.

---

# 19. Game test

This is important.

Launch FC or another controller-heavy game with Cyclone Battery running.

Check:

- controller input remains normal;
- no Bluetooth/USB weirdness introduced;
- no noticeable input lag;
- no repeated disconnects;
- app CPU remains negligible;
- no focus stealing;
- no popup spam.

The battery utility must be invisible during gameplay unless you intentionally show the widget.

---

# 20. If something is wrong: feed Arena evidence, not descriptions

Best input back to Arena:

```text
Hardware:
GameSir Cyclone 2
2.4 GHz dongle
XInput mode
Windows 11 x64
GameSir Connect closed

Expected:
Battery 73%

Observed:
Battery unknown

Diagnostic:
<PASTE COMPLETE DIAGNOSTIC>

Build/test output:
<PASTE IF RELEVANT>

Fix this in the existing repository and commit the change.
Do not broaden scope.
```

This is far more useful than:

`не працює`.

---

# 21. Publish the final EXE

After hardware tests succeed:

```powershell
.\scripts\publish-win-x64.ps1
```

Or manually:

```powershell
dotnet publish .\src\CycloneBattery.App\CycloneBattery.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

Microsoft single-file reference:

https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

Expected final location is usually similar to:

```text
src\CycloneBattery.App\bin\Release\net10.0-windows\win-x64\publish\
```

Expected main file:

```text
CycloneBattery.exe
```

---

# 22. Final PR / merge

Only after:

- Windows build passes;
- tests pass;
- real Cyclone 2 battery works;
- charging state works;
- reconnect works;
- tray/widget work;
- gaming is unaffected.

Then:

1. ask Arena to update README with any real hardware findings;
2. ask Arena to commit the final changes;
3. inspect Diff/Checks;
4. merge the PR in GitHub.

Important:
Arena currently structures a GitHub Agent session around one PR. Finish the session's work before closing/merging that PR if you want the same session to keep pushing changes.

Official reference:

https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode

---

# 23. Minimal future workflow

After MVP works, future changes become simple:

1. open a new Arena Agent session;
2. connect `cyclone-battery`;
3. give one focused feature request;
4. Arena creates a new working branch/PR;
5. test locally;
6. merge.

Examples:

- modern toast notifications;
- battery history;
- different tray icon styles;
- installer;
- other GameSir models.

Do not mix all of them into the first hardware-validation PR.

---

# 24. Source links

## Arena

Agent Mode:

https://arena.ai/agent

Coding in Agent Mode:

https://help.arena.ai/articles/1655691990-how-to-use-coding-in-agent-mode

Agent Mode:

https://help.arena.ai/articles/5432423882-how-to-use-agent-mode

File upload:

https://help.arena.ai/articles/5595418316-arena-how-to-file-upload

Fullstack Code Arena reference:

https://help.arena.ai/articles/5701270322-arena-how-to-code-arena

---

## GitHub

GitHub:

https://github.com/

New repository:

https://github.com/new

Git for Windows:

https://git-scm.com/download/win

---

## .NET / Windows

.NET 10 SDK:

https://dotnet.microsoft.com/en-us/download/dotnet/10.0

.NET support policy:

https://dotnet.microsoft.com/en-us/platform/support/policy

Single-file publishing:

https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

Windows Run registry:

https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

---

## HID

HidSharp NuGet:

https://www.nuget.org/packages/HidSharp

---

## Cyclone 2 research

cyclone2-linux:

https://github.com/vdemonchy/cyclone2-linux

Detailed protocol:

https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md

Independent reverse engineering:

https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md

Earlier public notes:

https://gist.github.com/NaokoAF/da4c166ed80e569276beee5a57bdeba9

---

# 25. The shortest possible version

If you want the absolute minimum manual work:

```text
1. Install Git + .NET 10 SDK.
2. Create private GitHub repo `cyclone-battery`.
3. Open https://arena.ai/agent
4. Connect that GitHub repo.
5. Upload TECH_SPEC.md.
6. Paste BUILD_PROMPT.md.
7. Let Arena finish its PR.
8. Clone/check out Arena branch on Windows.
9. Run build + tests.
10. Run --diagnostics with Cyclone 2 in XInput mode.
11. If battery fails, paste diagnostic log back to Arena.
12. When it works, run publish-win-x64.ps1.
13. Keep CycloneBattery.exe.
14. Merge the PR.
```

That is the intended workflow.
