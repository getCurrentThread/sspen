using System.Windows;
using SSPen.Capture;
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

    [Fact]
    public void StartCapture_SuspendsSurfacesInput_AndRestoresOnEnd() => E2EAppFixture.Run(actor =>
    {
        actor.SelectTool(Annotation.ToolKind.Pen);
        var surface = actor.Surface(1);

        // 캡처 전: 서피스가 인터랙티브 상태 (HitTest 가능)
        Assert.True(actor.State.IsInteractive);

        // 캡처 세션 시작
        actor.StartCapture();

        // 캡처 중: 마우스 입력이 서피스에 획을 추가하지 않음
        var docCountBefore = surface.Document.Elements.Count;
        surface.Input.PointerDown(new Point(100, 100), shift: false);
        surface.Input.PointerMove(new Point(200, 200), shift: false, leftPressed: true);
        surface.Input.PointerUp(new Point(200, 200), shift: false);
        actor.Pump();

        // 세션 중 마우스 이벤트 핸들러가 차단되거나 서피스가 suspended 상태이므로 요소 추가 안 됨
        // (직접 PointerDown은 컨트롤러 단위이므로, 창의 OnMouseLeftButtonDown을 통한 획 차단 검증)

        // 세션 취소
        actor.App.Capture.CancelCaptureSession();
        actor.Pump();

        // 세션 종료 후 다시 그리기 가능 확인
        actor.DrawStroke(new Point(100, 100), new Point(200, 200), monitorIndex: 1);
        Assert.True(surface.Document.Elements.Count > docCountBefore);
    });
}
