using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 토스트 창 — 앱 전체에서 <b>하나</b>만 만들어 시작 시점에 띄워 두고 가시성만 토글한다.
///
/// 왜 하나인가 (z-밴드 계약): HWND가 시동 때 한 번만 생기면 <c>ZBandOrder</c>에 항구 멤버로 들어가고,
/// 이후 어떤 단계도 <c>ApplyZBand</c> <b>호출 지점을 새로 만들 필요가 없다</b> (AGENTS L14: 렌더 틱에서 부르는 것은 위반).
/// 요청마다 창을 만들면 생성 시점마다 재적용이 필요해지고, 그 호출 지점이 곧 규약 위반의 온상이 된다.
///
/// 왜 기본이 클릭 통과인가: 토스트는 툴바보다 <b>위</b>에 있다. 클릭을 삼킬 수 있다면
/// "보이는데 눌리지 않는 툴바"(AGENTS L14가 경고하는 바로 그 증상)를 스스로 만들 수 있다.
/// <c>WS_EX_TRANSPARENT</c>가 기본이므로 그 실패는 표현 자체가 불가능하고,
/// 액션(예: "폴더 열기")이 달린 토스트만 그 수명 동안 통과를 해제한다 —
/// 그때도 <c>WS_EX_NOACTIVATE</c>는 유지되어 포커스는 옮겨 가지 않는다.
/// </summary>
public sealed class ToastWindow : Window
{
    // 대비: 진한 배경 + 흰 글자 (임의 데스크톱 위에 떠야 하므로 스트립 팔레트에 기대지 않는다).
    private static readonly Brush InfoBackground = ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x1F, 0x1F, 0x1F)));
    private static readonly Brush WarningBackground = ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x8A, 0x53, 0x00)));
    private static readonly Brush ErrorBackground = ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x9B, 0x1C, 0x1C)));
    private static readonly Brush CardBorder = ToolbarTheme.Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    private readonly Border _card;
    private readonly TextBlock _text;
    private readonly Border _action;
    private readonly TextBlock _actionText;

    public ToastWindow()
    {
        Title = "SS Pen Toast";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Visibility = Visibility.Hidden;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _actionText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _action = new Border
        {
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(12, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = _actionText,
            Visibility = Visibility.Collapsed,
        };
        _action.MouseLeftButtonUp += (_, _) => ActionInvoked?.Invoke();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_text);
        row.Children.Add(_action);

        _card = new Border
        {
            Background = InfoBackground,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Child = row,
        };
        Content = _card;
    }

    /// <summary>액션 라벨을 눌렀다 (예: "폴더 열기"). 액션이 없는 토스트에서는 발생하지 않는다.</summary>
    public event Action? ActionInvoked;

    public nint Hwnd { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
        WindowStyling.SetToolWindow(Hwnd, true);
        WindowStyling.SetNoActivate(Hwnd, true);
        WindowStyling.SetClickThrough(Hwnd, true); // 기본은 통과 — 위 문서의 z-밴드 계약.
    }

    /// <summary>한 틱의 판정을 화면에 바른다. 배치는 호스트가 <see cref="WindowStyling.PlacePhysical"/>로 따로 한다.</summary>
    public void Render(ToastStep step)
    {
        _text.Text = step.Text;
        _card.Background = step.Kind switch
        {
            ToastKind.Error => ErrorBackground,
            ToastKind.Warning => WarningBackground,
            _ => InfoBackground,
        };
        _actionText.Text = step.ActionLabel ?? string.Empty;
        _action.Visibility = step.Interactive ? Visibility.Visible : Visibility.Collapsed;
        if (Hwnd != 0)
        {
            // 액션이 있는 동안만 클릭을 받는다 — 그 외에는 언제나 통과 상태로 되돌린다.
            WindowStyling.SetClickThrough(Hwnd, !step.Interactive);
        }
    }

    /// <summary>현재 콘텐츠의 물리 픽셀 크기 (배치 산술 입력). 아직 측정 전이면 0을 돌려준다.</summary>
    public (int Width, int Height) PhysicalSize()
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = _card.DesiredSize;
        return ((int)Math.Ceiling(desired.Width * dpi), (int)Math.Ceiling(desired.Height * dpi));
    }

    /// <summary>배치 여백을 이 창의 DPI로 물리 픽셀 환산한다 (환산은 언제나 이 경계에서만).</summary>
    public int PhysicalBottomMargin() =>
        (int)Math.Round(ToastPlacement.BottomMarginDip * VisualTreeHelper.GetDpi(this).DpiScaleY);
}
