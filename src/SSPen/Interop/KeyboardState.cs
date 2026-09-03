namespace SSPen.Interop;

/// <summary>
/// 수식키 판정 정책 계층 (D3). <c>NativeMethods.GetAsyncKeyState</c>를 감싼다.
///
/// WPF <c>Keyboard.Modifiers</c>를 쓰면 안 되는 이유: 그것은 <b>스레드 로컬 입력 상태</b>를 읽는데,
/// 서피스 창은 <c>WS_EX_NOACTIVATE</c> + <c>ShowActivated=false</c>라 키보드 포커스를 절대 갖지 못한다.
/// 그래서 전역 핫키(Alt+Shift+V)로 선택 도구를 켠 흐름에서는 Shift가 항상 <c>None</c>으로 읽혀
/// <b>Shift+클릭 다중 선택·마퀴 누적·회전 스냅·도형 제약이 전부 조용히 죽는다</b>.
/// 전역 훅에서도 같다 — <c>Pin/PinClickThroughMonitor</c>는 Ctrl을 <see cref="Control"/>로 읽는다
/// (52단계에 자체 GetAsyncKeyState DllImport를 없애고 이 계층으로 합쳤다). 두 훅 모니터는 이 값을 <c>Func&lt;bool&gt;</c>로
/// 주입받으므로(합성 루트·PinManager가 배선) 헤드리스 증인은 OS 키 상태를 읽지 않는다 (53단계).
/// </summary>
internal static class KeyboardState
{
    private const short PressedMask = unchecked((short)0x8000);

    /// <summary>지금 물리적으로 눌려 있는가 (포그라운드 창과 무관).</summary>
    internal static bool IsDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & PressedMask) != 0;

    internal static bool Shift => IsDown(NativeMethods.VK_SHIFT);

    internal static bool Control => IsDown(NativeMethods.VK_CONTROL);

    /// <summary>
    /// Escape가 눌려 있는가 — 툴바 플라이아웃을 닫는 계기다. 툴바 창은 <c>WS_EX_NOACTIVATE</c>라
    /// KeyDown을 받을 수 없으므로 포인터 감시 틱이 물리 키 상태를 읽는 것 외에 방법이 없다.
    /// 남의 앱에서 누른 Escape도 읽히지만, 그 결과는 열려 있던 플라이아웃이 닫히는 것뿐이라 손해가 없다.
    /// </summary>
    internal static bool Escape => IsDown(NativeMethods.VK_ESCAPE);

    /// <summary>
    /// Ctrl·Alt·Win 중 하나라도 눌려 있는가 — 전역 키 훅이 남의 앱 조합키를 삼키지 않게 하는 게이트.
    ///
    /// <b>Shift는 일부러 뺐다.</b> Shift는 남의 앱 조합키이기 이전에 선택 도구의 1급 수식키다
    /// (Shift+클릭 토글, Shift+마퀴 누적). 즉 "여러 개 골라서 지운다"는 핵심 흐름은 Shift를 누른 채
    /// Delete를 누르는 것이 정상 경로다. Shift까지 통과시키면 그 Delete가 포커스를 가진 뒤쪽 앱으로
    /// 흘러가는데(서피스는 NOACTIVATE라 포커스는 늘 남에게 있다), 탐색기라면 <b>파일 영구 삭제</b>다.
    /// 두 손해의 크기가 비대칭이라 — 삼키면 훅이 살아 있는 짧은 구간에서 남의 Shift+Delete가 한 번
    /// 안 먹는 정도, 통과시키면 의도치 않은 영구 삭제 — 삼키는 쪽을 택한다. Ctrl+Backspace(단어 삭제)나
    /// Win+Delete처럼 SS Pen이 의미를 갖지 않는 조합은 그대로 통과한다.
    /// </summary>
    internal static bool NonShiftModifier =>
        Control
        || IsDown(NativeMethods.VK_MENU)
        || IsDown(NativeMethods.VK_LWIN)
        || IsDown(NativeMethods.VK_RWIN);
}
