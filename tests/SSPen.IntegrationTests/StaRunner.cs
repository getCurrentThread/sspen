using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace SSPen.IntegrationTests;

/// <summary>
/// STA 하니스 (CRIT-2): 테스트 본문을 STA 스레드에서 실행하고 디스패처를 정리한다.
/// 하니스가 자체 마커/테스트 창을 만들고 결과는 테스트 종료 코드로 보고한다.
/// </summary>
internal static class StaRunner
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true; // 행 걸린 스레드가 테스트 호스트 종료를 막지 않게.
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60)))
        {
            // 클리너 B5: 행 걸린 테스트가 무음 통과하지 않도록 명시적으로 실패시킨다.
            throw new TimeoutException("STA 테스트 스레드가 60초 안에 끝나지 않았습니다.");
        }
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>보류 중인 디스패처 작업을 소진 (DoEvents 대응).</summary>
    public static void PumpMessages()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
