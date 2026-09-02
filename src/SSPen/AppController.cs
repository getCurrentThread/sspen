using System.Windows;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Capture;
using SSPen.Diagnostics;
using SSPen.Interop;
using SSPen.Pin;
using SSPen.Settings;
using SSPen.Shell;
using SSPen.Updates;

namespace SSPen;

/// <summary>
/// 앱 셸 합성 루트: 설정 로드 → 모니터 열거 → 모니터별 서피스 + 툴바 생성 → 핫키 등록(부분 실패 허용)
/// → 트레이 → z-밴드 적용 (플랜 Startup/tray lifecycle). 종료 시 설정 저장·핫키 해제·GDI 정리.
/// 배선·수명·파사드만 담당한다: 핫키 테이블은 ShellHotkeys, 캡처 세션은 CaptureSessionController,
/// 설정 동기화는 SettingsBinder가 각각 소유한다.
/// </summary>
public sealed class AppController : IShellActions, ISettingsHost
{
    // LD-4: 합성 시점(UI 스레드)의 디스패처를 잡아 두고 셸 하위 컴포넌트에 주입한다.
    // Application.Current를 참조하지 않으므로 통합 테스트가 STA 스레드마다 무너지지 않는다 (R24).
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly AppState _state = new();
    private readonly SelectionModel _selection = new();
    private readonly UndoLedger _ledger;
    private readonly FadeSchedulerCore _fadeCore = new();
    private readonly FadingInkController _fading;
    private readonly List<ContentSurfaceWindow> _surfaces = [];
    private readonly SettingsBinder _settingsBinder;
    private readonly CaptureSessionController _capture;
    private readonly UpdateService _updateService;
    private ShellHotkeys? _shellHotkeys;
    private ToolbarWindow? _toolbar;
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private SelectionKeyMonitor? _selectionKeys;
    private bool _toolbarVisible = true;
    private PinManager? _pins;
    private readonly RenderTickController _renderTick;
    private readonly LedgerCommands _commands;

    public AppController(SettingsService? settingsService = null)
    {
        // LD-2: 원장은 문서를 잡지 않고 undo 시점에 현재 소유자를 조회한다 — 이관을 몇 번 거치든 안전하다.
        // 비용은 undo 1회마다 전 서피스 O(n) 선형 주사다 (R20, 현 규모에서 수용).
        _ledger = new UndoLedger(OwnerOf, _selection);
        _selection.AttachTo(_state);
        // 원장 명령 여섯 개는 LedgerCommands가 소유한다 (47단계). 서피스·핀이 필요한 자리는 지연 델리게이트다 — 둘 다 Start()에서야 생긴다.
        _commands = new LedgerCommands(
            _state, _selection, _ledger,
            documents: () => [.. _surfaces.Select(s => s.Document)],
            ownerOf: OwnerOf,
            flushPendingTransforms: FlushAllPendingTransforms,
            transferSurfaces: TransferSurfaces,
            closePins: () => _pins?.CloseAll());
        _fading = new FadingInkController(_fadeCore);
        // 공유 렌더 틱 정책은 RenderTickController가 소유한다 (45단계). 서피스 조회·후광 팬아웃·커서 폴링은 루트의 델리게이트다.
        _renderTick = new RenderTickController(
            _state, _fadeCore, _ledger, new CompositionTargetFrameSource(),
            now: () => DateTime.UtcNow,
            cursor: () => NativeMethods.GetCursorPos(out var c) ? (c.X, c.Y) : null,
            updateHalos: (x, y) =>
            {
                foreach (var surface in _surfaces)
                {
                    surface.UpdateHalo(x, y);
                }
            },
            // OwnerOf와 같은 술어(참조 포함)지만 문서가 아니라 서피스를 돌려준다 — 페이드 애니메이션은 창이 건다.
            ownerOf: element => _surfaces.FirstOrDefault(s => s.Document.Elements.Contains(element)));
        _settingsBinder = new SettingsBinder(_state, _fading, settingsService);
        _updateService = new UpdateService(_dispatcher, ExitApp);
        _capture = new CaptureSessionController(
            dispatcher: _dispatcher,
            toolbarVisible: () => _toolbarVisible && _toolbar?.Visibility == Visibility.Visible,
            setToolbarVisible: visible =>
            {
                if (_toolbar is not null)
                {
                    _toolbar.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                }
            },
            pins: () => _pins,
            warn: message => _tray?.ShowWarning(message),
            saveFolder: () => _settingsBinder.Settings.SaveFolder,
            applyZBand: ApplyZBand,
            setDecorationsVisible: visible =>
            {
                foreach (var surface in _surfaces)
                {
                    surface.SetDecorationsVisible(visible);
                }
            },
            setSurfacesSuspended: suspended =>
            {
                foreach (var surface in _surfaces)
                {
                    surface.SetSuspended(suspended);
                }
            });
    }

    public void Start()
    {
        // 설정 로드 (WI-14) 후 상태에 반영.
        _settingsBinder.Load();
        _settingsBinder.ApplyToState();

        // 모니터 토폴로지 진단 덤프 (프리모템 2 탐지 신호).
        var monitors = MonitorTopology.Enumerate();
        var virtualScreen = MonitorTopology.VirtualScreen();
        Log.Info($"가상 스크린: {virtualScreen}");
        foreach (var monitor in monitors)
        {
            Log.Info($"모니터 {monitor.DeviceName}: {monitor.Bounds}{(monitor.IsPrimary ? " (주)" : string.Empty)}");
        }

        // R8 1단계: 펜 뒤집기 구현 전에 태블릿·커서 구성을 로그에 남긴다 ('Eraser' 커서의 뒤집힘=True가 판별 신호).
        StylusProbe.LogTablets();

        // undo로 제거된 요소의 보류 페이드 취소 (CRIT-1 상호작용 계약).
        _ledger.ElementRemovedByUndo += _fading.OnElementRemoved;

        // 시동은 '열린 서피스 없음'에서 출발하는 로스터 diff다 — 닫을 것 없이 활성 모니터 전부를 만든다.
        var roster = SurfaceRosterPlan.Build(
            [], monitors, new HashSet<string>(_settingsBinder.Settings.DisabledMonitors));
        foreach (var monitor in roster.ToCreate)
        {
            CreateSurface(monitor);
        }

        // 툴바: 저장된 위치 복원 (AC-21), 없으면 주 모니터 우측 기본값.
        _toolbar = new ToolbarWindow(_state, this);
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        // 배치 산술(CRIT-17 스트립 높이 포함)은 ToolbarPlacement가 소유한다 (34단계).
        var (left, top) = ToolbarPlacement.Initial(
            _settingsBinder.Settings.ToolbarLeft, _settingsBinder.Settings.ToolbarTop, primary.Bounds);
        _toolbar.Left = left;
        _toolbar.Top = top;
        _toolbar.Show();
        _toolbar.LocationChanged += (_, _) =>
        {
            _settingsBinder.Settings.ToolbarLeft = _toolbar.Left;
            _settingsBinder.Settings.ToolbarTop = _toolbar.Top;
            _settingsBinder.ScheduleSave();
        };

        // 핀 z-앵커: 항상 마지막 서피스 바로 아래 (F5: 잉크는 핀 위).
        _pins = new PinManager(() => _surfaces.Count > 0 ? _surfaces[^1].Hwnd : 0, hooks: LowLevelHook.Native);
        _pins.PinsChanged += ApplyZBand;

        _shellHotkeys = new ShellHotkeys(
            _dispatcher, _state, () => _settingsBinder.Settings,
            _commands.Undo, _commands.ClearAll, StartCapture, ToggleToolbar, _commands.DeleteSelection);

        _hotkeys = new HotkeyService();
        _hotkeys.SetBindings(_shellHotkeys.BuildHotkeyMap());

        _tray = new TrayIcon(_state, OpenSettings, ExitApp, () => CheckForUpdates(isManual: true));
        _tray.WarnHotkeyConflicts(_hotkeys.FailedBindings);
        _hotkeys.RegistrationFailuresChanged += failed => _tray?.WarnHotkeyConflicts(failed);

        RunAtLogin.Apply(_settingsBinder.Settings.RunAtLogin);

        // R3/R4: 맨 ESC/Delete/Backspace는 서피스가 받을 수 없으므로 조건부 저수준 훅이 담당한다.
        // 게이트는 상태와 선택집합 양쪽에서 바뀌므로 두 이벤트 모두 구독한다.
        // 훅 배관은 Interop/LowLevelHook.Native — 핀 복귀 훅과 같은 OS 이음매다 (52단계); 인자는 이름으로 넘긴다.
        _selectionKeys = new SelectionKeyMonitor(
            _dispatcher, _state, _selection,
            blocked: () => _capture.IsActive || _settingsWindow is not null || _tray?.IsMenuOpen == true,
            clearSelection: _commands.ClearSelectionByEscape,
            deleteSelection: _commands.DeleteSelection,
            hooks: LowLevelHook.Native);
        _state.Changed += _selectionKeys.Refresh;
        _selection.SelectionChanged += _selectionKeys.Refresh;
        // 캡처 세션도 blocked 게이트에 들어가므로 시작·종료가 재판정 계기여야 한다 — 없으면
        // 세션 중 상태가 한 번이라도 바뀌었을 때 종료 후 훅이 되살아나지 않는다.
        _capture.ActiveChanged += _selectionKeys.Refresh;
        _tray.MenuOpenChanged += _selectionKeys.Refresh;

        _state.Changed += ApplyZBand;
        _state.Changed += _settingsBinder.SyncFromState;
        _state.Changed += _renderTick.Refresh;
        _renderTick.Refresh();
        ApplyZBand();

        if (_settingsBinder.Settings.CheckUpdateOnStart)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CheckForUpdates(isManual: false);
            };
            timer.Start();
        }

        Log.Info("셸 준비 완료 (서피스 " + _surfaces.Count + "개)");
    }

    public void Shutdown()
    {
        _renderTick.Stop(); // 틱 해제가 첫 줄 — 아래 구독 해제·창 닫기보다 먼저 (프레임이 닫힌 서피스를 만지지 않게).
        _state.Changed -= ApplyZBand;
        _state.Changed -= _settingsBinder.SyncFromState;
        _state.Changed -= _renderTick.Refresh;
        if (_selectionKeys is not null)
        {
            _state.Changed -= _selectionKeys.Refresh;
            _selection.SelectionChanged -= _selectionKeys.Refresh;
            _capture.ActiveChanged -= _selectionKeys.Refresh;
            if (_tray is not null)
            {
                _tray.MenuOpenChanged -= _selectionKeys.Refresh;
            }
        }
        _settingsBinder.SaveNow();
        _tray?.Dispose();
        _pins?.Dispose();
        _hotkeys?.Dispose();
        _selectionKeys?.Dispose();
        foreach (var surface in _surfaces)
        {
            surface.Detach();
            surface.Close();
        }
        _surfaces.Clear();
        _toolbar?.Close();
    }

    public void ExitApp()
    {
        Log.Info("종료 요청 (트레이/설정)");
        Application.Current.Shutdown();
    }

    // ---- ISettingsHost (WI-16) ----

    public AppSettings Settings => _settingsBinder.Settings;

    public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys =>
        _shellHotkeys?.RemappableHotkeys ?? [];

    public void RemapHotkey(string id, HotkeyDef def)
    {
        _settingsBinder.Settings.Hotkeys[id] = def;
        _settingsBinder.SaveNow();
        _hotkeys?.SetBindings(_shellHotkeys?.BuildHotkeyMap() ?? []); // 즉시 반영 (AC-23)
        Log.Info($"핫키 재지정: {id} → {HotkeyFormatting.Format(def)}");
    }

    public void SuppressHotkeys() => _hotkeys?.Suppress();

    public void RestoreHotkeys() => _hotkeys?.Restore();

    public void CheckForUpdates() => CheckForUpdates(isManual: true);

    /// <summary>표시 판정은 <see cref="UpdateCheckPresentation"/>이 소유한다 (35단계) — 여기는 결과별 UI 호출뿐이다.</summary>
    private void CheckForUpdates(bool isManual)
    {
        _updateService.CheckForUpdates(result =>
        {
            switch (UpdateCheckPresentation.Decide(result, isManual))
            {
                case UpdateCheckOutcome.ShowDialog:
                    var dialog = new UpdateDialog(result.ReleaseInfo!, _updateService);
                    dialog.Show();
                    dialog.Activate();
                    break;

                case UpdateCheckOutcome.ShowErrorDialog:
                    MessageBox.Show(
                        result.ErrorMessage ?? Strings.UpdateFailedTitle,
                        Strings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;

                case UpdateCheckOutcome.LogError:
                    Log.Warn($"자동 업데이트 확인 실패: {result.ErrorMessage}");
                    break;

                case UpdateCheckOutcome.ShowUpToDate:
                    MessageBox.Show(
                        Strings.UpdateLatestAlready,
                        Strings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;

                case UpdateCheckOutcome.Silent:
                    break;
            }
        });
    }

    public void ApplyGeneralSettings(AppSettings updated)
    {
        _settingsBinder.Replace(updated);
        RunAtLogin.Apply(_settingsBinder.Settings.RunAtLogin);
        SyncSurfacesWithSettings();
        ApplyZBand();
        Log.Info("일반 설정 적용");
    }

    /// <summary>
    /// 설정의 비활성 모니터 목록을 서피스 로스터에 반영한다. 판정(<see cref="SurfaceRosterPlan.Build"/>)은 순수 코어가,
    /// 닫기·생성의 <b>순서</b>는 여기가 소유한다. 토폴로지에서 사라진 모니터의 서피스는 닫지 않는다
    /// (보존이지 승인이 아니다 — SurfaceRosterPlan 문서 참조).
    /// </summary>
    private void SyncSurfacesWithSettings()
    {
        var monitors = Interop.MonitorTopology.Enumerate();
        var disabled = new HashSet<string>(_settingsBinder.Settings.DisabledMonitors);
        var roster = SurfaceRosterPlan.Build(
            [.. _surfaces.Select(s => s.Monitor.DeviceName)], monitors, disabled);

        // 1. 비활성화된 모니터의 서피스 정리 및 닫기 — 순서가 계약이다 (b0c237a): DetachFrom → RemoveAt → Detach → HideThenClose.
        for (int i = _surfaces.Count - 1; i >= 0; i--)
        {
            var surface = _surfaces[i];
            if (roster.ToClose.Contains(surface.Monitor.DeviceName))
            {
                _selection.DetachFrom(surface.Document);
                _surfaces.RemoveAt(i);
                surface.Detach();
                Shell.WindowLifetime.HideThenClose(surface);
                Log.Info($"모니터 서피스 비활성화 및 닫기: {surface.Monitor.DeviceName}");
            }
        }

        // 2. 새로 활성화된 모니터의 서피스 생성 및 표시
        foreach (var monitor in roster.ToCreate)
        {
            CreateSurface(monitor);
            Log.Info($"모니터 서피스 새로 생성 및 표시: {monitor.DeviceName}");
        }
    }

    /// <summary>
    /// 모니터 하나의 서피스를 만든다 — 시동과 설정 동기화가 <b>같은 배선</b>을 쓴다 (LD-2/R5 델리게이트 6종의 단일 소유).
    /// 순서가 계약이다: R17 <c>AttachTo(document)</c>가 창 생성보다 앞, <c>Show</c>가 목록 등록 뒤.
    /// </summary>
    private ContentSurfaceWindow CreateSurface(MonitorSurfaceInfo monitor)
    {
        var document = new AnnotationDocument(monitor.DeviceName);
        // R17: 문서에서 사라진 요소를 선택집합에서 떨어뜨려 댕글링 참조를 막는다.
        _selection.AttachTo(document);
        // 서피스 z-앵커: 항상 툴바 바로 아래 (사용자 조타 — 도구 선택 후에도 툴바 상호작용 보장).
        // 툴바는 서피스 뒤에 만들어지므로 지연 참조여야 한다.
        var surface = new ContentSurfaceWindow(
            monitor, _state, document, _ledger, _fading,
            _selection, OwnerOf, DpiOf, _commands.CommitTransform, _commands.EngageClickThrough,
            () => _toolbar?.Hwnd ?? 0,
            // 사용자 문자열은 Shell/Strings에만 산다 — 창(Annotation)에는 포맷터로 주입한다 (26단계).
            Strings.TableBadge);
        _surfaces.Add(surface);
        surface.Show();
        return surface;
    }

    // ---- IShellActions ----

    /// <summary>툴팁용 현재 유효 핫키 조합 (재지정 반영).</summary>
    public string? HotkeyLabel(string hotkeyId) => _shellHotkeys?.HotkeyLabel(hotkeyId);

    /// <summary>현재 페이딩 잉크 지속 시간(초, 0.1~5).</summary>
    public double FadingSeconds => FadingDurations.Clamp(_settingsBinder.Settings.FadingSeconds);

    /// <summary>툴바 플라이아웃에서 페이딩 지속 시간 변경 (설정 콤보와 동일 소유 지점).</summary>
    public void SetFadingDuration(double seconds)
    {
        _settingsBinder.SetFadingDuration(seconds);
        Log.Info($"페이딩 지속 시간: {_settingsBinder.Settings.FadingSeconds:0.#}초");
    }

    /// <summary>
    /// 진행 중인 휠 확대를 지금 원장에 확정한다 (R7).
    ///
    /// <b>원장에 싣거나 원장을 소비하는 모든 진입점의 선두에서 불러야 한다</b> — <see cref="LedgerCommands"/>가
    /// Undo·ClearAll·DeleteSelection 선두에서 주입받은 이 메서드를 부른다 (47단계). 휠 세션은 마지막
    /// 노치로부터 450ms 뒤에야 항목이 되므로, 그 사이에 다른 조작이 원장을 건드리면 순서가 뒤집힌다:
    /// 확대 직후 실행취소를 누르면 확대가 아니라 <b>그 이전 조작</b>이 취소되고, 뒤늦게 깨어난 타이머가
    /// 그 위에 변형 항목을 얹어 다음 실행취소 1회가 아무 일도 하지 않는다. "확대해 보고 마음에 안 들어
    /// 되돌린다"가 가장 자연스러운 조작이라 이 경로는 드문 경우가 아니다.
    /// </summary>
    private void FlushAllPendingTransforms()
    {
        foreach (var surface in _surfaces)
        {
            surface.FlushPendingTransforms();
        }
    }

    /// <summary>Alt+Shift+6: 가장 최근 조작 취소 — 본문은 <see cref="LedgerCommands.Undo"/> (47단계).</summary>
    public void Undo() => _commands.Undo();

    /// <summary>Alt+Shift+7: 모든 서피스 전체 지우기 + 핀 닫기 — 본문은 <see cref="LedgerCommands.ClearAll"/>.</summary>
    public void ClearAll() => _commands.ClearAll();

    /// <summary>Alt+Shift+D: 선택 요소 전부 삭제 (SEL-13) — 본문은 <see cref="LedgerCommands.DeleteSelection"/>. E2E 액터가 직접 부른다.</summary>
    public void DeleteSelection() => _commands.DeleteSelection();

    /// <summary>요소의 **현재** 소유 문서 (이관 후에도 유효). 어느 문서에도 없으면 null.</summary>
    private AnnotationDocument? OwnerOf(AnnotationElement element) =>
        _surfaces.FirstOrDefault(s => s.Document.Elements.Contains(element))?.Document;

    /// <summary>문서를 렌더하는 서피스의 DPI 배율 (D1: 모니터 간 이동 변위 환산). 못 찾으면 1.</summary>
    private double DpiOf(AnnotationDocument document) =>
        _surfaces.FirstOrDefault(s => ReferenceEquals(s.Document, document))?.DpiScale ?? 1;

    /// <summary>현재 서피스를 창 의존성 없는 이관 후보로 투사한다 — 사각형 선택은 <see cref="SurfaceProjection"/>이 소유한다 (32단계).</summary>
    private List<TransferSurface> TransferSurfaces() =>
        [.. _surfaces.Select(s => SurfaceProjection.ToTransferSurface(s.Document, s.Monitor, s.DpiScale))];

    /// <summary>Alt+Shift+S 캡처 세션 (WI-11) — CaptureSessionController에 위임.</summary>
    public void StartCapture() => _capture.StartCapture();

    /// <summary>설정 창 (WI-16). 단일 인스턴스로 열림.</summary>
    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            ApplyZBand();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _selectionKeys?.Refresh();
            ApplyZBand();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
        ApplyZBand();
        // D7: 설정 창은 자기 ESC 의미론(취소 버튼·폴더 선택 대화상자)을 갖는다 — 훅을 즉시 내린다.
        _selectionKeys?.Refresh();
    }

    private void ToggleToolbar()
    {
        if (_toolbar is null || _capture.IsActive)
        {
            return; // 캡처 세션 중 토글은 복원 플래그와 어긋나므로 무시 (아키텍트 어드바이저리).
        }
        _toolbarVisible = !_toolbarVisible;
        _toolbar.Visibility = _toolbarVisible ? Visibility.Visible : Visibility.Hidden;
        ApplyZBand();
    }

    // ---- z-밴드 (ARCH-5/R10): 설정창 > 캡처 오버레이+액션바 > 툴바 > 서피스 > 핀 > 기타 앱 ----

    /// <summary>순서 정책은 <see cref="ZBandOrder"/>가, 적용 시점은 이 클래스의 호출 지점들이 소유한다 (33단계).</summary>
    private void ApplyZBand() =>
        WindowStyling.ApplyZBand(ZBandOrder.Build(
            _settingsWindow?.Hwnd ?? 0,
            _capture.OverlayHwnd,
            _toolbar?.Hwnd ?? 0,
            _surfaces.Select(s => s.Hwnd),
            _pins?.Pins.Select(p => p.Hwnd) ?? []));

    // ---- 공유 렌더 틱 (ARCH-3/프리모템 1): 정책은 RenderTickController(45단계), 여기는 WPF 프레임 이벤트 어댑터뿐 ----

    /// <summary>
    /// <see cref="IFrameSource"/>의 WPF 어댑터 — <c>CompositionTarget.Rendering</c>을 아는 곳은 여기 하나뿐이다
    /// (ContentSurfaceWindow.DispatcherIdleScheduler 선례). 정적 이벤트라 Application이 필요 없고, 호출 스레드 Dispatcher에 묶인다.
    /// </summary>
    private sealed class CompositionTargetFrameSource : IFrameSource
    {
        public event Action? Frame;

        public void Start() => System.Windows.Media.CompositionTarget.Rendering += OnRendering;

        public void Stop() => System.Windows.Media.CompositionTarget.Rendering -= OnRendering;

        private void OnRendering(object? sender, EventArgs e) => Frame?.Invoke();
    }

    // ---- E2E 및 테스트 전용 접근자 ----
    internal AppState State => _state;
    internal SelectionModel Selection => _selection;
    internal UndoLedger Ledger => _ledger;
    internal IReadOnlyList<ContentSurfaceWindow> Surfaces => _surfaces;
    internal ToolbarWindow? Toolbar => _toolbar;
    internal SettingsBinder SettingsBinder => _settingsBinder;
    internal CaptureSessionController Capture => _capture;
    internal PinManager? Pins => _pins;
    internal SettingsWindow? CurrentSettingsWindow => _settingsWindow;
}
