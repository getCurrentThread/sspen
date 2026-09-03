using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SSPen.Shell;

/// <summary>
/// 툴바 시각 테마 (god file 분할, ARCH-11 후속): 프로즌 브러시·구분선·플라이아웃 어포던스 삼각형·
/// 로고 배지. 색 <b>값</b>은 <see cref="ShellPalette"/>가 소유한다 (대비비를 헤드리스로 잠그기 위해) —
/// 여기 남는 것은 그 색의 프로즌 브러시 인스턴스뿐이다.
/// </summary>
public static class ToolbarTheme
{
    /// <summary>활성 강조색. 로고 배지만 브랜드 색을 쓴다 (<see cref="ShellPalette.Brand"/>).</summary>
    public static readonly Color AccentColor = ShellPalette.Accent;

    public static readonly Brush AccentBrush = Freeze(new SolidColorBrush(AccentColor));
    public static readonly Brush BrandBrush = Freeze(new SolidColorBrush(ShellPalette.Brand));
    public static readonly Brush StripBrush = Freeze(new SolidColorBrush(ShellPalette.Strip));
    public static readonly Brush StripBorderBrush = Freeze(new SolidColorBrush(ShellPalette.StripBorder));
    public static readonly Brush ButtonHoverBrush = Freeze(new SolidColorBrush(ShellPalette.ButtonHover));
    public static readonly Brush ButtonPressedBrush = Freeze(new SolidColorBrush(ShellPalette.ButtonPressed));
    public static readonly Brush IconBrush = Freeze(new SolidColorBrush(ShellPalette.Icon));
    public static readonly Brush SeparatorBrush = Freeze(new SolidColorBrush(ShellPalette.Separator));
    public static readonly Brush SwatchBorderBrush = Freeze(new SolidColorBrush(ShellPalette.SwatchBorder));
    public static readonly Brush TooltipComboBrush = Freeze(new SolidColorBrush(ShellPalette.TooltipCombo));

    public static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static UIElement Separator() => new Border
    {
        Height = 1,
        Background = SeparatorBrush,
        Margin = new Thickness(4, 2, 4, 2),
    };

    /// <summary>우하단 모서리 삼각형 (하위 메뉴 어포던스, Epic Pen 대응) — 단일 소유 팩토리.</summary>
    public static System.Windows.Shapes.Polygon FlyoutMark() => new()
    {
        Points = [new Point(6, 0), new Point(6, 6), new Point(0, 6)],
        Fill = IconBrush,
        Width = 6,
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 2, 2),
        IsHitTestVisible = false,
    };

    public sealed class LogoBadge : Grid
    {
        public LogoBadge()
        {
            // 스트립 밖 투명 배경 위에 띄운 원형 배지 (사용자 조타: 로고 뒤 배경 제거).
            Width = 34;
            Height = 34;
            Margin = new Thickness(0, 0, 0, 2);
            Cursor = Cursors.SizeAll;
            Background = Brushes.Transparent;
            Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 28,
                Height = 28,
                // 로고는 앱 아이콘과 같은 브랜드 색이다 — 강조색이 대비를 위해 어두워져도 따라가지 않는다
                // (WCAG 1.4.3은 로고타입을 면제한다).
                Fill = BrandBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Children.Add(new TextBlock
            {
                Text = "S",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            // 문자열 대신 ToolTip 인스턴스를 쓰는 이유: 닫기 제어가 가능해야 한다
            // (ToolbarWindow가 툴바 숨김 시 모든 툴팁을 닫는다).
            Tooltip = new ToolTip { Content = Strings.AppName };
            ToolTip = Tooltip;
        }

        /// <summary>로고 툴팁 (수명 관리용 공개 참조).</summary>
        public ToolTip Tooltip { get; }
    }
}
