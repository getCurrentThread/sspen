using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 현재 도구 상태 한 줄 (AC-20).
///
/// 왜 필요한가: 도구·굵기·색은 전부 툴바 스트립에만 인코딩돼 있다 — 활성 도구는 버튼 배경,
/// 굵기는 점 지름, 색은 스와치. 그래서 툴바를 숨기면(Alt+Shift+0) 지금 무엇이 골라져 있는지
/// 알 방법이 <b>없고</b>, 커서도 그 정보를 담지 않는다. 굵기 증감·빠른 색상 핫키는 그 상태에서도
/// 계속 동작하므로, 사용자는 자기가 무엇을 바꿨는지 모른 채 바꾸게 된다.
///
/// 휠로 도구를 순환시키는 경로도 같은 문제다: 툴바 위에서 무심코 굴린 휠이 도구를 조용히 바꾼다.
/// </summary>
public static class StatusReadout
{
    /// <summary>도구 표시명 — <see cref="Strings"/> 밖에 문자열을 두지 않는다.</summary>
    public static string ToolName(ToolKind tool) => tool switch
    {
        ToolKind.Pen => Strings.Pen,
        ToolKind.Highlighter => Strings.Highlighter,
        ToolKind.Eraser => Strings.Eraser,
        ToolKind.Select => Strings.Select,
        ToolKind.Line => Strings.ShapeLine,
        ToolKind.Arrow => Strings.ShapeArrow,
        ToolKind.Rectangle => Strings.ShapeRectangle,
        ToolKind.Ellipse => Strings.ShapeEllipse,
        ToolKind.Text => Strings.ShapeText,
        ToolKind.Table => Strings.ShapeTable,
        _ => Strings.StatusNoTool,
    };

    /// <summary>굵기 단계의 1-기반 번호 — 열거 순서가 곧 단계다 (ThicknessStep 주석의 5점 계약).</summary>
    public static int StepNumber(ThicknessStep step) => (int)step + 1;

    /// <summary>굵기 단계 총 개수.</summary>
    public static int StepCount => Enum.GetValues<ThicknessStep>().Length;

    /// <summary>
    /// 색 표기는 <see cref="ColorPalette.ToHex"/>에서 알파 두 자리를 뗀 것이다 — 사용자가 읽는 것은
    /// #RRGGBB이고, 16진 변환기를 두 벌 두지 않으려 소유자를 그대로 쓴다.
    /// </summary>
    public static string ColorText(Color color)
    {
        string hex = ColorPalette.ToHex(color);
        return hex.Length == 9 ? "#" + hex[3..] : hex;
    }

    /// <summary>
    /// "펜 · 굵기 3/5 · #E74C3C · 페이딩 잉크 2초" 형태. 도구가 없으면(지우개 해제 등) 굵기·색은
    /// 아무것도 그리지 않으므로 생략한다. 페이딩은 꺼져 있으면 말하지 않는다 (평시 소음 방지).
    /// </summary>
    public static string Line(ToolKind tool, ThicknessStep thickness, Color color, bool fadingOn, double fadingSeconds)
    {
        var parts = new List<string> { ToolName(tool) };
        if (tool != ToolKind.None)
        {
            parts.Add(Strings.StatusThickness(StepNumber(thickness), StepCount));
            parts.Add(ColorText(color));
        }
        if (fadingOn)
        {
            parts.Add($"{Strings.HotkeyFadingInk} {Strings.FadingDuration(fadingSeconds)}");
        }
        return string.Join(" · ", parts);
    }
}
