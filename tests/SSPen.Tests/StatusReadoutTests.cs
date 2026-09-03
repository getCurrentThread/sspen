using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="StatusReadout"/>의 증인 (AC-20). 리드아웃은 툴바를 숨겼거나 휠로 조용히 바뀌었을 때
/// 도구 상태를 확인할 <b>유일한</b> 경로이므로, 문구가 비거나 도구 이름이 빠지면 그 경로가 사라진다.
/// </summary>
public class StatusReadoutTests
{
    private static readonly Color Red = Color.FromRgb(0xE7, 0x4C, 0x3C);

    /// <summary>새 도구를 열거에 추가하고 여기를 잊으면 리드아웃이 "도구 없음"으로 거짓말을 한다.</summary>
    [Fact]
    public void ToolName_EveryRealTool_HasItsOwnName()
    {
        var named = Enum.GetValues<ToolKind>()
            .Where(t => t != ToolKind.None)
            .Select(StatusReadout.ToolName)
            .ToList();

        Assert.All(named, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.DoesNotContain(Strings.StatusNoTool, named);
        Assert.Equal(named.Count, named.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ToolName_None_SaysSo() =>
        Assert.Equal(Strings.StatusNoTool, StatusReadout.ToolName(ToolKind.None));

    /// <summary>단계 번호는 1-기반이고 총수는 열거에서 온다 — 굵기 사다리가 늘면 표기가 따라 늘어난다.</summary>
    [Theory]
    [InlineData(ThicknessStep.XSmall, 1)]
    [InlineData(ThicknessStep.Medium, 3)]
    [InlineData(ThicknessStep.XLarge, 5)]
    public void StepNumber_IsOneBased(ThicknessStep step, int expected)
    {
        Assert.Equal(expected, StatusReadout.StepNumber(step));
        Assert.Equal(5, StatusReadout.StepCount);
    }

    /// <summary>사용자가 읽는 것은 #RRGGBB다 — ARGB 8자리는 알파 두 자리가 색처럼 보인다.</summary>
    [Fact]
    public void ColorText_DropsTheAlphaPair()
    {
        Assert.Equal("#E74C3C", StatusReadout.ColorText(Red));
        Assert.Equal("#FFFFFF", StatusReadout.ColorText(Colors.White));
    }

    [Fact]
    public void Line_Pen_NamesToolThicknessAndColor()
    {
        string line = StatusReadout.Line(ToolKind.Pen, ThicknessStep.Medium, Red, fadingOn: false, fadingSeconds: 2.0);

        Assert.Equal($"{Strings.Pen} · {Strings.Thickness} 3/5 · #E74C3C", line);
    }

    /// <summary>페이딩은 켜져 있을 때만 말한다 — 평시에 매 휠마다 "페이딩 꺼짐"을 읽히면 소음이다.</summary>
    [Fact]
    public void Line_FadingOn_AppendsTheDuration()
    {
        string line = StatusReadout.Line(ToolKind.Pen, ThicknessStep.Medium, Red, fadingOn: true, fadingSeconds: 2.0);

        Assert.EndsWith($"{Strings.HotkeyFadingInk} {Strings.FadingDuration(2.0)}", line, StringComparison.Ordinal);
    }

    /// <summary>도구가 없으면 아무것도 그리지 않으므로 굵기·색을 말하는 것은 거짓 정보다.</summary>
    [Fact]
    public void Line_NoTool_OmitsThicknessAndColor()
    {
        string line = StatusReadout.Line(ToolKind.None, ThicknessStep.Medium, Red, fadingOn: false, fadingSeconds: 2.0);

        Assert.Equal(Strings.StatusNoTool, line);
    }
}
