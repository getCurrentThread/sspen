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
}
