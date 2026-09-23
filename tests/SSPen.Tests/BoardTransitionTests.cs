using SSPen.Annotation;
using SSPen.Interop;
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

    // ---- 슬라이드 이동 거리 (90단계, A2-2): 논리 px로 일관, WorkArea는 CoordinateSpace로 논리화 ----

    [Fact]
    public void Travel_100Percent_EqualsWorkAreaHeight()
    {
        double travel = BoardTransition.Travel(1040, new PhysicalRect(0, 0, 1920, 1040), dpiScale: 1.0);

        Assert.Equal(1040, travel, precision: 9);
    }

    [Fact]
    public void Travel_150Percent_UsesLogicalWorkArea_NotPhysical()
    {
        // 회귀 방어: 물리 높이 1040을 그대로 고르면 이동 거리가 1.5배가 되어, EaseOut 커브에서
        // SlideUp이 280ms 중 처음 ~86ms 만에 화면 밖으로 빠진다 — 걷힘이 스냅처럼 보인다.
        double travel = BoardTransition.Travel(1040 / 1.5, new PhysicalRect(0, 0, 1920, 1040), dpiScale: 1.5);

        Assert.Equal(1040 / 1.5, travel, precision: 9);
        Assert.NotEqual(1040, travel, precision: 3);
    }

    [Fact]
    public void Travel_BeforeLayout_FallsBackToLogicalWorkArea()
    {
        // 레이아웃 전(ActualHeight = 0)에도 보드가 화면 밖에서 출발하도록 논리화한 작업 영역 높이로 폴백한다.
        double travel = BoardTransition.Travel(0, new PhysicalRect(0, 0, 1920, 1040), dpiScale: 1.5);

        Assert.Equal(1040 / 1.5, travel, precision: 9);
    }

    [Fact]
    public void Travel_NegativeOriginMonitor_UsesHeightOnly()
    {
        // 목표 토폴로지의 음수 원점 모니터: 원점은 거리에 끼어들지 않는다 (높이만 논리화).
        double travel = BoardTransition.Travel(0, new PhysicalRect(-1920, 0, 1920, 1080), dpiScale: 1.25);

        Assert.Equal(864, travel, precision: 9);
    }

    [Fact]
    public void Travel_LayoutTallerThanLogicalWorkArea_UsesLayoutHeight()
    {
        // 레이아웃 높이가 더 크면 그만큼 올려야 보드가 완전히 빠진다 (Math.Max의 다른 쪽 가지).
        double travel = BoardTransition.Travel(700, new PhysicalRect(0, 0, 1920, 1040), dpiScale: 1.5);

        Assert.Equal(700, travel, precision: 9);
    }
}
