# Repository Guidelines

## Project Overview

SS Pen (`SSPen`) is a Korean-language Windows screen-annotation tool (Epic Pen–style): transparent per-monitor overlays for pen/highlighter/shape/text drawing, global hotkeys, screen capture (copy/save/pin), fading ink, and a tray-driven lifetime. WPF on `net10.0-windows`, zero NuGet dependencies in the app — all OS interaction is hand-written Win32 P/Invoke.

## Architecture & Data Flow

- **No DI container, no MVVM, no XAML views** (only `App.xaml`). All UI is C#-built `Window` subclasses. `src/SSPen/AppController.cs` is the hand-wired composition root: it `new`s every service/window, delegates ownership of the hotkey table / capture session / settings sync to `Shell/ShellHotkeys.cs`, `Capture/CaptureSessionController.cs`, and `Settings/SettingsBinder.cs`, and implements the two interface seams `IShellActions` (defined in `Shell/IShellActions.cs`) and `ISettingsHost` (defined in `Shell/SettingsWindow.cs`) as thin facades so windows call back through interfaces, not the controller class.
- **State**: one mutable `AppState` (`Annotation/AppState.cs`) shared by reference with toolbar and all surfaces; a single coarse `Changed` event (no `INotifyPropertyChanged`). Cross-module notification is plain `event Action` everywhere.
- **Typical flow**: `RegisterHotKey` on a message-only window (`Shell/HotkeyService.cs`) → `WM_HOTKEY` → action from the `Shell/ShellHotkeys.cs` hotkey table mutates `AppState` → `Changed` → each `ContentSurfaceWindow` re-applies click-through/exstyle/cursor → mouse input (per-tool state machine in `Annotation/SurfaceInputController.cs`, style snapshotted at stroke start) builds an element → commit adds to per-monitor `AnnotationDocument` + records in the single global `UndoLedger` (chronological undo across all monitors) + schedules fade in `FadeSchedulerCore`.
- **Frame loop**: `CompositionTarget.Rendering` is subscribed only while needed (halo/fades active) and detaches itself when idle.
- **Interop policy**: all P/Invoke is centralized in `Interop/NativeMethods.cs` (`[LibraryImport]` source-generated; `[DllImport]` only where callbacks/strings require it). Policy layers wrap it: `WindowStyling` (exstyles, z-band, `AnchorBelow` + `KeepTopmost` hooks), `MonitorTopology`, `CoordinateSpace`, `CursorFactory`, `CaptureService` (GDI BitBlt, deliberately not Windows.Graphics.Capture).
- **The z-band needs guards in BOTH directions.** `ApplyZBand` only runs on `AppState.Changed`, so nothing re-asserts ordering while the user is merely drawing. `AnchorBelow` (on each surface) blocks "surface rises above toolbar"; `KeepTopmost` (on the toolbar) blocks "toolbar is pushed out of the topmost band" by an external app. Removing either one produces the same user-visible symptom — a toolbar that is **visible but unclickable**, because surfaces swallow every click.
- **Detect z-demotion via exstyle, never via `hwndInsertAfter`.** Windows resolves `HWND_TOPMOST`/`HWND_NOTOPMOST` into concrete HWNDs before the hook sees `WM_WINDOWPOSCHANGING` (measured: `insertAfter=66294 flags=0x13`), so intent cannot be recovered from the constant. `KeepTopmost` instead reads `WS_EX_TOPMOST` on `WM_WINDOWPOSCHANGED`; re-asserting is self-terminating because the flag is present on the recursive pass.
- **Never attach an HWND hook with `?.AddHook`.** A silently missing `HwndSource` disables a z-guard entirely, and the only symptom is an occasional dead toolbar. `WindowStyling.AddHookOrThrow` fails loudly instead.
- **Surfaces are placed on `MonitorSurfaceInfo.WorkArea`, not `Bounds`** (`Bounds` = `rcMonitor`, `WorkArea` = `rcWork`), so annotation never covers the taskbar. Placement, halo containment, physical↔local conversion, board travel, and `TransferSurface` must all use the *same* rect — mixing the two silently offsets ink and breaks cross-monitor selection drops. Capture is unaffected: it works off `MonitorTopology.VirtualScreen()`.
- **Coordinate spaces (strict rule)**: Win32 boundaries use physical pixels via `PhysicalRect`; physical↔logical DPI conversion happens ONLY in `Interop/CoordinateSpace.cs`. Negative virtual-screen origins are first-class (target topology: 3×1920×1080, origin −1920,0). App is PerMonitorV2 DPI-aware via `app.manifest`.
- **Selection/transform subsystem** (`Annotation/`, tool `ToolKind.Select`): element geometry stays get-only; all manipulation lives in one mutable `ElementTransformState` per element.
  - **State, not matrices (A3)**: `ElementTransformState` is a `readonly record struct (ScaleX, ScaleY, AngleDegrees, Translation)`. A free `Matrix` is forbidden as stored state because a general 2×2 does not decompose uniquely into rotation + anisotropic scale (shear leaks in), so decompose/recompose round-trips break. Shear is unrepresentable here, making the invariant true by type. `Translation` is a **displacement**, never a position.
  - **Single composition point**: matrices are built ONLY in `TransformMath.ToMatrix(state, pivot)`, in the order `T(-pivot)·S·R·T(pivot)·T(Translation)`. Scale precedes rotation, so resizing follows the element's local axes (Office-conformant OBB handles).
  - **Two bounds contracts, deliberately different**: the local frame (OBB corners via `TransformedCorners()`) drives decorations and handle hit-testing; the axis-aligned `TransformedBounds` is for **marquee intersection only**. Do not unify them — marquee stays axis-aligned by design (no SAT).
  - **`RenderMatrixFor` is the single owner** of visual transform output (`AnnotationVisualFactory`), and `ApplyRenderTransform` is the only place that assigns `RenderTransform`. `TextElement` needs a `T(Origin)` term that stroke/shape must not get; scattering that branch reintroduces pivot drift on the undo/rollback/transfer paths only.
  - **`AnnotationDocument.ElementTransformChanged`** is the notification channel for in-place transforms. Every `TransformState =` write MUST be followed by a raise on the owning document, or the model moves while the screen freezes — a defect headless tests pass.
  - **`SelectionModel` is global** (one instance, all surfaces) so a selection can survive crossing a monitor boundary. It subscribes to `AppState.ActiveToolChanged` **only** — subscribing to the coarse `Changed` would clear the selection on every quick-color click.
  - **`SuppressInvalidation()` scope is exactly two call sites**: the cross-monitor transfer in `AppController` and the ownership branch of `UndoLedger.TransformOperation.Undo`. Under-applying empties the selection the moment an element crosses monitors; over-applying leaves dangling references after eraser/fade removal.
  - **Transfer order is load-bearing** (`Remove` → rebase → `Add`): `Add` synchronously builds the visual and bakes the matrix, so rebasing after `Add` freezes a stale transform with no follow-up event. DPI rebase is `ScaleX/Y × (srcDpi/tgtDpi)`, angle invariant, and `Translation' = Rebase(c + Translation) − c`.
  - **Known limits**: marquee selects within one monitor only (`SEL-LIM-1`); ink is clipped at the monitor edge mid-drag (`SEL-LIM-2`); fading ink is unselectable (`SEL-LIM-3`); and **multi-select scale/rotate applies only to the element whose handle was grabbed** (`SEL-LIM-4`) — only *move* applies to the whole selection. Decorations are drawn per element rather than around a shared bounding box, so the grabbed handle's owner is the only unambiguous target. Office does group-transform against a common frame; matching that is out of scope.
- **Testability pattern**: pure logic is split from UI adapters — `FadeSchedulerCore` (injected clock) vs `FadingInkController`, `CaptureFileNaming` (injected `exists` callback), `ShiftConstraints`, `CoordinateSpace`, geometry hit-testing on `AnnotationElement`, `Shell/ToolbarStateMap` (button↔state mapping), arrow-head geometry in `Annotation/AnnotationVisualFactory`, `TransformMath`/`SelectionGeometry`/`SelectionOperations` (selection math and plan-before-mutate helpers). New logic should follow this split so it lands in the unit test project.
- **Dispatcher injection, not `Application.Current`**: `ShellHotkeys` and `CaptureSessionController` take a `Dispatcher` constructor argument. `Application.Current` survives only in `AppController.ExitApp` (tray shutdown). An `AppDomain` allows one `Application`, so any new dependency breaks the integration suite from its second STA thread onward.

## Key Directories

- `src/SSPen/Annotation/` — tool state (`AppState`), element model, per-monitor `ContentSurfaceWindow` overlay (thin window shell), `SurfaceInputController` (per-tool input state machine behind the `ISurfaceHost` seam), `AnnotationVisualFactory` (element→visual factory), `UndoLedger`, fading ink, Shift snapping, `ColorPalette` (the only color table: quick-color defaults + the 24-swatch extended palette + hex conversion).
- `src/SSPen/Shell/` — `ToolbarWindow` (window shell only; strip assembly in `ToolbarStripBuilder`, popup flyouts in `ToolbarFlyouts`, pure button↔state mapping in `ToolbarStateMap`, brushes/logo in `ToolbarTheme`, button identity via the `ToolbarButtonId` enum — never display strings), `IShellActions`, `ShellHotkeys` (hotkey table + labels), `SettingsWindow`, `HotkeyService`, `TrayIcon` (the only WinForms usage: `NotifyIcon`), `Strings.cs`, `Icons.cs`.
- `src/SSPen/Interop/` — all Win32 P/Invoke + policy wrappers (see above).
- `src/SSPen/Settings/` — `AppSettings` POCO, `SettingsBinder` (settings↔`AppState` two-way sync + debounced save), JSON persistence to `%APPDATA%\SS Pen\settings.json`, run-at-login registry.
- `src/SSPen/Capture/` — `CaptureSessionController` (session state machine: toolbar hide → DWM flush → BitBlt → overlay → restore), BitBlt capture, region overlay, clipboard/PNG outputs, file naming. After a region is dragged, clicking anywhere outside the action bar commits **pin** as the default action.
- `src/SSPen/Pin/` — pinned-screenshot windows incl. low-level mouse hook for click-through pins; `PinZoom` (pure cursor-anchored wheel zoom math — the point under the cursor stays fixed while scaling).
- `src/SSPen/Diagnostics/` — `Log.cs`: rolling daily file log at `%APPDATA%\SS Pen\logs\`; `InputRaceFilter` (pure predicate that identifies the benign "stale window handle" WPF input race so it is logged, not surfaced as a fatal dialog).
- `tests/SSPen.Tests/` — headless unit tests. `tests/SSPen.IntegrationTests/` — machine-bound Win32 tests.
- `build/publish.ps1` — publish + installer orchestrator. `installer/SSPen.iss` — Inno Setup 6 script.

## Development Commands

Run from repo root:

```powershell
dotnet build SSPen.sln -c Debug
dotnet run --project src/SSPen
dotnet test tests/SSPen.Tests/SSPen.Tests.csproj                    # safe anywhere
dotnet test tests/SSPen.Tests/SSPen.Tests.csproj --filter "FullyQualifiedName~HitTestTests"
dotnet test tests/SSPen.IntegrationTests/SSPen.IntegrationTests.csproj   # bound machine only
dotnet publish src/SSPen/SSPen.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
powershell -ExecutionPolicy Bypass -File build/publish.ps1                # publish + verify + installer
```

**Caution**: `dotnet test SSPen.sln` also runs the integration tests, which hard-assert the specific 3-monitor topology and need an interactive desktop — they fail everywhere else. Default to the unit project.

## Code Conventions & Common Patterns

- **Korean everywhere except identifiers**: all comments, XML docs, log messages, and commit subjects are Korean and cite spec IDs (`WI-nn`, `AC-nn`, `CRIT-n`, `ARCH-n`). Keep this convention when editing.
- **User-visible strings live only in `Shell/Strings.cs`** — never inline UI text elsewhere.
- **Types**: `sealed class` for everything stateful, `static class` for pure helpers, `record` / `readonly record struct` for value data. File-scoped namespaces. `nint` for HWNDs (never `IntPtr`).
- **Modern C#** (`LangVersion=latest`): collection expressions `[]`, switch expressions, target-typed `new`, pattern matching (`is { } x`, `is not null`). `Nullable=enable`.
- **No async/await anywhere** — concurrency is Dispatcher-based (`Dispatcher.Invoke`/`BeginInvoke`, `DispatcherTimer`, `CompositionTarget.Rendering`). Do not introduce `Task`-based code.
- **Error handling**: narrow catch filters (`catch (X ex) when …`), log via `Diagnostics/Log`, prefer graceful degradation (partial hotkey registration tolerated; corrupt settings quarantined to `.bad` and defaults regenerated). Top-level `DispatcherUnhandledException` logs, shows a Korean MessageBox, and marks handled — except for the benign stale-window input race (`Diagnostics/InputRaceFilter`), which is logged as a warning without a dialog.
- **Transient windows must not be destroyed under the mouse**: closing a pin or the capture overlay while the pointer is over it leaves WPF's input layer holding a dead `PresentationSource`, which throws `Win32Exception(1400)` on the *next* mouse move — far from the cause. Always close such windows through `Shell/WindowLifetime.HideThenClose`, which hides first (letting WPF re-hit-test while the HWND is alive) and destroys on a later dispatcher pass. Tooltips are separate popup HWNDs and do not vanish with their owner, so `ToolbarWindow` closes them (`ToolbarFlyouts.CloseTooltips`) whenever the toolbar hides; every tooltip must be a `ToolTip` instance registered there, never a bare string.
- **Fading ink duration**: range and UI presets live only in `Annotation/FadingDurations` (0.1–5s). Stored values are clamped on load, so settings written by the older 3/6/12s scheme open safely.
- **Settings**: `AppSettings` is additive-props-with-defaults (no migrations); saves debounced 800 ms; two-way sync with `AppState` guarded by an `_applyingSettings` re-entrancy flag (owned by `Settings/SettingsBinder.cs`). Array-valued defaults must be produced by a **factory** (`ColorPalette.DefaultQuickColorHex()`), never a shared `static readonly` array — otherwise editing one `AppSettings` mutates every other instance.
- **Fading ink is a toggle on drawing tools, not a tool** (`AppState.FadingInk` + `FadingApplies`): it composes with pen/highlighter/text/shape instead of replacing the active tool. `FadingAppliesTo` is the single place that decides which tools can fade; eraser/select/none cannot. Every gesture start (`StartStroke`/`StartShape`/`BeginTextEdit`) snapshots `FadingApplies` so toggling mid-drag cannot reclassify an in-flight element.
- **Colors come from `Annotation/ColorPalette`**, never inline hex arrays. Quick colors are user-editable instance state on `AppState` (`QuickColors` / `SetQuickColor`), so toolbar swatches must read the color **at click/refresh time** — capturing it at build time freezes the old palette.
- **Icons**: Fluent System Icons fonts embedded as WPF `<Resource>`; `Shell/Icons.cs` maps (regular=default, filled=selected) codepoint pairs. Font families are created lazily so the glyph table stays usable in headless unit tests (`pack://` needs a WPF `Application`).
- **App icon**: `Assets/AppIcon.ico` is a committed asset regenerated by `build/make-appicon.ps1` (accent circle + white "S", 16–256px). It is the single source for the exe icon (`ApplicationIcon`), the tray icon (loaded from the `SSPen.AppIcon.ico` manifest resource, sized per DPI), and the installer — do not hand-draw a second one at runtime.

## Important Files

- `src/SSPen/App.xaml.cs` — entry: single-instance mutex, `Log.Initialize`, unhandled-exception handler, starts `AppController`. `ShutdownMode=OnExplicitShutdown` (tray-driven lifetime).
- `src/SSPen/AppController.cs` — composition root; start/shutdown sequence, z-band ordering, shared render tick, `IShellActions`/`ISettingsHost` facades (hotkey table → `Shell/ShellHotkeys.cs`, capture session → `Capture/CaptureSessionController.cs`, settings sync → `Settings/SettingsBinder.cs`).
- `src/SSPen/SSPen.csproj` — `net10.0-windows`, `WinExe`, `UseWPF` + `UseWindowsForms` (NotifyIcon only; `System.Drawing`/`System.Windows.Forms` implicit usings removed to avoid WPF collisions), `AllowUnsafeBlocks` (required by `[LibraryImport]`, no manual unsafe), `app.manifest` for PerMonitorV2.
- `src/SSPen/Interop/NativeMethods.cs` — the only place raw P/Invoke may be added (exception: `Pin/PinClickThroughMonitor.cs` keeps its `WH_MOUSE_LL` hook local).
- `installer/SSPen.iss` — per-user install (`PrivilegesRequired=lowest`), consumes `publish/win-x64/`, emits `publish/installer/SSPen-Setup-1.0.0.exe`.

## Runtime/Tooling Preferences

- **.NET SDK 10 on Windows** is required (`net10.0-windows`, WPF). No `global.json`, no `Directory.Build.props`, no `.editorconfig`, no analyzers, no CI — do not assume any exist.
- **Zero NuGet packages in the app project**; keep it that way unless there is a compelling reason. Tests use xUnit 2.9.3 + `Microsoft.NET.Test.Sdk` 17.13.0 + `xunit.runner.visualstudio` 3.0.2.
- Release distribution is **self-contained win-x64** chosen at CLI time (no publish props in the csproj); `build/publish.ps1` verifies self-containment (masks `DOTNET_ROOT`) and compiles the installer with Inno Setup 6 (`winget install JRSoftware.InnoSetup`).
- `publish/` and `artifacts/` (ad-hoc QA PowerShell harness) are gitignored build outputs — never track them.

## Testing & QA

- **Framework**: xUnit (`[Fact]`, `[Theory]`/`[InlineData]`). Class naming `<Subject>Tests`; method naming `Method_Scenario_ExpectedOutcome` (e.g. `Load_CorruptJson_QuarantinesAndReturnsDefaults`). Implicit AAA with blank-line separation; private static factory helpers; `IDisposable` for temp-dir cleanup.
- **Unit tests** (`tests/SSPen.Tests/`): pure logic only — coordinate/DPI math, hit-testing, undo ledger, fade scheduling (injected clock, `Due(now)`), Shift constraints, capture rect math, file naming (injected `exists`), settings round-trip against a per-test temp dir. Windows-only (WPF types) but headless-safe.
- **Integration tests** (`tests/SSPen.IntegrationTests/`): real windows, real BitBlt pixel assertions, real `RegisterHotKey`, real exstyle read-back. Every test body runs through `StaRunner.Run` (STA thread, 60 s hard timeout, exception rethrow via `ExceptionDispatchInfo`); use `StaRunner.PumpMessages()` to drain the dispatcher. **Machine-bound**: hard-asserts 3×1920×1080 monitors with virtual screen origin (−1920,0); interactive session only; excluded from headless CI by design.
- **Integration parallelism is disabled** (`AssemblyInfo.cs`, `DisableTestParallelization = true`). The suite contends over one shared resource — the physical screen — so a class that shows a fullscreen topmost surface will corrupt another class's BitBlt pixel assertions. That failure only appears in a full-suite run and passes per-class, so do not re-enable it.
- **Mixed-DPI transfer is unverifiable on this rig** (all three monitors are 100%, so `r = 1` and an omitted DPI correction still passes every integration test). The only defense is the headless `RebaseState_*` witnesses, which must use `r ≠ 1`; a same-DPI assertion there proves nothing because the correct and naive formulas coincide when `r = 1`.
- When adding features, put the logic in a pure, injectable core (matching the existing split) and unit-test it; reserve integration tests for genuine Win32/WPF runtime behavior.

## Agent Tool-Call Rules (learned the hard way)

Two runtime guards killed real turns in this repo. Both are avoidable, and both are silent until the run dies.

### 1. Literal UTF-8 in tool inputs, never `\uXXXX` escapes

This is a Korean-language repo: commit subjects, `Shell/Strings.cs` entries, XML doc comments, and `.gjc/` workflow state all carry Hangul. That makes this rule load-bearing here, not cosmetic.

- **Write non-ASCII text in tool inputs as literal UTF-8.** This includes JSON serialized into a string field (no `ensure_ascii`-style output) and any prose passed through a shell `env` var. The only exception is when the escape is the intended source syntax of the file being written (regex character classes, codepoint bounds).
- **Failure mode:** the runtime detects the escaped non-ASCII tool call and a managed fallback retries the turn. If the re-issue is also escaped, it gives up after 2 retries and fails closed with `Managed fallback retried the escaped non-ASCII tool-call turn 2 times without a literal-UTF-8 re-issue`. The user just sees the turn abort mid-work. Announcing "re-issuing with literal UTF-8" and then sending the same escaped payload again is what turns one abort into a dead run.
- **Measured cost:** escaping Hangul inflates the payload about 2.7x (a real 2,545-char workflow-state blob became 6,840 chars). Turns that died carried 4,000-12,300 output tokens versus 150-800 for normal turns. Model output limits (128K) were never the cause. Do not misdiagnose this as a token or timeout problem.
- **Large payloads:** prefer the incremental delta the workflow CLI asks for (one round record by `round_key`, only changed facts, only changed fields) over resending whole arrays. When a body is genuinely large, stage it to a system temp dir (`$TEMP` on Windows; `/tmp` does not exist here) and pass the path instead of inlining it.
- **Recovery after an abort:** aborted turns may have partially executed. Verify state before retrying (`gjc <skill> read --json`, or `search` the state file for `state_revision`) instead of blindly re-running the mutation. Workflow-state CLI merges are keyed and idempotent, so a verified re-apply is safe but a blind one can double-write.

### 2. Planning-phase boundary blocks mutations

While `deep-interview` or `ralplan` is active, the phase boundary refuses every mutation with: `Deep-interview phase boundary: continue gathering context/questions/risks and emit a handoff/spec before code edits.`

What it blocks, including on files unrelated to the interview:

- `edit` / `write` / `ast_edit` on any repo file, this one included
- shell redirection (`cmd > file`), even into `$TEMP`
- pipes that look like mutations (`| head`, `| tail`)
- occasionally a `bash` command whose text merely resembles a mutation, so prefer `read`/`search` over shelling out

How to work with it instead of fighting it:

- To inspect state or command output, use `read` on the returned `artifact://<id>` or `search` against the state file. Do not reach for a redirect.
- The guard reads `active` and `phase` from `.gjc/_session-{sessionid}/state/skill-active-state.json`. Flipping only `current_phase` is not enough and flipping only `active` through `gjc deep-interview write` is silently ignored (`active` is runtime-owned there).
- To edit repo files mid-interview, release the guard through the state CLI, then restore it immediately:

```bash
gjc state deep-interview write --input '{"active":false,"current_phase":"handoff"}' --json
# ... perform the edit ...
gjc state deep-interview write --input '{"active":true,"current_phase":"interviewing"}' --force --json
```

The restore needs `--force`: `handoff` to `interviewing` is a backward transition and is rejected without it (`invalid deep-interview phase transition from handoff to interviewing`).

- `gjc state clear --mode <skill>` is destructive; never use it to unblock an edit. Interview transcript, scores, and facts live in `deep-interview-state.json` and survive an `active` toggle, so pausing costs nothing.
- Ask before pausing when the user is mid-interview. The boundary exists to stop premature implementation, and a repo-file edit that the user explicitly requested is the only routine reason to lift it.
