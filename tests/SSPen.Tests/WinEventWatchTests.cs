using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="WinEventWatch"/> 래퍼의 증인 (54단계 L3, LowLevelHookTests 선례). <see cref="FakeWinEventInstaller"/>로 OS 없이 돈다.
/// 잠그는 것: 이벤트 범위 전달, 멱등 Install/Uninstall, 설치 실패 시 재시도, Dispose = Uninstall(래치 없음), 프로시저 인스턴스
/// 동일성(GC 고정), 콜백이 (이벤트, hwnd, idObject)를 받는다, 해제 뒤 잔여 이벤트는 버린다, 설치기를 나눠 쓰는 두 워치의 해제는 자기 경로만 푼다(92단계).
/// </summary>
public class WinEventWatchTests
{
    private sealed class Rig
    {
        public FakeWinEventInstaller Fake { get; } = new();

        public List<(uint Event, nint Hwnd, int IdObject)> Seen { get; } = [];

        public WinEventWatch Watch { get; }

        public Rig(uint min = NativeMethods.EVENT_OBJECT_REORDER, uint max = NativeMethods.EVENT_OBJECT_REORDER)
        {
            Watch = new WinEventWatch(min, max, (e, h, o) => Seen.Add((e, h, o)), Fake);
        }
    }

    [Fact]
    public void Install_PassesEventRange_AndReportsInstalled()
    {
        var rig = new Rig(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);

        Assert.True(rig.Watch.Install());

        var install = Assert.Single(rig.Fake.Installs);
        Assert.Equal(NativeMethods.EVENT_SYSTEM_FOREGROUND, install.Min);
        Assert.Equal(NativeMethods.EVENT_SYSTEM_FOREGROUND, install.Max);
        Assert.True(rig.Watch.IsInstalled);
    }

    [Fact]
    public void Install_Twice_InstallsOnce()
    {
        var rig = new Rig();

        Assert.True(rig.Watch.Install());
        Assert.True(rig.Watch.Install());

        Assert.Single(rig.Fake.Installs);
    }

    [Fact]
    public void Install_InstallerReturnsZero_ReturnsFalse_AndRetriesNextTime()
    {
        var rig = new Rig();
        rig.Fake.NextHandle = 0;

        Assert.False(rig.Watch.Install());
        Assert.False(rig.Watch.IsInstalled);

        rig.Fake.NextHandle = 0x77;
        Assert.True(rig.Watch.Install());
        Assert.Equal(2, rig.Fake.Installs.Count);
        Assert.Same(rig.Fake.Installs[0].Proc, rig.Fake.Installs[1].Proc); // GC 고정 인스턴스 동일성
    }

    [Fact]
    public void Uninstall_PassesLiveHandle_AndIsIdempotent()
    {
        var rig = new Rig();
        rig.Watch.Install();

        rig.Watch.Uninstall();
        rig.Watch.Uninstall();

        Assert.Equal([0x2000], rig.Fake.Uninstalls);
        Assert.False(rig.Watch.IsInstalled);
    }

    [Fact]
    public void Dispose_Uninstalls_AndAllowsReinstall()
    {
        var rig = new Rig();
        rig.Watch.Install();

        rig.Watch.Dispose();
        Assert.False(rig.Watch.IsInstalled);

        Assert.True(rig.Watch.Install());
        Assert.Equal(2, rig.Fake.Installs.Count);
    }

    [Fact]
    public void Fire_WhileInstalled_DeliversEventHwndAndObject()
    {
        var rig = new Rig();
        rig.Watch.Install();

        rig.Fake.Fire(NativeMethods.EVENT_OBJECT_REORDER, 0x1234, idObject: -4, idChild: 9);

        var seen = Assert.Single(rig.Seen);
        Assert.Equal((NativeMethods.EVENT_OBJECT_REORDER, (nint)0x1234, -4), seen);
    }

    /// <summary>
    /// 한 설치기를 두 워치가 나눠 쓸 때(ZBandVerifier의 REORDER·FOREGROUND) 가짜의 기본 핸들은 경로마다 다르다 —
    /// 한쪽을 풀어도 다른 쪽 경로는 살아 있다. 둘이 같은 핸들을 받던 때는 FOREGROUND를 풀면 먼저 설치된 REORDER 경로가 풀렸다 (92단계).
    /// </summary>
    [Fact]
    public void Uninstall_TwoWatchesOnOneInstaller_DefaultHandlesDiffer_UninstallsOnlyItsOwnRoute()
    {
        var fake = new FakeWinEventInstaller();
        var reorder = new WinEventWatch(
            NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER, (_, _, _) => { }, fake);
        var foreground = new WinEventWatch(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, (_, _, _) => { }, fake);
        Assert.True(reorder.Install());
        Assert.True(foreground.Install());

        foreground.Uninstall();

        Assert.Equal([fake.NextHandle + 1], fake.Uninstalls);
        Assert.Equal([(NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER)], fake.LiveRanges);
    }

    [Fact]
    public void Fire_AfterUninstall_IsDropped()
    {
        var rig = new Rig();
        rig.Watch.Install();
        rig.Watch.Uninstall();

        rig.Fake.Fire(NativeMethods.EVENT_OBJECT_REORDER, 0x1234);

        Assert.Empty(rig.Seen);
    }
}
