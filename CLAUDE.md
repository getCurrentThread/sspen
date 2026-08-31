# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Source of truth

**Read `AGENTS.md` first.** It is comprehensive and current: architecture, data flow, the z-band/topmost
guard system, the transform/selection subsystem invariants, coordinate-space rules, code conventions,
testing conventions, and two hard-won agent tool-call gotchas (literal UTF-8 in tool inputs; the
`deep-interview`/`ralplan` planning-phase mutation guard). Do not duplicate that content here — this file
only adds a quick-start command reference.

## What this is

SS Pen (`SSPen`): a Korean-language Windows screen-annotation tool (Epic Pen–style). WPF on
`net10.0-windows`, zero NuGet dependencies in the app project, all OS interaction via hand-written Win32
P/Invoke (`src/SSPen/Interop/`). No DI container, no MVVM, no XAML views beyond `App.xaml` — UI is
C#-built `Window` subclasses, composition root is `src/SSPen/AppController.cs`.

## Commands

```powershell
# build
dotnet build SSPen.sln -c Debug

# run
dotnet run --project src/SSPen

# all tests (solution-wide, safe on any machine)
dotnet test SSPen.sln

# unit & simulation tests (headless-safe, fast)
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
```

CI (`.github/workflows/ci.yml`) runs on `windows-latest` for every push and PR: `dotnet restore` → `dotnet build -c Release` → unit/simulation tests → integration tests → self-contained publish validation → Inno Setup installer packaging.

## Conventions worth repeating from AGENTS.md

- Comments, XML docs, log messages, and commit subjects are **Korean**, citing spec IDs
  (`WI-nn`, `AC-nn`, `CRIT-n`, `ARCH-n`). Match this when editing existing files.
- User-visible strings live only in `Shell/Strings.cs`.
- No `async`/`await` — concurrency is Dispatcher-based. Do not introduce `Task`-based code.
- New logic should be split into pure, injectable core + thin UI adapter (see AGENTS.md
  "Testability pattern") so it lands in `tests/SSPen.Tests/` rather than needing the
  machine-bound integration suite.
