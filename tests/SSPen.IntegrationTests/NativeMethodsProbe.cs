using System.Runtime.InteropServices;

namespace SSPen.IntegrationTests;

/// <summary>
/// 통합 테스트 전용 Win32 프로브. 앱의 <c>NativeMethods</c>는 internal이지만 InternalsVisibleTo로 보인다 —
/// 그래도 교란 주입(SetWindowPos)과 z-순서 리드백(GetWindow/GetDesktopWindow/IsWindowVisible)은 여기서 따로 선언한다:
/// 앱 P/Invoke 표면을 테스트 편의로 늘리지 않고, 프로브가 앱과 같은 바인딩을 공유해 서로의 오류를 가리지 않게 하기 위해서다.
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
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private const uint GwChild = 5;
    private const uint GwHwndNext = 2;

    /// <summary>z-순서상 <paramref name="upper"/>가 <paramref name="lower"/>보다 위인가.</summary>
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
}
