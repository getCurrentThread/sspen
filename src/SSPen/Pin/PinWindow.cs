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
public sealed class PinWindow : Window
{
    private readonly double _baseWidth;
    private readonly double _baseHeight;
    private readonly Func<nint> _zAnchor;
    private System.Windows.Interop.HwndSourceHook? _zHook; // GC 고정
    private double _scale = 1.0;
    private double _opacityBeforeClickThrough = 1.0;
    private bool _closing;

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

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Shell.ToolbarTheme.AccentColor),
            BorderThickness = new Thickness(1),
            Child = new Image { Source = image, Stretch = Stretch.Fill },
        };
    }

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
    }

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
