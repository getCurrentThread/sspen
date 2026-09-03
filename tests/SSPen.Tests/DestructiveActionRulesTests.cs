using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="DestructiveActionRules"/>의 증인 (AC-19). 핵심 계약은 "마찰은 실행취소 가능성을 따른다"는 것이다 —
/// 되돌릴 수 있는 판서 지우기에는 대화상자를 붙이지 않고, 되돌릴 수 없는 핀 닫기에만 붙인다.
/// </summary>
public class DestructiveActionRulesTests
{
    /// <summary>핀은 원장 밖이라 되돌릴 수 없다 — 여기만 확인을 받는다.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 3)]
    public void ClearAll_PinsOpen_RequiresConfirmation(int inkCount, int pinCount)
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount, pinCount);

        Assert.True(prompt.NeedsConfirm);
        Assert.True(prompt.HasAnything);
        Assert.Equal(pinCount, prompt.PinCount);
    }

    /// <summary>판서만 있으면 실행취소 1회로 전부 돌아온다 — 대화상자는 마찰만 늘린다.</summary>
    [Fact]
    public void ClearAll_InkOnly_DoesNotRequireConfirmation()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: 12, pinCount: 0);

        Assert.False(prompt.NeedsConfirm);
        Assert.True(prompt.HasAnything);
        Assert.Equal(0, prompt.PinCount);
    }

    /// <summary>지울 것이 없으면 확인도 알림도 없다 (무동작은 말을 걸지 않는다).</summary>
    [Fact]
    public void ClearAll_NothingToClear_NeedsNothingAndReportsNothing()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: 0, pinCount: 0);

        Assert.False(prompt.NeedsConfirm);
        Assert.False(prompt.HasAnything);
    }

    /// <summary>음수는 계산 실수의 산물이지 사용자의 상태가 아니다 — 0으로 접어 대화상자를 띄우지 않는다.</summary>
    [Fact]
    public void ClearAll_NegativeCounts_AreTreatedAsEmpty()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: -3, pinCount: -1);

        Assert.False(prompt.NeedsConfirm);
        Assert.False(prompt.HasAnything);
        Assert.Equal(0, prompt.PinCount);
    }
}
