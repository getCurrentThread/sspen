using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 설정 창의 단축키 재지정 보류분 (67단계, A6-2). 다른 모든 항목처럼 확인을 눌러야 적용되므로, 캡처한 조합은 여기 쌓였다가
/// 확인 시 <see cref="Drain"/>으로 한꺼번에 나간다.
///
/// 예전에는 이 상태가 창 private 사전(<c>_pendingHotkeys</c>)과 행마다의 closure 변수(<c>effective = def</c>) 두 벌이었고,
/// "한 창에서 두 항목을 같은 조합으로 바꾸는 경우를 잡으려면 보류분도 충돌 표에 있어야 한다"는 합성(<see cref="Overlay"/>)이
/// 창 메서드 안에만 있어 '보류 편집이 조합을 차지한다'·'보류 편집이 옛 조합을 비운다'(임시 조합을 거친 맞바꾸기)에 증인이 없었다.
///
/// 호스트를 받지 않는다: 억제 → 모달 → 복원(ARCH-8)과 '캡처 시점에는 설정을 쓰지 않음'은 <see cref="HotkeyRemapFlow"/>의 계약이고,
/// 이 클래스는 호스트를 모르므로 그 계약을 구조적으로 깰 수 없다.
/// 순서는 <see cref="Dictionary{TKey,TValue}"/> 의미 그대로다 — 삭제가 없으므로 첫 스테이징 순서를 유지하고, 같은 id를 다시 스테이징하면
/// 값만 바뀌고 자리는 그대로다.
/// </summary>
public sealed class HotkeyDraft
{
    private readonly Dictionary<string, HotkeyDef> _staged = [];

    /// <summary>보류분이 없다.</summary>
    public bool IsEmpty => _staged.Count == 0;

    /// <summary>확정된 캡처를 보류한다. 같은 id면 마지막 값이 이긴다.</summary>
    public void Stage(string id, HotkeyDef def) => _staged[id] = def;

    /// <summary>보류 값이 있으면 그것, 없으면 <paramref name="live"/> — 캡처 대화상자의 초기 표시값.</summary>
    public HotkeyDef EffectiveFor(string id, HotkeyDef live) =>
        _staged.TryGetValue(id, out var pending) ? pending : live;

    /// <summary>라이브 표에 보류분을 덮은 표 — 충돌 판정의 입력. 행 순서와 표시명은 라이브 표 그대로다.</summary>
    public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> Overlay(
        IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> live) =>
        live.Select(entry => (entry.Id, entry.Name, EffectiveFor(entry.Id, entry.Effective))).ToList();

    /// <summary>보류분을 반영한 표에서 <see cref="HotkeyConflictRules.Find"/>를 부른다.</summary>
    /// <returns>충돌하는 기능의 표시명, 없으면 null.</returns>
    public string? Conflict(
        IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> live,
        string editingId,
        HotkeyDef candidate,
        int quickColorSlots) =>
        HotkeyConflictRules.Find(Overlay(live), editingId, candidate, quickColorSlots);

    /// <summary>보류분을 스테이징 순서대로 복사해 돌려주고 비운다.</summary>
    public IReadOnlyList<(string Id, HotkeyDef Def)> Drain()
    {
        var drained = _staged.Select(pair => (pair.Key, pair.Value)).ToList();
        _staged.Clear();
        return drained;
    }
}
