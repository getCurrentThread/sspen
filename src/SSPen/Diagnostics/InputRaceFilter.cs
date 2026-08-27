namespace SSPen.Diagnostics;

/// <summary>
/// "사라진 창을 가리키는 WPF 입력 경주" 식별 (순수 로직).
///
/// 배경: 마우스가 창 위에 있는 상태로 핀/캡처 오버레이가 파괴되면, WPF 입력 계층이 죽은
/// PresentationSource를 계속 참조하다 다음 마우스 이동에서 <c>ClientToScreen</c>이
/// <c>ERROR_INVALID_WINDOW_HANDLE(1400)</c>으로 실패한다. 상태를 손상시키지 않고 다음 이동이면
/// 스스로 회복되므로, 치명적 오류 대화상자를 띄우고 앱을 끊는 대신 경고만 남긴다.
///
/// 근원 처리는 <c>Shell.WindowLifetime.HideThenClose</c>가 담당하고 이 필터는 최후 방어선이다.
/// UI에서 분리한 이유: 판정 규칙 자체는 예외 객체만 보는 순수 함수라 단위 테스트가 가능하다.
/// </summary>
public static class InputRaceFilter
{
    private const int ErrorInvalidWindowHandle = 1400;

    /// <summary>
    /// 두 조건을 <b>모두</b> 만족할 때만 무해로 판정한다.
    ///
    /// 오류 코드만 보고 삼키지 않는 이유: 1400은 잘못된 HWND로 <c>SetWindowPos</c>를 부르는
    /// 진짜 인터롭 버그에서도 난다. 그런 버그까지 조용히 숨기면 z-밴드나 배치가 망가진 채
    /// 아무 신호 없이 굴러간다. 호출 경로가 WPF 입력/팝업 파이프라인일 때만 무해하다고 본다.
    /// </summary>
    public static bool IsBenignStaleWindowRace(Exception? ex)
    {
        if (ex is not System.ComponentModel.Win32Exception win32
            || win32.NativeErrorCode != ErrorInvalidWindowHandle)
        {
            return false;
        }
        string trace = ex.StackTrace ?? string.Empty;
        return trace.Contains("PopupControlService", StringComparison.Ordinal)
            || trace.Contains("MouseDevice", StringComparison.Ordinal)
            || trace.Contains("InputManager", StringComparison.Ordinal);
    }
}
