using System.Runtime.ExceptionServices;

namespace SSPen.Tests;

/// <summary>
/// 헤드리스 유닛 테스트용 STA 실행 헬퍼. WPF 비주얼 트리 객체(<c>Canvas</c>, <c>Path</c>, <c>TextBox</c>)를
/// 만드는 본문만 STA 쓰레드로 보내고 예외는 스택을 보존해 재던진다.
///
/// 통합 프로젝트의 <c>StaRunner.Run</c>(60초 타임아웃 + 디스패처 펌프 + <c>InvokeShutdown</c>)과는 의미가 다르다 —
/// 여기에는 펌프가 없으므로 <c>DispatcherTimer</c>는 영영 틱하지 않는다 (그래서 휠 유휴는
/// <see cref="FakeIdleScheduler"/>가 대신한다, R7). 이름을 다르게 둔 이유가 그것이다.
///
/// 사용: 파일 머리에 <c>using static SSPen.Tests.StaThread;</c> 를 두고 <c>RunSta(() =&gt; …)</c>.
/// <c>Geometry</c>/<c>StreamGeometry</c>/<c>StylusPointCollection</c>은 xUnit 기본 MTA 쓰레드에서도
/// 만들어지므로(2026-09-02 실측) 이 헬퍼가 필요 없다.
/// </summary>
internal static class StaThread
{
    public static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
