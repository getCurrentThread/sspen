using System.Reflection;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary><see cref="ShellMetrics"/>의 증인: 단일 소스 트립와이어와 히트 타깃 하한.</summary>
public class ShellMetricsTests
{
    /// <summary>
    /// 스트립 폭은 그리는 쪽(<c>ToolbarStripBuilder</c>)과 배치하는 쪽(<c>ToolbarPlacement</c>)이 함께 본다 —
    /// 갈라지면 툴바가 화면 가장자리에서 어긋난 위치에 앉는다.
    /// </summary>
    [Fact]
    public void StripWidth_MatchesToolbarPlacement() =>
        Assert.Equal(ToolbarPlacement.StripWidth, ShellMetrics.StripWidth);

    /// <summary>30 미만으로 내리면 펜·터치 입력에서 버튼을 놓친다.</summary>
    [Fact]
    public void ButtonSize_IsAtLeast30() => Assert.True(ShellMetrics.ButtonSize >= 30);

    /// <summary>글리프는 버튼 안에 들어가야 한다.</summary>
    [Fact]
    public void GlyphFitsInsideTheButton() => Assert.True(ShellMetrics.GlyphSize < ShellMetrics.ButtonSize);

    [Fact]
    public void TypeScale_IsOrdered() =>
        Assert.True(ShellMetrics.FontCaption < ShellMetrics.FontBody && ShellMetrics.FontBody < ShellMetrics.FontSection);

    /// <summary>
    /// 치수 표에 <c>Brush</c>가 들어오면 MTA 테스트 스레드에서 정적 초기화가 터진다 (Freezable은 스레드에 묶인다).
    /// 색은 <see cref="ShellPalette"/>, 브러시는 <c>ToolbarTheme</c>이 소유한다.
    /// </summary>
    [Fact]
    public void Metrics_CarryOnlyNumbers()
    {
        var members = typeof(ShellMetrics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToList();

        Assert.NotEmpty(members);
        Assert.All(members, f => Assert.True(
            f.FieldType == typeof(double) || f.FieldType == typeof(int),
            $"{f.Name}은(는) {f.FieldType.Name} — 치수 표에는 숫자만 둔다"));
    }
}
