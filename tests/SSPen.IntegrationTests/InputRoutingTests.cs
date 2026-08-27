using System.Windows;
using System.Windows.Media;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 클릭 통과 입력 라우팅 검증 (AC-2/ARCH-1의 나머지 절반, 클리너 B4).
/// exstyle 리드백이 아니라 실제 OS 히트테스트 경로(WindowFromPoint)로 단언한다:
/// 근사투명 배경(#01000000) 서피스는 상호작용 상태에서 클릭을 받고,
/// WS_EX_TRANSPARENT를 켜면 같은 지점의 히트가 아래 창으로 통과한다.
/// </summary>
public class InputRoutingTests
{
    [Fact]
    public void HitTest_RoutesThroughSurface_ThenPassesWhenClickThrough() => StaRunner.Run(() =>
    {
        // 아래: 불투명 대상 창 / 위: 서피스와 동일 구성(근사투명 히트테스트 배경).
        var bottom = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.DarkSlateGray,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 300,
            Top = 300,
            Width = 200,
            Height = 200,
        };
        var top = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)), // ARCH-1 히트테스트 배경
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 300,
            Top = 300,
            Width = 200,
            Height = 200,
        };
        try
        {
            bottom.Show();
            top.Show();
            nint bottomHwnd = WindowStyling.GetHwnd(bottom);
            nint topHwnd = WindowStyling.GetHwnd(top);
            // top을 topmost 밴드 최상단으로 (재현 가능한 z-순서).
            WindowStyling.ApplyZBand([topHwnd, bottomHwnd]);
            StaRunner.PumpMessages();
            Thread.Sleep(150); // 레이어드 비트맵 커밋 대기.

            // 프로브 지점은 실제 창 사각형 중심에서 도출 (DPI 배율 무관 — 아키텍트 어드바이저리).
            Assert.True(GetWindowRect(bottomHwnd, out var rect));
            var point = new NativePoint
            {
                X = (rect.Left + rect.Right) / 2,
                Y = (rect.Top + rect.Bottom) / 2,
            };

            // 1) 상호작용 상태: 근사투명 배경이 클릭을 잡는다.
            Assert.Equal(topHwnd, RootWindowFromPoint(point));

            // 2) 클릭 통과: 같은 지점의 히트가 아래 창으로 내려간다.
            WindowStyling.SetClickThrough(topHwnd, true);
            StaRunner.PumpMessages();
            Assert.Equal(bottomHwnd, RootWindowFromPoint(point));

            // 3) 왕복 복원 (AC-17 대칭성).
            WindowStyling.SetClickThrough(topHwnd, false);
            StaRunner.PumpMessages();
            Assert.Equal(topHwnd, RootWindowFromPoint(point));
        }
        finally
        {
            top.Close();
            bottom.Close();
        }
    });

    private static nint RootWindowFromPoint(NativePoint point)
    {
        nint hit = WindowFromPoint(point);
        return hit == 0 ? 0 : GetAncestor(hit, 2 /* GA_ROOT */);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
}
