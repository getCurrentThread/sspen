using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary><see cref="PressStateRules"/>의 증인: 상태 우선순위와 클릭 래치.</summary>
public class PressStateRulesTests
{
    [Theory]
    [InlineData(false, false, false, ButtonVisualState.Idle)]
    [InlineData(false, true, false, ButtonVisualState.Hover)]
    [InlineData(false, true, true, ButtonVisualState.Pressed)]
    public void Resolve_InactiveButton_FollowsThePointer(bool active, bool hovered, bool pressed, ButtonVisualState expected) =>
        Assert.Equal(expected, PressStateRules.Resolve(active, hovered, pressed));

    /// <summary>켜진 토글은 손을 얹었다고 꺼진 것처럼 보이면 안 된다.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Resolve_Active_WinsOverHoverAndPress(bool hovered, bool pressed) =>
        Assert.Equal(ButtonVisualState.Active, PressStateRules.Resolve(active: true, hovered, pressed));

    /// <summary>버튼에서 눌렀다가 밖으로 끌어 놓으면 취소하려는 손이다 — 눌림 표시도 풀린다.</summary>
    [Fact]
    public void Resolve_PressedButDraggedAway_IsIdle() =>
        Assert.Equal(ButtonVisualState.Idle, PressStateRules.Resolve(active: false, hovered: false, pressed: true));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // 끌어서 취소
    [InlineData(false, true, false)]   // 밖에서 시작한 클릭
    [InlineData(false, false, false)]
    public void ShouldFire_RequiresBothEndsInside(bool pressedInside, bool releasedInside, bool expected) =>
        Assert.Equal(expected, PressStateRules.ShouldFire(pressedInside, releasedInside));
}
