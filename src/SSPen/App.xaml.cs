using System.Windows;
using SSPen.Diagnostics;
using SSPen.Shell;

namespace SSPen;

public partial class App : Application
{
    private AppController? _controller;
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 단일 인스턴스 (WI-15): 이미 실행 중이면 조용히 종료.
        _singleInstance = new Mutex(initiallyOwned: true, "SSPen-SingleInstance", out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Log.Initialize();
        DispatcherUnhandledException += (_, args) =>
        {
            if (InputRaceFilter.IsBenignStaleWindowRace(args.Exception))
            {
                // 사라진 창(핀·캡처 오버레이)을 WPF 입력 계층이 한 박자 늦게 가리키다 나는 경주다.
                // 상태를 손상시키지 않고 다음 마우스 이동이면 스스로 회복되므로,
                // 치명적 오류 창을 띄우고 앱을 쓸모없게 끊는 대신 경고만 남긴다.
                // 근원 처리는 WindowLifetime.HideThenClose가 담당하고, 이건 최후 방어선이다.
                Log.Warn($"무해한 입력 경주 무시 (사라진 창 핸들): {args.Exception.Message}");
                args.Handled = true;
                return;
            }
            Log.Error("처리되지 않은 예외", args.Exception);
            MessageBox.Show(
                Strings.FatalErrorBody + "%APPDATA%\\SS Pen\\logs",
                Strings.FatalErrorTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Shutdown();
        if (_ownsMutex)
        {
            // 중복 인스턴스는 뮤텍스를 소유한 적이 없다 — ReleaseMutex 예외 방지.
            _singleInstance?.ReleaseMutex();
        }
        _singleInstance?.Dispose();
        Log.Info("=== SS Pen 종료 ===");
        base.OnExit(e);
    }
}
