using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ThicknessScale"/>의 증인 (30단계, R9). 단계 전수 표와 양끝 클램프를 잠근다 — 단계가 늘면 행이 따라오고
/// 기대 표에 없는 단계는 빨갛다. AppState 파생 프로퍼티가 이 표에 위임함은 ToolStyleTests(굵기·TextFontSize 케이스)가 본다.
/// </summary>
public class ThicknessScaleTests
{
    [Theory]
    [MemberData(nameof(AllSteps))]
    public void PenPixels_EveryStep_MatchesTable(ThicknessStep step)
    {
        double expected = step switch
        {
            ThicknessStep.XSmall => 2,
            ThicknessStep.Small => 4,
            ThicknessStep.Medium => 6,
            ThicknessStep.Large => 10,
            ThicknessStep.XLarge => 16,
            _ => throw new Xunit.Sdk.XunitException($"새 단계 {step}의 펜 px를 이 표에 적으세요."),
        };

        Assert.Equal(expected, ThicknessScale.PenPixels(step));
        Assert.Equal(expected * 3, ThicknessScale.HighlighterPixels(step));
    }

    [Theory]
    [MemberData(nameof(AllSteps))]
    public void FontSize_EveryStep_MatchesTable(ThicknessStep step)
    {
        double expected = step switch
        {
            ThicknessStep.XSmall => 12,
            ThicknessStep.Small => 16,
            ThicknessStep.Medium => 24,
            ThicknessStep.Large => 36,
            ThicknessStep.XLarge => 48,
            _ => throw new Xunit.Sdk.XunitException($"새 단계 {step}의 텍스트 크기를 이 표에 적으세요."),
        };

        Assert.Equal(expected, ThicknessScale.FontSize(step));
    }

    [Fact]
    public void Step_ClampsAtBothEnds()
    {
        Assert.Equal(ThicknessStep.XSmall, ThicknessScale.Step(ThicknessStep.XSmall, -1));
        Assert.Equal(ThicknessStep.Small, ThicknessScale.Step(ThicknessStep.XSmall, +1));
        Assert.Equal(ThicknessStep.XLarge, ThicknessScale.Step(ThicknessStep.XLarge, +1));
        Assert.Equal(ThicknessStep.Large, ThicknessScale.Step(ThicknessStep.XLarge, -1));
        Assert.Equal(ThicknessStep.XLarge, ThicknessScale.Step(ThicknessStep.Medium, +99));
    }

    /// <summary>표는 합치지 않는다 (f70c3fb): 같은 단계라도 펜 px와 텍스트 크기는 다른 양이다.</summary>
    [Fact]
    public void PenPixels_AndFontSize_AreDifferentQuantities() =>
        Assert.All(Enum.GetValues<ThicknessStep>(), s => Assert.NotEqual(ThicknessScale.PenPixels(s), ThicknessScale.FontSize(s)));

    [Fact]
    public void PenPixels_NotOnAppState_ByReflection() => Assert.Null(typeof(AppState).GetMethod("PenPixels"));

    public static TheoryData<ThicknessStep> AllSteps()
    {
        var data = new TheoryData<ThicknessStep>();
        foreach (var step in Enum.GetValues<ThicknessStep>())
        {
            data.Add(step);
        }
        return data;
    }
}
