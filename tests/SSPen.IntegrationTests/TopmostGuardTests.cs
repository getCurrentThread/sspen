using System.Windows;
using System.Windows.Media;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 툴바 톱모스트 방어 검증 (사용자 보고 18차).
///
/// 증상: 그리는 중 갑자기 툴바 버튼이 전부 안 눌린다. 툴바는 **보이는데** 클릭만 안 먹는다.
/// 원인: 외부 앱이 툴바의 WS_EX_TOPMOST를 벗겨 밴드 밖으로 내리면, 톱모스트인 서피스가
///       툴바 위를 덮어 클릭을 전부 가로챈다. 방어는 서피스 쪽(AnchorBelow)에만 있었고
///       이 방향은 아무도 복구하지 않았다. ApplyZBand는 앱 내부 상태 변화에만 불리므로
///       그리는 동안에는 영원히 호출되지 않는다.
///
/// 이 테스트는 실제 창에 실제 SetWindowPos를 걸어 exstyle 리드백으로 판정한다.
/// </summary>
public class TopmostGuardTests
{
    private const long WsExTopmost = 0x00000008;
    private static readonly nint HwndNoTopmost = -2;
    private const uint SwpNoSizeMoveActivate = 0x0001 | 0x0002 | 0x0010;

    private static Window NewTopmostWindow()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = 120,
            Top = 120,
            Width = 100,
            Height = 100,
        };
        window.Show();
        return window;
    }

    [Fact]
    public void KeepTopmost_RestoresBand_WhenExternallyDemoted() => StaRunner.Run(() =>
    {
        var window = NewTopmostWindow();
        try
        {
            nint hwnd = WindowStyling.GetHwnd(window);
            var hook = WindowStyling.KeepTopmost(hwnd);
            Assert.NotNull(hook);
            Assert.NotEqual(0, WindowStyling.GetExStyle(hwnd) & WsExTopmost);

            // 외부 앱이 우리 창을 톱모스트 밴드 밖으로 밀어내는 상황을 그대로 재현한다.
            NativeMethodsProbe.SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoSizeMoveActivate);
            StaRunner.PumpMessages();

            // 훅이 WM_WINDOWPOSCHANGED에서 벗겨진 exstyle을 보고 되돌려야 한다.
            Assert.NotEqual(0, WindowStyling.GetExStyle(hwnd) & WsExTopmost);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void KeepTopmost_DoesNotFightOrderingWithinBand() => StaRunner.Run(() =>
    {
        // 밴드 **안에서의** 순서 조정(ApplyZBand가 특정 HWND 뒤로 넣는 것)까지 되돌리면
        // z-밴드가 영원히 싸우게 된다. 톱모스트가 유지되는 한 훅은 개입하지 않아야 한다.
        var upper = NewTopmostWindow();
        var lower = NewTopmostWindow();
        try
        {
            nint upperHwnd = WindowStyling.GetHwnd(upper);
            nint lowerHwnd = WindowStyling.GetHwnd(lower);
            var hook = WindowStyling.KeepTopmost(lowerHwnd);
            Assert.NotNull(hook);

            WindowStyling.ApplyZBand([upperHwnd, lowerHwnd]);
            StaRunner.PumpMessages();

            // 둘 다 여전히 톱모스트 (훅이 순서 조정을 방해하지 않았다).
            Assert.NotEqual(0, WindowStyling.GetExStyle(upperHwnd) & WsExTopmost);
            Assert.NotEqual(0, WindowStyling.GetExStyle(lowerHwnd) & WsExTopmost);
        }
        finally
        {
            lower.Close();
            upper.Close();
        }
    });

    [Fact]
    public void AnchorBelow_KeepsSurfaceUnderToolbar_WhenSurfaceRaised() => StaRunner.Run(() =>
    {
        // 반대 방향 교란: 서피스가 밴드 최상단으로 올라가려 할 때 툴바 아래로 되돌려진다.
        var toolbar = NewTopmostWindow();
        var surface = NewTopmostWindow();
        try
        {
            nint toolbarHwnd = WindowStyling.GetHwnd(toolbar);
            nint surfaceHwnd = WindowStyling.GetHwnd(surface);
            var hook = WindowStyling.AnchorBelow(surfaceHwnd, () => toolbarHwnd);
            Assert.NotNull(hook);

            NativeMethodsProbe.SetWindowPos(surfaceHwnd, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, SwpNoSizeMoveActivate);
            StaRunner.PumpMessages();

            Assert.True(
                NativeMethodsProbe.IsAbove(toolbarHwnd, surfaceHwnd),
                "서피스가 툴바 위로 올라갔다 — AnchorBelow 훅이 잡지 못했다.");
        }
        finally
        {
            surface.Close();
            toolbar.Close();
        }
    });

    [Fact]
    public void AddHook_Throws_WhenHwndHasNoSource() => StaRunner.Run(() =>
    {
        // 조용한 실패(?.AddHook)를 금지한 계약: 훅이 안 붙으면 z-방어가 통째로 사라지는데
        // 그 증상은 "가끔 툴바가 안 눌림"이라는 재현 어려운 형태로만 나타난다.
        Assert.ThrowsAny<Exception>(() => WindowStyling.KeepTopmost(0x1234));
    });
}
