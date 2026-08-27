using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>보드 전이 판정 (사용자 요청 16차: 위→아래로 내려오고 다시 위로 걷힌다).</summary>
public class BoardTransitionTests
{
    [Fact]
    public void ShouldShow_BoardOff_IsNeverShown()
    {
        Assert.False(BoardTransition.ShouldShow(BoardMode.None, allMonitors: true, isPrimary: true));
    }

    [Fact]
    public void ShouldShow_SingleMonitorScope_OnlyPrimary()
    {
        // Round 13 범위 규칙: 한 화면 표시면 주 모니터만 그린다.
        Assert.True(BoardTransition.ShouldShow(BoardMode.White, allMonitors: false, isPrimary: true));
        Assert.False(BoardTransition.ShouldShow(BoardMode.White, allMonitors: false, isPrimary: false));
    }

    [Fact]
    public void Resolve_OffToOn_SlidesDown()
    {
        var kind = BoardTransition.Resolve(
            wasShown: false, previous: BoardMode.None, shouldShow: true, current: BoardMode.White);

        Assert.Equal(BoardTransitionKind.SlideDown, kind);
    }

    [Fact]
    public void Resolve_OnToOff_SlidesUp()
    {
        var kind = BoardTransition.Resolve(
            wasShown: true, previous: BoardMode.White, shouldShow: false, current: BoardMode.None);

        Assert.Equal(BoardTransitionKind.SlideUp, kind);
    }

    [Fact]
    public void Resolve_WhiteToBlack_RecolorsWithoutSliding()
    {
        // 보드는 계속 떠 있고 색만 바뀐다. 슬라이드로 처리하면 화면이 한 번 걷혔다 다시 내려와 산만하다.
        var kind = BoardTransition.Resolve(
            wasShown: true, previous: BoardMode.White, shouldShow: true, current: BoardMode.Black);

        Assert.Equal(BoardTransitionKind.Recolor, kind);
    }

    [Fact]
    public void Resolve_NoChange_EmitsNone()
    {
        // 핵심 회귀 방어: AppState.Changed는 색·굵기 변경에도 불리는 단일 이벤트다.
        // 여기서 None이 아니면 퀵컬러를 누를 때마다 보드가 다시 내려와 덜그럭거린다.
        var kind = BoardTransition.Resolve(
            wasShown: true, previous: BoardMode.White, shouldShow: true, current: BoardMode.White);

        Assert.Equal(BoardTransitionKind.None, kind);
    }

    [Fact]
    public void Resolve_StaysHidden_EmitsNone()
    {
        var kind = BoardTransition.Resolve(
            wasShown: false, previous: BoardMode.None, shouldShow: false, current: BoardMode.None);

        Assert.Equal(BoardTransitionKind.None, kind);
    }

    [Fact]
    public void Resolve_HiddenMonitor_BoardColorChange_StaysNone()
    {
        // 보조 모니터에서 한 화면 범위로 보드 색만 바뀐 경우: 안 보이던 채로 계속 안 보인다.
        var kind = BoardTransition.Resolve(
            wasShown: false, previous: BoardMode.White, shouldShow: false, current: BoardMode.Black);

        Assert.Equal(BoardTransitionKind.None, kind);
    }
}
