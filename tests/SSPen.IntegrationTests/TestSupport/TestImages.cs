using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SSPen.IntegrationTests;

/// <summary>
/// 통합 스위트의 테스트용 핀 이미지 한 곳 (92단계). <c>AnchorBelowTests.SolidImage</c>·<c>PinChromeBoundsTests.Swatch</c>·
/// <c>PinZoomSmoothnessTests.Swatch</c>가 같은 단색(CornflowerBlue, #6495ED) 비트맵을 따로 만들던 것을 합쳤다.
/// 세 곳 모두 픽셀을 읽지 않고 크기만 쓴다 — 픽셀을 읽는 증인은 자기 이미지(<c>PinZoomSmoothnessTests.HalfAndHalf</c>)를 따로 둔다.
/// </summary>
internal static class TestImages
{
    /// <summary><paramref name="width"/>×<paramref name="height"/> 물리 px(96 DPI) 단색 비트맵 — 크기만 맞으면 된다.</summary>
    internal static BitmapSource Solid(int width, int height)
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
}
