using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyRemapFlow"/>의 증인 (40단계, ARCH-8/AC-23). 억제 → 모달 → 재등록 → 복원의 순서와 '취소·예외에도 복원'을 잠근다.
/// </summary>
public class HotkeyRemapFlowTests
{
    private static readonly HotkeyDef Captured = new(Modifiers: 0x0001, VirtualKey: 0x42);

    [Fact]
    public void Run_Captured_SuppressRemapRestore_InOrder()
    {
        var host = new FakeSettingsHost();

        var result = HotkeyRemapFlow.Run(host, "undo", () => { host.Calls.Add("Dialog"); return Captured; });

        Assert.Equal(Captured, result);
        Assert.Equal(["Suppress", "Dialog", "Remap:undo:1+66", "Restore"], host.Calls);
    }

    [Fact]
    public void Run_Cancelled_DoesNotRemap_ButRestores()
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

    [Fact]
    public void Run_RemapThrows_StillRestores()
    {
        var host = new ThrowingRemapHost();

        Assert.Throws<InvalidOperationException>(() => HotkeyRemapFlow.Run(host, "undo", () => Captured));

        Assert.Equal(["Suppress", "Restore"], host.Calls);
    }

    private sealed class ThrowingRemapHost : ISettingsHost
    {
        public List<string> Calls { get; } = [];
        public AppSettings Settings { get; } = new();
        public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys => [];
        public void RemapHotkey(string id, HotkeyDef def) => throw new InvalidOperationException("remap failed");
        public void SuppressHotkeys() => Calls.Add("Suppress");
        public void RestoreHotkeys() => Calls.Add("Restore");
        public void ApplyGeneralSettings(AppSettings updated) { }
        public void CheckForUpdates() { }
        public void ExitApp() { }
    }
}
