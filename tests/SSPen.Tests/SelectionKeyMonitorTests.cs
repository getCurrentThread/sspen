using System.Runtime.InteropServices;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Interop;
using SSPen.Shell;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <see cref="SelectionKeyMonitor"/>의 헤드리스 증인 (53단계, R3/R4). 순수 <see cref="SelectionKeyMonitor.Decide"/>의 진리표와 평가 순서,
/// 게이트에 따른 설치/해제, Dispose 래치, 그리고 <see cref="FakeHookInstaller"/>로 쏜 합성 키 이벤트의 소비/통과를 잠근다.
/// 리그 모양이 계약이다: 술어는 교체 가능한 thunk로 주입해 "먼저 설치, 그 다음 독약" 순서로 게이트가 읽히지 않음을 증언한다
/// (Refresh 자체가 blocked를 읽으므로 처음부터 던지는 thunk를 주면 설치가 안 된다). 행동은 디스패처로 미뤄지므로 소비 사실들은
/// STA에서 <see cref="DispatcherPump.Drain"/>으로 확인한다. lParam은 항상 실제 KBDLLHOOKSTRUCT를 가리킨다 — 0으로 쏘지 않는다.
/// </summary>
public class SelectionKeyMonitorTests
{
    // 프로덕션이 쓰지 않는 메시지라 NativeMethods에 두지 않는다.
    private const int WM_KEYUP = 0x0101;

    private sealed class Rig
    {
        public AppState State { get; } = new();

        public SelectionModel Selection { get; } = new();

        public FakeHookInstaller Fake { get; } = new();

        public Func<bool> BlockedThunk { get; set; } = () => false;

        public Func<bool> ModifierThunk { get; set; } = () => false;

        public int ClearCount { get; private set; }

        public int DeleteCount { get; private set; }

        public SelectionKeyMonitor Monitor { get; }

        /// <summary>게이트가 서는 리그: 선택 도구 + 요소 1개 + blocked 거짓 (SurfacesVisible 기본 true, ClickThrough 기본 false).</summary>
        public Rig()
        {
            State.ActiveTool = ToolKind.Select;
            Selection.Add(TestGeometry.NewStroke());
            Monitor = new SelectionKeyMonitor(
                Dispatcher.CurrentDispatcher, State, Selection,
                blocked: () => BlockedThunk(),
                nonShiftModifierDown: () => ModifierThunk(),
                clearSelection: () => ClearCount++,
                deleteSelection: () => DeleteCount++,
                hooks: Fake);
        }

        public static Func<bool> Poison(string what) => () => throw new InvalidOperationException($"{what}를 읽었다");
    }

    /// <summary>실제 KBDLLHOOKSTRUCT를 비관리 메모리에 두고 포인터를 lParam으로 준다.</summary>
    private sealed class KeyEvent : IDisposable
    {
        public nint Ptr { get; }

        public KeyEvent(int vkCode)
        {
            Ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.KBDLLHOOKSTRUCT>());
            Marshal.StructureToPtr(new NativeMethods.KBDLLHOOKSTRUCT { vkCode = (uint)vkCode }, Ptr, false);
        }

        public void Dispose() => Marshal.FreeHGlobal(Ptr);
    }

    // ---- Decide ----

    [Theory]
    [InlineData(WM_KEYUP, NativeMethods.VK_ESCAPE, true, false, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_SYSKEYDOWN, NativeMethods.VK_ESCAPE, true, false, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_ESCAPE, false, false, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_ESCAPE, true, true, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_DELETE, true, true, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_ESCAPE, true, false, SelectionKeyVerdict.ClearSelection)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_DELETE, true, false, SelectionKeyVerdict.DeleteSelection)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_BACK, true, false, SelectionKeyVerdict.DeleteSelection)]
    [InlineData(NativeMethods.WM_KEYDOWN, 0x41 /* A */, true, false, SelectionKeyVerdict.Pass)]
    [InlineData(NativeMethods.WM_KEYDOWN, NativeMethods.VK_SHIFT, true, false, SelectionKeyVerdict.Pass)]
    public void Decide_Table(int message, int vkCode, bool gateHolds, bool modifierDown, SelectionKeyVerdict expected) =>
        Assert.Equal(expected, SelectionKeyMonitor.Decide(message, vkCode, () => gateHolds, () => modifierDown));

    /// <summary>평가 순서 계약 1: WM_KEYDOWN이 아니면 게이트도 수식키도 읽지 않는다.</summary>
    [Theory]
    [InlineData(WM_KEYUP)]
    [InlineData(NativeMethods.WM_SYSKEYDOWN)]
    public void Decide_NonKeyDown_ReadsNeitherGateNorModifier(int message) =>
        Assert.Equal(SelectionKeyVerdict.Pass, SelectionKeyMonitor.Decide(message, NativeMethods.VK_ESCAPE, Rig.Poison("게이트"), Rig.Poison("수식키")));

    /// <summary>평가 순서 계약 2: 게이트가 거짓이면 수식키(OS 호출)를 읽지 않는다.</summary>
    [Fact]
    public void Decide_GateFails_DoesNotReadModifier() =>
        Assert.Equal(SelectionKeyVerdict.Pass, SelectionKeyMonitor.Decide(NativeMethods.WM_KEYDOWN, NativeMethods.VK_DELETE, () => false, Rig.Poison("수식키")));

    // ---- Refresh / Dispose ----

    [Fact]
    public void Refresh_GateHolds_InstallsKeyboardHookOnce()
    {
        var rig = new Rig();

        rig.Monitor.Refresh();
        rig.Monitor.Refresh();

        var install = Assert.Single(rig.Fake.Installs);
        Assert.Equal(NativeMethods.WH_KEYBOARD_LL, install.HookId);
        Assert.True(rig.Fake.IsInstalled);
    }

    [Fact]
    public void Refresh_GateNotHolding_NeverInstalls()
    {
        var rig = new Rig();
        rig.Selection.Clear();

        rig.Monitor.Refresh();

        Assert.Empty(rig.Fake.Installs);
    }

    /// <summary>게이트 네 항 + blocked 중 하나만 거짓이 돼도 해제한다. ClickThrough=true 행은 도구도 None으로 내린다(AppState.ClickThrough setter) — IsInteractive 단독 항은 hidden 행이 증언한다.</summary>
    [Theory]
    [InlineData("tool")]
    [InlineData("clickthrough")]
    [InlineData("hidden")]
    [InlineData("selection")]
    [InlineData("blocked")]
    public void Refresh_AnyGateTermFalse_Uninstalls(string term)
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        Assert.True(rig.Fake.IsInstalled);

        switch (term)
        {
            case "tool": rig.State.ActiveTool = ToolKind.Pen; break;
            case "clickthrough": rig.State.ClickThrough = true; break;
            case "hidden": rig.State.SurfacesVisible = false; break;
            case "selection": rig.Selection.Clear(); break;
            case "blocked": rig.BlockedThunk = () => true; break;
            default: throw new Xunit.Sdk.XunitException($"모르는 항 {term}");
        }
        rig.Monitor.Refresh();

        Assert.Equal([0x1000], rig.Fake.Uninstalls);
        Assert.False(rig.Fake.IsInstalled);
    }

    /// <summary>래치 보존: Dispose 뒤 Refresh는 게이트가 서 있어도 무동작 (PinClickThroughMonitor와 다른 소유자 결정).</summary>
    [Fact]
    public void Refresh_AfterDispose_IsNoOp_EvenWhenGateHolds()
    {
        var rig = new Rig();
        rig.Monitor.Refresh();

        rig.Monitor.Dispose();
        rig.Monitor.Refresh();

        Assert.Single(rig.Fake.Installs);
        Assert.Equal([0x1000], rig.Fake.Uninstalls);
    }

    [Fact]
    public void Dispose_Twice_UninstallsOnce_AndDisposeWithoutInstallIsNoOp()
    {
        var fresh = new Rig();
        fresh.Monitor.Dispose();
        Assert.Empty(fresh.Fake.Uninstalls);

        var installed = new Rig();
        installed.Monitor.Refresh();
        installed.Monitor.Dispose();
        installed.Monitor.Dispose();
        Assert.Single(installed.Fake.Uninstalls);
    }

    // ---- 훅 콜백 (합성 이벤트) ----

    [Fact]
    public void Proc_Escape_WhileGateHolds_Consumes_AndDefersClearSelection() => RunSta(() =>
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        using var key = new KeyEvent(NativeMethods.VK_ESCAPE);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_KEYDOWN, key.Ptr);

        Assert.Equal(1, result);
        Assert.Empty(rig.Fake.Nexts);
        Assert.Equal(0, rig.ClearCount); // 콜백 안에서 실행하지 않는다 — 디스패처로 미룬다
        DispatcherPump.Drain(Dispatcher.CurrentDispatcher);
        Assert.Equal(1, rig.ClearCount);
        Assert.Equal(0, rig.DeleteCount);
    });

    [Theory]
    [InlineData(NativeMethods.VK_DELETE)]
    [InlineData(NativeMethods.VK_BACK)]
    public void Proc_DeleteOrBackspace_Consumes_AndDefersDeleteSelection(int vkCode) => RunSta(() =>
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        using var key = new KeyEvent(vkCode);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_KEYDOWN, key.Ptr);

        Assert.Equal(1, result);
        Assert.Empty(rig.Fake.Nexts);
        DispatcherPump.Drain(Dispatcher.CurrentDispatcher);
        Assert.Equal(1, rig.DeleteCount);
        Assert.Equal(0, rig.ClearCount);
    });

    /// <summary>훅 안 재판정 (AGENTS: "a stale hook must never swallow another app's Delete") — Refresh 없이 선택이 비면 통과한다.</summary>
    [Fact]
    public void Proc_GateDroppedWithoutRefresh_PassesThrough_AndSchedulesNothing() => RunSta(() =>
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        rig.Selection.Clear(); // 리그는 SelectionChanged를 Refresh에 잇지 않는다 — 낡은 훅
        using var key = new KeyEvent(NativeMethods.VK_ESCAPE);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_KEYDOWN, key.Ptr);

        Assert.Equal(42, result);
        Assert.Single(rig.Fake.Nexts);
        DispatcherPump.Drain(Dispatcher.CurrentDispatcher);
        Assert.Equal(0, rig.ClearCount);
        Assert.Equal(0, rig.DeleteCount);
    });

    [Fact]
    public void Proc_NonShiftModifierDown_PassesThrough()
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        rig.ModifierThunk = () => true;
        using var key = new KeyEvent(NativeMethods.VK_ESCAPE);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_KEYDOWN, key.Ptr);

        Assert.Equal(42, result);
        Assert.Single(rig.Fake.Nexts);
    }

    /// <summary>먼저 설치, 그 다음 독약: WM_SYSKEYDOWN은 게이트(blocked)를 읽기 전에 통과한다.</summary>
    [Fact]
    public void Proc_SysKeyDown_PassesThrough_WithoutReadingGate()
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        rig.BlockedThunk = Rig.Poison("게이트");
        using var key = new KeyEvent(NativeMethods.VK_ESCAPE);

        nint result = rig.Fake.Fire(0, NativeMethods.WM_SYSKEYDOWN, key.Ptr);

        Assert.Equal(42, result);
        Assert.Single(rig.Fake.Nexts);
    }

    /// <summary>nCode &lt; 0 가드는 래퍼 소유 — 콜백(디코드·게이트)에 닿지 않고 같은 인자로 CallNext.</summary>
    [Fact]
    public void Proc_NegativeCode_NeverReachesGate()
    {
        var rig = new Rig();
        rig.Monitor.Refresh();
        rig.BlockedThunk = Rig.Poison("게이트");
        using var key = new KeyEvent(NativeMethods.VK_ESCAPE);

        nint result = rig.Fake.Fire(-1, NativeMethods.WM_KEYDOWN, key.Ptr);

        Assert.Equal(42, result);
        var next = Assert.Single(rig.Fake.Nexts);
        Assert.Equal((0x1000, -1, (nint)NativeMethods.WM_KEYDOWN, key.Ptr), next);
    }
}
