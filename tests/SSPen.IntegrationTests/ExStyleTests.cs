using System.Windows;
using System.Windows.Media;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 클릭 통과 exstyle 전이 검증 (AC-2/AC-17의 기계 검증 절반).
/// GetWindowLongPtr 리드백으로 TRANSPARENT/TOOLWINDOW/NOACTIVATE 전이를 단언한다.
/// </summary>
public class ExStyleTests
{
    private static Window NewTestWindow()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 100,
            Top = 100,
            Width = 120,
            Height = 120,
        };
        window.Show();
        return window;
    }

    [Fact]
    public void ClickThrough_Toggle_ReadsBack() => StaRunner.Run(() =>
    {
        var window = NewTestWindow();
        try
        {
            nint hwnd = WindowStyling.GetHwnd(window);
            Assert.NotEqual((nint)0, hwnd);

            Assert.False(WindowStyling.IsClickThrough(hwnd));

            WindowStyling.SetClickThrough(hwnd, true);
            Assert.True(WindowStyling.IsClickThrough(hwnd));

            WindowStyling.SetClickThrough(hwnd, false);
            Assert.False(WindowStyling.IsClickThrough(hwnd));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ContentSurfaceStyleCombination_MatchesEpicPenObservation() => StaRunner.Run(() =>
    {
        // F8: 콘텐츠 서피스 관측 exStyle=0x80028 = LAYERED(0x80000)|TRANSPARENT(0x20)|TOPMOST(0x8).
        var window = NewTestWindow();
        try
        {
            window.Topmost = true;
            nint hwnd = WindowStyling.GetHwnd(window);
            WindowStyling.SetToolWindow(hwnd, true);
            WindowStyling.SetClickThrough(hwnd, true);

            long exStyle = WindowStyling.GetExStyle(hwnd);
            Assert.Equal(0x80028L, exStyle & 0x80028L);
            // 서피스는 추가로 TOOLWINDOW(0x80)도 켜둔다 (Alt+Tab 제외).
            Assert.Equal(0x80L, exStyle & 0x80L);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void NoActivate_Toggle_ReadsBack() => StaRunner.Run(() =>
    {
        var window = NewTestWindow();
        try
        {
            nint hwnd = WindowStyling.GetHwnd(window);
            WindowStyling.SetNoActivate(hwnd, true);
            Assert.NotEqual(0L, WindowStyling.GetExStyle(hwnd) & 0x08000000L);

            // 텍스트 도구 IME 핸드셰이크 (ARCH-2): 해제 → 복원.
            WindowStyling.SetNoActivate(hwnd, false);
            Assert.Equal(0L, WindowStyling.GetExStyle(hwnd) & 0x08000000L);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void PlacePhysical_NegativeOrigin_PlacesOnLeftMonitor() => StaRunner.Run(() =>
    {
        // 프리모템 2: 음수 원점 배치 — DISPLAY1(-1920,0) 위 (-1900,50) 자리.
        var window = NewTestWindow();
        try
        {
            nint hwnd = WindowStyling.GetHwnd(window);
            var target = new PhysicalRect(-1900, 50, 200, 150);
            WindowStyling.PlacePhysical(hwnd, target);
            StaRunner.PumpMessages();

            Assert.True(NativeMethods_GetWindowRect(hwnd, out var actual));
            Assert.Equal(target.X, actual.Left);
            Assert.Equal(target.Y, actual.Top);
            Assert.Equal(target.Width, actual.Right - actual.Left);
            Assert.Equal(target.Height, actual.Bottom - actual.Top);
        }
        finally
        {
            window.Close();
        }
    });

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    private static extern bool NativeMethods_GetWindowRect(nint hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
