using System.Runtime.InteropServices;

namespace SSPen.IntegrationTests;

/// <summary>
/// 통합 테스트 전용 Win32 프로브. 앱의 <c>NativeMethods</c>는 internal이지만 InternalsVisibleTo로 보인다 —
/// 그래도 교란 주입(SetWindowPos/SetForegroundWindow)과 z-순서·창 리드백(GetWindow/GetDesktopWindow/IsWindowVisible/
/// FindWindow/GetWindowRect)은 여기서 따로 선언한다:
/// 앱 P/Invoke 표면을 테스트 편의로 늘리지 않고, 프로브가 앱과 같은 바인딩을 공유해 서로의 오류를 가리지 않게 하기 위해서다.
/// 테스트 파일에 extern을 다시 적지 말고 여기에 모은다 (61단계, A8-7) — 한 파일만 쓰는 선언은 그 파일에 남아도 된다(19단계 승격 규칙).
///
/// 앱 쪽 규칙과 달리 <c>[DllImport]</c>를 쓰는 이유: <c>[LibraryImport]</c> 소스 생성기는
/// unsafe 코드를 방출해 <c>AllowUnsafeBlocks</c>가 필요한데, 테스트 프로젝트에 그 스위치를
/// 켜는 것보다 이 몇 줄을 DllImport로 두는 편이 표면적이 작다.
/// </summary>
internal static class NativeMethodsProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out ProbeRect lpRect);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private const uint GwChild = 5;

    /// <summary><c>GetWindow</c>의 GW_HWNDNEXT — z-순서상 바로 아래 창.</summary>
    internal const uint GwHwndNext = 2;

    /// <summary><c>GetWindow</c>의 GW_OWNER — 소유자 창.</summary>
    internal const uint GwOwner = 4;

    /// <summary>exstyle 리드백 상수 (앱 NativeMethods와 같은 값을 테스트 쪽에서 따로 든다 — 프로브 원칙).</summary>
    internal const long WsExNoActivate = 0x08000000;
    internal const long WsExToolWindow = 0x00000080;

    /// <summary><c>GetWindowRect</c>의 결과 사각형 (물리 픽셀, Win32 RECT 배치).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProbeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// z-순서상 <paramref name="upper"/>가 <paramref name="lower"/>보다 위인가 — 데스크톱의 첫 자식부터
    /// <b>보이는 창만</b> 훑어 먼저 만나는 쪽으로 판정한다. 둘 다 못 만나면 <c>false</c>.
    /// </summary>
    internal static bool IsAbove(nint upper, nint lower)
    {
        nint h = GetWindow(GetDesktopWindow(), GwChild);
        while (h != 0)
        {
            if (IsWindowVisible(h))
            {
                if (h == upper)
                {
                    return true;
                }
                if (h == lower)
                {
                    return false;
                }
            }
            h = GetWindow(h, GwHwndNext);
        }
        return false;
    }

    /// <summary>
    /// z-순서에서 <paramref name="above"/> 아래 어딘가에 <paramref name="below"/>가 있는가 (직하가 아니어도 됨).
    /// <see cref="IsAbove"/>와 <b>의미가 다르다</b>: 데스크톱이 아니라 <paramref name="above"/>에서 출발해 GW_HWNDNEXT를 따라가며,
    /// 보이지 않는 창(IME 창 등)도 건너뛰지 않는다. AnchorBelowTests의 IME 창 관련 판정이 이 워크에 기대므로 둘을 합치지 않는다.
    /// </summary>
    internal static bool IsBelowByNextWalk(nint above, nint below)
    {
        for (nint w = GetWindow(above, GwHwndNext); w != 0; w = GetWindow(w, GwHwndNext))
        {
            if (w == below)
            {
                return true;
            }
        }
        return false;
    }
}
