using System.Windows;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Capture;
using SSPen.Diagnostics;
using SSPen.Interop;
using SSPen.Pin;
using SSPen.Settings;
using SSPen.Shell;

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
    private ShellHotkeys? _shellHotkeys;
    private ToolbarWindow? _toolbar;
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private bool _toolbarVisible = true;
    private PinManager? _pins;
    private bool _renderTickAttached;

    public AppController()
    {
        // LD-2: 원장은 문서를 잡지 않고 undo 시점에 현재 소유자를 조회한다 — 이관을 몇 번 거치든 안전하다.
        // 비용은 undo 1회마다 전 서피스 O(n) 선형 주사다 (R20, 현 규모에서 수용).
        _ledger = new UndoLedger(OwnerOf, _selection);
        _selection.AttachTo(_state);
        _fading = new FadingInkController(_fadeCore);
        _settingsBinder = new SettingsBinder(_state, _fading);
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

        // undo로 제거된 요소의 보류 페이드 취소 (CRIT-1 상호작용 계약).
        _ledger.ElementRemovedByUndo += _fading.OnElementRemoved;

        foreach (var monitor in monitors)
        {
            var document = new AnnotationDocument(monitor.DeviceName);
            // R17: 문서에서 사라진 요소를 선택집합에서 떨어뜨려 댕글링 참조를 막는다.
            _selection.AttachTo(document);
            // 서피스 z-앵커: 항상 툴바 바로 아래 (사용자 조타 — 도구 선택 후에도 툴바 상호작용 보장).
            var surface = new ContentSurfaceWindow(
                monitor, _state, document, _ledger, _fading,
                _selection, OwnerOf, OnCommitTransform, () => _toolbar?.Hwnd ?? 0);
            _surfaces.Add(surface);
            surface.Show();
        }

        // 툴바: 저장된 위치 복원 (AC-21), 없으면 주 모니터 우측 기본값.
        _toolbar = new ToolbarWindow(_state, this);
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        if (_settingsBinder.Settings.ToolbarLeft is { } savedLeft && _settingsBinder.Settings.ToolbarTop is { } savedTop)
        {
            _toolbar.Left = savedLeft;
            _toolbar.Top = savedTop;
        }
        else
        {
            _toolbar.Left = primary.Bounds.X + primary.Bounds.Width - 34 - 12;
            // CRIT-17: 실제 스트립 높이 = 로고(34+2) + 테두리(4) + 버튼 14개·구분선 4개·팀컴러 블록.
            // 선택 버튼 추가로 494 → 524. 이 값이 틀어지면 툴바가 모니터 중앙에서 밀려난다.
            _toolbar.Top = primary.Bounds.Y + (primary.Bounds.Height - 524) / 2.0;
        }
        _toolbar.Show();
        _toolbar.LocationChanged += (_, _) =>
        {
            _settingsBinder.Settings.ToolbarLeft = _toolbar.Left;
            _settingsBinder.Settings.ToolbarTop = _toolbar.Top;
            _settingsBinder.ScheduleSave();
        };

        // 핀 z-앵커: 항상 마지막 서피스 바로 아래 (F5: 잉크는 핀 위).
        _pins = new PinManager(() => _surfaces.Count > 0 ? _surfaces[^1].Hwnd : 0);
        _pins.PinsChanged += ApplyZBand;

        _shellHotkeys = new ShellHotkeys(
            _dispatcher, _state, () => _settingsBinder.Settings,
            Undo, ClearAll, StartCapture, ToggleToolbar, DeleteSelection);

        _hotkeys = new HotkeyService();
        _hotkeys.SetBindings(_shellHotkeys.BuildHotkeyMap());

        _tray = new TrayIcon(_state, OpenSettings, ExitApp);
        _tray.WarnHotkeyConflicts(_hotkeys.FailedBindings);
        _hotkeys.RegistrationFailuresChanged += failed => _tray?.WarnHotkeyConflicts(failed);

        RunAtLogin.Apply(_settingsBinder.Settings.RunAtLogin);

        _state.Changed += ApplyZBand;
        _state.Changed += _settingsBinder.SyncFromState;
        _state.Changed += UpdateRenderTickSubscription;
        UpdateRenderTickSubscription();
        ApplyZBand();
        Log.Info("셸 준비 완료 (서피스 " + _surfaces.Count + "개)");
    }

    public void Shutdown()
    {
        if (_renderTickAttached)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRenderTick;
            _renderTickAttached = false;
        }
        _settingsBinder.SaveNow();
        _tray?.Dispose();
        _pins?.Dispose();
        _hotkeys?.Dispose();
    }

    private void ExitApp()
    {
        Log.Info("종료 요청 (트레이)");
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

    public void ApplyGeneralSettings(AppSettings updated)
    {
        _settingsBinder.Replace(updated);
        RunAtLogin.Apply(_settingsBinder.Settings.RunAtLogin);
        Log.Info("일반 설정 적용");
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

    /// <summary>Alt+Shift+6: 어느 모니터에서든 가장 최근 조작 취소 (전역 시간순 원장).</summary>
    public void Undo() => _ledger.Undo();

    /// <summary>
    /// Alt+Shift+7: 모든 서피스 전체 지우기 — 판서는 하나의 원장 항목.
    /// 고정해 둔 핀 캡처도 함께 닫는다 (사용자 요청 15차): "전체 지우기"가 화면을
    /// 깨끗이 비우는 동작이라고 기대하는데 핀만 남으면 다시 하나씩 닫아야 했다.
    /// <b>핀 닫기는 실행취소 대상이 아니다</b> — 원장은 판서 문서만 다룬다.
    /// </summary>
    public void ClearAll()
    {
        var cleared = _surfaces
            .Select(s => (s.Document, s.Document.Clear()))
            .ToList();
        _ledger.RecordClearAll(cleared);
        // R10: 장식은 선택집합을 따라가므로 해제하지 않으면 빈 화면에 핸들만 남는다.
        _selection.Clear();
        _pins?.CloseAll();
    }

    /// <summary>
    /// Alt+Shift+D: 선택 요소 전부 삭제 (SEL-13). 문서가 여럿이어도 원장 **1항목**이라
    /// 실행취소 1번으로 전부 원래 자리에 돌아온다 (f3).
    /// </summary>
    public void DeleteSelection()
    {
        // 계획을 먼저 완결한다 — 제거하면서 수집하면 앞 요소가 빠질 때마다 뒤 인덱스가 밀려 복원 자리가 어긋난다.
        var plan = SelectionOperations.PlanDelete(_selection.Elements, OwnerOf);
        if (plan.Count == 0)
        {
            return;
        }
        foreach (var entry in plan)
        {
            entry.Document.Remove(entry.Element);
        }
        _ledger.RecordDeleteSelection(
            [.. plan.Select(e => (e.Document, e.Element, e.Index))]);
        _selection.Clear();
        Log.Info($"선택 삭제: 요소 {plan.Count}개");
    }

    /// <summary>요소의 **현재** 소유 문서 (이관 후에도 유효). 어느 문서에도 없으면 null.</summary>
    private AnnotationDocument? OwnerOf(AnnotationElement element) =>
        _surfaces.FirstOrDefault(s => s.Document.Elements.Contains(element))?.Document;

    /// <summary>
    /// 변형 드래그 1회 확정 (SEL-12). 다중 선택·다중 문서가 섞여도 원장 **1항목**이다 (f3).
    /// </summary>
    private void OnCommitTransform(IReadOnlyList<TransformDelta> deltas, (int X, int Y) dropPhysical)
    {
        // 이관 절차는 SelectionTransfer가 소유한다 — 테스트와 프로덕션이 **같은 코드**를 타야
        // 순서(R19)나 억제 스코프(LD-5)를 뒤집었을 때 증인이 빨간불이 된다.
        var surfaces = TransferSurfaces();
        // CRIT-06: 놓은 지점이 어느 모니터에도 걸치지 않으면 (모니터 사이 공백 등) 이관하지 않고 원본을 유지한다.
        var target = SelectionTransfer.ResolveTarget(surfaces, dropPhysical.X, dropPhysical.Y);
        var committed = target is { } to
            ? SelectionTransfer.Execute(deltas, surfaces, to, _selection)
            : deltas;

        _ledger.RecordTransform(committed);
        Log.Info($"변형 확정: 요소 {committed.Count}개, 놓은 지점 ({dropPhysical.X},{dropPhysical.Y})");
    }

    /// <summary>현재 서피스를 창 의존성 없는 이관 후보로 투사한다.</summary>
    private List<TransferSurface> TransferSurfaces() =>
        // 서피스가 실제로 덤는 사각형과 반드시 같아야 한다 (작업 영역) — 어긋나면 모니터 간
        // 선택 이관의 드롭 판정과 좌표 재기준이 틀어진다 (사용자 요청 18차).
        [.. _surfaces.Select(s => new TransferSurface(s.Document, s.Monitor.WorkArea, s.DpiScale))];

    /// <summary>Alt+Shift+S 캡처 세션 (WI-11) — CaptureSessionController에 위임.</summary>
    public void StartCapture() => _capture.StartCapture();

    /// <summary>설정 창 (WI-16). 단일 인스턴스로 열림.</summary>
    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
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

    // ---- z-밴드 (ARCH-5/R10): 캡처 오버레이+액션바 > 툴바 > 서피스 > 핀 > 기타 앱 ----

    private void ApplyZBand()
    {
        var order = new List<nint>();
        if (_capture.OverlayHwnd != 0)
        {
            order.Add(_capture.OverlayHwnd);
        }
        if (_toolbar is not null && _toolbar.Hwnd != 0)
        {
            order.Add(_toolbar.Hwnd);
        }
        order.AddRange(_surfaces.Where(s => s.Hwnd != 0).Select(s => s.Hwnd));
        if (_pins is not null)
        {
            order.AddRange(_pins.Pins.Where(p => p.Hwnd != 0).Select(p => p.Hwnd));
        }
        WindowStyling.ApplyZBand(order);
    }

    // ---- 공유 렌더 틱: 후광 추적(ARCH-3) + 페이드 마감 처리(프리모템 1) ----
    // 상시 구독 금지 (아키텍트 어드바이저리): 후광/페이딩이 필요할 때만 붙이고, 틱에서 스스로 뗀다.

    private void UpdateRenderTickSubscription()
    {
        bool needed = _state.HaloActive || _state.FadingInk || _fadeCore.PendingCount > 0;
        if (needed && !_renderTickAttached)
        {
            System.Windows.Media.CompositionTarget.Rendering += OnRenderTick;
            _renderTickAttached = true;
        }
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_state.HaloActive && !_state.FadingInk && _fadeCore.PendingCount == 0)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRenderTick;
            _renderTickAttached = false;
            return;
        }

        if (_state.HaloActive && NativeMethods.GetCursorPos(out var cursor))
        {
            foreach (var surface in _surfaces)
            {
                surface.UpdateHalo(cursor.X, cursor.Y);
            }
        }

        if (_fadeCore.PendingCount > 0)
        {
            foreach (var element in _fadeCore.Due(DateTime.UtcNow))
            {
                var owner = _surfaces.FirstOrDefault(s => s.Document.Elements.Contains(element));
                if (owner is null)
                {
                    continue;
                }
                owner.AnimateFadeOut(element, TimeSpan.FromMilliseconds(700), () =>
                {
                    owner.Document.Remove(element);
                    _ledger.PurgeElement(element);
                });
            }
        }
    }
}
