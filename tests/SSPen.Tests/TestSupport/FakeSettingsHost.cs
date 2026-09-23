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

    /// <summary>일괄 반영 1회 = 기록 1줄 (79단계, A6-3): "RemapBatch:id1,id2,…" (스테이징 순서).</summary>
    public void RemapHotkeys(IReadOnlyList<(string Id, HotkeyDef Def)> batch) =>
        Calls.Add($"RemapBatch:{string.Join(',', batch.Select(entry => entry.Id))}");

    public void SuppressHotkeys() => Calls.Add("Suppress");

    public void RestoreHotkeys() => Calls.Add("Restore");

    public void ApplyGeneralSettings(AppSettings updated) => Calls.Add("Apply");

    public void CheckForUpdates() => Calls.Add("CheckForUpdates");
}
