using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyRemapFlow"/>의 증인 (40단계, ARCH-8/AC-23). 억제 → 모달 → 복원의 순서와 '취소·예외에도 복원'을 잠근다.
/// 재지정 <b>쓰기</b>가 여기에 없다는 것도 계약이다: 예전에는 캡처 직후 곧바로 SaveNow까지 해서
/// 취소를 눌러도 단축키만 이미 디스크에 남는 비대칭이 있었다.
/// </summary>
public class HotkeyRemapFlowTests
{
    private static readonly HotkeyDef Captured = new(Modifiers: 0x0001, VirtualKey: 0x42);

    [Fact]
    public void Run_Captured_SuppressDialogRestore_InOrder()
    {
        var host = new FakeSettingsHost();

        var result = HotkeyRemapFlow.Run(host, "undo", () => { host.Calls.Add("Dialog"); return Captured; });

        Assert.Equal(Captured, result);
        Assert.Equal(["Suppress", "Dialog", "Restore"], host.Calls);
    }

    /// <summary>확정해도 설정을 쓰지 않는다 — 반영은 창이 확인 시점에 모아서 한다.</summary>
    [Fact]
    public void Run_Captured_DoesNotWriteSettings()
    {
        var host = new FakeSettingsHost();

        HotkeyRemapFlow.Run(host, "undo", () => Captured);

        Assert.DoesNotContain(host.Calls, call => call.StartsWith("Remap", StringComparison.Ordinal));
        Assert.Empty(host.Settings.Hotkeys);
    }

    [Fact]
    public void Run_Cancelled_ReturnsNull_ButRestores()
    {
        var host = new FakeSettingsHost();

        var result = HotkeyRemapFlow.Run(host, "undo", () => null);

        Assert.Null(result);
        Assert.Equal(["Suppress", "Restore"], host.Calls);
    }

    [Fact]
    public void Run_DialogThrows_StillRestores_AndPropagates()
    {
        var host = new FakeSettingsHost();

        Assert.Throws<InvalidOperationException>(() =>
            HotkeyRemapFlow.Run(host, "undo", () => throw new InvalidOperationException("boom")));

        Assert.Equal(["Suppress", "Restore"], host.Calls);
    }
}
