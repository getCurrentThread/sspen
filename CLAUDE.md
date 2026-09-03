# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Source of truth

**Read `AGENTS.md` first.** It is comprehensive and current: architecture, data flow, the z-band/topmost
guard system, the transform/selection subsystem invariants, coordinate-space rules, code conventions,
testing conventions, and two hard-won agent tool-call gotchas (literal UTF-8 in tool inputs; the
`deep-interview`/`ralplan` planning-phase mutation guard). Do not duplicate that content here — this file
only adds a quick-start command reference, the packaging/runtime facts, and the few conventions that get
broken most often.

## What this is

SS Pen (`SSPen`): a Korean-language Windows screen-annotation tool (Epic Pen–style). WPF on
`net10.0-windows`, zero NuGet dependencies in the app project, all OS interaction via hand-written Win32
P/Invoke (`src/SSPen/Interop/`). No DI container, no MVVM, no XAML views beyond `App.xaml` — UI is
C#-built `Window` subclasses, composition root is `src/SSPen/AppController.cs`.

`src/SSPen/Updates/` is the app's only network code (`UpdateService` polls GitHub releases at
`getCurrentThread/sspen`). It is also the most likely place for someone to reach for `async` — don't; the
no-`async` rule holds there too.

Prerequisites: Windows 10 1809+ x64, .NET SDK 10. Building the installer additionally needs Inno Setup 6
(`winget install JRSoftware.InnoSetup`).

## Commands

```powershell
# build
dotnet build SSPen.sln -c Debug

# run
dotnet run --project src/SSPen

# all tests (solution-wide) — NOT safe to run casually: it pulls in the integration and E2E
# projects, which open real fullscreen topmost windows and take over the physical screen.
dotnet test SSPen.sln

# unit & simulation tests (headless-safe, fast) — the default for everyday work (1204 cases, <1 s)
dotnet test tests/SSPen.Tests/SSPen.Tests.csproj
dotnet test tests/SSPen.Tests/SSPen.Tests.csproj --filter "FullyQualifiedName~HitTestTests"

# integration tests (Win32 OS-bound)
dotnet test tests/SSPen.IntegrationTests/SSPen.IntegrationTests.csproj

# end-to-end user workflow tests (AppController & UI actor simulation)
dotnet test tests/SSPen.E2ETests/SSPen.E2ETests.csproj

# all-in-one local verification & packaging pipeline (build + all tests + publish verify + installer)
powershell -ExecutionPolicy Bypass -File build/verify.ps1

# publish (self-contained win-x64) + verify + Inno Setup installer
powershell -ExecutionPolicy Bypass -File build/publish.ps1

# publish and silent install on local PC
powershell -ExecutionPolicy Bypass -File build/publish.ps1
Stop-Process -Name SSPen -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
$installer = Get-ChildItem publish/installer/SSPen-Setup-*.exe | Select-Object -Last 1
Start-Process -FilePath $installer.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
Start-Process -FilePath "$env:LOCALAPPDATA\Programs\SS Pen\SSPen.exe"
```

CI (`.github/workflows/ci.yml`) runs on `windows-latest` for every push and PR to `main`/`master`:
`dotnet restore` → `dotnet build -c Release` → unit/simulation tests → **E2E tests** → integration tests
→ self-contained publish validation → Inno Setup installer packaging → upload of the
`SSPen-Setup-Installer` artifact. The integration step carries `continue-on-error: true` (headless-runner
tolerance), so a green CI run does **not** prove the integration suite passed — check that step's log.
Pushing a `v*` tag triggers `.github/workflows/release.yml`, which publishes the self-contained build, the
installer and a portable zip to a GitHub release.

## Publish & installer gotchas

**규칙**: 배포(혹은 빌드/패키징)를 수행하고 난 뒤에는 항상 현재 컴퓨터에 무음(silent) 모드로 설치를 완료하고 앱을 다시 실행해야 한다.
반드시 `Stop-Process -Name SSPen -Force`로 기존 인스턴스를 종료하고, `Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait`로 설치 완료를 대기한 후, `$env:LOCALAPPDATA\Programs\SS Pen\SSPen.exe`를 실행하여 새 버전을 즉시 띄운다.

`build/publish.ps1` is the only supported path: it publishes, then *proves* self-containment twice
(publish-folder assertions plus a launch with `DOTNET_ROOT` masked to an empty dir), then compiles
`installer/SSPen.iss`. `publish/` and `artifacts/` are gitignored, so a deploy produces nothing to commit.

Two facts about the installer that are not visible from the `.iss` source alone:

- **`AppId` reuse wins over `DefaultDirName`.** The AppId is shared with earlier installs that used a
  different product name, so Inno upgrades in place at the *recorded* location. On a machine with such an
  install, the app lands in `%LOCALAPPDATA%\Programs\SSAFY Pen\` even though the script says `SS Pen`.
  Verify the real path from the `InstallLocation` value under `HKCU:\...\Uninstall\*`, not from the script.
- **Uninstall deletes user data.** `[UninstallDelete]` removes `%APPDATA%\SS Pen` — settings *and* logs.
  Uninstall/reinstall is therefore not a safe way to "clean up" an install.

The installed `SSPen.exe` carries the source commit in its `ProductVersion` (`<Version>+<sha>`); use that
to confirm which build is actually installed. The version number itself lives in two places that must be
bumped together: `<Version>` in `src/SSPen/SSPen.csproj` and `MyAppVersion` in `installer/SSPen.iss`.

## Where things live at runtime

| Path | What |
|---|---|
| `%APPDATA%\SS Pen\settings.json` | settings; a corrupt file is quarantined as `.bad` and defaults are regenerated |
| `%APPDATA%\SS Pen\logs\sspen-yyyyMMdd.log` | daily rolling log — first place to look when the app misbehaves |
| `사진\SS Pen\` | default capture output folder (configurable) |

## Conventions worth repeating from AGENTS.md

- Comments, XML docs, log messages, and commit subjects are **Korean**, citing spec IDs
  (`WI-nn`, `AC-nn`, `CRIT-n`, `ARCH-n`). Match this when editing existing files.
- User-visible strings live only in `Shell/Strings.cs` — with one known, deliberate exception: the Korean
  `ErrorMessage` text in `Updates/UpdateCheckerCore.cs` and `Updates/UpdateService.cs` (recorded as
  "deliberately not done" in AGENTS.md). Don't cite those as precedent for new hardcoded strings.
- No `async`/`await` — concurrency is Dispatcher-based. Do not introduce `Task`-based code.
- New logic should be split into pure, injectable core + thin UI adapter (see AGENTS.md
  "Testability pattern") so it lands in `tests/SSPen.Tests/` rather than needing the
  machine-bound integration suite.
- Tests are xUnit: class `<Subject>Tests`, method `Method_Scenario_ExpectedOutcome`. Adversarial suites
  use the `*RedTeamTests.cs` suffix; shared fakes live in `tests/SSPen.Tests/TestSupport/`.
