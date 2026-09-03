namespace SSPen.Shell;

/// <summary>포인터 감시 한 틱의 결정 (38단계): 전부 닫을지, 감시를 멈출지, 다음 이탈 카운터.</summary>
public readonly record struct FlyoutWatchStep(bool CloseAll, bool StopWatch, int AwayTicks);

/// <summary>
/// 플라이아웃 포인터 감시 판정 (38단계, ARCH-11). StaysOpen=true 플라이아웃은 밖 클릭으로 안 닫히므로 ToolbarFlyouts의
/// DispatcherTimer(150ms)가 틱마다 여기에 묻는다. 어댑터(ToolbarFlyouts.FlyoutWatchTick)는 입력 수집(IsOpen/IsMouseOver)과
/// 실행(IsOpen=false·Stop)만 남긴다. OpenFlyout이 카운터를 0으로 리셋하고 CloseFlyoutsExcept(null)만 타이머를 멈추는 규약은 그대로.
/// 5분기 표 (Escape는 anyOpen 다음, 포인터보다 먼저):
///   anyOpen=false                         → CloseAll=false, StopWatch=true,  AwayTicks 그대로 (다음 OpenFlyout이 0으로 리셋)
///   escapePressed=true                    → CloseAll=true,  StopWatch=true,  AwayTicks=0
///   pointerOver=true                      → CloseAll=false, StopWatch=false, AwayTicks=0
///   이탈, ticks+1 &lt; AwayTicksToClose   → CloseAll=false, StopWatch=false, AwayTicks=ticks+1 (열린 채 유지)
///   이탈, ticks+1 &gt;= AwayTicksToClose  → CloseAll=true,  StopWatch=true,  AwayTicks=ticks+1
/// </summary>
public static class FlyoutWatchRules
{
    /// <summary>포인터 이탈이 이 틱 수만큼 이어지면 닫는다 (150ms 간격 × 2 ≈ 300ms — Epic Pen 감각).</summary>
    public const int AwayTicksToClose = 2;

    public static FlyoutWatchStep Tick(bool anyOpen, bool pointerOver, int awayTicks, bool escapePressed = false)
    {
        if (!anyOpen)
        {
            return new FlyoutWatchStep(CloseAll: false, StopWatch: true, AwayTicks: awayTicks);
        }
        // Escape는 포인터보다 먼저다: 열린 플라이아웃을 닫는 표준 키이고, 지금까지는 포인터를
        // 멀리 치우는 것 말고 닫을 방법이 없었다. 마우스가 아직 위에 있어도 닫힌다.
        if (escapePressed)
        {
            return new FlyoutWatchStep(CloseAll: true, StopWatch: true, AwayTicks: 0);
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
