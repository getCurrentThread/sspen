using System.Windows;
using System.Windows.Threading;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 토스트 어댑터 — 판정은 <see cref="ToastQueue"/>가, 창은 <see cref="ToastWindow"/>가 소유한다.
/// 여기 남는 것은 타이머·배치·가시성 세 가지뿐이다.
///
/// 타이머는 <see cref="ToastStep.StopTimer"/>에 따라 스스로 내려간다 — 알릴 것이 없을 때
/// 폴링 루프를 돌려 두지 않는 규율은 <c>RenderTickController</c>·<see cref="FlyoutWatchRules"/>와 같다.
/// 디스패처는 주입받는다 (LD-4: <c>Application.Current</c>를 참조하면 통합 테스트가 STA마다 무너진다).
/// </summary>
public sealed class ToastHost
{
    /// <summary>표시 갱신 주기. 표시 시간이 초 단위라 100ms면 눈에 띄는 지연 없이 충분하다.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly ToastQueue _queue;
    private readonly ToastWindow _window;
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _now;
    private Action? _pendingAction;
    private bool _suspended;

    public ToastHost(Dispatcher dispatcher, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _queue = new ToastQueue(_now);
        _window = new ToastWindow();
        _window.ActionInvoked += () =>
        {
            var action = _pendingAction;
            _pendingAction = null;
            action?.Invoke();
        };
        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Normal, (_, _) => Tick(), dispatcher);
        _timer.Stop();
    }

    /// <summary>z-밴드 등록용 HWND. 시동에서 한 번 <see cref="Prepare"/>한 뒤로 값이 바뀌지 않는다.</summary>
    public nint Hwnd => _window.Hwnd;

    /// <summary>
    /// 창을 만들어 HWND를 확보한다 (시동 1회). 숨긴 채로 <c>Show()</c>하는 이유는 z-밴드 때문이다 —
    /// 이 시점에 HWND가 있어야 이후 모든 <c>ApplyZBand</c>가 토스트를 이미 포함한다.
    /// </summary>
    public void Prepare()
    {
        _window.Show();
        _window.Visibility = Visibility.Hidden;
    }

    public void Show(ToastRequest request)
    {
        if (_suspended)
        {
            return; // 캡처 세션 중에는 결과물에 찍힌다 — 알림은 세션이 끝난 뒤에 온다.
        }
        _queue.Push(request);
        Tick();
        if (_queue.HasWork)
        {
            _timer.Start();
        }
    }

    /// <summary>캡처 세션처럼 화면을 비워야 하는 구간. 표시 중이던 것과 대기열을 함께 버린다.</summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (!suspended)
        {
            return;
        }
        _queue.Clear();
        _timer.Stop();
        _window.Visibility = Visibility.Hidden;
    }

    public void Close() => WindowLifetime.HideThenClose(_window);

    private void Tick()
    {
        var step = _queue.Tick(_now());
        if (step.Visible)
        {
            _pendingAction = step.OnAction;
            _window.Render(step);
            _window.Visibility = Visibility.Visible;
            Place();
        }
        else
        {
            _pendingAction = null;
            _window.Visibility = Visibility.Hidden;
        }
        if (step.StopTimer)
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// 커서가 있는 화면의 작업 영역 하단 중앙 (배치 산술은 <see cref="ToastPlacement"/>).
    /// <c>Window.Left/Top</c>(DIP) 대신 물리 픽셀 <c>SetWindowPos</c>를 쓰는 이유: 혼합 DPI에서
    /// DIP 대입은 배율이 섞인 값을 낳는다 (툴바 초기 배치가 같은 이유로 화면 밖으로 나갔다).
    /// </summary>
    private void Place()
    {
        if (_window.Hwnd == 0)
        {
            return;
        }
        try
        {
            var monitors = MonitorTopology.Enumerate();
            if (monitors.Count == 0)
            {
                return;
            }
            var (cursorX, cursorY) = NativeMethods.GetCursorPos(out var c) ? (c.X, c.Y) : (0, 0);
            var monitor = ToastPlacement.MonitorFor(monitors, cursorX, cursorY);
            var (width, height) = _window.PhysicalSize();
            var (x, y) = ToastPlacement.Anchor(monitor.WorkArea, width, height, _window.PhysicalBottomMargin());
            NativeMethods.SetWindowPos(
                _window.Hwnd, 0, x, y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }
        catch (Exception ex)
        {
            // 배치 실패로 알림 자체를 잃지는 않는다 — 창은 이미 보이는 상태다.
            Log.Warn($"토스트 배치 실패: {ex.Message}");
        }
    }
}
