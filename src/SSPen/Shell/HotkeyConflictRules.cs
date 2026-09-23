using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 단축키 충돌 검출 (WI-16/AC-23의 순수 코어).
///
/// 이전에는 검출이 <b>아예 없었다</b>: <c>HotkeyCaptureDialog</c>는 맨 수식키와 수식키 없는 키만 거절했고,
/// 이미 쓰이는 조합을 그대로 받아들였다. 충돌은 나중에 <c>RegisterHotKey</c>가 실패할 때
/// <b>트레이 풍선</b>으로 — 즉 그 조합을 만든 창 밖에서, 5초 뒤에, 지나가는 형태로 — 나타났다.
///
/// 예약 조합(<c>Ctrl+Shift+1~6</c> 바로가기 색상, 소유자는 <see cref="QuickColorHotkeys"/>)까지 여기서 안다: 그 여섯은 재지정 목록에 없어
/// 설정 UI가 존재 자체를 모르는데, 사용자가 거기에 다른 기능을 얹으면 조용히 하나가 죽는다.
/// </summary>
public static class HotkeyConflictRules
{
    /// <param name="table">재지정 가능한 단축키 표 (id/표시명/현재 유효 조합).</param>
    /// <param name="editingId">지금 편집 중인 항목. 자기 자신과는 충돌하지 않는다.</param>
    /// <param name="quickColorSlots">바로가기 색상 칸 수 (오늘 6칸).</param>
    /// <returns>충돌하는 기능의 표시명, 없으면 null.</returns>
    public static string? Find(
        IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> table,
        string editingId,
        HotkeyDef candidate,
        int quickColorSlots)
    {
        foreach (var (id, name, effective) in table)
        {
            // 자기 자신의 현재 조합을 다시 고르는 것은 충돌이 아니다 — 흔한 조작이고, 막으면 사용자는 버그로 읽는다.
            if (string.Equals(id, editingId, StringComparison.Ordinal))
            {
                continue;
            }
            if (Same(effective, candidate))
            {
                return name;
            }
        }

        for (int slot = 0; slot < quickColorSlots; slot++)
        {
            // 예약 조합과 표시명은 등록 측과 같은 소유자에서 읽는다 (67단계, A9-3) — 따로 적어 두면 한쪽만 바뀐다.
            if (Same(candidate, QuickColorHotkeys.For(slot)))
            {
                return QuickColorHotkeys.Name(slot);
            }
        }
        return null;
    }

    private static bool Same(HotkeyDef a, HotkeyDef b) =>
        a.Modifiers == b.Modifiers && a.VirtualKey == b.VirtualKey;
}
