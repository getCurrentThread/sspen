using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 세로형 플로팅 툴바 (스펙 F6/F12 실측 재현): 스트립 너비 34px(테두리 2px + 내부 30px),
/// 버튼 30x30 세로 스택, 로고/드래그 핸들 34x34 원형, 활성 강조색 #FF00ADEF.
/// UX 개선 (Epic Pen 실물 대조, 사용자 조타): 밝은 스트립 + 진한 아이콘(고대비),
/// 플라이아웃 모서리 삼각형 어포던스, 현재 색·굵기 미리보기 원, 펜/형광펜 색 배지,
/// 그룹 구분선, 하단 현재 색 대형 스와치. 아이콘은 Fluent UI System Icons만 사용 (자산 미복사, F21).
/// 플라이아웃(도형/굵기/보드/빠른 색상 확장)은 WPF Popup으로 호스팅 (플랜 ARCH-11 확정).
/// 시각 조립은 <see cref="ToolbarStripBuilder"/>, 플라이아웃 호스팅은 <see cref="ToolbarFlyouts"/>,
/// 버튼↔상태 매핑은 <see cref="ToolbarStateMap"/>가 각각 소유한다 (god file 분할, ARCH-11 후속).
/// 이 클래스는 창 수명·조립·배선만 담당한다.
/// </summary>
public sealed class ToolbarWindow : Window
{
    private readonly AppState _state;
    private readonly IShellActions _actions;
    private readonly ToolbarFlyouts _flyouts;
    private readonly ToolbarParts _parts;
    private bool _menuCollapsed;

    // z-방어 훅. 필드로 붙잡지 않으면 GC가 거두어 방어가 조용히 사라진다.
    private System.Windows.Interop.HwndSourceHook? _topmostHook;

    public ToolbarWindow(AppState state, IShellActions actions)
    {
        _state = state;
        _actions = actions;

        Title = "SS Pen Toolbar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _flyouts = new ToolbarFlyouts(_state, _actions, () => IsMouseOver);
        var (host, logo, parts) = ToolbarStripBuilder.Build(
            _state, _actions, _flyouts,
            onToggleMenuCollapsed: ToggleMenuCollapsed,
            onRotateShapes: RotateShapes,
            onRotatePenGroup: RotatePenGroup,
            onSelectTool: SelectTool,
            onToggleFading: ToggleFading,
            onRotateBoard: RotateBoard);
        _parts = parts;
        logo.MouseLeftButtonDown += (_, _) => DragMove();

        Content = host;

        MouseWheel += (_, e) =>
        {
            if (_menuCollapsed)
            {
                return;
            }
            _state.ActiveTool = ToolbarStateMap.NextToolByWheel(_state.ActiveTool, e.Delta);
            e.Handled = true;
        };

        _state.Changed += () => Dispatcher.Invoke(() =>
        {
            _parts.RefreshActiveStates(_state);
            // 원형 유지: 분할 전 RefreshActiveStates는 보드 플라이아웃 강조도 함께 갱신했다
            // (핫키 Alt+Shift+W/B로 보드가 바뀔 때 열려 있는 플라이아웃 강조 동기화).
            _flyouts.HighlightBoardSelection();
        });
        // 캡처 세션 등으로 툴바가 숨겨지면 Popup은 자체 HWND라 함께 사라지지 않으므로 직접 닫는다
        // (아키텍트 자문: 캡처 결과물/오버레이 위 플라이아웃 잔류 방지).
        _flyouts.RegisterTooltip(logo.Tooltip);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _flyouts.CloseFlyoutsExcept(null);
                // 툴팁도 자체 HWND 팝업이라 함께 사라지지 않는다. 캐프처는 카메라 버튼
                // 클릭(=마우스가 버튼 위)으로 시작해 툴바를 숨기므로 정확히 이 경로다.
                _flyouts.CloseTooltips();
            }
        };
    }

    public nint Hwnd { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
        WindowStyling.SetToolWindow(Hwnd, true);
        // 사용자 보고 18차: 외부 앱이 툴바를 톱모스트 밴드 밖으로 밀어내면 서피스가 그 위를 덤어
        // 버튼이 전부 죽는다. 서피스 쪽 AnchorBelow 훅은 이 방향을 잡지 못하므로 툴바도 자기 방어를 갖는다.
        _topmostHook = WindowStyling.KeepTopmost(Hwnd);
        _parts.RefreshActiveStates(_state);
    }

    /// <summary>눈 버튼: 메뉴 접기/펼치기 토글 (Epic Pen 동작 — 사용자 조타).</summary>
    private void ToggleMenuCollapsed()
    {
        // SizeToContent 재측정이 창 위치를 미묘하게 밀어내는 문제 (사용자 조타):
        // 토글 전 위치를 잡아 두고 레이아웃 완료 후 강제로 복원한다.
        double left = Left;
        double top = Top;
        _menuCollapsed = !_menuCollapsed;
        _parts.SetMenuCollapsed(_menuCollapsed);
        // 사용자 조타: 메뉴 접기와 동시에 판서(페인트된 부분)도 숨기고, 펼치면 다시 보인다.
        _state.SurfacesVisible = !_menuCollapsed;
        _flyouts.CloseFlyoutsExcept(null);
        _parts.RefreshButton(_state, ToolbarButtonId.Visibility);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Left = left;
            Top = top;
        });
    }

    private void SelectTool(ToolKind tool)
    {
        // 같은 도구 재선택 시 해제 (Epic Pen 동작: 도구 없음 = 포인터 모드).
        _state.ActiveTool = _state.ActiveTool == tool ? ToolKind.None : tool;
    }

    private void RotateShapes() => _state.ActiveTool = ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, _state.ActiveTool);

    private void RotatePenGroup() => _state.ActiveTool = ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, _state.ActiveTool);

    /// <summary>
    /// 보드 그룹 버튼 (사용자 요청 15차): 꺼져 있으면 켜기, 켜져 있으면 끄기.
    /// 켜지는 색은 설정값을 따른다 (사용자 요청 17차).
    /// </summary>
    private void RotateBoard()
    {
        _state.Board = AppState.NextBoard(_state.Board, _state.DefaultBoard);
        _flyouts.HighlightBoardSelection();
    }

    /// <summary>
    /// 페이딩 잉크 버튼 (사용자 요청 17차): 토글. 현재 도구를 건드리지 않고
    /// 켜고/끄기만 하므로, 펜·도형 등 쓰던 도구 그대로 조합된다.
    /// 지속 시간은 호버 플라이아웃에서 고른다 (재클릭 순환 폐기 — 토글과 충돌한다).
    /// </summary>
    private void ToggleFading()
    {
        _state.FadingInk = !_state.FadingInk;
        _flyouts.HighlightFadingSelection();
    }
}
