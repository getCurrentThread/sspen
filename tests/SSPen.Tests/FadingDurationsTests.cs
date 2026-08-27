using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>페이딩 잉크 지속 시간 사다리·정규화 (사용자 요청 16차: 0.1~5초 재조정).</summary>
public class FadingDurationsTests
{
    [Fact]
    public void Steps_SpanTheFullRange()
    {
        // 사다리 양 끝이 곧 허용 범위여야 한다 — 어긋나면 UI로 도달 못 하는 구간이 생긴다.
        Assert.Equal(FadingDurations.Min, FadingDurations.Steps[0]);
        Assert.Equal(FadingDurations.Max, FadingDurations.Steps[^1]);
    }

    [Fact]
    public void Steps_AreStrictlyAscending()
    {
        for (int i = 1; i < FadingDurations.Steps.Length; i++)
        {
            Assert.True(
                FadingDurations.Steps[i] > FadingDurations.Steps[i - 1],
                $"사다리가 오름차순이 아니다: [{i - 1}]={FadingDurations.Steps[i - 1]} [{i}]={FadingDurations.Steps[i]}");
        }
    }

    [Theory]
    [InlineData(6.0, 5.0)]    // 이전 체계의 "보통"
    [InlineData(12.0, 5.0)]   // 이전 체계의 "길게"
    [InlineData(999.0, 5.0)]
    public void Clamp_AboveMax_ComesDownToMax(double stored, double expected)
    {
        // 이전 설정 파일(3/6/12초)이 그대로 열려도 새 상한을 넘지 않아야 한다.
        Assert.Equal(expected, FadingDurations.Clamp(stored));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(0.05)]
    public void Clamp_BelowMin_RisesToMin(double stored)
    {
        Assert.Equal(FadingDurations.Min, FadingDurations.Clamp(stored));
    }

    [Fact]
    public void Clamp_InRangeValue_IsPreservedExactly()
    {
        // 범위 안이면 사다리 칸이 아니어도 존중한다 (손으로 편집한 settings.json).
        Assert.Equal(1.7, FadingDurations.Clamp(1.7));
    }

    [Fact]
    public void Clamp_NaN_FallsBackToDefault()
    {
        Assert.Equal(FadingDurations.Default, FadingDurations.Clamp(double.NaN));
    }

    [Fact]
    public void Default_IsWithinRange()
    {
        Assert.InRange(FadingDurations.Default, FadingDurations.Min, FadingDurations.Max);
    }

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(0.5, 1)]
    [InlineData(5.0, 5)]
    [InlineData(0.4, 1)]     // 0.5에 더 가깝다
    [InlineData(2.6, 4)]     // 3.0에 더 가깝다
    public void NearestIndex_PicksClosestStep(double seconds, int expected)
    {
        Assert.Equal(expected, FadingDurations.NearestIndex(seconds));
    }

    [Fact]
    public void NearestIndex_OutOfRangeValue_ClampsFirst()
    {
        // 이전 체계 12초는 상한으로 재단된 뒤 마지막 칸을 가리켜야 한다.
        Assert.Equal(FadingDurations.Steps.Length - 1, FadingDurations.NearestIndex(12.0));
    }

    [Fact]
    public void Next_AdvancesOneStep()
    {
        Assert.Equal(0.5, FadingDurations.Next(0.1));
        Assert.Equal(1.0, FadingDurations.Next(0.5));
    }

    [Fact]
    public void Next_AtLastStep_WrapsToFirst()
    {
        // 버튼 재클릭 로테이션이 상한에서 막히면 되돌아갈 방법이 없다.
        Assert.Equal(FadingDurations.Min, FadingDurations.Next(FadingDurations.Max));
    }

    [Fact]
    public void Next_VisitsEveryStepExactlyOncePerCycle()
    {
        var visited = new List<double>();
        double current = FadingDurations.Steps[0];
        for (int i = 0; i < FadingDurations.Steps.Length; i++)
        {
            visited.Add(current);
            current = FadingDurations.Next(current);
        }

        Assert.Equal(FadingDurations.Steps, visited);
        Assert.Equal(FadingDurations.Steps[0], current); // 한 바퀴 후 제자리
    }

    [Fact]
    public void Same_TreatsFloatingPointNoiseAsEqual()
    {
        // 0.1 + 0.2 == 0.30000000000000004. 정확 비교로는 플라이아웃 강조가 사라진다.
        Assert.True(FadingDurations.Same(0.1 + 0.2, 0.3));
    }

    [Fact]
    public void Same_DistinguishesAdjacentSteps()
    {
        Assert.False(FadingDurations.Same(0.1, 0.5));
    }
}
