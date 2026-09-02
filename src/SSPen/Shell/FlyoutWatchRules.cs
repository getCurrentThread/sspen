namespace SSPen.Shell;

/// <summary>포인터 감시 한 틱의 결정 (38단계): 전부 닫을지, 감시를 멈출지, 다음 이탈 카운터.</summary>
public readonly record struct FlyoutWatchStep(bool CloseAll, bool StopWatch, int AwayTicks);

/// <summary>
/// 플라이아웃 포인터 감시 판정 (38단계, ARCH-11). StaysOpen=true 플라이아웃은 밖 클릭으로 안 닫히므로 ToolbarFlyouts의
/// DispatcherTimer(150ms)가 틱마다 여기에 묻는다. 어댑터(ToolbarFlyouts.FlyoutWatchTick)는 입력 수집(IsOpen/IsMouseOver)과
/// 실행(IsOpen=false·Stop)만 남긴다. OpenFlyout이 카운터를 0으로 리셋하고 CloseFlyoutsExcept(null)만 타이머를 멈추는 규약은 그대로.
/// 4분기 표:
///   anyOpen=false                         → CloseAll=false, StopWatch=true,  AwayTicks 그대로 (다음 OpenFlyout이 0으로 리셋)
///   pointerOver=true                      → CloseAll=false, StopWatch=false, AwayTicks=0
///   이탈, ticks+1 &lt; AwayTicksToClose   → CloseAll=false, StopWatch=false, AwayTicks=ticks+1 (열린 채 유지)
///   이탈, ticks+1 &gt;= AwayTicksToClose  → CloseAll=true,  StopWatch=true,  AwayTicks=ticks+1
/// </summary>
public static class FlyoutWatchRules
{
    /// <summary>포인터 이탈이 이 틱 수만큼 이어지면 닫는다 (150ms 간격 × 2 ≈ 300ms — Epic Pen 감각).</summary>
    public const int AwayTicksToClose = 2;

    public static FlyoutWatchStep Tick(bool anyOpen, bool pointerOver, int awayTicks)
    {
        if (!anyOpen)
        {
            return new FlyoutWatchStep(CloseAll: false, StopWatch: true, AwayTicks: awayTicks);
        }
        if (pointerOver)
        {
            return new FlyoutWatchStep(CloseAll: false, StopWatch: false, AwayTicks: 0);
        }
        int next = awayTicks + 1;
        bool close = next >= AwayTicksToClose;
        return new FlyoutWatchStep(CloseAll: close, StopWatch: close, AwayTicks: next);
    }
}
