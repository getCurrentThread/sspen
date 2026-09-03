using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 핀 고정 윈도우 (WI-13, AC-14..18): 캡처 이미지를 캡처 위치에 최상위로 띄우는 뷰어.
/// 휠=확대/축소, 드래그=이동, Ctrl+휠=투명도, Ctrl+가운데 버튼=클릭 통과 토글, Esc/더블클릭=닫기.
/// 복수 핀 허용. 핀 귀속 판서는 Non-Goal 2 (잉크는 z-밴드에 따라 핀 위에 렌더링된다).
/// </summary>
public sealed class PinWindow : Window, IClickThroughPin
{
    private readonly double _baseWidth;
    private readonly double _baseHeight;
    private readonly Func<nint> _zAnchor;
    private System.Windows.Interop.HwndSourceHook? _zHook; // GC 고정
    private double _scale = 1.0;
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

    public PinWindow(BitmapSource image, PhysicalRect region, Func<nint> zAnchor)
    {
        _baseWidth = Math.Max(region.Width, 8);
        _baseHeight = Math.Max(region.Height, 8);
        _zAnchor = zAnchor;

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
        Width = _baseWidth;
        Height = _baseHeight;

        // 크롬은 **이 창 안의 오버레이**다 — 별도 HWND가 아니다.
        // PinClickThroughMonitor는 GetWindowRect(Hwnd)로 되찾기 히트테스트를 하므로, 팝업 창을 쓰면
        // 눈에는 핀 위인데 복구 사각형 밖인 픽셀 띠가 생긴다. BorderThickness도 1로 유지한다 —
        // 늘리면 이미지가 리플로우되어 PhysicalBounds가 _baseWidth/_baseHeight와 어긋난다.
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
        var state = PinChromeRules.Resolve(IsMouseOver, IsClickThrough, _scale, Width, Height);
        _chrome.Visibility = state.ShowChrome ? Visibility.Visible : Visibility.Collapsed;
        _clickThroughBadge.Visibility = state.ShowClickThroughBadge ? Visibility.Visible : Visibility.Collapsed;
        _zoomLabel.Text = state.ZoomPercent;
    }

    /// <summary>원래 크기(100%)로 되돌린다 — 배율이 얼마인지도, 되돌리는 법도 화면에 없던 기능이다.</summary>
    private void ResetZoom()
    {
        var zoom = PinZoom.ResetToOriginal(_scale, Left, Top, _baseWidth, _baseHeight);
        _scale = zoom.Scale;
        Left = zoom.Left;
        Top = zoom.Top;
        Width = zoom.Width;
        Height = zoom.Height;
        RefreshChrome();
    }

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
        // 핀은 서피스 아래 밴드에 고정 (F5: 핀 위 판서 보장 — 클릭/드래그로 올라가도 서피스 아래 유지).
        _zHook = WindowStyling.AnchorBelow(Hwnd, _zAnchor);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_zHook is not null && Hwnd != 0)
        {
            System.Windows.Interop.HwndSource.FromHwnd(Hwnd)?.RemoveHook(_zHook);
            _zHook = null;
        }
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
            Opacity = Math.Min(Opacity, 0.85);
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
        return new PhysicalRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+휠 = 투명도 (0.15 ~ 1.0).
            double step = e.Delta > 0 ? 0.05 : -0.05;
            Opacity = Math.Clamp(Opacity + step, 0.15, 1.0);
        }
        else
        {
            // 휠 = 확대/축소. 커서 아래 지점을 고정해 그림이 커서에서 달아나지 않게 한다
            // (사용자 요청 15차). 수학은 PinZoom이 소유한다.
            var cursor = e.GetPosition(this);
            var zoom = PinZoom.ZoomAtCursor(
                _scale, e.Delta, Left, Top, _baseWidth, _baseHeight, cursor.X, cursor.Y);
            _scale = zoom.Scale;
            Left = zoom.Left;
            Top = zoom.Top;
            Width = zoom.Width;
            Height = zoom.Height;
            RefreshChrome();
        }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
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
