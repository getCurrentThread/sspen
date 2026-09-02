using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="LowLevelHook"/> 래퍼의 증인 (52단계, R3/R4 + AC-17). <see cref="FakeHookInstaller"/>로 OS 없이 돈다 (MTA).
/// 잠그는 것: 훅 id 전달, 멱등 Install/Uninstall, 설치 실패 시 재시도, Dispose = Uninstall(래치 없음 — 결정 b), 프로시저 인스턴스
/// 동일성(GC 고정), nCode &lt; 0 가드(콜백 미호출·같은 인자로 CallNext), 소비 = 1 반환 + CallNext 생략, 통과 = CallNext 결과 반환,
/// 콜백이 원시 wParam/lParam을 그대로 받는다(결정 a).
/// </summary>
public class LowLevelHookTests
{
    private sealed class Rig
    {
        public FakeHookInstaller Fake { get; } = new();

        public List<(nint WParam, nint LParam)> Seen { get; } = [];

        public bool Consume { get; set; }

        public LowLevelHook Hook { get; }

        public Rig(int hookId = NativeMethods.WH_KEYBOARD_LL)
        {
            Hook = new LowLevelHook(hookId, (w, l) =>
            {
                Seen.Add((w, l));
                return Consume;
            }, Fake);
        }
    }

    [Fact]
    public void Install_CallsInstallerWithHookId_AndReportsInstalled()
    {
        var rig = new Rig(NativeMethods.WH_MOUSE_LL);

        Assert.True(rig.Hook.Install());

        var install = Assert.Single(rig.Fake.Installs);
        Assert.Equal(NativeMethods.WH_MOUSE_LL, install.HookId);
        Assert.True(rig.Hook.IsInstalled);
    }

    [Fact]
    public void Install_Twice_InstallsOnce_AndReturnsTrue()
    {
        var rig = new Rig();

        Assert.True(rig.Hook.Install());
        Assert.True(rig.Hook.Install());

        Assert.Single(rig.Fake.Installs);
    }

    [Fact]
    public void Install_InstallerReturnsZero_ReturnsFalse_StaysUninstalled_AndRetriesNextTime()
    {
        var rig = new Rig();
        rig.Fake.NextHandle = 0;

        Assert.False(rig.Hook.Install());
        Assert.False(rig.Hook.IsInstalled);

        rig.Fake.NextHandle = 0x2000;
        Assert.True(rig.Hook.Install());
        Assert.Equal(2, rig.Fake.Installs.Count);
        Assert.True(rig.Hook.IsInstalled);
    }

    [Fact]
    public void Uninstall_NotInstalled_IsNoOp()
    {
        var rig = new Rig();

        rig.Hook.Uninstall();

        Assert.Empty(rig.Fake.Uninstalls);
    }

    [Fact]
    public void Uninstall_PassesHandle_ClearsInstalled_AndIsIdempotent()
    {
        var rig = new Rig();
        rig.Hook.Install();

        rig.Hook.Uninstall();
        rig.Hook.Uninstall();

        Assert.Equal([0x1000], rig.Fake.Uninstalls);
        Assert.False(rig.Hook.IsInstalled);
    }

    /// <summary>결정 b: 래치는 소유자 몫 — SelectionKeyMonitor는 래치하고 PinClickThroughMonitor는 하지 않는다. 래퍼가 래치하면 후자를 표현할 수 없다.</summary>
    [Fact]
    public void Dispose_IsUninstall_AndDoesNotLatch_InstallAfterDisposeWorks()
    {
        var rig = new Rig();
        rig.Hook.Install();

        rig.Hook.Dispose();
        Assert.Equal([0x1000], rig.Fake.Uninstalls);
        Assert.False(rig.Hook.IsInstalled);

        Assert.True(rig.Hook.Install());
        Assert.Equal(2, rig.Fake.Installs.Count);
    }

    [Fact]
    public void Proc_PinnedDelegate_IsTheSameInstanceAcrossReinstalls()
    {
        var rig = new Rig();

        rig.Hook.Install();
        rig.Hook.Uninstall();
        rig.Hook.Install();

        Assert.Same(rig.Fake.Installs[0].Proc, rig.Fake.Installs[1].Proc);
    }

    [Fact]
    public void Proc_NegativeCode_CallsNextWithSameArgs_WithoutCallback()
    {
        var rig = new Rig();
        rig.Hook.Install();

        nint result = rig.Fake.Fire(-1, 7, 9);

        Assert.Equal(rig.Fake.CallNextResult, result);
        Assert.Empty(rig.Seen);
        Assert.Equal([(0x1000, -1, 7, 9)], rig.Fake.Nexts);
    }

    [Fact]
    public void Proc_CallbackConsumes_ReturnsOne_AndSkipsCallNext()
    {
        var rig = new Rig { Consume = true };
        rig.Hook.Install();

        nint result = rig.Fake.Fire(0, 0x0100, 0x1234);

        Assert.Equal(1, result);
        Assert.Empty(rig.Fake.Nexts);
    }

    [Fact]
    public void Proc_CallbackPasses_ReturnsCallNextResult_WithSameArgs()
    {
        var rig = new Rig { Consume = false };
        rig.Hook.Install();

        nint result = rig.Fake.Fire(0, 0x0100, 0x1234);

        Assert.Equal(42, result);
        Assert.Equal([(0x1000, 0, 0x0100, 0x1234)], rig.Fake.Nexts);
    }

    /// <summary>결정 a 고정: 래퍼는 페이로드를 해석하지 않는다 — 디코드 시점과 구조체는 호출자(모니터)가 정한다.</summary>
    [Fact]
    public void Proc_CallbackReceivesRawWParamLParam()
    {
        var rig = new Rig();
        rig.Hook.Install();

        rig.Fake.Fire(0, 0x0100, 0x1234);

        Assert.Equal([(0x0100, 0x1234)], rig.Seen);
    }
}
