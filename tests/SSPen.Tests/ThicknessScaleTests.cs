using SSPen.Annotation;
using SSPen.Settings;
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

    // ---- 66단계 (A4-5·A9-2): 저장값 복원 FromStored와 공통 기본 단계 Default ----

    /// <summary>범위 밖 저장값(손상·구버전 설정)은 양끝 단계로 재단된다 — 상한은 리터럴 4가 아니라 열거 길이다.</summary>
    [Theory]
    [InlineData(-1, ThicknessStep.XSmall)]
    [InlineData(0, ThicknessStep.XSmall)]
    [InlineData(2, ThicknessStep.Medium)]
    [InlineData(4, ThicknessStep.XLarge)]
    [InlineData(99, ThicknessStep.XLarge)]
    public void FromStored_Value_ClampsToEnumRange(int stored, ThicknessStep expected) =>
        Assert.Equal(expected, ThicknessScale.FromStored(stored));

    /// <summary>SettingsBinder.SyncFromState가 쓰는 (int)단계를 그대로 되읽으면 같은 단계다 — 저장·복원 왕복이 단계를 잃지 않는다.</summary>
    [Theory]
    [MemberData(nameof(AllSteps))]
    public void FromStored_EveryStepIndex_RoundTrips(ThicknessStep step) =>
        Assert.Equal(step, ThicknessScale.FromStored((int)step));

    /// <summary>
    /// AppSettings의 정수 기본값(JSON 표기 호환 때문에 리터럴 2)과 새 AppState의 초기 단계가 모두 <see cref="ThicknessScale.Default"/>와 같다.
    /// </summary>
    [Theory]
    [MemberData(nameof(ToolStyleTests.AllStyleGroups), MemberType = typeof(ToolStyleTests))]
    public void Default_AllGroups_EqualsAppSettingsAndAppStateDefaults(ToolStyleGroup group)
    {
        int stored = StoredThicknessOf(new AppSettings(), group);

        Assert.Equal((int)ThicknessScale.Default, stored);
        Assert.Equal(ThicknessScale.Default, ThicknessScale.FromStored(stored));
        Assert.Equal(ThicknessScale.Default, new AppState().ThicknessOf(group));
    }

    private static int StoredThicknessOf(AppSettings settings, ToolStyleGroup group) => group switch
    {
        ToolStyleGroup.Pen => settings.PenThickness,
        ToolStyleGroup.Highlighter => settings.HighlighterThickness,
        ToolStyleGroup.Shape => settings.ShapeThickness,
        _ => throw new Xunit.Sdk.XunitException($"새 그룹 {group}의 AppSettings 굵기 속성을 이 표에 적으세요."),
    };

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
