using System.Windows.Media;

namespace SSPen.Shell;

/// <summary>
/// 셸 색 토큰 + WCAG 대비 계산 (순수).
///
/// <b>왜 브러시가 아니라 색인가</b>: <see cref="Color"/>는 구조체이고 <c>Freezable</c>이 아니라
/// 스레드에 묶이지 않는다. 그래서 이 표는 xUnit 기본 MTA 스레드에서 그대로 읽을 수 있고,
/// 대비비가 회귀하면 스위트가 빨개진다. 브러시 인스턴스는 계속 <see cref="ToolbarTheme"/>이 소유한다
/// (호출 지점 변경 없음).
///
/// <b>고친 것</b>: 기존 강조색 #00ADEF는 흰 글자와의 대비가 2.55:1로 WCAG 1.4.3(4.5:1)에 한참 못 미쳤고,
/// 호버 배경 #E0F4FD는 흰 스트립과 1.13:1이라 사실상 보이지 않았다 — 누를 수 있는 것이 어디까지인지
/// 알 방법이 없었다는 뜻이다. 구분선·테두리도 1.4.11(3:1)에 못 미쳤다.
/// 로고는 브랜드 색을 그대로 쓴다 (1.4.3 로고타입 면제).
/// </summary>
public static class ShellPalette
{
    /// <summary>로고 배지 전용 브랜드 색 — 앱 아이콘과 같은 파랑. 글자를 얹는 용도가 아니다.</summary>
    public static readonly Color Brand = Rgb(0x00, 0xAD, 0xEF);

    /// <summary>강조색: 활성 버튼 배경·선택 링·핸들. 흰 글자를 얹으므로 4.5:1을 넘겨야 한다.</summary>
    public static readonly Color Accent = Rgb(0x00, 0x71, 0xA8);

    public static readonly Color Strip = Colors.White;
    public static readonly Color Icon = Rgb(0x1F, 0x1F, 0x1F);
    public static readonly Color TooltipCombo = Rgb(0x70, 0x70, 0x70);

    /// <summary>호버 배경 — 단독으로는 여전히 옅다. 어포던스는 1px 강조색 외곽선이 담당한다.</summary>
    public static readonly Color ButtonHover = Rgb(0xDC, 0xED, 0xF6);

    /// <summary>눌림 배경: 호버보다 확실히 진해야 '눌렸다'가 보인다.</summary>
    public static readonly Color ButtonPressed = Rgb(0xBB, 0xDC, 0xEC);

    public static readonly Color Separator = Rgb(0x94, 0x94, 0x94);
    public static readonly Color StripBorder = Rgb(0x8A, 0x8A, 0x8A);
    public static readonly Color SwatchBorder = Rgb(0x8A, 0x8A, 0x8A);

    /// <summary>WCAG 2.x 상대 휘도.</summary>
    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte raw)
        {
            double c = raw / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>WCAG 2.x 대비비 (1.0~21.0). 인자 순서는 결과에 영향이 없다.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
