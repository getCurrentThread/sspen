using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 핀 고정 윈도우 (WI-13, AC-14..18): 캡처 이미지를 캡처 위치에 최상위로 띄우는 뷰어.
/// 휠=확대/축소, 드래그=이동, Ctrl+휠=투명도, Ctrl+가운데 버튼=클릭 통과 토글, Esc/더블클릭=닫기.
/// 확대/축소의 적용 흐름은 <see cref="PinZoomController"/>가 갖는다 — 물리 픽셀, 한 번의 SetWindowPos, 버스트 병합 (82단계).
/// 복수 핀 허용. 핀 귀속 판서는 Non-Goal 2. z-밴드에서 핀은 툴바 바로 아래, 판서 서피스(보드 포함) 위다 (71단계 사용자 결정) —
/// 판서 모드에서도 통과가 아닌 핀은 자기 영역의 클릭(드래그·휠)을 받고 핀 영역의 잉크는 핀에 가려진다.
/// 통과 핀(Ctrl+가운데 버튼)은 입력을 아래 서피스로 흘린다.
/// </summary>
public sealed class PinWindow : Window, IClickThroughPin
{
    private readonly PinZoomController _zoom;
    private readonly Func<nint> _zAnchor;
    private readonly Func<bool> _controlDown; // Ctrl 판정 (D3) — PinManager가 복귀 훅과 같은 KeyboardState 썽크를 준다 (81단계).
    private System.Windows.Interop.HwndSourceHook? _zHook; // GC 고정 (요청 단계: AnchorBelow)
    private System.Windows.Interop.HwndSourceHook? _zKeepBelowHook; // GC 고정 (결과 단계: KeepBelow, 54단계 L2)
    private double _opacityBeforeClickThrough = 1.0;
    private bool _closing;
    private readonly FrameworkElement _chrome;
    private readonly FrameworkElement _clickThroughBadge;
    private readonly TextBlock _zoomLabel = new()
    {
        Foreground = Brushes.White,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 4, 0),
    };

    public PinWindow(BitmapSource image, PhysicalRect region, Func<nint> zAnchor, Func<bool> controlDown)
    {
        _zAnchor = zAnchor;
        _controlDown = controlDown;
        _zoom = new PinZoomController(
            region,
            windowRect: () => Hwnd == 0 ? null : PhysicalBounds(),
            cursor: () => NativeMethods.GetCursorPos(out var c) ? (c.X, c.Y) : null,
            moveResize: bounds => WindowStyling.MoveResizePhysical(Hwnd, bounds),
            // Input 우선순위 = 디스패처가 "Win32 큐에 입력·게시 메시지가 남아 있으면 기다리는" 대역(Background..Input)의 맨 위다.
            // 그래서 빠르게 굴린 휠 여러 칸이 먼저 모두 처리된 뒤 한 번만 적용된다. Loaded 이상(전경 대역)으로 올리면
            // 게시 메시지로 곧바로 끼어들어 칸마다 적용되어 병합이 깨진다. 그 대역 안에서는 가장 높은 값이라, 입력이 비는 즉시
            // Background 이하의 다른 작업보다 먼저 돈다 (Dispatcher.RequestBackgroundProcessing/IsInputPending — 82단계 소스 판독).
            postCoalesced: apply => Dispatcher.BeginInvoke(DispatcherPriority.Input, apply),
            applied: RefreshChrome);

        Title = "SS Pen Pin";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = region.X;
        Top = region.Y;
        Width = _zoom.BaseWidth;
        Height = _zoom.BaseHeight;

        // 크롬은 **이 창 안의 오버레이**다 — 별도 HWND가 아니다.
        // PinClickThroughMonitor는 GetWindowRect(Hwnd)로 되찾기 히트테스트를 하므로, 팝업 창을 쓰면
        // 눈에는 핀 위인데 복구 사각형 밖인 픽셀 띠가 생긴다. BorderThickness도 1로 유지한다 —
        // 늘리면 이미지가 리플로우되어 PhysicalBounds가 배율 1.0의 기준 크기(_zoom.BaseWidth/BaseHeight)와 어긋난다.
        _chrome = BuildChrome();
        _clickThroughBadge = BuildClickThroughBadge();
        var layers = new Grid();
        layers.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        layers.Children.Add(_clickThroughBadge);
        layers.Children.Add(_chrome);
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Shell.ToolbarTheme.AccentColor),
            BorderThickness = new Thickness(1),
            Child = layers,
        };
        MouseEnter += (_, _) => RefreshChrome();
        MouseLeave += (_, _) => RefreshChrome();
        RefreshChrome();
    }

    /// <summary>
    /// 호버 도구모음: 배율 표시 + 원래 크기 / 클릭 통과 / 닫기.
    ///
    /// 각 버튼은 <b><c>MouseLeftButtonDown</c>에서</b> <c>e.Handled = true</c>로 이벤트를 끊는다.
    /// <see cref="OnMouseLeftButtonDown"/>은 창 자신의 버블 핸들러이고 <c>handledEventsToo</c>로 등록돼
    /// 있지 않으므로, 자식이 처리한 이벤트는 <c>DragMove()</c>와 더블클릭 닫기에 <b>도달하지 않는다</b>.
    /// 이 파일에서 가장 깨지기 쉬운 지점이다 — Up에서 끊으면 이미 드래그가 시작된 뒤라 늦다.
    /// </summary>
    private FrameworkElement BuildChrome()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_zoomLabel);
        row.Children.Add(ChromeButton(Shell.Strings.PinZoomReset, null, ResetZoom));
        row.Children.Add(ChromeButton(
            Shell.Strings.PinClickThrough, Shell.Strings.PinClickThroughHint, () => SetClickThrough(true)));
        row.Children.Add(ChromeButton(Shell.Strings.PinClose, null, ClosePin));

        return new Border
        {
            Background = ChromeBackground,
            CornerRadius = new CornerRadius(0, 0, 0, 4),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = row,
            Visibility = Visibility.Collapsed,
        };
    }

    private UIElement ChromeButton(string text, string? tooltip, Action action)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip is null ? text : $"{text} — {tooltip}",
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11 },
        };
        border.MouseEnter += (_, _) => border.Background = ChromeHover;
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        // Down에서 끊는 것이 계약이다 (BuildChrome 문서 참조).
        border.MouseLeftButtonDown += (_, e) => e.Handled = true;
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return border;
    }

    /// <summary>
    /// 통과 중 <b>상시</b> 표식. 호버 크롬으로는 알릴 수 없다 — 통과 상태에서는 창이 마우스를 받지 못한다.
    /// 예전의 유일한 단서는 Opacity를 0.85로 낮추는 것이었는데, 이미 더 투명하게 해 둔 사용자에게는 아무 변화도 없었다.
    /// </summary>
    private FrameworkElement BuildClickThroughBadge() => new Border
    {
        Background = ChromeBackground,
        CornerRadius = new CornerRadius(0, 0, 4, 0),
        Padding = new Thickness(6, 2, 6, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Visibility = Visibility.Collapsed,
        Child = new TextBlock
        {
            Text = Shell.Strings.PinClickThroughBadge,
            Foreground = Brushes.White,
            FontSize = 11,
        },
    };

    private void RefreshChrome()
    {
        // Width/Height는 WPF가 MoveResizePhysical의 WM_SIZE로 맞춘 값이다 (82단계 실측, PinZoomSmoothnessTests).
        var state = PinChromeRules.Resolve(IsMouseOver, IsClickThrough, _zoom.Scale, Width, Height);
        _chrome.Visibility = state.ShowChrome ? Visibility.Visible : Visibility.Collapsed;
        _clickThroughBadge.Visibility = state.ShowClickThroughBadge ? Visibility.Visible : Visibility.Collapsed;
        _zoomLabel.Text = state.ZoomPercent;
    }

    /// <summary>
    /// 원래 크기(100%)로 되돌린다 — 배율이 얼마인지도, 되돌리는 법도 화면에 없던 기능이다.
    /// 휠과 같은 적용 경로(중심 고정, 물리 기준 크기, 한 번의 SetWindowPos)를 탄다 (82단계).
    /// </summary>
    internal void ResetZoom() => _zoom.Reset();

    private static readonly Brush ChromeBackground =
        Shell.ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0x1F, 0x1F, 0x1F)));

    private static readonly Brush ChromeHover =
        Shell.ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    public nint Hwnd { get; private set; }

    public bool IsClickThrough { get; private set; }

    public event Action<PinWindow>? PinClosed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
        WindowStyling.SetToolWindow(Hwnd, true);
        // 핀은 툴바 아래 밴드에 고정 (71단계 사용자 결정: 툴바 > 핀 > 서피스 — 클릭/드래그로 올라가도 툴바를 덮지 않는다).
        _zHook = WindowStyling.AnchorBelow(Hwnd, _zAnchor);
        _zKeepBelowHook = WindowStyling.KeepBelow(Hwnd, _zAnchor, "핀");
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Hwnd != 0)
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(Hwnd);
            if (_zHook is not null)
            {
                source?.RemoveHook(_zHook);
                _zHook = null;
            }
            if (_zKeepBelowHook is not null)
            {
                source?.RemoveHook(_zKeepBelowHook);
                _zKeepBelowHook = null;
            }
        }
        Hwnd = 0; // 낡은 HWND가 밴드 목록에 남지 않게 (54단계 L5; 툴바·서피스와 같은 규약).
        base.OnClosed(e);
    }

    /// <summary>Ctrl+가운데 버튼 토글 (켜기: 창 내부, 끄기: 전역 훅 경유 — PinClickThroughMonitor).</summary>
    public void SetClickThrough(bool on)
    {
        if (IsClickThrough == on)
        {
            return;
        }
        IsClickThrough = on;
        WindowStyling.SetClickThrough(Hwnd, on);
        // 통과 상태 시각 힌트: 살짝 어둡게 — 끄면 사용자가 정한 투명도로 복원 (AC-16).
        if (on)
        {
            _opacityBeforeClickThrough = Opacity;
            Opacity = PinOpacity.DimForClickThrough(Opacity);
        }
        else
        {
            Opacity = _opacityBeforeClickThrough;
        }
        RefreshChrome();
        ClickThroughChanged?.Invoke(on);
    }

    /// <summary>
    /// 클릭 통과가 켜지거나 꺼졌다. 셸이 되찾는 제스처를 <b>토스트로</b> 알리는 계기다 —
    /// 상시 배지가 상태를 보여 주더라도, 되찾는 방법(Ctrl+가운데 버튼)은 어딘가에서 한 번은 말해 줘야 한다.
    /// </summary>
    public event Action<bool>? ClickThroughChanged;

    /// <summary>물리 픽셀 기준 현재 창 사각형 (전역 훅 히트테스트용).</summary>
    public PhysicalRect PhysicalBounds()
    {
        NativeMethods.GetWindowRect(Hwnd, out var r);
        return PhysicalRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            ClosePin();
            return;
        }
        DragMove();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Ctrl은 주입된 비동기 키 상태로 읽는다 (D3, 81단계 A7-6) — 핀은 포그라운드가 아닐 때가 보통이라
        // 스레드 로컬 Keyboard.Modifiers는 None이 되어 Ctrl+휠이 확대로 새던 결함이다.
        if (_controlDown())
        {
            // Ctrl+휠 = 투명도 — 계단·범위는 PinOpacity가 소유한다.
            Opacity = PinOpacity.Next(Opacity, e.Delta);
        }
        else
        {
            // 휠 = 확대/축소. 커서 아래 지점을 고정해 그림이 커서에서 달아나지 않게 한다 (사용자 요청 15차).
            // 물리 픽셀 계산·병합·한 번의 적용은 PinZoomController가 한다 (82단계) — Left/Top/Width/Height를 여기서 대입하지 않는다.
            _zoom.Wheel(e.Delta);
        }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        // 버튼 먼저, Ctrl은 그다음 — 가운데 버튼이 아니면 키 상태를 읽지도 않는다 (전역 복귀 훅과 같은 주입 소스).
        if (e.ChangedButton == MouseButton.Middle && _controlDown())
        {
            SetClickThrough(!IsClickThrough);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            ClosePin();
            e.Handled = true;
        }
    }

    public void ClosePin()
    {
        if (_closing)
        {
            return; // Esc 연타·더블클릭 중복 방지.
        }
        _closing = true;
        PinClosed?.Invoke(this);
        // 마우스가 핀 위에 있는 채로 HWND를 파괴하면 WPF 입력 계층이 죽은 창을 계속 가리키다
        // 다음 마우스 이동에서 Win32 1400으로 터진다 (WindowLifetime 참조).
        Shell.WindowLifetime.HideThenClose(this);
    }
}
