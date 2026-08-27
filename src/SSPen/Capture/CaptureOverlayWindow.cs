using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Interop;
using SSPen.Shell;

namespace SSPen.Capture;

/// <summary>캡처 세션 결과 액션.</summary>
public enum CaptureAction
{
    Copy,
    Save,
    Pin,
    Cancel,
}

/// <summary>
/// 캡처 오버레이 (WI-11): 고정된 스냅샷을 가상 스크린 전체 크기 단일 창에 띄우고
/// 드래그로 영역을 고른다. 배경 딤 처리는 사용자 요청으로 제거 — 선택 영역은
/// 강조색 테두리 + 크기 표시로만 구분한다. 선택 확정 시 복사/저장/핀 고정/취소 도구모음.
/// 단일 가상스크린 창이므로 선택 수학은 하나의 좌표 공간에서 끝난다 (-1920 이음새 무관).
/// </summary>
public sealed class CaptureOverlayWindow : Window
{
    private readonly PhysicalRect _virtualScreen;
    private readonly Action<CaptureAction, PhysicalRect> _onComplete;
    private readonly Canvas _canvas;
    private readonly System.Windows.Shapes.Rectangle _selectionRect;
    private readonly TextBlock _sizeReadout;
    private readonly Border _actionBar;
    private Point _dragStart;
    private bool _dragging;
    private bool _committed;
    private Rect _selection = Rect.Empty;

    public CaptureOverlayWindow(
        BitmapSource snapshot,
        PhysicalRect virtualScreen,
        Action<CaptureAction, PhysicalRect> onComplete)
    {
        _virtualScreen = virtualScreen;
        _onComplete = onComplete;

        Title = "SS Pen Capture Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;

        Left = virtualScreen.X;
        Top = virtualScreen.Y;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;

        var image = new Image
        {
            Source = snapshot,
            Stretch = Stretch.Fill,
            Width = virtualScreen.Width,
            Height = virtualScreen.Height,
        };

        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(ToolbarTheme.AccentColor),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _sizeReadout = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 12,
            Visibility = Visibility.Collapsed,
        };

        _actionBar = BuildActionBar();
        _actionBar.Visibility = Visibility.Collapsed;

        _canvas = new Canvas { Width = virtualScreen.Width, Height = virtualScreen.Height };
        _canvas.Children.Add(image);
        _canvas.Children.Add(_selectionRect);
        _canvas.Children.Add(_sizeReadout);
        _canvas.Children.Add(_actionBar);
        Content = _canvas;
    }

    public nint Hwnd { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
        WindowStyling.SetToolWindow(Hwnd, true);
        WindowStyling.PlacePhysical(Hwnd, _virtualScreen);
        Activate();
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Complete(CaptureAction.Cancel);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_actionBar.Visibility == Visibility.Visible)
        {
            // 도구모음 안을 눌렀으면 그 버튼의 핸들러가 처리한다 (여기선 손대지 않는다).
            if (IsInsideActionBar(e.OriginalSource as DependencyObject))
            {
                return;
            }
            // 그 밖 아무 곳이나 누르면 기본 동작 = 핀 고정 (사용자 요청 15차).
            // 선택 영역 안을 눌러도, 밖을 눌러도 동일하게 핀으로 끝난다.
            e.Handled = true;
            Complete(CaptureAction.Pin);
            return;
        }
        _dragStart = e.GetPosition(_canvas);
        _dragging = true;
        _committed = false;
        _selection = Rect.Empty;
        UpdateSelectionVisual();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }
        var current = e.GetPosition(_canvas);
        _selection = new Rect(_dragStart, current);
        UpdateSelectionVisual();
        UpdateReadout(current);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        ReleaseMouseCapture();
        _sizeReadout.Visibility = Visibility.Collapsed;

        if (_selection.Width < 4 || _selection.Height < 4)
        {
            _selection = Rect.Empty;
            UpdateSelectionVisual();
            return; // 너무 작은 드래그는 무시하고 다시 선택.
        }
        ShowActionBar();
    }

    private void UpdateSelectionVisual()
    {
        if (_selection.IsEmpty)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(_selectionRect, _selection.X);
        Canvas.SetTop(_selectionRect, _selection.Y);
        _selectionRect.Width = _selection.Width;
        _selectionRect.Height = _selection.Height;
        _selectionRect.Visibility = Visibility.Visible;
    }

    private void UpdateReadout(Point cursor)
    {
        _sizeReadout.Text = $"{(int)_selection.Width} x {(int)_selection.Height}";
        _sizeReadout.Visibility = Visibility.Visible;
        Canvas.SetLeft(_sizeReadout, Math.Min(cursor.X + 16, _virtualScreen.Width - 90));
        Canvas.SetTop(_sizeReadout, Math.Clamp(cursor.Y + 16, 0, _virtualScreen.Height - 24));
    }

    private Border BuildActionBar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(ActionButton(Strings.CaptureCopy, CaptureAction.Copy));
        panel.Children.Add(ActionButton(Strings.CaptureSave, CaptureAction.Save));
        panel.Children.Add(ActionButton(Strings.CapturePin, CaptureAction.Pin));
        panel.Children.Add(ActionButton(Strings.CaptureCancel, CaptureAction.Cancel));
        return new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2B2B2B")),
            BorderBrush = new SolidColorBrush(ToolbarTheme.AccentColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = panel,
        };
    }

    private UIElement ActionButton(string label, CaptureAction action)
    {
        var button = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 13 },
        };
        button.MouseEnter += (_, _) => button.Background =
            new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Complete(action);
        };
        return button;
    }

    private void ShowActionBar()
    {
        double x = Math.Clamp(_selection.Right - 240, 0, _virtualScreen.Width - 250);
        double y = _selection.Bottom + 8;
        if (y > _virtualScreen.Height - 44)
        {
            y = Math.Max(_selection.Top - 44, 0);
        }
        Canvas.SetLeft(_actionBar, x);
        Canvas.SetTop(_actionBar, y);
        _actionBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 클릭 지점이 도구모음 안인가 (시각 트리 조상 탐색).
    /// 기본 핀 동작이 복사/저장/취소 버튼 클릭까지 삼키면 그 버튼들을 누를 수 없게 된다.
    /// </summary>
    private bool IsInsideActionBar(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _actionBar))
            {
                return true;
            }
        }
        return false;
    }

    private void Complete(CaptureAction action)
    {
        // 핀 기본 동작과 버튼 클릭이 겹쳐 두 번 들어오면 핀이 중복 생성된다.
        if (_committed)
        {
            return;
        }
        _committed = true;

        // 캔버스/이미지는 스냅샷 물리 픽셀 크기로 잡혀 있으므로 캔버스 단위 == 스냅샷 픽셀이다.
        // 따라서 변환은 항등 + 가상 스크린 원점 보정만 수행한다 (아키텍트 2세대 권고:
        // dpi 곱셈은 오히려 시각적 선택과 어긋난다. 혼합 DPI는 이연 목록 4번/Non-Goal).
        var region = _selection.IsEmpty
            ? new PhysicalRect(0, 0, 0, 0)
            : new PhysicalRect(
                _virtualScreen.X + (int)Math.Round(_selection.X),
                _virtualScreen.Y + (int)Math.Round(_selection.Y),
                (int)Math.Round(_selection.Width),
                (int)Math.Round(_selection.Height));
        _onComplete(action, region);
    }
}
