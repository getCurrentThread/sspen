using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SSPen.Shell;

/// <summary>
/// 툴바 시각 테마 (god file 분할, ARCH-11 후속): 프로즌 브러시·구분선·플라이아웃 어포던스 삼각형·
/// 로고 배지. 스펙 F6/F12 실측 재현 색상 (밝은 스트립 + 진한 아이콘, 활성 강조색 #FF00ADEF).
/// </summary>
public static class ToolbarTheme
{
    public static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#FF00ADEF");

    public static readonly Brush AccentBrush = Freeze(new SolidColorBrush(AccentColor));
    public static readonly Brush StripBrush = Freeze(new SolidColorBrush(Colors.White));
    public static readonly Brush StripBorderBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCFCFCF")));
    public static readonly Brush ButtonHoverBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0F4FD")));
    public static readonly Brush IconBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1F1F1F")));
    public static readonly Brush SeparatorBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE4E4E4")));
    public static readonly Brush SwatchBorderBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB8B8B8")));
    public static readonly Brush TooltipComboBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF707070")));

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
                Fill = AccentBrush,
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
