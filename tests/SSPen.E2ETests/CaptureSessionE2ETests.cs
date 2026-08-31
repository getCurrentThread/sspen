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
}
