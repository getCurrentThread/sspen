namespace SSPen.Interop;

/// <summary>
/// SetWinEventHook가 요구하는 콜백 서명 (54단계 L3). <see cref="IWinEventInstaller"/>가 노출해야 하므로 공개다.
/// </summary>
public delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

/// <summary>
/// OS 경계 이음매: SetWinEventHook / UnhookWinEvent. 프로덕션 구현은 <see cref="WinEventWatch.Native"/> 하나,
/// 테스트는 <c>FakeWinEventInstaller</c>가 프로시저를 잡아 두고 합성 이벤트를 쏜다 (<see cref="LowLevelHook"/>의 52단계 선례).
/// </summary>
public interface IWinEventInstaller
{
    /// <summary>훅 핸들, 실패 시 0. 모든 프로세스·스레드, WINEVENT_OUTOFCONTEXT(콜백은 설치 스레드의 메시지 루프에서 온다).</summary>
    nint Install(uint eventMin, uint eventMax, WinEventProc proc);

    void Uninstall(nint handle);
}

/// <summary>
/// 조건부 WinEvent 훅 관용구 (54단계 L3). 소유하는 것은 <see cref="LowLevelHook"/>와 같다 — GC 고정 프로시저 필드,
/// 멱등 <see cref="Install"/>/<see cref="Uninstall"/>, <see cref="IsInstalled"/>, <see cref="Dispose"/> = <see cref="Uninstall"/>(래치 없음).
/// 콜백은 (이벤트, hwnd, idObject)만 받는다 — 나머지 인자는 z-밴드 검증에 쓸모가 없다.
/// 콜백은 짧아야 한다(OUTOFCONTEXT라도 설치 스레드의 펌프 안에서 불린다) — 행동은 Dispatcher로 넘겨라.
/// </summary>
public sealed class WinEventWatch : IDisposable
{
    /// <summary>실제 OS 설치기 — NativeMethods.SetWinEventHook/UnhookWinEvent의 유일한 호출자.</summary>
    public static IWinEventInstaller Native { get; } = new NativeInstaller();

    private readonly uint _eventMin;
    private readonly uint _eventMax;
    private readonly Action<uint, nint, int> _callback;
    private readonly IWinEventInstaller _installer;
    private readonly WinEventProc _proc; // GC 고정
    private nint _handle;

    public WinEventWatch(uint eventMin, uint eventMax, Action<uint, nint, int> callback, IWinEventInstaller installer)
    {
        _eventMin = eventMin;
        _eventMax = eventMax;
        _callback = callback;
        _installer = installer;
        _proc = Proc;
    }

    public bool IsInstalled => _handle != 0;

    /// <summary>멱등. 설치기가 0을 주면 false이고 미설치로 남는다 — 다음 Install이 재시도한다.</summary>
    public bool Install()
    {
        if (_handle == 0)
        {
            _handle = _installer.Install(_eventMin, _eventMax, _proc);
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

    public void Dispose() => Uninstall();

    private void Proc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_handle == 0)
        {
            return; // 해제 직후 큐에 남아 있던 이벤트는 버린다.
        }
        _callback(eventType, hwnd, idObject);
    }

    private sealed class NativeInstaller : IWinEventInstaller
    {
        public nint Install(uint eventMin, uint eventMax, WinEventProc proc) =>
            NativeMethods.SetWinEventHook(eventMin, eventMax, 0, proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        public void Uninstall(nint handle) => NativeMethods.UnhookWinEvent(handle);
    }
}
