using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 토스트 창의 exstyle 실측 증인.
///
/// 왜 통합 테스트인가: 토스트는 z-밴드 <b>최상단</b>이다. 그 창이 클릭을 삼킬 수 있으면
/// "보이는데 눌리지 않는 툴바"(AGENTS L14가 경고하는 증상)를 앱이 스스로 만들어낼 수 있다.
/// 안전의 근거가 <c>WS_EX_TRANSPARENT</c>라는 <b>OS 상태</b>이므로, 판정이 아니라 실제 리드백이 증인이어야 한다.
/// 상호작용 토스트(액션 라벨)일 때만 통과가 풀리고 <c>WS_EX_NOACTIVATE</c>는 그때도 유지된다는 것까지 함께 고정한다.
/// </summary>
public class ToastWindowExStyleTests
{
    [Fact]
    public void ToastWindow_ByDefault_IsClickThroughAndNoActivate() => StaRunner.Run(() =>
    {
        var window = new ToastWindow();
        window.Show();
        try
        {
            long exStyle = WindowStyling.GetExStyle(window.Hwnd);

            Assert.NotEqual(0, window.Hwnd);
            Assert.True(WindowStyling.IsClickThrough(window.Hwnd), "토스트는 기본으로 클릭을 통과시켜야 한다.");
            Assert.NotEqual(0, exStyle & NativeMethodsProbe.WsExNoActivate);
            Assert.NotEqual(0, exStyle & NativeMethodsProbe.WsExToolWindow);
        }
        finally
        {
            WindowLifetime.HideThenClose(window);
        }
    });

    /// <summary>액션이 있는 토스트만 클릭을 받는다 — 그때도 포커스는 옮겨 가지 않는다.</summary>
    [Fact]
    public void Render_InteractiveStep_TakesClicksButStillNeverActivates() => StaRunner.Run(() =>
    {
        var window = new ToastWindow();
        window.Show();
        try
        {
            window.Render(new ToastStep(
                Visible: true, Text: "캡처를 저장했습니다", Kind: ToastKind.Info,
                ActionLabel: "폴더 열기", Interactive: true, StopTimer: false));

            Assert.False(WindowStyling.IsClickThrough(window.Hwnd));
            Assert.NotEqual(0, WindowStyling.GetExStyle(window.Hwnd) & NativeMethodsProbe.WsExNoActivate);

            // 액션이 없는 다음 토스트에서는 반드시 통과 상태로 되돌아온다.
            window.Render(new ToastStep(
                Visible: true, Text: "캡처를 복사했습니다", Kind: ToastKind.Info,
                ActionLabel: null, Interactive: false, StopTimer: false));

            Assert.True(WindowStyling.IsClickThrough(window.Hwnd));
        }
        finally
        {
            WindowLifetime.HideThenClose(window);
        }
    });
}
