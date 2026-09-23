using System.Windows.Threading;

namespace SSPen.Annotation;

/// <summary>
/// <see cref="IIdleScheduler"/>의 WPF 어댑터 (R7). <c>DispatcherTimer</c> +
/// <c>DispatcherPriority.Background</c> + Stop 후 Start 디바운스 — 원래
/// <c>SurfaceInputController.StepWheelScale</c>에 있던 관용구 그대로다.
///
/// 73단계에서 <see cref="ContentSurfaceWindow"/>의 private 중첩 클래스를 그대로 꺼냈다 — 서피스마다 휠 디바운스용으로 하나씩,
/// 합성 루트가 실험적 z-순서 주기 정정(<c>Shell/ZBandPoller</c>)용으로 하나를 만든다. 동작은 한 글자도 바꾸지 않았다.
///
/// <c>Task</c>/<c>await</c>로 바꾸지 않는다: 확정 경로가 <c>TransformState</c>를 쓰고
/// 소유 문서에 알리고 <see cref="UndoLedger"/>에 append 한다 — 전부 UI 스레드 전용이다.
/// </summary>
public sealed class DispatcherIdleScheduler(Dispatcher dispatcher) : IIdleScheduler
{
    private DispatcherTimer? _timer;

    public event Action? Tick;

    public void Restart(TimeSpan interval)
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Interval = interval;
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel() => _timer?.Stop();

    private void OnTimerTick(object? sender, EventArgs e) => Tick?.Invoke();
}
