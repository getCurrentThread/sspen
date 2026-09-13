using System.Windows;
using System.Windows.Controls;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// 텍스트 도구 핸드셰이크의 z-밴드 요청 (54단계 L0). 서피스 활성화는 앱 안에서 서피스가 툴바 위로 올라갈 수 있는 유일한 계기다 —
/// 활성화 <b>뒤</b>에 밴드 재적용을 요청해야 하고(앞이면 적용 뒤에 상승이 온다), 커밋(NOACTIVATE 복원) 뒤에도 한 번 더 요청한다.
/// 창 없이 <see cref="SurfaceHarness"/>의 호출 기록으로 순서를 단언한다.
/// </summary>
public class TextEditZBandTests
{
    [Fact]
    public void PointerDown_TextTool_RequestsZBandAfterActivation()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;

            h.Controller.PointerDown(new Point(10, 10), shift: false, overActiveEditor: false);

            Assert.Equal(["SetNoActivate(False)", "ActivateWindow", "RequestZBand"], h.HostCalls);
            Assert.Single(h.Canvas.Children.OfType<TextBox>());
        });
    }

    [Fact]
    public void ClickOutside_CommitsText_RequestsZBandAfterNoActivateRestored()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;
            h.Controller.PointerDown(new Point(10, 10), shift: false, overActiveEditor: false);
            h.Canvas.Children.OfType<TextBox>().Single().Text = "가";
            h.HostCalls.Clear();

            h.Controller.PointerDown(new Point(400, 400), shift: false, overActiveEditor: false);

            Assert.Equal(["SetNoActivate(True)", "RequestZBand"], h.HostCalls);
            Assert.Equal(2, h.ZBandRequests);
        });
    }

    [Fact]
    public void PointerDown_PenTool_NeverTouchesActivationOrZBand()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;

            h.Controller.PointerDown(new Point(10, 10), shift: false);
            h.Controller.PointerMove(new Point(30, 30), shift: false, leftPressed: true);
            h.Controller.PointerUp(new Point(30, 30), shift: false);

            Assert.Empty(h.HostCalls);
        });
    }

    private sealed class Harness() : SurfaceHarness(new SurfaceHarnessOptions());
}
