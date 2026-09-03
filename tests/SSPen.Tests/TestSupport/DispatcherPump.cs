using System.Windows.Threading;

namespace SSPen.Tests;

/// <summary>
/// 큐에 쌓인 <c>Dispatcher.BeginInvoke</c> 작업을 한 번 소진한다 (53단계). 옵트인·1회성이다 — <see cref="StaThread.RunSta"/> 자체에는
/// 펌프가 없고(그래서 휠 유휴는 FakeIdleScheduler), 훅 모니터처럼 콜백 안에서 행동을 디스패처로 미루는 코드의 증인만 부른다.
/// 디스패처의 스레드(RunSta 본문 안)에서 불러야 한다. 원리: ApplicationIdle 우선순위 작업을 동기 Invoke하면 WPF가 중첩 프레임을 밀어
/// 그보다 높은 우선순위(BeginInvoke 기본 Normal)의 작업을 먼저 돌린다 — 통합 StaRunner.PumpMessages와 같은 메커니즘에 감시견을 더했다.
/// 프레임이 도는 동안에는 기한이 된 DispatcherTimer도 틱할 수 있다 — 이 리그들에는 타이머가 없다.
/// </summary>
internal static class DispatcherPump
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(10);

    public static void Drain(Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        bool ran = false;
        try
        {
            dispatcher.Invoke(() => ran = true, DispatcherPriority.ApplicationIdle, CancellationToken.None, Watchdog);
        }
        catch (TimeoutException)
        {
            ran = false;
        }
        if (!ran)
        {
            throw new TimeoutException($"DispatcherPump.Drain이 {Watchdog.TotalSeconds}초 안에 비워지지 않았다 — 디스패처가 멈췄거나 Send 우선순위 작업이 끝없이 쌓인다.");
        }
    }
}
