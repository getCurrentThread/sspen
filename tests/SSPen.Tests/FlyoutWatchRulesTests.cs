using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="FlyoutWatchRules"/>의 증인 (38단계, ARCH-11). 4분기 표 + 2틱 연속 이탈 시퀀스. 타이머는 어댑터(ToolbarFlyouts)에
/// 남아 있으므로 여기는 판정만 본다 — 150ms 간격 자체는 이 표의 관심사가 아니다.
/// </summary>
public class FlyoutWatchRulesTests
{
    /// <summary>열린 팝업이 없으면 감시만 멈춘다: 닫을 것도 없고 카운터도 건드리지 않는다 (다음 OpenFlyout이 0으로 리셋).</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Tick_NothingOpen_StopsWatchWithoutClosing(bool pointerOver, int awayTicks)
    {
        var step = FlyoutWatchRules.Tick(anyOpen: false, pointerOver, awayTicks);

        Assert.Equal(new FlyoutWatchStep(CloseAll: false, StopWatch: true, AwayTicks: awayTicks), step);
    }

    /// <summary>포인터가 툴바나 팝업 위에 있으면 이탈 카운터를 0으로 되돌리고 열린 채 둔다.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Tick_PointerOver_ResetsAwayTicksAndKeepsOpen(int awayTicks)
    {
        var step = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: true, awayTicks);

        Assert.Equal(new FlyoutWatchStep(CloseAll: false, StopWatch: false, AwayTicks: 0), step);
    }

    [Fact]
    public void Tick_FirstTickAway_KeepsOpen()
    {
        var step = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, awayTicks: 0);

        Assert.Equal(new FlyoutWatchStep(CloseAll: false, StopWatch: false, AwayTicks: 1), step);
    }

    [Fact]
    public void Tick_SecondTickAway_ClosesAllAndStopsWatch()
    {
        var step = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, awayTicks: 1);

        Assert.Equal(new FlyoutWatchStep(CloseAll: true, StopWatch: true, AwayTicks: 2), step);
    }

    /// <summary>문턱은 '>='다 (원형 <c>++ticks &gt;= 2</c>): 카운터가 어떤 이유로 문턱을 넘어 있어도 닫는다.</summary>
    [Fact]
    public void Tick_AwayBeyondThreshold_StillCloses()
    {
        var step = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, awayTicks: 5);

        Assert.True(step.CloseAll);
        Assert.True(step.StopWatch);
        Assert.Equal(6, step.AwayTicks);
    }

    /// <summary>열림(카운터 0) → 이탈 → 이탈: 정확히 두 번째 이탈 틱에서 닫힌다. 중간에 되돌아오면 처음부터 다시 센다.</summary>
    [Fact]
    public void Tick_Sequence_ClosesExactlyOnSecondConsecutiveAwayTick()
    {
        int ticks = 0; // OpenFlyout이 0으로 리셋한 직후.

        var first = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, ticks);
        ticks = first.AwayTicks;
        var back = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: true, ticks);
        ticks = back.AwayTicks;
        var again = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, ticks);
        ticks = again.AwayTicks;
        var second = FlyoutWatchRules.Tick(anyOpen: true, pointerOver: false, ticks);

        Assert.False(first.CloseAll);
        Assert.False(back.CloseAll);
        Assert.False(again.CloseAll);
        Assert.True(second.CloseAll);
        Assert.Equal(FlyoutWatchRules.AwayTicksToClose, second.AwayTicks);
    }

    /// <summary>오늘 값 고정: 150ms 타이머 × 2틱 ≈ 300ms (Epic Pen 감각). 바꾸려면 이 줄과 어댑터 주석을 함께 바꾼다.</summary>
    [Fact]
    public void AwayTicksToClose_IsTwo_Today() => Assert.Equal(2, FlyoutWatchRules.AwayTicksToClose);

    /// <summary>
    /// Escape는 포인터보다 먼저다: 마우스가 아직 플라이아웃 위에 있어도 닫힌다. 지금까지는
    /// 포인터를 멀리 치우는 것 말고 닫을 방법이 없었다 (StaysOpen=true라 밖 클릭도 안 먹는다).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tick_EscapePressed_ClosesRegardlessOfThePointer(bool pointerOver)
    {
        var step = FlyoutWatchRules.Tick(anyOpen: true, pointerOver, awayTicks: 0, escapePressed: true);

        Assert.True(step.CloseAll);
        Assert.True(step.StopWatch);
        Assert.Equal(0, step.AwayTicks);
    }

    /// <summary>열린 것이 없으면 Escape는 남의 것이다 — 감시만 멈춘다.</summary>
    [Fact]
    public void Tick_EscapeWithNothingOpen_DoesNothingButStop()
    {
        var step = FlyoutWatchRules.Tick(anyOpen: false, pointerOver: false, awayTicks: 1, escapePressed: true);

        Assert.False(step.CloseAll);
        Assert.True(step.StopWatch);
    }
}
