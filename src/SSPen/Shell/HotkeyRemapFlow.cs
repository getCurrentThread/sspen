using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 핫키 재지정의 순서 오케스트레이터 (40단계, ARCH-8/AC-23): 라이브 핫키 맵 억제 → 캡처 대화상자 → (확정이면) 즉시 재등록 →
/// <b>반드시</b> 복원. 대화상자는 델리게이트로 받아 헤드리스로 검증한다 (CaptureFileNaming의 주입 exists 선례).
/// 이 순서가 계약인 이유: 억제 없이 모달을 띄우면 캡처 중 눌린 조합이 라이브 핫키로 발화하고, 복원을 빠뜨리면 창을 닫은 뒤
/// 전역 핫키가 죽은 채 남는다 — 예외가 나도 복원은 finally다.
/// </summary>
public static class HotkeyRemapFlow
{
    /// <returns>확정된 조합, 취소면 null.</returns>
    public static HotkeyDef? Run(ISettingsHost host, string id, Func<HotkeyDef?> showDialog)
    {
        host.SuppressHotkeys();
        try
        {
            var captured = showDialog();
            if (captured is { } def)
            {
                host.RemapHotkey(id, def);
            }
            return captured;
        }
        finally
        {
            host.RestoreHotkeys();
        }
    }
}
