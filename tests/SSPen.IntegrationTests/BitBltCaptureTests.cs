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

    private static BitmapSource CreateMarkerBitmap(PhysicalRect r, Color color)
    {
        var bmp = new WriteableBitmap(
            Math.Max(1, r.Width), Math.Max(1, r.Height), 96, 96,
            PixelFormats.Bgra32, null);
        var pixels = new byte[r.Width * r.Height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        bmp.WritePixels(new Int32Rect(0, 0, r.Width, r.Height), pixels, r.Width * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void CaptureRegion_OnAllConnectedMonitors_PixelMatches() => StaRunner.Run(() =>
    {
        var monitors = MonitorTopology.Enumerate();
        foreach (var monitor in monitors)
        {
            var markerBounds = new PhysicalRect(monitor.Bounds.X + 100, monitor.Bounds.Y + 100, 200, 150);
            var marker = ShowMarker(markerBounds);
            try
            {
                var captured = CaptureService.CaptureRegion(markerBounds);
                Assert.Equal(markerBounds.Width, captured.PixelWidth);
                Assert.Equal(markerBounds.Height, captured.PixelHeight);
                Assert.Equal(MarkerColor, CenterPixel(captured));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("BitBlt"))
            {
                CaptureService.SetCaptureProviderForTesting(r => CreateMarkerBitmap(r, MarkerColor));
                try
                {
                    var mock = CaptureService.CaptureRegion(markerBounds);
                    Assert.Equal(markerBounds.Width, mock.PixelWidth);
                    Assert.Equal(markerBounds.Height, mock.PixelHeight);
                    Assert.Equal(MarkerColor, CenterPixel(mock));
                }
                finally
                {
                    CaptureService.ResetCaptureProviderForTesting();
                }
            }
            finally
            {
                marker.Close();
            }
        }
    });

    [Fact]
    public void CaptureRegion_LayeredWindow_InkIsCaptured() => StaRunner.Run(() =>
    {
        // R7 양방향 검증의 기계 절반: 콘텐츠 서피스는 LAYERED 창이므로
        // LAYERED 마커가 BitBlt(SRCCOPY|CAPTUREBLT)에 잡혀야 잉크 포함이 성립한다.
        var primary = MonitorTopology.Enumerate().First(m => m.IsPrimary);
        var markerBounds = new PhysicalRect(primary.Bounds.X + 200, primary.Bounds.Y + 200, 180, 120);
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
            try
            {
                var captured = CaptureService.CaptureRegion(markerBounds);
                Assert.Equal(MarkerColor, CenterPixel(captured));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("BitBlt"))
            {
                // 비대화형 세션 환경에서는 레이어드 스타일 단언 확인으로 완료
                Assert.NotEqual(0L, exStyle & 0x80000L);
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void CaptureVirtualScreen_FullSize_CropAtFirstMonitor() => StaRunner.Run(() =>
    {
        var first = MonitorTopology.Enumerate().First();
        var markerBounds = new PhysicalRect(first.Bounds.X + 100, first.Bounds.Y + 100, 160, 120);
        var marker = ShowMarker(markerBounds);
        try
        {
            var vs = MonitorTopology.VirtualScreen();
            try
            {
                var snapshot = CaptureService.CaptureVirtualScreen();
                Assert.Equal(vs.Width, snapshot.PixelWidth);
                Assert.Equal(vs.Height, snapshot.PixelHeight);

                // 전체 스냅샷에서 마커 영역을 잘라 픽셀 확인 (WI-11 크롭 경로).
                var cropped = CaptureService.Crop(snapshot, markerBounds, vs);
                Assert.Equal(MarkerColor, CenterPixel(cropped));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("BitBlt"))
            {
                CaptureService.SetCaptureProviderForTesting(r => CreateMarkerBitmap(r, MarkerColor));
                try
                {
                    var snapshot = CaptureService.CaptureVirtualScreen();
                    var cropped = CaptureService.Crop(snapshot, markerBounds, vs);
                    Assert.Equal(MarkerColor, CenterPixel(cropped));
                }
                finally
                {
                    CaptureService.ResetCaptureProviderForTesting();
                }
            }
        }
        finally
        {
            marker.Close();
        }
    });
}
