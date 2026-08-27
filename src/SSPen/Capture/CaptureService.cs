using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SSPen.Interop;

namespace SSPen.Capture;

/// <summary>
/// GDI BitBlt 캡처 서비스 (WI-10, 스펙 고정: WGC 아님 — F19 SDR 32bpp 환경).
/// 전체 가상 스크린(5760x1080, 원점 -1920,0)을 물리 픽셀로 스냅샷한다.
/// 잉크 포함 as-seen 의미론: 콘텐츠 서피스는 보이는 채로 찍는다 (인텐트 확정).
/// </summary>
public static class CaptureService
{
    /// <summary>영역의 비트맵 내 오프셋 (순수 사각형 수학, 음수 원점 회귀 방지 — R2).</summary>
    public static (int X, int Y) RegionToBitmapOffset(PhysicalRect region, PhysicalRect virtualScreen) =>
        (region.X - virtualScreen.X, region.Y - virtualScreen.Y);

    /// <summary>가상 스크린 전체를 BitBlt로 스냅샷.</summary>
    public static BitmapSource CaptureVirtualScreen()
    {
        var vs = MonitorTopology.VirtualScreen();
        return CaptureRegion(vs);
    }

    /// <summary>물리 사각형 영역을 BitBlt로 스냅샷.</summary>
    public static BitmapSource CaptureRegion(PhysicalRect region)
    {
        if (region.IsEmpty)
        {
            throw new ArgumentException("빈 영역은 캡처할 수 없습니다.", nameof(region));
        }

        nint screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
        {
            throw new InvalidOperationException("화면 DC를 얻지 못했습니다.");
        }
        nint memDc = 0;
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            previous = NativeMethods.SelectObject(memDc, bitmap);
            if (!NativeMethods.BitBlt(
                memDc, 0, 0, region.Width, region.Height,
                screenDc, region.X, region.Y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                throw new InvalidOperationException("BitBlt 실패");
            }
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != 0)
            {
                NativeMethods.SelectObject(memDc, previous);
            }
            if (bitmap != 0)
            {
                NativeMethods.DeleteObject(bitmap);
            }
            if (memDc != 0)
            {
                NativeMethods.DeleteDC(memDc);
            }
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>가상 스크린 스냅샷에서 물리 영역을 잘라낸다 (경계 클램프 포함).</summary>
    public static BitmapSource Crop(BitmapSource snapshot, PhysicalRect region, PhysicalRect virtualScreen)
    {
        var clamped = CoordinateSpace.Clamp(region, virtualScreen);
        if (clamped.IsEmpty)
        {
            throw new ArgumentException("가상 스크린과 겹치지 않는 영역입니다.", nameof(region));
        }
        var (x, y) = RegionToBitmapOffset(clamped, virtualScreen);
        var cropped = new CroppedBitmap(snapshot, new Int32Rect(x, y, clamped.Width, clamped.Height));
        cropped.Freeze();
        return cropped;
    }
}
