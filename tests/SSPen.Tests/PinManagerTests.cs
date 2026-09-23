using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Interop;
using SSPen.Pin;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="PinManager.Adopt"/>의 배선 증인 (83단계, A7-1, AC-17). 핀이 <b>스스로</b> 클릭 통과를 켜는 두 경로(크롬 '클릭 통과'
/// 버튼, 창 안 Ctrl+가운데 버튼)는 <see cref="PinWindow.ClickThroughChanged"/>만 올린다. 그 구독이 복귀 훅의
/// <see cref="PinClickThroughMonitor.Refresh"/>를 부르지 않으면 WH_MOUSE_LL은 끝내 설치되지 않고, 입력을 받지 못하는 통과 핀은
/// 다른 핀을 만들거나 닫기 전까지 되찾을 수 없다 — AGENTS L35의 "값만 있고 Refresh 계기가 없는 게이트 항" 부류다.
/// <see cref="PinClickThroughMonitorTests"/>는 게이트를 잠그지만 "언제 Refresh되는가"는 여기서 잠근다.
/// 창은 띄우지 않는다(Show 없음, Hwnd = 0): <c>WindowStyling.SetClickThrough</c>는 OS에서 조용히 실패하고 상태·이벤트만 돈다.
/// </summary>
public class PinManagerTests
{
    [Fact]
    public void Adopt_NonClickThroughPin_JoinsPinsWithoutInstallingHook() => RunSta(() =>
    {
        var (mgr, fake) = NewManager();
        var pin = NewPin();

        mgr.Adopt(pin);

        Assert.Same(pin, Assert.Single(mgr.Pins));
        Assert.False(fake.IsInstalled);
        Assert.Empty(fake.Installs);
    });

    [Fact]
    public void Adopt_PinEngagesClickThrough_InstallsReclaimHook() => RunSta(() =>
    {
        var (mgr, fake) = NewManager();
        var pin = NewPin();
        mgr.Adopt(pin);

        pin.SetClickThrough(true);

        Assert.True(fake.IsInstalled);
        var install = Assert.Single(fake.Installs);
        Assert.Equal(NativeMethods.WH_MOUSE_LL, install.HookId);
    });

    [Fact]
    public void Adopt_PinDisengagesClickThrough_UninstallsReclaimHook() => RunSta(() =>
    {
        var (mgr, fake) = NewManager();
        var pin = NewPin();
        mgr.Adopt(pin);

        pin.SetClickThrough(true);
        pin.SetClickThrough(false);

        Assert.False(fake.IsInstalled);
        Assert.Equal([(nint)0x1000], fake.Uninstalls);
    });

    [Fact]
    public void Adopt_EngageClickThrough_RaisesClickThroughEngagedOnce_AndDisengageDoesNot() => RunSta(() =>
    {
        var (mgr, _) = NewManager();
        var pin = NewPin();
        mgr.Adopt(pin);
        int engaged = 0;
        mgr.ClickThroughEngaged += () => engaged++;

        pin.SetClickThrough(true);
        pin.SetClickThrough(false);

        Assert.Equal(1, engaged);
    });

    [Fact]
    public void Adopt_EngagedHandlerThrows_HookAlreadyInstalled() => RunSta(() =>
    {
        // Refresh가 토스트 계기보다 먼저다 — 알림 쪽이 던져도 되찾는 경로는 이미 살아 있어야 한다.
        var (mgr, fake) = NewManager();
        var pin = NewPin();
        mgr.Adopt(pin);
        mgr.ClickThroughEngaged += () => throw new InvalidOperationException("토스트 실패 흉내");

        Assert.Throws<InvalidOperationException>(() => pin.SetClickThrough(true));

        Assert.True(fake.IsInstalled);
    });

    [Fact]
    public void Adopt_ClosedClickThroughPin_RemovedFromPins_AndHookReleased() => RunSta(() =>
    {
        var (mgr, fake) = NewManager();
        var pin = NewPin();
        mgr.Adopt(pin);
        int changed = 0;
        mgr.PinsChanged += () => changed++;
        pin.SetClickThrough(true);

        // 띄운 적 없는 창이라 HideThenClose는 곧바로 Close한다 — PinClosed는 그 전에 동기로 온다.
        pin.ClosePin();

        Assert.Empty(mgr.Pins);
        Assert.False(fake.IsInstalled);
        Assert.Equal(1, changed);
    });

    private static (PinManager Manager, FakeHookInstaller Fake) NewManager()
    {
        var fake = new FakeHookInstaller();
        return (new PinManager(() => 0, fake), fake);
    }

    private static PinWindow NewPin()
    {
        var image = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[256], 32);
        return new PinWindow(image, new PhysicalRect(0, 0, 8, 8), () => 0, () => false);
    }
}
