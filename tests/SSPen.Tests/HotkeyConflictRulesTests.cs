using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyConflictRules"/>의 증인 (WI-16/AC-23). 이전에는 검출이 아예 없어, 이미 쓰이는 조합을 그대로 받아들이고
/// 충돌은 나중에 RegisterHotKey 실패의 트레이 풍선으로만 스쳐 갔다.
/// </summary>
public class HotkeyConflictRulesTests
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint AltShift = Alt | Shift;
    private const uint KeyA = 0x41;
    private const uint KeyB = 0x42;
    private const uint Key3 = 0x33;

    private static IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> Table() =>
    [
        ("undo", "실행 취소", new HotkeyDef(AltShift, KeyA)),
        ("clear", "전체 지우기", new HotkeyDef(AltShift, KeyB)),
    ];

    [Fact]
    public void Find_CombinationUsedByAnotherEntry_ReturnsThatEntryName()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(AltShift, KeyB), quickColorSlots: 6);

        Assert.Equal("전체 지우기", conflict);
    }

    /// <summary>자기 현재 조합을 다시 고르는 것은 충돌이 아니다 — 흔한 조작이고, 막으면 버그로 읽힌다.</summary>
    [Fact]
    public void Find_ReassigningYourOwnCurrentCombination_IsNotAConflict()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(AltShift, KeyA), quickColorSlots: 6);

        Assert.Null(conflict);
    }

    /// <summary>같은 키라도 수식키 조합이 다르면 다른 단축키다.</summary>
    [Fact]
    public void Find_SameKeyDifferentModifiers_IsNotAConflict()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(Control, KeyB), quickColorSlots: 6);

        Assert.Null(conflict);
    }

    /// <summary>
    /// 바로가기 색상 Ctrl+Shift+1~6은 재지정 목록에 없어 설정 UI가 존재를 모른다 —
    /// 그래서 규칙이 대신 안다. 모르면 사용자가 그 위에 다른 기능을 얹고 하나가 조용히 죽는다.
    /// </summary>
    [Fact]
    public void Find_ReservedQuickColorCombination_IsReported()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(Control | Shift, Key3), quickColorSlots: 6);

        Assert.Equal($"{Strings.QuickColorName} 3", conflict);
    }

    /// <summary>칸 수 밖의 숫자는 예약이 아니다 (오늘 6칸이므로 Ctrl+Shift+7은 비어 있다).</summary>
    [Fact]
    public void Find_QuickColorSlotBeyondTheCount_IsFree()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(Control | Shift, 0x37), quickColorSlots: 6);

        Assert.Null(conflict);
    }

    [Fact]
    public void Find_UnusedCombination_IsFree()
    {
        var conflict = HotkeyConflictRules.Find(Table(), editingId: "undo", new HotkeyDef(Alt, 0x5A), quickColorSlots: 6);

        Assert.Null(conflict);
    }

    [Fact]
    public void Find_EmptyTable_OnlyChecksReservedCombinations()
    {
        Assert.Null(HotkeyConflictRules.Find([], "undo", new HotkeyDef(AltShift, KeyA), quickColorSlots: 6));
        Assert.NotNull(HotkeyConflictRules.Find([], "undo", new HotkeyDef(Control | Shift, 0x31), quickColorSlots: 6));
    }
}
