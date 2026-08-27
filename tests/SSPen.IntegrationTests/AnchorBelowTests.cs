using System.Windows;
using System.Windows.Media;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// z-밴드 항구 고정 회귀 검증 (사용자 조타 버그 수정: 도구 선택 후 툴바 상호작용 불가).
/// WindowStyling.AnchorBelow 훅이 걸린 창은 HWND_TOPMOST/HWND_TOP 올리기 시도가
/// 앵커 창 바로 아래로 돌려지는지 실제 SetWindowPos + z-순서 워크로 단언한다.
/// </summary>
public class AnchorBelowTests
{
    private const uint GW_HWNDNEXT = 2;
    private const uint SwpFlags = 0x0001 | 0x0002 | 0x0010; // NOSIZE | NOMOVE | NOACTIVATE

    [Fact]
    public void HookedWindow_TopmostRaise_LandsBelowAnchor() => StaRunner.Run(() =>
    {
        var anchor = NewTestWindow(320, 320);
        var hooked = NewTestWindow(340, 340);
        try
        {
            anchor.Show();
            hooked.Show();
            nint anchorHwnd = WindowStyling.GetHwnd(anchor);
            nint hookedHwnd = WindowStyling.GetHwnd(hooked);
            var hook = WindowStyling.AnchorBelow(hookedHwnd, () => anchorHwnd);

            // 앵커를 톱모스트 최상단에 두고, 훅 걸린 창을 그 위로 올리려 시도한다.
            WindowStyling.ApplyZBand([anchorHwnd, hookedHwnd]);
            StaRunner.PumpMessages();

            // HWND_TOPMOST 올리기 → 앵커 바로 아래로 돌려져야 한다.
            SetWindowPos(hookedHwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, GetWindow(anchorHwnd, GW_HWNDNEXT));

            // HWND_TOP 올리기도 동일하게 앵커 아래 유지.
            SetWindowPos(hookedHwnd, 0 /* HWND_TOP */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, GetWindow(anchorHwnd, GW_HWNDNEXT));

            // HWND_NOTOPMOST 분기 (gen-4 자문): 강등 시 OS가 톱모스트 밴드에서 빼므로 직하 고정은
            // 보장되지 않는다 — 불변식(앵커 위로 올라가지 않음)만 단언하고, 재상승으로 복구를 확인.
            SetWindowPos(hookedHwnd, (nint)(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(IsBelow(anchorHwnd, hookedHwnd));

            SetWindowPos(hookedHwnd, (nint)(-1) /* HWND_TOPMOST 재상승 */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, GetWindow(anchorHwnd, GW_HWNDNEXT));

            // 훅 델리게이트는 검증 동안 살아 있어야 한다 (GC 핀 의도 명시, gen-4 자문).
            GC.KeepAlive(hook);
        }
        finally
        {
            hooked.Close();
            anchor.Close();
        }
    });

    /// <summary>z-순서에서 above 아래 어딘가에 below가 있는지 (직하가 아니어도 됨).</summary>
    private static bool IsBelow(nint above, nint below)
    {
        for (nint w = GetWindow(above, GW_HWNDNEXT); w != 0; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (w == below)
            {
                return true;
            }
        }
        return false;
    }

    private static Window NewTestWindow(double left, double top) => new()
    {
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        AllowsTransparency = true,
        Background = Brushes.DarkGray,
        Topmost = true,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = left,
        Top = top,
        Width = 120,
        Height = 120,
    };

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);
}
