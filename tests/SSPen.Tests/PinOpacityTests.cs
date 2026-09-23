using SSPen.Pin;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 핀 투명도 규칙의 증인 (AC-16, 81단계 A7-6). 창 안의 매직 넘버(0.05 계단, 0.15~1.0 클램프, 통과 때 0.85 상한)를
/// 옮긴 순수 코어라 수식은 옛 코드와 같아야 한다 — 비교는 정밀도를 지정한다.
/// </summary>
public class PinOpacityTests
{
    [Fact]
    public void Next_PositiveDelta_AddsStep()
    {
        Assert.Equal(0.55, PinOpacity.Next(0.5, 120), 10);
    }

    [Fact]
    public void Next_NegativeDelta_SubtractsStep()
    {
        Assert.Equal(0.45, PinOpacity.Next(0.5, -120), 10);
    }

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.18)]
    public void Next_AtFloor_ClampsToMin(double current)
    {
        Assert.Equal(PinOpacity.Min, PinOpacity.Next(current, -120), 10);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.98)]
    public void Next_AtCeiling_ClampsToMax(double current)
    {
        Assert.Equal(PinOpacity.Max, PinOpacity.Next(current, 120), 10);
    }

    [Fact]
    public void Next_ZeroDelta_SubtractsStep_Today()
    {
        // 현행 특성화: delta가 0이면 투명 쪽 한 계단이다 (옛 창 코드 `e.Delta > 0 ? 0.05 : -0.05`).
        Assert.Equal(0.45, PinOpacity.Next(0.5, 0), 10);
    }

    [Fact]
    public void DimForClickThrough_Opaque_ReturnsCeiling()
    {
        Assert.Equal(0.85, PinOpacity.DimForClickThrough(1.0), 10);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(0.85)]
    public void DimForClickThrough_AlreadyMoreTransparent_Unchanged(double current)
    {
        Assert.Equal(current, PinOpacity.DimForClickThrough(current), 10);
    }

    [Fact]
    public void Constants_MatchLegacyLiterals()
    {
        Assert.Equal(0.05, PinOpacity.Step);
        Assert.Equal(0.15, PinOpacity.Min);
        Assert.Equal(1.0, PinOpacity.Max);
        Assert.Equal(0.85, PinOpacity.ClickThroughCeiling);
    }
}
