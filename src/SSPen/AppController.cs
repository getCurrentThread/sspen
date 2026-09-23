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
    private readonly UpdateCheckFlow _updateFlow;
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
    // 토스트는 시동에서 한 번 만들어 프로세스 수명 내내 살아 있다 — 그래야 z-밴드 호출 지점이 늘지 않는다
    // (AGENTS L14; ToastWindow 문서 참조). 다른 창들과 마찬가지로 생성은 Start()다: 생성자에서 Window를
    // 만들면 합성 루트를 STA 밖에서 세우는 테스트가 무너진다.
    private ToastHost? _toasts;

    // 54단계 L3: z-밴드 적용·사후 검증. 최상위 z-순서 변화(REORDER, 데스크톱)·포그라운드 전환(FOREGROUND)이 검증을 깨우고,
    // 실제 순서가 ZBandOrder와 다를 때만 밴드를 다시 돌린다. 워치 두 개·정책(코얼레싱·백오프)·종료 플래그는
    // ZBandVerifier가 소유한다 (72단계, A8-1·A1-1) — 루트는 호출 지점('언제 적용하는가')만 가진다.
    private readonly ZBandVerifier _zBand;
    private bool _zBandSubscribed;
    // 73단계 (실험적, 설정 ZBandPolling·기본 켜짐): 2초마다 같은 IsOrdered 검사 후 어긋나면 Repair — 이벤트 계층의 안전망.
    // 생성은 Start(_zBand.Install 직후), 켜고 끄기는 Start·ApplyGeneralSettings, 정지는 Shutdown(_zBand.Stop 옆, 창 닫기 전).
    private ZBandPoller? _zPoller;

    // A8-2: 로그인 시 시작(HKCU Run) 반영은 이 델리게이트 하나로만 나간다. 기본값은 RunAtLogin.Apply(프로덕션),
    // E2E 픽스처는 기록 람다를 주입해 개발자 PC의 실제 레지스트리 값을 건드리지 않는다.
    private readonly Action<bool> _applyRunAtLogin;

    /// <param name="settingsService">설정 영속화 위치. null이면 %APPDATA%\SS Pen (프로덕션).</param>
    /// <param name="applyRunAtLogin">
    /// 로그인 시 시작 값을 반영하는 이음매 (A8-2). null이면 <see cref="RunAtLogin.Apply"/> — App.xaml.cs는 넘기지 않는다.
    /// 호출 지점은 Start와 ApplyGeneralSettings 두 곳뿐이다.
    /// </param>
    public AppController(SettingsService? settingsService = null, Action<bool>? applyRunAtLogin = null)
    {
        _applyRunAtLogin = applyRunAtLogin ?? RunAtLogin.Apply;
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
        // 업데이트 확인 흐름 (77단계, A1-2): 로그 수준·문구 폴백·판정별 표시는 UpdateCheckFlow가, 안내 상자 owner는
        // DialogOwnerRules가 소유한다 — 루트는 창을 만들고 띄우는 어댑터만 넘긴다.
        _updateFlow = new UpdateCheckFlow(
            check: _updateService.CheckForUpdates,
            current: () => UpdateService.CurrentVersion,
            logInfo: Log.Info,
            logWarn: Log.Warn,
            showRelease: info =>
            {
                var dialog = new UpdateDialog(info, _updateService);
                dialog.Show();
                dialog.Activate();
            },
            showMessage: ShowUpdateMessage);
        // z-밴드 검증기 (72단계): 생성은 OS를 건드리지 않는다 — 훅 설치는 Start의 Install이다. BandOrder는 호출 시점에
        // 토스트·설정창·캡처·툴바·핀·서피스를 읽는 지연 조회라 아직 없는 창(0)은 Build가 건너뛴다.
        // 게시 우선순위는 Background를 명시한다 (AGENTS L14 — 입력·렌더보다 뒤).
        _zBand = new ZBandVerifier(
            bandOrder: BandOrder,
            applyBand: WindowStyling.ApplyZBand,
            isWindow: WindowStyling.IsWindow,
            below: WindowStyling.Below,
            desktop: NativeMethods.GetDesktopWindow,
            postBackground: action => _dispatcher.BeginInvoke(DispatcherPriority.Background, action),
            winEvents: WinEventWatch.Native);
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
            report: ReportCaptureOutcome,
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
            },
            setToastSuspended: suspended => _toasts?.SetSuspended(suspended));
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

        // 토스트: 서피스보다 먼저, 툴바보다 앞서 HWND를 확보한다 — 이후의 모든 ApplyZBand가
        // 토스트를 이미 포함하므로 알림을 낼 때마다 밴드를 다시 적용할 필요가 없다.
        _toasts = new ToastHost(_dispatcher);
        _toasts.Prepare();

        // 툴바: 저장된 위치 복원 (AC-21), 없으면 주 모니터 우측 기본값.
        _toolbar = new ToolbarWindow(_state, this);
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        // 배치 산술(CRIT-17 스트립 높이 포함)은 ToolbarPlacement가 소유한다 (34단계).
        var (left, top) = ToolbarPlacement.Initial(
            _settingsBinder.Settings.ToolbarLeft, _settingsBinder.Settings.ToolbarTop, primary.Bounds);
        _toolbar.Left = left;
        _toolbar.Top = top;
        _toolbar.Show();
        PlaceToolbar(primary, virtualScreen);
        _toolbar.LocationChanged += (_, _) =>
        {
            _settingsBinder.Settings.ToolbarLeft = _toolbar.Left;
            _settingsBinder.Settings.ToolbarTop = _toolbar.Top;
            _settingsBinder.ScheduleSave();
        };

        // 핀 z-앵커: 툴바 바로 아래 (71단계 사용자 결정: 툴바 > 핀 > 서피스 — 핀은 판서 서피스·보드 위에 보인다).
        _pins = new PinManager(() => _toolbar?.Hwnd ?? 0, hooks: LowLevelHook.Native);
        _pins.PinsChanged += ApplyZBand;
        // 클릭 통과에 걸리면 그 핀은 마우스를 받지 못한다 — 되찾는 제스처를 아는 것이 유일한 복구 경로다.
        _pins.ClickThroughEngaged += () =>
            _toasts?.Show(new ToastRequest(ToastKind.Info, Strings.PinClickThroughEngaged));

        _shellHotkeys = new ShellHotkeys(
            _dispatcher, _state, () => _settingsBinder.Settings,
            // 실행취소·전체 지우기는 루트의 래퍼를 거친다 (원장 호출 + 확인 대화상자 + 결과 알림) —
            // 핫키와 툴바 버튼이 같은 경로를 타야 마찰과 알림이 한쪽에서만 빠지지 않는다.
            Undo, ClearAll, StartCapture, ToggleToolbar, _commands.DeleteSelection);

        // OS 등록은 Interop/HotkeyRegistrar.Native — 핀·선택 키 훅의 hooks: 인자와 같은 이음매 관용구다 (76단계, C-3).
        _hotkeys = new HotkeyService(registrar: HotkeyRegistrar.Native);
        _hotkeys.SetBindings(_shellHotkeys.BuildHotkeyMap());

        _tray = new TrayIcon(_state, OpenSettings, ExitApp, () => CheckForUpdates(isManual: true), ShowToolbar);
        _tray.WarnHotkeyConflicts(_hotkeys.FailedBindings);
        _hotkeys.RegistrationFailuresChanged += failed => _tray?.WarnHotkeyConflicts(failed);

        _applyRunAtLogin(_settingsBinder.Settings.RunAtLogin);

        // R3/R4: 맨 ESC/Delete/Backspace는 서피스가 받을 수 없으므로 조건부 저수준 훅이 담당한다.
        // 게이트는 상태와 선택집합 양쪽에서 바뀌므로 두 이벤트 모두 구독한다.
        // 훅 배관은 Interop/LowLevelHook.Native — 핀 복귀 훅과 같은 OS 이음매다 (52단계); 인자는 이름으로 넘긴다.
        // 수식키 읽기는 KeyboardState.NonShiftModifier(Shift 제외 정책의 소유자)를 주입한다 (53단계).
        _selectionKeys = new SelectionKeyMonitor(
            _dispatcher, _state, _selection,
            blocked: () => _capture.IsActive || _settingsWindow is not null || _tray?.IsMenuOpen == true,
            nonShiftModifierDown: () => KeyboardState.NonShiftModifier,
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
        _zBandSubscribed = true;
        _state.Changed += _settingsBinder.SyncFromState;
        _state.Changed += _renderTick.Refresh;
        _renderTick.Refresh();
        ApplyZBand();

        // 54단계 L2/L3: 툴바 z-변화와 전역 z-순서 변화가 밴드 검증을 깨운다. 설치 실패는 진단만 남긴다 — 요청·결과 단계 훅은 그대로 산다.
        // 두 훅은 한쪽이 실패해도 둘 다 시도하고, 로그는 어느 쪽이 실패했는지 밝힌다 (70단계, A9-8: 예전 || 단락 평가 결함).
        // 설치·실패 로그·WinEvent 콜백은 ZBandVerifier가 소유한다 (72단계).
        _toolbar.ZOrderChanged += _zBand.RequestVerify;
        _zBand.Install();

        // 73단계 실험적 z-순서 주기 정정 (사용자 결정: 2초 고정·설정은 체크박스만·기본 켜짐). 검증기와 같은 IsOrdered로 보고
        // 어긋나면 Repair한다 — Apply가 아니므로 검증기의 백오프를 풀지 않고, 백오프로 쉬는 중에도 정정한다(정책을 참조하지 않는다).
        // 캡처 세션 중에는 검사를 건너뛴다: 세션이 툴바 숨김 → DwmFlush → BitBlt → 오버레이 순서를 소유하므로, 그 사이의
        // 정정은 숨긴 툴바·오버레이의 z를 흔들어 캡처 결과를 바꿀 수 있다. 타이머는 Background 우선순위(AGENTS L14의 유일한 시간 기반 예외).
        _zPoller = new ZBandPoller(
            new DispatcherIdleScheduler(_dispatcher),
            blocked: () => _capture.IsActive,
            isOrdered: _zBand.IsOrdered,
            repair: _zBand.Repair,
            log: Log.Info);
        _zPoller.SetEnabled(_settingsBinder.Settings.ZBandPolling);

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
        // z-밴드 검증 정지가 첫 줄 — 이후의 검증 요청을 무시하고 WinEvent 두 훅을 푼다. 이미 큐에 든 검증은
        // pending만 풀고 아무것도 적용하지 않는다 (예전 _shuttingDown 플래그 + 워치 해제, 72단계).
        _zBand.Stop();
        // 주기 정정도 같은 자리에서 멈춘다 — 창을 닫기 전이어야 파괴 중인 창에 Repair(SetWindowPos)가 걸리지 않는다 (73단계).
        _zPoller?.Dispose();
        _renderTick.Stop(); // 틱 해제 — 아래 구독 해제·창 닫기보다 먼저 (프레임이 닫힌 서피스를 만지지 않게).
        if (_toolbar is not null)
        {
            _toolbar.ZOrderChanged -= _zBand.RequestVerify;
        }
        _state.Changed -= ApplyZBand;
        _zBandSubscribed = false;
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
        if (_pins is not null)
        {
            // 핀 닫힘이 밴드 재적용을 부르면 이미 파괴된 창들에 SetWindowPos가 걸린다 (54단계 L5).
            _pins.PinsChanged -= ApplyZBand;
        }
        _pins?.Dispose();
        _hotkeys?.Dispose();
        _selectionKeys?.Dispose();
        foreach (var surface in _surfaces)
        {
            surface.ZBandRequested -= ApplyZBand;
            surface.Detach();
            surface.Close();
        }
        _surfaces.Clear();
        _toolbar?.Close();
        _toasts?.Close();
    }

    /// <summary>트레이 "종료"와 업데이트 재시작 경로 — 합성 루트가 메서드 그룹으로 직접 배선한다 (ISettingsHost 경유 아님).</summary>
    public void ExitApp() => ShutdownApplication("트레이/업데이트");

    /// <summary>
    /// 툴바 설정 메뉴의 "프로그램 종료" (55단계). 확인 없이 바로 종료한다 — <see cref="ExitApp"/>과 동작은 같고,
    /// 종료 경로(툴바 메뉴)를 로그에 구분해 남기려고 분리해 둔다.
    /// </summary>
    public void RequestExit() => ShutdownApplication("툴바 설정 메뉴");

    /// <summary>
    /// 앱 종료의 단일 지점 (57단계, A1-5). <c>Application.Current</c>가 남은 유일한 곳이다 (LD-4/R24 — AGENTS L48).
    /// 로그 문구는 합치기 전 두 경로와 바이트 동일하다: "종료 요청 (트레이/업데이트)" / "종료 요청 (툴바 설정 메뉴)".
    /// </summary>
    private static void ShutdownApplication(string origin)
    {
        Log.Info($"종료 요청 ({origin})");
        Application.Current.Shutdown();
    }

    // ---- ISettingsHost (WI-16) ----

    public AppSettings Settings => _settingsBinder.Settings;

    public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys =>
        _shellHotkeys?.RemappableHotkeys ?? [];

    /// <summary>보류분 일괄 반영 — 전부 쓴 뒤 저장 1회·재등록 1회. 순서는 <see cref="HotkeyRemapFlow.ApplyBatch"/>가 소유한다 (79단계, A6-3).</summary>
    public void RemapHotkeys(IReadOnlyList<(string Id, HotkeyDef Def)> batch) =>
        HotkeyRemapFlow.ApplyBatch(
            batch,
            _settingsBinder.Settings.Hotkeys,
            save: _settingsBinder.SaveNow,
            rebind: () => _hotkeys?.SetBindings(_shellHotkeys?.BuildHotkeyMap() ?? [])); // 최종 표로 한 번 (AC-23)

    public void SuppressHotkeys() => _hotkeys?.Suppress();

    public void RestoreHotkeys() => _hotkeys?.Restore();

    public void CheckForUpdates() => CheckForUpdates(isManual: true);

    /// <summary>
    /// 흐름(시작 로그 → 확인 → 요약 로그 → 판정별 표시)은 <see cref="UpdateCheckFlow"/>가, 표시 판정은
    /// <see cref="UpdateCheckPresentation"/>이 소유한다 (35단계 → 77단계, A1-2).
    /// </summary>
    private void CheckForUpdates(bool isManual) => _updateFlow.Run(isManual);

    /// <summary>
    /// 수동 확인 안내창. 앱 창은 전부 Topmost라 owner 없는 MessageBox는 그 밑으로 숨어 설정창의 "지금 확인"이 먹통처럼
    /// 보인다 — 설정창(없으면 보이는 툴바)을 owner로 물려 같은 최상단 층에 올린다 (UpdateDialog의 실패 상자와 같은 방식).
    /// 숨겨진 툴바는 owner로 못 쓴다 — 그 판정은 <see cref="DialogOwnerRules"/>다 (77단계, A1-2).
    /// </summary>
    private void ShowUpdateMessage(string text, MessageBoxImage image)
    {
        var owner = DialogOwnerWindow();
        if (owner is null)
        {
            MessageBox.Show(text, Strings.AppName, MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(owner, text, Strings.AppName, MessageBoxButton.OK, image);
        }
    }

    /// <summary><see cref="DialogOwnerRules.Choose"/>의 판정을 실제 창으로 옮긴다 — 설정창 &gt; 보이는 툴바 &gt; 없음 (77단계, A1-2).</summary>
    private Window? DialogOwnerWindow() =>
        DialogOwnerRules.Choose(_settingsWindow is not null, _toolbar is { IsVisible: true }) switch
        {
            DialogOwner.Settings => _settingsWindow,
            DialogOwner.Toolbar => _toolbar,
            _ => null,
        };

    public void ApplyGeneralSettings(AppSettings updated)
    {
        _settingsBinder.Replace(updated);
        _applyRunAtLogin(_settingsBinder.Settings.RunAtLogin);
        SyncSurfacesWithSettings();
        ApplyZBand();
        // 73단계: 설정의 "실험적 기능" 체크박스 — 같은 값이면 무동작(멱등)이다.
        _zPoller?.SetEnabled(_settingsBinder.Settings.ZBandPolling);
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
                surface.ZBandRequested -= ApplyZBand;
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
        // 서피스 z-앵커: 서피스 밴드 바로 위 창 — 맨 아래 핀, 핀이 없으면 툴바 (사용자 조타 — 도구 선택 후에도 툴바·핀 상호작용 보장;
        // 71단계 사용자 결정: 툴바 > 핀 > 서피스). 툴바·PinManager는 서피스 뒤에 만들어지므로 지연 참조여야 한다 — 그 전에는 0(훅 무동작).
        var surface = new ContentSurfaceWindow(
            monitor, _state, document, _ledger, _fading,
            _selection, OwnerOf, DpiOf, _commands.CommitTransform, _commands.EngageClickThrough,
            () => ZBandOrder.SurfaceAnchor(_toolbar?.Hwnd ?? 0, _pins?.Pins.Select(p => p.Hwnd) ?? []),
            // 사용자 문자열은 Shell/Strings에만 산다 — 창(Annotation)에는 포맷터로 주입한다 (26단계).
            Strings.TableBadge);
        // 54단계 L0: 텍스트 도구 핸드셰이크(활성화) 직후·커밋 직후 밴드를 다시 적용한다.
        surface.ZBandRequested += ApplyZBand;
        _surfaces.Add(surface);
        surface.Show();
        if (_zBandSubscribed)
        {
            // 54단계 L5: 서피스는 생성자에서 AppState.Changed를 구독한다. 시동 뒤 만들어진 서피스는 ApplyZBand보다 뒤에 구독되어
            // 상태 변경마다 "밴드 적용 → 서피스 표시 토글(상승)" 순서가 되므로, ApplyZBand를 다시 맨 뒤로 보낸다.
            _state.Changed -= ApplyZBand;
            _state.Changed += ApplyZBand;
        }
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
    /// 현재 도구 상태 한 줄을 토스트로 알린다 (AC-20). 문구는 <see cref="StatusReadout"/>가, 표시는
    /// 1단계 토스트가 맡는다 — 새 창도, 새 z-밴드 멤버도 없다. <c>Transient</c>라 휠을 연속으로 굴려도
    /// 마지막 상태 하나만 남는다(줄 서지 않는다).
    /// </summary>
    public void ShowStatusReadout() =>
        _toasts?.Show(new ToastRequest(
            ToastKind.Info,
            StatusReadout.Line(_state.ActiveTool, _state.Thickness, _state.CurrentColor, _state.FadingInk, FadingSeconds),
            Transient: true));

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

    /// <summary>
    /// Alt+Shift+6: 가장 최근 조작 취소 — 본문은 <see cref="LedgerCommands.Undo"/> (47단계).
    /// 되돌릴 것이 없으면 알린다: 이전에는 단축키를 눌러도 화면상 아무 반응이 없어
    /// 실행취소가 고장 난 것인지 되돌릴 게 없는 것인지 구별할 수 없었다.
    /// </summary>
    public void Undo()
    {
        if (!_commands.Undo())
        {
            _toasts?.Show(new ToastRequest(ToastKind.Info, Strings.UndoNothing));
        }
    }

    /// <summary>
    /// Alt+Shift+7: 모든 서피스 전체 지우기 + 핀 닫기 — 본문은 <see cref="LedgerCommands.ClearAll"/>.
    /// 마찰 판정은 <see cref="DestructiveActionRules"/>가 소유하고 여기는 대화상자·알림 실행만 한다:
    /// 판서는 실행취소 1회로 돌아오지만 함께 닫히는 핀은 원장 밖이라 되돌릴 수 없다.
    /// </summary>
    public void ClearAll()
    {
        var prompt = DestructiveActionRules.ClearAll(_commands.ClearableCount(), _pins?.Pins.Count ?? 0);
        if (!prompt.HasAnything)
        {
            return; // 지울 것이 없다 — 확인도 알림도 없다.
        }
        if (prompt.NeedsConfirm)
        {
            var answer = MessageBox.Show(
                Strings.ClearAllConfirm(prompt.PinCount),
                Strings.ClearAllConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }
        _commands.ClearAll();
        // 지운 직후가 되돌리는 법을 알려 줄 유일한 시점이다 (핀은 그 대상이 아니라는 것은 확인 대화상자가 이미 말했다).
        string? undoCombo = _shellHotkeys?.HotkeyLabel("undo");
        _toasts?.Show(new ToastRequest(
            ToastKind.Info,
            undoCombo is null ? Strings.ClearAllDone : Strings.ClearAllDoneWithUndo(undoCombo)));
    }

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

    /// <summary>
    /// 캡처 결과를 사용자에게 알린다 — 판정은 <see cref="CaptureOutcomeRules"/>, 문구·액션 라벨은
    /// <see cref="CaptureOutcomeText"/>(68단계, C-2), 표시는 <see cref="ToastHost"/>가 각각 소유하고
    /// 여기는 셋을 잇는 배선과 탐색기 실행뿐이다.
    /// </summary>
    private void ReportCaptureOutcome(CaptureOutcome outcome)
    {
        if (outcome.Message == CaptureMessageId.None || _toasts is null)
        {
            return;
        }
        var label = CaptureOutcomeText.ActionLabel(outcome);
        Action? open = label is null ? null : () => RevealInExplorer(outcome.Path!);
        _toasts.Show(new ToastRequest(outcome.Kind, CaptureOutcomeText.Text(outcome), label, open));
    }

    /// <summary>저장한 파일을 탐색기에서 선택된 상태로 연다. 실패해도 알림 자체를 잃지 않는다.</summary>
    private static void RevealInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"탐색기 열기 실패: {ex.Message}");
        }
    }

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

    /// <summary>
    /// 툴바 최종 배치 (AC-21/CRIT-17). 레이아웃이 끝난 뒤에 하는 이유가 둘 있다.
    ///
    /// 하나, <b>배율</b>: 예전에는 주 모니터의 물리 픽셀 사각형을 DIP인 <c>Left/Top</c>에 그대로 대입해
    /// 150% 화면에서 첫 실행 툴바가 통째로 화면 밖에 놓였다. 여기서는 창의 실제 DPI로 환산해 물리 좌표로 옮긴다.
    /// 둘, <b>높이</b>: <c>StripHeight</c> 상수는 손으로 유지하는 값이라 버튼이 하나 늘 때마다 중앙이 어긋났다.
    /// 실측 <c>ActualHeight</c>를 쓰면 그 상수는 첫 프레임의 임시 위치로만 남는다.
    ///
    /// 저장된 위치가 있으면 옮기지 않고 <b>클램프만</b> 한다 — 정상 값은 그대로라 설정 마이그레이션이 필요 없다.
    /// </summary>
    private void PlaceToolbar(MonitorSurfaceInfo primary, PhysicalRect virtualScreen)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_toolbar is null || _toolbar.Hwnd == 0)
            {
                return;
            }
            double dpiX = System.Windows.Media.VisualTreeHelper.GetDpi(_toolbar).DpiScaleX;
            double dpiY = System.Windows.Media.VisualTreeHelper.GetDpi(_toolbar).DpiScaleY;
            var saved = ToolbarPlacement.Restored(
                _settingsBinder.Settings.ToolbarLeft,
                _settingsBinder.Settings.ToolbarTop,
                CoordinateSpace.ToLogical(virtualScreen, dpiX));
            if (saved is { } position)
            {
                _toolbar.Left = position.Left;
                _toolbar.Top = position.Top;
                return;
            }
            // DIP→물리 환산은 CoordinateSpace가 소유한다 (AGENTS L18, 65단계 A1-7) — 크기는 올림, 여백은 반올림. 축 선택은 그대로.
            int width = CoordinateSpace.ToPhysicalExtent(_toolbar.ActualWidth, dpiX);
            int height = CoordinateSpace.ToPhysicalExtent(_toolbar.ActualHeight, dpiY);
            var (x, y) = ToolbarPlacement.PhysicalOnPrimary(
                primary.WorkArea, width, height, CoordinateSpace.ToPhysicalLength(ToolbarPlacement.RightMargin, dpiX));
            WindowStyling.MovePhysical(_toolbar.Hwnd, x, y);
        });
    }

    private void ToggleToolbar()
    {
        if (_toolbar is null || _capture.IsActive)
        {
            return; // 캡처 세션 중 토글은 복원 플래그와 어긋나므로 무시 (아키텍트 어드바이저리).
        }
        SetToolbarVisible(!_toolbarVisible);
    }

    /// <summary>
    /// 트레이 "툴바 보이기" (AC-22). 숨긴 툴바의 복귀 경로가 Alt+Shift+0 하나뿐이던 것을 고친다 —
    /// 토글이 아니라 <b>보이기</b>인 이유: 이 메뉴를 여는 사람은 이미 툴바를 못 찾은 사람이라
    /// 여기서 다시 숨겨지면 같은 곳을 두 번 돌게 된다.
    /// </summary>
    private void ShowToolbar()
    {
        if (_toolbar is null || _capture.IsActive)
        {
            return;
        }
        SetToolbarVisible(true);
    }

    /// <summary>
    /// 툴바 설정 메뉴 "도구 막대 닫기" (55단계). 토글이 아니라 <b>숨기기</b>다 — 메뉴는 툴바 위에서만 열리므로
    /// 이미 숨겨진 상태에서 호출될 일이 없다. 복귀는 트레이 "툴바 보이기"(<see cref="ShowToolbar"/>) 또는 Alt+Shift+0.
    /// </summary>
    public void HideToolbar()
    {
        if (_toolbar is null || _capture.IsActive)
        {
            return; // 캡처 세션 중에는 복원 플래그와 어긋나므로 무시 (ToggleToolbar와 같은 가드).
        }
        SetToolbarVisible(false);
    }

    private void SetToolbarVisible(bool visible)
    {
        _toolbarVisible = visible;
        _toolbar!.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        ApplyZBand();
    }

    // ---- z-밴드 (ARCH-5/R10): 토스트 > 설정창 > 캡처 오버레이+액션바 > 툴바 > 핀 > 서피스(보드) > 기타 앱 (71단계 사용자 결정) ----

    /// <summary>
    /// 정규 재적용 — 순서 정책은 <see cref="ZBandOrder"/>가, 적용과 백오프 해제는 <see cref="ZBandVerifier.Apply"/>가,
    /// 적용 시점은 이 클래스의 호출 지점들이 소유한다 (33단계, 72단계). 이벤트 구독·해제(<c>+=</c>/<c>-=</c>)가 같은
    /// 델리게이트로 맞아떨어지도록 메서드로 남긴다.
    /// </summary>
    private void ApplyZBand() => _zBand.Apply();

    private List<nint> BandOrder(bool includeToast) =>
        ZBandOrder.Build(
            includeToast ? _toasts?.Hwnd ?? 0 : 0,
            _settingsWindow?.Hwnd ?? 0,
            _capture.OverlayHwnd,
            _toolbar?.Hwnd ?? 0,
            _pins?.Pins.Select(p => p.Hwnd) ?? [],
            _surfaces.Select(s => s.Hwnd));

    // ---- 공유 렌더 틱 (ARCH-3/프리모템 1): 정책은 RenderTickController(45단계), 여기는 WPF 프레임 이벤트 어댑터뿐 ----

    /// <summary>
    /// <see cref="IFrameSource"/>의 WPF 어댑터 — <c>CompositionTarget.Rendering</c>을 프레임 루프로 구독하는 곳은 이 클래스 하나다.
    /// 이와 별개로 <c>CaptureSessionController.WaitForRenderPass</c>가 캡처 직전 일회성 동기 대기(120ms 상한)로 구독했다가 즉시 뗀다
    /// (프레임 루프 구독자가 아니다). 이음매 + 얇은 WPF 어댑터 모양은 <c>Annotation/DispatcherIdleScheduler</c>가 선례다.
    /// 정적 이벤트라 Application이 필요 없고, 호출 스레드 Dispatcher에 묶인다.
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
    internal ZBandPoller? ZPoller => _zPoller;
    internal PinManager? Pins => _pins;
    internal SettingsWindow? CurrentSettingsWindow => _settingsWindow;
}
