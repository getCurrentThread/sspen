using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Interop;
using SSPen.Pin;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// 핀 창의 Ctrl 판정이 <b>주입된</b> <c>controlDown</c>을 읽는다는 증인 (81단계, A7-6, D3).
/// 핀은 <c>ShowActivated=false</c>라 캡처 직후나 다른 앱을 쓰는 동안에는 포그라운드가 아닌데, 휠은 호버만으로 핀에 온다.
/// 그때 스레드 로컬 <c>Keyboard.Modifiers</c>는 None이라 Ctrl+휠이 투명도 대신 확대로, Ctrl+가운데 버튼이 무시로 읽혔다.
/// 테스트 스레드에는 포커스가 없으므로 <c>Keyboard.Modifiers</c>는 여기서도 None이다 — 옛 코드는 이 증인에서 빨갛다.
/// 창은 띄우지 않는다(Show 없음): 라우트 이벤트를 직접 올려 <c>OnMouseWheel</c>/<c>OnMouseDown</c>만 태운다.
/// </summary>
public class PinWindowInputTests
{
    private const int Side = 100;

    [Fact]
    public void OnMouseWheel_InjectedCtrlDown_ChangesOpacityNotSize() => RunSta(() =>
    {
        var pin = NewPin(controlDown: () => true);

        pin.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });

        Assert.Equal(0.95, pin.Opacity, 10);
        Assert.Equal(Side, pin.Width);
        Assert.Equal(Side, pin.Height);
    });

    [Fact]
    public void OnMouseDown_MiddleWithInjectedCtrl_TogglesClickThrough() => RunSta(() =>
    {
        var pin = NewPin(controlDown: () => true);

        pin.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = Mouse.MouseDownEvent });

        Assert.True(pin.IsClickThrough);
    });

    [Fact]
    public void OnMouseDown_MiddleWithoutInjectedCtrl_LeavesClickThroughOff() => RunSta(() =>
    {
        var pin = NewPin(controlDown: () => false);

        pin.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = Mouse.MouseDownEvent });

        Assert.False(pin.IsClickThrough);
    });

    [Fact]
    public void OnMouseDown_RightWithInjectedCtrl_DoesNotConsultCtrl() => RunSta(() =>
    {
        // 평가 순서는 버튼 먼저다 — 가운데 버튼이 아니면 Ctrl 썽크를 부르지도 않는다.
        int reads = 0;
        var pin = NewPin(controlDown: () =>
        {
            reads++;
            return true;
        });

        pin.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.MouseDownEvent });

        Assert.Equal(0, reads);
        Assert.False(pin.IsClickThrough);
    });

    private static PinWindow NewPin(Func<bool> controlDown)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        return new PinWindow(image, new PhysicalRect(0, 0, Side, Side), () => 0, controlDown);
    }
}
