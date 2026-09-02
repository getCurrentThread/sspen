namespace SSPen.Interop;

/// <summary>
/// SetWindowsHookEx가 요구하는 훅 프로시저 서명. 52단계에 <c>NativeMethods.HookProc</c>(내부 중첩)에서 옮겨 공개했다 —
/// 공개 이음매 <see cref="IHookInstaller"/>가 이 타입을 노출해야 하기 때문이다. 마샬링 모양은 그대로다.
/// </summary>
public delegate nint LowLevelHookProc(int nCode, nint wParam, nint lParam);

/// <summary>
/// 훅 콜백. nCode &gt;= 0인 이벤트만 받는다 (nCode &lt; 0 통과는 <see cref="LowLevelHook"/>이 소유한다).
/// (wParam, lParam)은 원시값이다 — 두 저수준 훅의 페이로드 구조체가 다르고(KBDLLHOOKSTRUCT/MSLLHOOKSTRUCT) 디코드 시점도
/// 호출자가 정해야 한다(마우스 모니터는 WM_MOUSEMOVE 홍수를 견디려고 메시지·Ctrl을 먼저 거른 뒤 구조체를 읽는다).
/// true = 소비(1 반환, 하위 창으로 보내지 않음), false = CallNextHookEx.
/// </summary>
public delegate bool LowLevelHookCallback(nint wParam, nint lParam);

/// <summary>
/// OS 경계 이음매: SetWindowsHookEx / UnhookWindowsHookEx / CallNextHookEx. 프로덕션 구현은 <see cref="LowLevelHook.Native"/> 하나,
/// 테스트는 <c>FakeHookInstaller</c>가 프로시저를 잡아 두고 합성 이벤트를 쏜다 (52단계).
/// </summary>
public interface IHookInstaller
{
    /// <summary>훅 핸들, 실패 시 0. hMod = 0, dwThreadId = 0(전역 저수준 훅)은 52단계 이전 두 모니터의 호출 그대로다.</summary>
    nint Install(int hookId, LowLevelHookProc proc);

    void Uninstall(nint handle);

    nint CallNext(nint handle, int nCode, nint wParam, nint lParam);
}

/// <summary>
/// 조건부 저수준 훅 관용구 (52단계; 선택 키 훅 R3/R4 + 핀 복귀 훅 AC-17이 각자 갖고 있던 사본을 하나로).
/// 소유하는 것: GC 고정 프로시저 필드(객체 수명 동안 한 인스턴스, 재설치에도 같은 것), 멱등 <see cref="Install"/>/<see cref="Uninstall"/>,
/// <see cref="IsInstalled"/>, <c>nCode &lt; 0 → CallNextHookEx</c> 가드, 콜백의 bool을 "1 반환 / CallNextHookEx"로 잇기.
/// 결정: (a) 콜백은 원시 wParam/lParam — 디코드는 호출자 몫. (b) <see cref="Dispose"/> = <see cref="Uninstall"/>이며 래치하지 않는다 —
/// Dispose 뒤 Refresh의 의미(무동작인지 재설치인지)는 소유자마다 다르므로 소유자가 정한다. (c) 저수준 훅은 설치한 스레드(메시지 루프가
/// 있는 UI 스레드)에서 설치·전달·해제된다; 마샬링하지 않는다; 콜백은 짧아야 한다(LowLevelHooksTimeout을 넘기면 OS가 조용히 훅을
/// 뗀다) — 행동은 Dispatcher.BeginInvoke로 넘겨라. 콜백 안에서 동기 Uninstall이 불리면 CallNext에 핸들 0이 가는데, 현대 Windows의
/// CallNextHookEx는 hhk를 무시하므로 다루지 않는다. 로그는 소유자 몫이다(설치 완료/실패·해제 문구는 두 모니터가 그대로 갖는다).
/// <see cref="Native"/>는 상태 없는 정적 설치기다 — <c>NativeMethods</c>가 internal이라 OS 바인딩이 앱 어셈블리 안에 살아야 한다.
/// </summary>
public sealed class LowLevelHook : IDisposable
{
    /// <summary>실제 OS 설치기 — NativeMethods.SetWindowsHookEx/UnhookWindowsHookEx/CallNextHookEx의 유일한 호출자.</summary>
    public static IHookInstaller Native { get; } = new NativeInstaller();

    private readonly int _hookId;
    private readonly LowLevelHookCallback _callback;
    private readonly IHookInstaller _installer;
    private readonly LowLevelHookProc _proc; // GC 고정
    private nint _handle;

    public LowLevelHook(int hookId, LowLevelHookCallback callback, IHookInstaller installer)
    {
        _hookId = hookId;
        _callback = callback;
        _installer = installer;
        _proc = Proc;
    }

    public bool IsInstalled => _handle != 0;

    /// <summary>
    /// 멱등. 이미 설치돼 있으면 true. 설치기가 0을 주면 false이고 미설치로 남는다 — 다음 Install이 재시도한다
    /// (52단계 이전 Refresh의 재시도와 같다). 고정된 프로시저 인스턴스는 재설치에도 같은 것이다.
    /// </summary>
    public bool Install()
    {
        if (_handle == 0)
        {
            _handle = _installer.Install(_hookId, _proc);
        }
        return _handle != 0;
    }

    /// <summary>멱등. 미설치면 아무것도 하지 않는다.</summary>
    public void Uninstall()
    {
        if (_handle == 0)
        {
            return;
        }
        nint handle = _handle;
        _handle = 0;
        _installer.Uninstall(handle);
    }

    /// <summary>= <see cref="Uninstall"/>. 래치 없음 — Dispose 뒤 Install은 허용된다 (결정 b).</summary>
    public void Dispose() => Uninstall();

    private nint Proc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0 || !_callback(wParam, lParam))
        {
            return _installer.CallNext(_handle, nCode, wParam, lParam);
        }
        return 1; // 소비: 하위 창으로 보내지 않는다.
    }

    private sealed class NativeInstaller : IHookInstaller
    {
        public nint Install(int hookId, LowLevelHookProc proc) => NativeMethods.SetWindowsHookEx(hookId, proc, 0, 0);

        public void Uninstall(nint handle) => NativeMethods.UnhookWindowsHookEx(handle);

        public nint CallNext(nint handle, int nCode, nint wParam, nint lParam) => NativeMethods.CallNextHookEx(handle, nCode, wParam, lParam);
    }
}
