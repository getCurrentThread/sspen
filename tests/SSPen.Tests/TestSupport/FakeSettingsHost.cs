using SSPen.Settings;
using SSPen.Shell;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ISettingsHost"/>의 기록형 가짜 (40단계 — 이 저장소 최초). 호출 순서를 <see cref="Calls"/>에 남긴다.
/// </summary>
internal sealed class FakeSettingsHost : ISettingsHost
{
    public List<string> Calls { get; } = [];

    public AppSettings Settings { get; } = new();

    public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys { get; init; } = [];

    public void RemapHotkey(string id, HotkeyDef def) => Calls.Add($"Remap:{id}:{def.Modifiers}+{def.VirtualKey}");

    public void SuppressHotkeys() => Calls.Add("Suppress");

    public void RestoreHotkeys() => Calls.Add("Restore");

    public void ApplyGeneralSettings(AppSettings updated) => Calls.Add("Apply");

    public void CheckForUpdates() => Calls.Add("CheckForUpdates");

    public void ExitApp() => Calls.Add("Exit");
}
