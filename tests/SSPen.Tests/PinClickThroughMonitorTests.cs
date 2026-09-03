using System.Runtime.InteropServices;
using System.Windows.Threading;
using SSPen.Interop;
using SSPen.Pin;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <see cref="PinClickThroughMonitor"/>의 헤드리스 증인 (53단계, AC-17). 순수 <see cref="PinClickThroughMonitor.HitClickThroughPin"/>,
/// 통과 핀 유무에 따른 설치/해제, 래치 없는 Dispose(오늘의 의미론 특성화), 합성 마우스 이벤트의 메시지 → Ctrl → 디코드 → 핀 목록
/// 순서를 잠근다. 리그 모양은 SelectionKeyMonitorTests와 같다: 핀 목록·Ctrl은 교체 가능한 thunk로 주입하고 "먼저 설치, 그 다음
/// 독약"으로 읽히지 않음을 증언한다. lParam은 항상 실제 MSLLHOOKSTRUCT를 가리킨다.
/// </summary>
public class PinClickThroughMonitorTests
{
    // 프로덕션이 쓰지 않는 메시지라 NativeMethods에 두지 않는다.
    private const int WM_MOUSEMOVE = 0x0200;

    private sealed class FakePin : IClickThroughPin
    {
        public FakePin(bool clickThrough, PhysicalRect bounds)
        {
            IsClickThrough = clickThrough;
            Bounds = bounds;
        }

        public bool IsClickThrough { get; set; }

        public PhysicalRect Bounds { get; }

        public int BoundsQueries { get; private set; }

        public List<bool> Calls { get; } = [];

        public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;

        public PhysicalRect PhysicalBounds()
        {
            BoundsQueries++;
            return Bounds;
        }

        public void SetClickThrough(bool on) => Calls.Add(on);
    }

    private sealed class Rig
    {
        public List<IClickThroughPin> Pins { get; } = [];

        public FakeHookInstaller Fake { get; } = new();

        public Func<IReadOnlyList<IClickThroughPin>> PinsThunk { get; set; }

        public Func<bool> CtrlThunk { get; set; } = () => true;

        public int Changed { get; private set; }

        public PinClickThroughMonitor Monitor { get; }

        public Rig()
        {
            PinsThunk = () => Pins;
            Monitor = new PinClickThroughMonitor(
                pins: () => PinsThunk(),
                controlDown: () => CtrlThunk(),
                clickThroughChanged: () => Changed++,
                hooks: Fake);
        }
    }

    private static readonly PhysicalRect Area = new(10, 10, 100, 50); // Right 110, Bottom 60

    /// <summary>실제 MSLLHOOKSTRUCT를 비관리 메모리에 두고 포인터를 lParam으로 준다.</summary>
    private sealed class MouseEvent : IDisposable
    {
        public nint Ptr { get; }

        public MouseEvent(int x, int y)
        {
            Ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MSLLHOOKSTRUCT>());
            Marshal.StructureToPtr(new NativeMethods.MSLLHOOKSTRUCT { pt = new NativeMethods.POINT { X = x, Y = y } }, Ptr, false);
        }

        public void Dispose() => Marshal.FreeHGlobal(Ptr);
    }

    // ---- HitClickThroughPin ----

    [Fact]
    public void HitClickThroughPin_ReturnsFirstClickThroughPinContainingPoint_InListOrder()
    {
        var a = new FakePin(clickThrough: false, Area);
        var b = new FakePin(clickThrough: true, Area);
        var c = new FakePin(clickThrough: true, Area);

        Assert.Same(b, PinClickThroughMonitor.HitClickThroughPin([a, b, c], 50, 30));
    }

    [Fact]
    public void HitClickThroughPin_DoesNotReadBoundsOfNonClickThroughPins_AndStopsAtFirstHit()
    {
        var a = new FakePin(clickThrough: false, Area);
        var b = new FakePin(clickThrough: true, Area);
        var c = new FakePin(clickThrough: true, Area);

        PinClickThroughMonitor.HitClickThroughPin([a, b, c], 50, 30);

        Assert.Equal(0, a.BoundsQueries);
        Assert.Equal(1, b.BoundsQueries);
        Assert.Equal(0, c.BoundsQueries);
    }

    /// <summary>PhysicalRect.Contains는 반개구간 — 오른쪽·아래 변은 밖이다.</summary>
    [Theory]
    [InlineData(5, 5)]
    [InlineData(110, 30)]
    [InlineData(50, 60)]
    [InlineData(9, 30)]
    public void HitClickThroughPin_NoHit_ReturnsNull(int x, int y) =>
        Assert.Null(PinClickThroughMonitor.HitClickThroughPin([new FakePin(clickThrough: true, Area)], x, y));

    [Fact]
    public void HitClickThroughPin_EmptyList_ReturnsNull() =>
        Assert.Null(PinClickThroughMonitor.HitClickThroughPin([], 50, 30));

    // ---- Refresh / Dispose ----

    [Fact]
    public void Refresh_InstallsMouseHook_OnlyWhileAClickThroughPinExists()
    {
        var rig = new Rig();

        rig.Monitor.Refresh();
        Assert.Empty(rig.Fake.Installs);

        var pin = new FakePin(clickThrough: true, Area);
        rig.Pins.Add(pin);
        rig.Monitor.Refresh();
        rig.Monitor.Refresh();
        var install = Assert.Single(rig.Fake.Installs);
        Assert.Equal(NativeMethods.WH_MOUSE_LL, install.HookId);

        pin.IsClickThrough = false;
        rig.Monitor.Refresh();
        Assert.Equal([0x1000], rig.Fake.Uninstalls);
        Assert.False(rig.Fake.IsInstalled);
    }

    /// <summary>오늘의 의미론 고정 (보존이지 승인이 아니다): 래치가 없어 Dispose 뒤 Refresh가 다시 건다 — PinManager.Dispose는 CloseAll이 먼저라 프로덕션에서는 드러나지 않는다.</summary>
    [Fact]
    public void Refresh_AfterDispose_ReinstallsWhileClickThroughPinRemains_Today()
    {
        var rig = new Rig();
        rig.Pins.Add(new FakePin(clickThrough: true, Area));
        rig.Monitor.Refresh();

        rig.Monitor.Dispose();
        Assert.Equal([0x1000], rig.Fake.Uninstalls);

        rig.Monitor.Refresh();
        Assert.Equal(2, rig.Fake.Installs.Count);
    }

    [Fact]
    public void Dispose_NotInstalled_IsNoOp_AndTwiceUninstallsOnce()
    {
        var fresh = new Rig();
        fresh.Monitor.Dispose();
        Assert.Empty(fresh.Fake.Uninstalls);

        var installed = new Rig();
        installed.Pins.Add(new FakePin(clickThrough: true, Area));
        installed.Monitor.Refresh();
        installed.Monitor.Dispose();
        installed.Monitor.Dispose();
        Assert.Single(installed.Fake.Uninstalls);
    }

    // ---- 훅 콜백 (합성 이벤트) ----

    [Fact]
    public void Proc_CtrlMiddleButton_OverClickThroughPin_Consumes_ThenDefersToggleOffAndNotify() => RunSta(() =>
    {
        var rig = new Rig();
        var pin = new FakePin(clickThrough: true, Area);
        rig.Pins.Add(pin);
        rig.Monitor.Refresh();
        using var click = new MouseEvent(50, 30);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_MBUTTONDOWN, click.Ptr);

        Assert.Equal(1, result);
        Assert.Empty(rig.Fake.Nexts);
        Assert.Empty(pin.Calls); // 콜백 안에서 실행하지 않는다 — 핀의 디스패처로 미룬다
        Assert.Equal(0, rig.Changed);
        DispatcherPump.Drain(Dispatcher.CurrentDispatcher);
        Assert.Equal([false], pin.Calls);
        Assert.Equal(1, rig.Changed);
    });

    /// <summary>먼저 설치, 그 다음 독약: Ctrl 없는 가운데 버튼은 핀 목록을 읽기 전에 통과한다.</summary>
    [Fact]
    public void Proc_MiddleButtonWithoutCtrl_PassesThrough_WithoutQueryingPins()
    {
        var rig = new Rig();
        rig.Pins.Add(new FakePin(clickThrough: true, Area));
        rig.Monitor.Refresh();
        rig.CtrlThunk = () => false;
        rig.PinsThunk = () => throw new InvalidOperationException("핀 목록을 읽었다");
        using var click = new MouseEvent(50, 30);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_MBUTTONDOWN, click.Ptr);

        Assert.Equal(42, result);
        Assert.Single(rig.Fake.Nexts);
    }

    /// <summary>순서 계약: 메시지 → Ctrl. WM_MOUSEMOVE 홍수에서는 OS 키 상태를 읽지 않는다.</summary>
    [Fact]
    public void Proc_MouseMove_PassesThrough_WithoutReadingCtrl()
    {
        var rig = new Rig();
        rig.Pins.Add(new FakePin(clickThrough: true, Area));
        rig.Monitor.Refresh();
        rig.CtrlThunk = () => throw new InvalidOperationException("Ctrl을 읽었다");
        using var move = new MouseEvent(50, 30);

        nint result = rig.Fake.Fire(0, WM_MOUSEMOVE, move.Ptr);

        Assert.Equal(42, result);
        Assert.Single(rig.Fake.Nexts);
    }

    [Fact]
    public void Proc_OverNonClickThroughPin_PassesThrough()
    {
        var rig = new Rig();
        var elsewhere = new FakePin(clickThrough: true, new PhysicalRect(500, 500, 10, 10)); // 훅을 살리는 통과 핀
        var under = new FakePin(clickThrough: false, Area);
        rig.Pins.Add(elsewhere);
        rig.Pins.Add(under);
        rig.Monitor.Refresh();
        using var click = new MouseEvent(50, 30);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_MBUTTONDOWN, click.Ptr);

        Assert.Equal(42, result);
        Assert.Empty(under.Calls);
        Assert.Empty(elsewhere.Calls);
    }

    /// <summary>nCode &lt; 0 가드는 래퍼 소유 — Ctrl도 읽지 않는다.</summary>
    [Fact]
    public void Proc_NegativeCode_PassesThrough_WithoutReadingCtrl()
    {
        var rig = new Rig();
        rig.Pins.Add(new FakePin(clickThrough: true, Area));
        rig.Monitor.Refresh();
        rig.CtrlThunk = () => throw new InvalidOperationException("Ctrl을 읽었다");
        using var click = new MouseEvent(50, 30);

        nint result = rig.Fake.Fire(-1, NativeMethods.WM_MBUTTONDOWN, click.Ptr);

        Assert.Equal(42, result);
        var next = Assert.Single(rig.Fake.Nexts);
        Assert.Equal(-1, next.Code);
    }
}
