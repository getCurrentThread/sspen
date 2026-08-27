using System.Runtime.InteropServices;
using SSPen.Diagnostics;

namespace SSPen.Pin;

/// <summary>
/// 클릭 통과 상태 핀의 복귀 경로 (AC-17 왕복 토글 보장).
/// WS_EX_TRANSPARENT 핀은 창 입력을 받을 수 없으므로, 통과 핀이 1개 이상일 때만
/// WH_MOUSE_LL 훅을 걸어 Ctrl+가운데 버튼이 통과 핀 사각형 위에서 눌리면 다시 토글한다.
/// 통과 핀이 없으면 훅을 즉시 해제한다 (상시 훅 금지).
/// </summary>
public sealed class PinClickThroughMonitor : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int VK_CONTROL = 0x11;

    private readonly PinManager _manager;
    private readonly LowLevelMouseProc _proc; // GC 고정
    private nint _hook;

    public PinClickThroughMonitor(PinManager manager)
    {
        _manager = manager;
        _proc = HookProc;
    }

    /// <summary>통과 핀 존재 여부에 따라 훅 설치/해제.</summary>
    public void Refresh()
    {
        bool needHook = _manager.Pins.Any(p => p.IsClickThrough);
        if (needHook && _hook == 0)
        {
            _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, 0, 0);
            Log.Info($"핀 복귀 마우스 훅 설치 {(_hook == 0 ? "실패" : "완료")}");
        }
        else if (!needHook && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
            Log.Info("핀 복귀 마우스 훅 해제");
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        // 아키텍트 B1: WPF Keyboard.Modifiers는 스레드 로컬 입력 상태라 타 앱이 포그라운드일 때
        // 항상 None을 반환한다 — 전역 훅에서는 비동기 키 상태를 직접 읽어야 한다.
        if (nCode >= 0 && (int)wParam == WM_MBUTTONDOWN
            && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            foreach (var pin in _manager.Pins)
            {
                if (pin.IsClickThrough && pin.PhysicalBounds().Contains(data.pt.X, data.pt.Y))
                {
                    pin.Dispatcher.BeginInvoke(() =>
                    {
                        pin.SetClickThrough(false);
                        _manager.NotifyClickThroughChanged();
                    });
                    return 1; // 소비: 하위 창으로 보내지 않는다.
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
