using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Interop;
using SSPen.Pin;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 핀 호버 크롬이 <b>복구 사각형을 줄이지 않는다</b>는 실측 증인.
///
/// 왜 통합인가: 클릭 통과에 걸린 핀을 되찾는 유일한 경로는 전역 <c>WH_MOUSE_LL</c> 훅이
/// <c>PhysicalBounds()</c>(= <c>GetWindowRect</c>) 안에서 Ctrl+가운데 클릭을 보는 것이다.
/// 크롬을 별도 HWND 팝업으로 만들었거나 테두리 두께를 늘렸다면, 눈에는 핀 위인데 복구 사각형 밖인
/// 픽셀 띠가 생겨 되찾기가 조용히 실패한다. 그 사실은 실제 창 사각형으로만 확인할 수 있다.
/// </summary>
public class PinChromeBoundsTests
{
    private static BitmapSource Swatch(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.CornflowerBlue, null, new System.Windows.Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    [Fact]
    public void PhysicalBounds_WithChromeShown_StillCoversTheWholeWindow() => StaRunner.Run(() =>
    {
        var region = new PhysicalRect(180, 180, 320, 240);
        var pin = new PinWindow(Swatch(region.Width, region.Height), region, () => 0);
        pin.Show();
        WindowStyling.PlacePhysical(pin.Hwnd, region);
        StaRunner.PumpMessages();
        try
        {
            var bounds = pin.PhysicalBounds();

            // 크롬은 창 안쪽 오버레이라 창 사각형에 영향을 주지 않는다.
            Assert.Equal(region.Width, bounds.Width);
            Assert.Equal(region.Height, bounds.Height);
            // 되찾기 히트테스트가 보는 네 모서리가 전부 창 안이다.
            Assert.True(bounds.Contains(bounds.X, bounds.Y));
            Assert.True(bounds.Contains(bounds.Right - 1, bounds.Bottom - 1));
            // 크롬이 놓이는 우상단도 포함된다 — 여기가 잘려 나가면 되찾기가 조용히 실패한다.
            Assert.True(bounds.Contains(bounds.Right - 1, bounds.Y));
        }
        finally
        {
            pin.ClosePin();
        }
    });

    /// <summary>클릭 통과를 켜도 창 사각형은 그대로다 (배지는 오버레이일 뿐이다).</summary>
    [Fact]
    public void PhysicalBounds_AfterEngagingClickThrough_IsUnchanged() => StaRunner.Run(() =>
    {
        var region = new PhysicalRect(200, 200, 260, 200);
        var pin = new PinWindow(Swatch(region.Width, region.Height), region, () => 0);
        pin.Show();
        WindowStyling.PlacePhysical(pin.Hwnd, region);
        StaRunner.PumpMessages();
        try
        {
            var before = pin.PhysicalBounds();
            pin.SetClickThrough(true);
            StaRunner.PumpMessages();

            Assert.Equal(before, pin.PhysicalBounds());
            Assert.True(WindowStyling.IsClickThrough(pin.Hwnd));
        }
        finally
        {
            pin.ClosePin();
        }
    });
}
