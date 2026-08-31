using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SSPen.Interop;

namespace SSPen.E2ETests;

/// <summary>
/// E2E 테스트용 애플리케이션 수명 주기 관리 픽스처.
/// 독립 STA 스레드에서 AppController의 완전한 기동, 가상 토폴로지 주입, 디스패처 펌핑, 안전 종료를 담당한다.
/// </summary>
public static class E2EAppFixture
{
    private static readonly MonitorSurfaceInfo[] Default3Monitors =
    [
        new(@"\\.\DISPLAY1", new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1040), IsPrimary: false),
        new(@"\\.\DISPLAY2", new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), IsPrimary: true),
        new(@"\\.\DISPLAY3", new PhysicalRect(1920, 0, 1920, 1080), new PhysicalRect(1920, 0, 1920, 1040), IsPrimary: false),
    ];

    public static void Run(Action<VirtualUserActor> testAction, MonitorSurfaceInfo[]? customTopology = null)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 1. 가상 토폴로지 주입 (다중 모니터, 음수 원점)
                var monitors = customTopology ?? Default3Monitors;
                MonitorTopology.SetProviderForTesting(() => monitors);

                // 2. 가상 캡처 프로바이더 주입
                Capture.CaptureService.SetCaptureProviderForTesting(r =>
                {
                    var bmp = new WriteableBitmap(
                        Math.Max(1, r.Width), Math.Max(1, r.Height), 96, 96,
                        System.Windows.Media.PixelFormats.Bgra32, null);
                    bmp.Freeze();
                    return bmp;
                });

                // 3. AppController 기동 (실제 컴포지션 루트 실행)
                var app = new AppController();
                app.Start();
                PumpMessages();

                // 4. VirtualUserActor 생성 및 시나리오 실행
                var actor = new VirtualUserActor(app);
                testAction(actor);

                // 5. AppController 안전 종료
                app.Shutdown();
                foreach (var surface in app.Surfaces)
                {
                    surface.Close();
                }
                app.Toolbar?.Close();
                app.CurrentSettingsWindow?.Close();
                PumpMessages();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                MonitorTopology.ResetProviderForTesting();
                Capture.CaptureService.ResetCaptureProviderForTesting();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        if (thread.IsAlive)
        {
            thread.Interrupt();
            throw new TimeoutException("E2E 테스트 실행 시간 초과 (30초)");
        }

        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public static void PumpMessages()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            (Action)(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
