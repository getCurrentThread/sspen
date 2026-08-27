using System.Runtime.InteropServices;

namespace SSPen.Interop;

/// <summary>
/// Win32 P/Invoke 목록 (플랜 WI-2 인터롭 인벤토리).
/// 모든 좌표는 물리 픽셀. 논리 좌표 변환은 <see cref="CoordinateSpace"/>에서만 수행한다.
/// </summary>
internal static partial class NativeMethods
{
    // ---- 창 스타일 ----
    internal const int GWL_EXSTYLE = -20;

    internal const long WS_EX_TOPMOST = 0x00000008;
    internal const long WS_EX_TRANSPARENT = 0x00000020;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const long WS_EX_NOACTIVATE = 0x08000000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // ---- z-순서 / 배치 ----
    internal static readonly nint HWND_TOPMOST = -1;
    internal static readonly nint HWND_NOTOPMOST = -2;
    internal static readonly nint HWND_TOP = 0;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOZORDER = 0x0004;

    // z-밴드 고정 훅 (툴바 항상 서피스 위, 서피스 항상 핀 위 — R10 상시 보증)
    internal const int WM_WINDOWPOSCHANGING = 0x0046;
    internal const int WM_WINDOWPOSCHANGED = 0x0047;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    // ---- 저수준 키보드 훅 (R3/R4: 서피스 창은 WS_EX_NOACTIVATE라 키보드 포커스를 갖지 못한다) ----
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;

    internal const int VK_BACK = 0x08;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_DELETE = 0x2E;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12; // Alt
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    /// <summary>
    /// 비동기 키 상태 (D3). WPF <c>Keyboard.Modifiers</c>는 <b>스레드 로컬 입력 상태</b>라
    /// 남의 앱이 포그라운드일 때 항상 <c>None</c>을 반환한다. 서피스 창은 영구 NOACTIVATE라
    /// 절대 포커스를 갖지 못하므로, 전역 핫키로 선택 도구를 켠 흐름에서는 Shift+클릭 다중 선택이
    /// 통째로 죽는다. 수식키 판정은 반드시 이쪽을 읽어야 한다.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    // ---- 전역 핫키 ----
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int WM_HOTKEY = 0x0312;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    // ---- 커서 (강조 커서 후광 추적: 클릭 통과 중에도 동작) ----
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    // ---- 모니터 열거 (음수 원점 포함 물리 좌표) ----
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    internal const uint MONITORINFOF_PRIMARY = 0x0001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX lpmi);

    // ---- GDI (BitBlt 캡처, WI-10에서 소비) ----
    internal const int SRCCOPY = 0x00CC0020;
    internal const int CAPTUREBLT = 0x40000000;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(
        nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, int rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    // ---- 커서 (지우개 커스텀 커서 — 사용자 조타: 도구별 커서 UX) ----
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreateIconIndirect(ref ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    // ---- DWM (캡처 시퀀스: 툴바 숨김 → 합성 플러시 → BitBlt, WI-11) ----
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmFlush();

    // ---- 구조체 ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        public int fIcon; // BOOL: 1=icon, 0=cursor (blittable for LibraryImport)
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
