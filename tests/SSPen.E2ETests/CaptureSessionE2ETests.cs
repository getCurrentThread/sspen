using System.Windows;
using SSPen.Capture;
using SSPen.Interop;
using Xunit;

namespace SSPen.E2ETests;

public class CaptureSessionE2ETests
{
    [Fact]
    public void StartCapture_TogglesToolbarVisibilityAndActivatesSession() => E2EAppFixture.Run(actor =>
    {
        var toolbar = actor.App.Toolbar;
        Assert.NotNull(toolbar);
        Assert.Equal(Visibility.Visible, toolbar.Visibility);

        // 캡처 세션 시작
        actor.StartCapture();

        // 툴바 숨김 처리 확인 (캡처 중 툴바가 가리지 않도록)
        Assert.Equal(Visibility.Hidden, toolbar.Visibility);

        // 세션 취소 (ESC)
        actor.App.Capture.CancelCaptureSession();
        actor.Pump();

        // 툴바 복원 확인
        Assert.Equal(Visibility.Visible, toolbar.Visibility);
    });

    /// <summary>
    /// 세션 중 서피스 입력 중단의 관측 가능한 결과는 실제 창의 클릭 통과(WS_EX_TRANSPARENT)다 (A8-3) — 마우스가 서피스를 지나
    /// 캡처 오버레이에 닿는다. 컨트롤러를 직접 부르는 <c>PointerDown</c>은 창의 중단 가드를 우회하므로 증인이 될 수 없다.
    /// 호출 순서 자체는 헤드리스 <c>CaptureSessionControllerTests</c>가 본다.
    /// </summary>
    [Fact]
    public void StartCapture_SuspendsSurfacesInput_AndRestoresOnEnd() => E2EAppFixture.Run(actor =>
    {
        actor.SelectTool(Annotation.ToolKind.Pen);
        var surface = actor.Surface(1);

        // 캡처 전: 펜 도구라 서피스가 입력을 받는다 (클릭 통과 아님)
        Assert.False(WindowStyling.IsClickThrough(surface.Hwnd));

        // 캡처 세션 시작: 서피스 입력 중단 = 클릭 통과
        actor.StartCapture();
        Assert.True(WindowStyling.IsClickThrough(surface.Hwnd));

        // 세션 취소: 상태에 맞게 다시 입력을 받는다
        actor.App.Capture.CancelCaptureSession();
        actor.Pump();
        Assert.False(WindowStyling.IsClickThrough(surface.Hwnd));

        // 세션 종료 후 다시 그리기 가능 확인
        var docCountBefore = surface.Document.Elements.Count;
        actor.DrawStroke(new Point(100, 100), new Point(200, 200), monitorIndex: 1);
        Assert.True(surface.Document.Elements.Count > docCountBefore);
    });
}
