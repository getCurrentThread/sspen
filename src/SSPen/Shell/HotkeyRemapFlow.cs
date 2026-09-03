using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 핫키 재지정의 순서 오케스트레이터 (40단계, ARCH-8/AC-23): 라이브 핫키 맵 억제 → 캡처 대화상자 → (확정이면) 즉시 재등록 →
/// <b>반드시</b> 복원. 대화상자는 델리게이트로 받아 헤드리스로 검증한다 (CaptureFileNaming의 주입 exists 선례).
/// 이 순서가 계약인 이유: 억제 없이 모달을 띄우면 캡처 중 눌린 조합이 라이브 핫키로 발화하고, 복원을 빠뜨리면 창을 닫은 뒤
/// 전역 핫키가 죽은 채 남는다 — 예외가 나도 복원은 finally다.
///
/// <b>여기서 설정을 쓰지 않는다</b>: 예전에는 캡처 직후 <c>host.RemapHotkey</c>가 <c>SaveNow()</c>까지 해서,
/// 다른 모든 설정이 "확인을 눌러야 적용"인데 단축키만 <b>취소해도 이미 디스크에 남는</b> 비대칭이 있었다.
/// 재지정 반영은 창이 모아 두었다가 <c>Apply()</c>에서 한다 (AC-23의 즉시 재등록은 그 시점에 그대로 일어난다).
/// </summary>
public static class HotkeyRemapFlow
{
    /// <returns>확정된 조합, 취소면 null. 호출자가 보류 목록에 담았다가 확인 시 적용한다.</returns>
    public static HotkeyDef? Run(ISettingsHost host, string id, Func<HotkeyDef?> showDialog)
    {
        host.SuppressHotkeys();
        try
        {
            return showDialog();
        }
        finally
        {
            host.RestoreHotkeys();
        }
    }
}
