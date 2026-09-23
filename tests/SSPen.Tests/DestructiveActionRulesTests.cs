using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="DestructiveActionRules"/>의 증인 (AC-19). 핵심 계약은 "마찰은 실행취소 가능성을 따른다"는 것이다 —
/// 되돌릴 수 있는 판서 지우기에는 대화상자를 붙이지 않고, 되돌릴 수 없는 핀 닫기에만 붙인다.
/// </summary>
public class DestructiveActionRulesTests
{
    /// <summary>핀은 원장 밖이라 되돌릴 수 없다 — 여기만 확인을 받는다.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 3)]
    public void ClearAll_PinsOpen_RequiresConfirmation(int inkCount, int pinCount)
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount, pinCount);

        Assert.True(prompt.NeedsConfirm);
        Assert.True(prompt.HasAnything);
        Assert.Equal(pinCount, prompt.PinCount);
    }

    /// <summary>판서만 있으면 실행취소 1회로 전부 돌아온다 — 대화상자는 마찰만 늘린다.</summary>
    [Fact]
    public void ClearAll_InkOnly_DoesNotRequireConfirmation()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: 12, pinCount: 0);

        Assert.False(prompt.NeedsConfirm);
        Assert.True(prompt.HasAnything);
        Assert.Equal(0, prompt.PinCount);
    }

    /// <summary>지울 것이 없으면 확인도 알림도 없다 (무동작은 말을 걸지 않는다).</summary>
    [Fact]
    public void ClearAll_NothingToClear_NeedsNothingAndReportsNothing()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: 0, pinCount: 0);

        Assert.False(prompt.NeedsConfirm);
        Assert.False(prompt.HasAnything);
    }

    /// <summary>음수는 계산 실수의 산물이지 사용자의 상태가 아니다 — 0으로 접어 대화상자를 띄우지 않는다.</summary>
    [Fact]
    public void ClearAll_NegativeCounts_AreTreatedAsEmpty()
    {
        var prompt = DestructiveActionRules.ClearAll(inkCount: -3, pinCount: -1);

        Assert.False(prompt.NeedsConfirm);
        Assert.False(prompt.HasAnything);
        Assert.Equal(0, prompt.PinCount);
    }

    // ---- 완료 알림 (85단계, A1-3) ----

    /// <summary>판서를 지웠으면 오늘 문구와 바이트까지 같다 — 핀을 함께 닫았어도 되돌릴 수 있는 것은 판서이므로 안내는 그대로다.</summary>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    public void DoneNotice_InkCleared_WithCombo_EqualsTodayText(int clearedInk, int closedPins)
    {
        var text = DestructiveActionRules.DoneNotice(clearedInk, closedPins, undoCombo: "Alt+Shift+6");

        Assert.Equal(Strings.ClearAllDoneWithUndo("Alt+Shift+6"), text);
    }

    /// <summary>실행취소 단축키가 해제돼 있으면 조합키 없이 완료만 알린다 (오늘 문구와 동일).</summary>
    [Fact]
    public void DoneNotice_InkCleared_NoCombo_IsPlainDone()
    {
        Assert.Equal(Strings.ClearAllDone, DestructiveActionRules.DoneNotice(clearedInk: 1, closedPins: 0, undoCombo: null));
    }

    /// <summary>
    /// 판서 0개에 핀만 닫혔으면 원장 항목이 생기지 않는다 — '판서를 지웠습니다 (되돌리기: …)'를 믿고 실행취소를 누르면
    /// 그 이전의 무관한 조작이 되살아난다. 그래서 닫은 핀 수만 알리고 되돌리기 안내는 뺀다.
    /// </summary>
    [Fact]
    public void DoneNotice_PinsOnly_DoesNotMentionUndo()
    {
        var text = DestructiveActionRules.DoneNotice(clearedInk: 0, closedPins: 2, undoCombo: "Alt+Shift+6");

        Assert.Equal(Strings.ClearAllPinsClosed(2), text);
        Assert.DoesNotContain("Alt+Shift+6", text);
        Assert.DoesNotContain(Strings.ClearAllDone, text);
    }

    /// <summary>아무것도 지우거나 닫지 않았으면 알림이 없다 (무동작은 말을 걸지 않는다). 음수는 0으로 본다.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, -1)]
    public void DoneNotice_NothingCleared_IsNull(int clearedInk, int closedPins)
    {
        Assert.Null(DestructiveActionRules.DoneNotice(clearedInk, closedPins, undoCombo: "Alt+Shift+6"));
    }
}
