using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Capture;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 프리모템 2 / AC-10 기계 검증: 알려진 마커 패턴 창을 각 모니터에 띄우고
/// BitBlt 캡처 → 픽셀 비교. 하니스가 자체 마커 창을 만든다 (CRIT-2 계약).
/// </summary>
public class BitBltCaptureTests
{
    private static readonly Color MarkerColor = Color.FromRgb(255, 0, 255); // 마젠타 마커

    private static Window ShowMarker(PhysicalRect bounds)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = new SolidColorBrush(MarkerColor),
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = bounds.X,
            Top = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        };
        window.Show();
        WindowStyling.PlacePhysical(WindowStyling.GetHwnd(window), bounds);
        StaRunner.PumpMessages();
        Thread.Sleep(400); // DWM 합성 대기
        return window;
    }

    private static Color CenterPixel(BitmapSource source)
    {
        int cx = source.PixelWidth / 2;
        int cy = source.PixelHeight / 2;
        var cropped = new CroppedBitmap(source, new Int32Rect(cx, cy, 1, 1));
        var pixel = new byte[4];
        cropped.CopyPixels(pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]); // BGRA
    }

    [Theory]
    [InlineData(-1900, 50)]  // 왼쪽 모니터 (음수 원점)
    [InlineData(400, 300)]   // 주 모니터
    [InlineData(2300, 200)]  // 오른쪽 모니터
    public void CaptureRegion_MarkerWindow_PixelMatches(int x, int y) => StaRunner.Run(() =>
    {
        var markerBounds = new PhysicalRect(x, y, 200, 150);
        var marker = ShowMarker(markerBounds);
        try
        {
            var captured = CaptureService.CaptureRegion(markerBounds);
            Assert.Equal(markerBounds.Width, captured.PixelWidth);
            Assert.Equal(markerBounds.Height, captured.PixelHeight);
            Assert.Equal(MarkerColor, CenterPixel(captured));
        }
        finally
        {
            marker.Close();
        }
    });

    [Fact]
    public void CaptureRegion_LayeredWindow_InkIsCaptured() => StaRunner.Run(() =>
    {
        // R7 양방향 검증의 기계 절반: 콘텐츠 서피스는 LAYERED 창이므로
        // LAYERED 마커가 BitBlt(SRCCOPY|CAPTUREBLT)에 잡혀야 잉크 포함이 성립한다.
        var markerBounds = new PhysicalRect(500, 400, 180, 120);
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, // → WS_EX_LAYERED
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = new SolidColorBrush(MarkerColor),
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = markerBounds.X,
            Top = markerBounds.Y,
            Width = markerBounds.Width,
            Height = markerBounds.Height,
        };
        window.Show();
        WindowStyling.PlacePhysical(WindowStyling.GetHwnd(window), markerBounds);
        StaRunner.PumpMessages();
        Thread.Sleep(400);
        try
        {
            long exStyle = WindowStyling.GetExStyle(WindowStyling.GetHwnd(window));
            Assert.NotEqual(0L, exStyle & 0x80000L); // LAYERED 확인
            var captured = CaptureService.CaptureRegion(markerBounds);
            Assert.Equal(MarkerColor, CenterPixel(captured));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void CaptureVirtualScreen_FullSize_CropAtNegativeOrigin() => StaRunner.Run(() =>
    {
        var markerBounds = new PhysicalRect(-1800, 100, 160, 120);
        var marker = ShowMarker(markerBounds);
        try
        {
            var vs = MonitorTopology.VirtualScreen();
            var snapshot = CaptureService.CaptureVirtualScreen();
            Assert.Equal(vs.Width, snapshot.PixelWidth);
            Assert.Equal(vs.Height, snapshot.PixelHeight);

            // 전체 스냅샷에서 마커 영역을 잘라 픽셀 확인 (WI-11 크롭 경로).
            var cropped = CaptureService.Crop(snapshot, markerBounds, vs);
            Assert.Equal(MarkerColor, CenterPixel(cropped));
        }
        finally
        {
            marker.Close();
        }
    });
}
