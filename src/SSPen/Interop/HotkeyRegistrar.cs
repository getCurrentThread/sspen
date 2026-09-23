namespace SSPen.Interop;

/// <summary>
/// OS 경계 이음매: RegisterHotKey / UnregisterHotKey (76단계, C-3). 프로덕션 구현은 <see cref="HotkeyRegistrar.Native"/> 하나,
/// 테스트는 <c>FakeHotkeyRegistrar</c>가 점유 집합과 호출 기록으로 대신한다 — <see cref="LowLevelHook"/>(52단계)·
/// <see cref="WinEventWatch"/>(54단계)의 설치기 이음매와 같은 관용구다. 인자는 OS 호출 그대로 넘긴다:
/// id = 바인딩 인덱스, <c>MOD_NOREPEAT</c> 합성은 호출자(<c>Shell/HotkeyService</c>)가 이미 끝낸 값이다.
/// </summary>
public interface IHotkeyRegistrar
{
    /// <summary>성공 여부. 다른 소유자가 같은 조합을 점유하고 있으면 false (프리모템 3: 부분 실패는 호출자가 허용한다).</summary>
    bool Register(nint hwnd, int id, uint modifiers, uint vk);

    void Unregister(nint hwnd, int id);
}

/// <summary>
/// <see cref="IHotkeyRegistrar"/>의 프로덕션 바인딩 소유자. <c>NativeMethods</c>가 internal이라 OS 바인딩이 앱 어셈블리 안에 살아야 한다
/// (<see cref="LowLevelHook.Native"/>와 같은 이유). 새 P/Invoke는 없다 — <c>NativeMethods</c>에 있던 두 선언에 위임만 한다.
/// </summary>
public static class HotkeyRegistrar
{
    /// <summary>실제 OS 등록기 — NativeMethods.RegisterHotKey/UnregisterHotKey의 유일한 호출자.</summary>
    public static IHotkeyRegistrar Native { get; } = new NativeRegistrar();

    private sealed class NativeRegistrar : IHotkeyRegistrar
    {
        public bool Register(nint hwnd, int id, uint modifiers, uint vk) => NativeMethods.RegisterHotKey(hwnd, id, modifiers, vk);

        public void Unregister(nint hwnd, int id) => NativeMethods.UnregisterHotKey(hwnd, id);
    }
}
