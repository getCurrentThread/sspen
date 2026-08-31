using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 플라이아웃(Popup) 호스팅 (ARCH-11 확정, god file 분할 후속): 도형/펜/굵기/페이딩/보드/팔레트
/// 플라이아웃과 포인터 감시 타이머를 소유한다. 소유자(ToolbarWindow)가 이 인스턴스와 함께
/// 타이머 수명을 갖는다 — Dispose 없이 창 종료 시 GC로 정리된다 (기존 동작 유지).
/// </summary>
public sealed class ToolbarFlyouts
{
    private readonly AppState _state;
    private readonly IShellActions _actions;

    public readonly Popup ShapesFlyout;
    public readonly Popup ThicknessFlyout;
    public readonly Popup PaletteFlyout;
    public readonly Popup BoardFlyout;
    public readonly Popup FadingFlyout;
    public readonly Popup PenFlyout;

    // 툴팁도 플라이아웃처럼 **자체 HWND를 가진 팝업**이라 소유 창을 숨겼다고 함께 사라지지 않는다.
    // 캐프처 세션은 카메라 버튼 클릭(=마우스가 그 버튼 위, 툴팁이 열려 있을 수 있는 상태)으로
    // 시작해 툴바를 숨기므로, 닫지 않으면 캘처 결과물 위에 툴팁이 떠 있거나 죽은 배치 대상을
    // 가리키게 된다. 플라이아웃과 동일한 이유로 같은 곳에서 수명을 관리한다.
    private readonly List<ToolTip> _tooltips = [];

    private readonly List<(TextBlock Label, double Seconds)> _fadingItems = [];
    private readonly List<(Border Item, Border Swatch, TextBlock Label, BoardMode Mode)> _boardItems = [];
    private readonly List<(System.Windows.Shapes.Ellipse Dot, ThicknessStep Step)> _thicknessItems = [];

    // 포인터 감시: StaysOpen=true 플라이아웃은 밖 클릭으로 안 닫히므로,
    // 포인터가 툴바/플라이아웃을 벗어나면 잠시 후 닫는다 (Epic Pen 감각).
    private readonly DispatcherTimer _flyoutWatch;
    private int _flyoutAwayTicks;

    /// <summary>포인터가 툴바 위에 있는지 (FlyoutWatchTick이 참조하는 소유 창의 IsMouseOver).</summary>
    private readonly Func<bool> _ownerIsMouseOver;

    public ToolbarFlyouts(AppState state, IShellActions actions, Func<bool> ownerIsMouseOver)
    {
        _state = state;
        _actions = actions;
        _ownerIsMouseOver = ownerIsMouseOver;

        // 플라이아웃은 각자의 버튼 옆에서 열린다 (사용자 조타: 오른쪽 상단 무질서 해소 —
        // PlacementTarget을 소유 버튼으로 지정, Epic Pen처럼 버튼 행 옆에 세트로 보이게).
        ShapesFlyout = NewFlyout();
        ThicknessFlyout = NewFlyout();
        PaletteFlyout = NewFlyout();
        BoardFlyout = NewFlyout();
        FadingFlyout = NewFlyout();
        PenFlyout = NewFlyout();

        _flyoutWatch = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(150) };
        _flyoutWatch.Tick += (_, _) => FlyoutWatchTick();
    }

    public Popup[] AllFlyouts => [ShapesFlyout, ThicknessFlyout, PaletteFlyout, BoardFlyout, FadingFlyout, PenFlyout];

    /// <summary>생성된 툴팁을 수명 관리 대상으로 등록한다 (<see cref="ToolbarStripBuilder.AttachTooltip"/>가 호출).</summary>
    internal void RegisterTooltip(ToolTip tooltip) => _tooltips.Add(tooltip);

    /// <summary>열려 있는 툴팁을 전부 닫는다 (툴바 숨김 시).</summary>
    public void CloseTooltips()
    {
        foreach (var tooltip in _tooltips)
        {
            tooltip.IsOpen = false;
        }
    }

    /// <summary>플라이아웃 자식 트리(도형/펜/굵기/페이딩/보드/팔레트)를 빌드한다. Build() 조립 순서상 버튼 이후 호출.</summary>
    public void BuildAllFlyouts()
    {
        BuildShapesFlyout();
        BuildThicknessFlyout();
        BuildPaletteFlyout();
        BuildBoardFlyout();
        BuildFadingFlyout();
        BuildPenFlyout();
    }

    // StaysOpen=true 필수 (사용자 조타: 빠릿한 호버 전환): false면 Popup이 마우스를
    // 캡처해 다른 버튼의 MouseEnter가 안 와서 호버로 서브메뉴가 안 바뀐다.
    // 닫기는 FlyoutWatchTick 포인터 감시가 대신 담당.
    private static Popup NewFlyout() => new()
    {
        AllowsTransparency = true,
        Placement = PlacementMode.Right,
        StaysOpen = true,
        HorizontalOffset = 8,
        VerticalOffset = -4,
    };

    /// <summary>하나만 열리게: 다른 플라이아웃을 닫고 지정 플라이아웃을 호버로 연다 (Epic Pen 호버 전개).</summary>
    public void HoverOpen(UIElement trigger, Popup flyout)
    {
        trigger.MouseEnter += (_, _) => OpenFlyout(flyout);
    }

    public void CloseFlyoutsExcept(Popup? keep)
    {
        foreach (var popup in AllFlyouts)
        {
            if (!ReferenceEquals(popup, keep))
            {
                popup.IsOpen = false;
            }
        }
        if (keep is null)
        {
            _flyoutWatch.Stop();
        }
    }

    /// <summary>다른 플라이아웃을 즉시 닫고 지정 플라이아웃을 연다; 포인터 감시 시작.</summary>
    public void OpenFlyout(Popup flyout)
    {
        CloseFlyoutsExcept(flyout);
        flyout.IsOpen = true;
        _flyoutAwayTicks = 0;
        _flyoutWatch.Start();
    }

    /// <summary>포인터가 툴바도 열린 플라이아웃도 아닌 상태가 2틱(≈300ms) 지속되면 모두 닫는다.</summary>
    public void FlyoutWatchTick()
    {
        bool anyOpen = false;
        bool over = _ownerIsMouseOver();
        foreach (var popup in AllFlyouts)
        {
            if (popup.IsOpen)
            {
                anyOpen = true;
                if (popup.Child is { IsMouseOver: true })
                {
                    over = true;
                }
            }
        }
        if (!anyOpen)
        {
            _flyoutWatch.Stop();
            return;
        }
        if (over)
        {
            _flyoutAwayTicks = 0;
            return;
        }
        if (++_flyoutAwayTicks >= 2)
        {
            CloseFlyoutsExcept(null);
        }
    }

    public void ToggleThicknessFlyout()
    {
        if (ThicknessFlyout.IsOpen) { CloseFlyoutsExcept(null); } else { OpenFlyout(ThicknessFlyout); }
    }

    private void BuildShapesFlyout()
    {
        // 텍스트는 펜 그룹으로 이동 (사용자 조타) — 도형은 선/화살표/사각형/타원 4종.
        var panel = FlyoutPanel();
        panel.Children.Add(FlyoutItem(Strings.ShapeLine, Icons.Line, () => SelectTool(ToolKind.Line), "line"));
        panel.Children.Add(FlyoutItem(Strings.ShapeArrow, Icons.ArrowUpRight, () => SelectTool(ToolKind.Arrow), "arrow"));
        panel.Children.Add(FlyoutItem(Strings.ShapeRectangle, Icons.Square, () => SelectTool(ToolKind.Rectangle), "rectangle"));
        panel.Children.Add(FlyoutItem(Strings.ShapeEllipse, Icons.Circle, () => SelectTool(ToolKind.Ellipse), "ellipse"));
        ShapesFlyout.Child = FlyoutBorder(panel);
    }

    private void BuildPenFlyout()
    {
        // 펜 그룹 (사용자 조타): 펜/형광펜/텍스트 — Epic Pen의 펜+A 하위 목록 대응.
        var panel = FlyoutPanel();
        panel.Children.Add(FlyoutItem(Strings.Pen, Icons.Pen, () => SelectTool(ToolKind.Pen), "pen"));
        panel.Children.Add(FlyoutItem(Strings.Highlighter, Icons.Highlight, () => SelectTool(ToolKind.Highlighter), "highlighter"));
        panel.Children.Add(FlyoutItem(Strings.ShapeText, Icons.TextT, () => SelectTool(ToolKind.Text), "text"));
        PenFlyout.Child = FlyoutBorder(panel);
    }

    private void SelectTool(ToolKind tool)
    {
        // 같은 도구 재선택 시 해제 (Epic Pen 동작: 도구 없음 = 포인터 모드).
        _state.ActiveTool = _state.ActiveTool == tool ? ToolKind.None : tool;
    }

    private void BuildThicknessFlyout()
    {
        // Epic Pen 크기 선택기 대응 (사용자 조타: 5단계): 실제 크기의 채워진 원 5개, 라벨 없음.
        var panel = FlyoutPanel();
        _thicknessItems.Clear();
        panel.Children.Add(ThicknessItem(6, ThicknessStep.XSmall));
        panel.Children.Add(ThicknessItem(10, ThicknessStep.Small));
        panel.Children.Add(ThicknessItem(14, ThicknessStep.Medium));
        panel.Children.Add(ThicknessItem(18, ThicknessStep.Large));
        panel.Children.Add(ThicknessItem(22, ThicknessStep.XLarge));
        var border = FlyoutBorder(panel);
        border.MouseWheel += (_, e) =>
        {
            int direction = e.Delta > 0 ? 1 : -1;
            _state.StepThickness(direction);
            HighlightThicknessSelection();
            e.Handled = true;
        };
        ThicknessFlyout.Child = border;
        // 열 때마다 활성 그룹의 현재 단계 강조.
        ThicknessFlyout.Opened += (_, _) => HighlightThicknessSelection();
    }

    /// <summary>굵기 플라이아웃의 현재 단계 강조 갱신.</summary>
    public void HighlightThicknessSelection()
    {
        foreach (var (dot, step) in _thicknessItems)
        {
            dot.Fill = step == _state.Thickness ? ToolbarTheme.AccentBrush : ToolbarTheme.IconBrush;
        }
    }

    private Border ThicknessItem(double diameter, ThicknessStep step)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = ToolbarTheme.IconBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _thicknessItems.Add((dot, step));
        var dotHost = new Grid { Width = 26, Height = 26 };
        dotHost.Children.Add(dot);
        var item = new Border { Background = Brushes.Transparent, Child = dotHost, Padding = new Thickness(2) };
        item.MouseEnter += (_, _) => item.Background = ToolbarTheme.ButtonHoverBrush;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        item.MouseLeftButtonUp += (_, _) =>
        {
            _state.Thickness = step;
            ThicknessFlyout.IsOpen = false;
        };
        return item;
    }

    private void BuildFadingFlyout()
    {
        var panel = FlyoutPanel();
        _fadingItems.Clear();
        // 사다리는 FadingDurations가 단독 소유한다 — 여기서 숫자를 재열거하면
        // 버튼 로테이션과 설정 콤보가 서로 다른 목록을 가질 수 있다.
        foreach (double seconds in FadingDurations.Steps)
        {
            panel.Children.Add(FadingItem(seconds));
        }
        var border = FlyoutBorder(panel);
        border.MouseWheel += (_, e) =>
        {
            double nextSec = FadingDurations.StepByWheel(_actions.FadingSeconds, e.Delta);
            _actions.SetFadingDuration(nextSec);
            HighlightFadingSelection();
            e.Handled = true;
        };
        FadingFlyout.Child = border;
        // 열 때마다 현재 지속 시간 강조 (로테이션 시에도 재사용).
        FadingFlyout.Opened += (_, _) => HighlightFadingSelection();
    }

    /// <summary>페이딩 플라이아웃의 현재 지속 시간 강조 갱신.</summary>
    public void HighlightFadingSelection()
    {
        foreach (var (label, seconds) in _fadingItems)
        {
            bool selected = FadingDurations.Same(seconds, _actions.FadingSeconds);
            label.Foreground = selected ? ToolbarTheme.AccentBrush : ToolbarTheme.IconBrush;
            label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private Border FadingItem(double seconds)
    {
        // 표기가 "2초"처럼 짧아져 두 줄 분할이 필요 없다 (이전 "짧게 (3초)" 형식 폐기).
        var text = new TextBlock
        {
            Text = Strings.FadingDuration(seconds),
            Foreground = ToolbarTheme.IconBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6, 4, 6, 4),
        };
        _fadingItems.Add((text, seconds));
        var item = new Border { Background = Brushes.Transparent, Child = text, Padding = new Thickness(2) };
        item.MouseEnter += (_, _) => item.Background = ToolbarTheme.ButtonHoverBrush;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        item.MouseLeftButtonUp += (_, _) =>
        {
            _actions.SetFadingDuration(seconds);
            FadingFlyout.IsOpen = false;
        };
        return item;
    }

    private void BuildBoardFlyout()
    {
        var panel = FlyoutPanel();
        _boardItems.Clear();
        panel.Children.Add(BoardItem(Strings.Whiteboard, BoardMode.White, "whiteboard"));
        panel.Children.Add(BoardItem(Strings.Blackboard, BoardMode.Black, "blackboard"));
        BoardFlyout.Child = FlyoutBorder(panel);
        // 열 때마다 현재 보드 강조 (페이딩 플라이아웃과 동일 문법).
        BoardFlyout.Opened += (_, _) => HighlightBoardSelection();
    }

    /// <summary>보드 항목 (사용자 조타 14차): 동일 글리프 대신 실제 보드 색 스와치로 화이트/블랙을 즉시 구분.</summary>
    private Border BoardItem(string label, BoardMode mode, string hotkeyId)
    {
        var swatch = new Border
        {
            Width = 18,
            Height = 13,
            CornerRadius = new CornerRadius(2),
            Background = mode == BoardMode.White ? Brushes.White : Brushes.Black,
            BorderBrush = ToolbarTheme.SwatchBorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 2),
        };
        var text = new TextBlock
        {
            Text = label,
            Foreground = ToolbarTheme.IconBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 2, 4, 2) };
        stack.Children.Add(swatch);
        stack.Children.Add(text);
        var item = new Border { Background = Brushes.Transparent, Child = stack, Padding = new Thickness(2) };
        ToolbarStripBuilder.AttachTooltip(_actions, item, label, hotkeyId, this);
        _boardItems.Add((item, swatch, text, mode));
        item.MouseEnter += (_, _) => item.Background = ToolbarTheme.ButtonHoverBrush;
        item.MouseLeave += (_, _) => HighlightBoardSelection();
        item.MouseLeftButtonUp += (_, _) =>
        {
            _state.ToggleBoard(mode);
            CloseFlyoutsExcept(null);
        };
        return item;
    }

    /// <summary>보드 플라이아웃의 현재 보드 강조 갱신 + 보드 버튼 스와치 배지.</summary>
    public void HighlightBoardSelection()
    {
        foreach (var (item, swatch, label, mode) in _boardItems)
        {
            bool selected = _state.Board == mode;
            item.Background = selected ? ToolbarTheme.AccentBrush : Brushes.Transparent;
            label.Foreground = selected ? Brushes.White : ToolbarTheme.IconBrush;
            swatch.BorderBrush = selected ? Brushes.White : ToolbarTheme.SwatchBorderBrush;
        }
    }

    private void BuildPaletteFlyout()
    {
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(4) };
        // 확장 팔레트는 ColorPalette가 단독 소유한다 (설정 창 바로가기 색상 선택기와 공유).
        foreach (var color in ColorPalette.Extended)
        {
            var swatch = new Border
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(1),
                Background = ToolbarTheme.Freeze(new SolidColorBrush(color)),
                BorderBrush = ToolbarTheme.SwatchBorderBrush,
                BorderThickness = new Thickness(1),
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _state.CurrentColor = color;
                PaletteFlyout.IsOpen = false;
            };
            grid.Children.Add(swatch);
        }
        PaletteFlyout.Child = FlyoutBorder(grid);
    }

    private static StackPanel FlyoutPanel() => new() { Orientation = Orientation.Horizontal };

    private Border FlyoutItem(string label, (string Regular, string Filled)? icon, Action onClick, string? hotkeyId = null)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 2, 4, 2) };
        if (icon is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = icon.Value.Regular,
                FontFamily = Icons.Regular,
                FontSize = 18,
                Foreground = ToolbarTheme.IconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ToolbarTheme.IconBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var item = new Border { Background = Brushes.Transparent, Child = stack, Padding = new Thickness(2) };
        ToolbarStripBuilder.AttachTooltip(_actions, item, label, hotkeyId, this);
        item.MouseEnter += (_, _) => item.Background = ToolbarTheme.ButtonHoverBrush;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        item.MouseLeftButtonUp += (_, _) =>
        {
            onClick();
            CloseFlyoutsExcept(null);
        };
        return item;
    }

    private static UIElement FlyoutBorder(UIElement child)
    {
        // 그림자 여백을 확보한 라운드 카드 (Epic Pen 하위 목록의 떠 있는 세트 느낌).
        var card = new Border
        {
            Background = ToolbarTheme.StripBrush,
            BorderBrush = ToolbarTheme.StripBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 4, 12, 12),
            Child = child,
            Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Direction = 315,
                Opacity = 0.35,
                Color = Colors.Black,
            },
        };
        return new Grid { Children = { card } };
    }
}
