namespace SSPen.Shell;

/// <summary>전체 지우기 직전의 판정: 확인이 필요한가, 무엇을 몇 개 지우는가.</summary>
public readonly record struct ClearAllPrompt(bool NeedsConfirm, bool HasAnything, int PinCount);

/// <summary>
/// 파괴적 조작의 마찰 판정 (AC-19).
///
/// 마찰을 <b>실행취소 가능성</b>에 맞춘다: 판서 지우기는 원장 1항목이라 Alt+Shift+6 한 번으로 전부 돌아오지만,
/// 함께 닫히는 고정 캡처(핀)는 <b>원장 밖</b>이라 되돌릴 수 없다 (<c>LedgerCommands.ClearAll</c> 문서).
/// 그래서 핀이 있을 때만 확인을 받는다 — 되돌릴 수 있는 조작에까지 대화상자를 붙이면
/// 사용자는 곧 읽지 않고 누르게 되고, 정작 되돌릴 수 없는 경우의 경고도 같이 무력해진다.
///
/// 지울 것이 하나도 없으면 확인도 알림도 없다: 아무 일도 하지 않은 조작은 말을 걸지 않는다.
/// 완료 알림(<see cref="DoneNotice"/>)도 같은 기준을 따른다 — 되돌리기 안내는 실제로 지운 판서가 있을 때만 붙는다 (85단계, A1-3).
/// </summary>
public static class DestructiveActionRules
{
    public static ClearAllPrompt ClearAll(int inkCount, int pinCount)
    {
        int pins = Math.Max(0, pinCount);
        bool hasAnything = Math.Max(0, inkCount) > 0 || pins > 0;
        return new ClearAllPrompt(
            NeedsConfirm: pins > 0,
            HasAnything: hasAnything,
            PinCount: pins);
    }

    /// <summary>
    /// 전체 지우기 완료 알림 문구 (85단계, A1-3). 되돌리기 안내는 <b>지운 판서가 있을 때만</b> 붙인다 —
    /// 원장은 판서가 1개 이상일 때만 항목을 만들므로(<c>UndoLedger.RecordClearAll</c>), 핀만 닫힌 경우에 안내를 붙이면
    /// 사용자가 누른 실행취소가 그 이전의 무관한 조작을 되살린다. 판서를 지운 경우의 문구는 예전과 바이트까지 같다.
    /// </summary>
    /// <param name="clearedInk"><c>LedgerCommands.ClearAll</c>이 돌려준, 실제로 지운 판서 요소 수.</param>
    /// <param name="closedPins">
    /// 함께 <b>실제로</b> 닫은 핀 수 (<c>LedgerCommands.ClearAll</c>의 결과). 확인 전에 읽은 <see cref="ClearAllPrompt.PinCount"/>를
    /// 넘기지 말 것 — 모달이 떠 있는 동안 핀이 닫히거나 생기면 알림이 실제와 어긋난다 (100단계, FINAL-REVIEW-DONENOTICE-COUNT).
    /// </param>
    /// <param name="undoCombo">실행취소 단축키 표기. 해제돼 있으면 null.</param>
    /// <returns>알릴 문구. 지운 것도 닫은 것도 없으면 null(무동작은 말을 걸지 않는다).</returns>
    public static string? DoneNotice(int clearedInk, int closedPins, string? undoCombo)
    {
        if (clearedInk > 0)
        {
            return undoCombo is null ? Strings.ClearAllDone : Strings.ClearAllDoneWithUndo(undoCombo);
        }
        return closedPins > 0 ? Strings.ClearAllPinsClosed(closedPins) : null;
    }
}
