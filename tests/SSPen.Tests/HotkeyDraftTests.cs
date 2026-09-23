using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyDraft"/>의 증인 (67단계, A6-2). 보류분을 충돌 표에 덮는 합성이 설정 창 private 메서드에만 있던 동안
/// HotkeyConflictRulesTests는 라이브 표만 넣어 검증했다 — '보류 편집이 조합을 차지한다', '보류 편집이 옛 조합을 비운다'
/// (임시 조합을 거친 맞바꾸기)에는 증인이 없었다. 호스트를 모르는 순수 클래스라 기본 MTA 스레드에서 돈다.
/// </summary>
public class HotkeyDraftTests
{
    private const uint AltShift = 0x0001 | 0x0004;
    private const uint CtrlShift = 0x0002 | 0x0004;
    private const int QuickColorSlots = 6;

    private static readonly HotkeyDef ComboA = new(AltShift, 0x41);
    private static readonly HotkeyDef ComboB = new(AltShift, 0x42);
    private static readonly HotkeyDef ComboTemp = new(AltShift, 0x43);

    /// <summary>라이브 표: 펜 = Alt+Shift+A, 지우개 = Alt+Shift+B.</summary>
    private static IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> Live() =>
    [
        ("pen", "펜", ComboA),
        ("eraser", "지우개", ComboB),
    ];

    /// <summary>보류 편집이 조합을 차지한다: 펜 → 임시 조합을 스테이징하면, 지우개가 같은 조합을 고를 때 펜과 충돌한다.</summary>
    [Fact]
    public void Conflict_PendingEditOccupiesCombo_ReportsThatEntry()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        Assert.Equal("펜", draft.Conflict(Live(), "eraser", ComboTemp, QuickColorSlots));
    }

    /// <summary>보류 편집이 옛 조합을 비운다: 펜을 다른 조합으로 옮겨 두면 펜의 라이브 조합은 지우개가 가져갈 수 있다.</summary>
    [Fact]
    public void Conflict_PendingEditReleasesLiveCombo_IsFree()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        Assert.Null(draft.Conflict(Live(), "eraser", ComboA, QuickColorSlots));
    }

    /// <summary>자기 보류 조합을 다시 고르는 것은 충돌이 아니다.</summary>
    [Fact]
    public void Conflict_SelfWithPending_IsNotConflict()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        Assert.Null(draft.Conflict(Live(), "pen", ComboTemp, QuickColorSlots));
    }

    /// <summary>보류분을 덮어도 바로가기 색상 예약 조합은 그대로 보고된다.</summary>
    [Fact]
    public void Conflict_QuickColorReserved_StillReported()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        Assert.Equal($"{Strings.QuickColorName} 2", draft.Conflict(Live(), "eraser", new HotkeyDef(CtrlShift, 0x32), QuickColorSlots));
    }

    /// <summary>대화상자 초기값: 보류 값이 있으면 그것, 없으면 라이브 값 (예전 closure 재대입과 같은 값).</summary>
    [Fact]
    public void EffectiveFor_Staged_ReturnsPending_ElseLive()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        Assert.Equal(ComboTemp, draft.EffectiveFor("pen", ComboA));
        Assert.Equal(ComboB, draft.EffectiveFor("eraser", ComboB));
    }

    /// <summary>
    /// Dictionary 의미 그대로: 같은 id를 다시 스테이징하면 마지막 값이 이기고 자리는 첫 스테이징 위치다.
    /// 맞바꾸기(지우개 → 임시, 펜 → 지우개의 옛 조합, 지우개 → 펜의 옛 조합)의 Drain 순서는 [(지우개, A), (펜, B)] — 예전 foreach와 같다.
    /// </summary>
    [Fact]
    public void Stage_SameIdTwice_LastWinsAndKeepsFirstPosition()
    {
        var draft = new HotkeyDraft();
        draft.Stage("eraser", ComboTemp);
        draft.Stage("pen", ComboB);
        draft.Stage("eraser", ComboA);

        Assert.Equal([("eraser", ComboA), ("pen", ComboB)], draft.Drain());
    }

    [Fact]
    public void Drain_Empties()
    {
        var draft = new HotkeyDraft();
        draft.Stage("pen", ComboTemp);

        var first = draft.Drain();

        Assert.Single(first);
        Assert.True(draft.IsEmpty);
        Assert.Empty(draft.Drain());
        Assert.Equal(ComboA, draft.EffectiveFor("pen", ComboA));
    }

    [Fact]
    public void IsEmpty_TracksStaging()
    {
        var draft = new HotkeyDraft();
        Assert.True(draft.IsEmpty);

        draft.Stage("pen", ComboTemp);

        Assert.False(draft.IsEmpty);
    }

    /// <summary>덮은 표의 행 순서와 표시명은 라이브 표 그대로다 — 충돌 알림이 가리키는 이름이 바뀌지 않는다.</summary>
    [Fact]
    public void Overlay_KeepsLiveOrderAndNames_ReplacesOnlyStagedCombo()
    {
        var draft = new HotkeyDraft();
        draft.Stage("eraser", ComboTemp);

        var overlay = draft.Overlay(Live());

        Assert.Equal([("pen", "펜", ComboA), ("eraser", "지우개", ComboTemp)], overlay);
    }

    /// <summary>
    /// 창의 충돌 검사를 통과한 스테이징만으로 맞바꾸기(임시 조합 경유)를 하면 최종 표에 중복 조합이 없다 —
    /// 보류분을 <b>한꺼번에</b> 적용하면 안전하다는 증인 (79단계 A6-3 일괄 적용의 전제 — HotkeyRemapFlow.ApplyBatch).
    /// </summary>
    [Fact]
    public void Overlay_AfterAcceptedSwapViaTemp_HasNoDuplicateCombos()
    {
        var draft = new HotkeyDraft();
        var live = Live();
        foreach (var (id, def) in new[] { ("eraser", ComboTemp), ("pen", ComboB), ("eraser", ComboA) })
        {
            Assert.Null(draft.Conflict(live, id, def, QuickColorSlots)); // 창이 받아들이는 경로다.
            draft.Stage(id, def);
        }

        var combos = draft.Overlay(live).Select(e => (e.Effective.Modifiers, e.Effective.VirtualKey)).ToList();

        Assert.Equal(combos.Count, combos.Distinct().Count());
        Assert.Equal([("pen", "펜", ComboB), ("eraser", "지우개", ComboA)], draft.Overlay(live));
    }

    /// <summary>
    /// 특성화(A6-3): 같은 맞바꾸기의 Drain을 라이브 표에 <b>한 건씩</b> 쓰면 첫 건 직후 두 항목이 같은 조합이 된다 —
    /// 78단계까지의 건별 RemapHotkey가 그 순간 RegisterHotKey 하나를 실패시켜 가짜 트레이 경고를 띄운 이유다.
    /// 79단계부터는 한꺼번에 적용하므로 이 중간 상태가 등록에 닿지 않는다 (HotkeyRemapFlowTests.ApplyBatch_SwapViaTemp_NeverReportsRegistrationFailure).
    /// </summary>
    [Fact]
    public void SequentialApplyOfDrain_FirstLeg_ProducesDuplicate()
    {
        var draft = new HotkeyDraft();
        draft.Stage("eraser", ComboTemp);
        draft.Stage("pen", ComboB);
        draft.Stage("eraser", ComboA);
        var live = Live().ToDictionary(e => e.Id, e => e.Effective);

        var (firstId, firstDef) = draft.Drain()[0];
        live[firstId] = firstDef;

        Assert.Equal(("eraser", ComboA), (firstId, firstDef));
        Assert.Equal(live["pen"], live["eraser"]);
    }
}
