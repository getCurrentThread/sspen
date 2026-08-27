using SSPen.Capture;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-10: 캡처 사각형 수학 — 음수 원점 가상 스크린에서의 오프셋/클램프 (R2 회귀 방지).</summary>
public class CaptureRegionMathTests
{
    private static readonly PhysicalRect VirtualScreen = new(-1920, 0, 5760, 1080);

    [Fact]
    public void RegionToBitmapOffset_LeftMonitorRegion_HasSmallOffset()
    {
        // 왼쪽 모니터 (-1900, 50) 영역 → 비트맵 (20, 50).
        var region = new PhysicalRect(-1900, 50, 200, 150);
        var (x, y) = CaptureService.RegionToBitmapOffset(region, VirtualScreen);
        Assert.Equal(20, x);
        Assert.Equal(50, y);
    }

    [Fact]
    public void RegionToBitmapOffset_PrimaryMonitorOrigin_MapsTo1920()
    {
        var region = new PhysicalRect(0, 0, 100, 100);
        var (x, y) = CaptureService.RegionToBitmapOffset(region, VirtualScreen);
        Assert.Equal(1920, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void RegionToBitmapOffset_SeamSpanningRegion()
    {
        // x=0 이음새를 걸치는 영역 (-100..200).
        var region = new PhysicalRect(-100, 10, 300, 50);
        var (x, y) = CaptureService.RegionToBitmapOffset(region, VirtualScreen);
        Assert.Equal(1820, x);
        Assert.Equal(10, y);
    }

    [Fact]
    public void Clamp_RegionBeyondRightEdge_Clips()
    {
        var region = new PhysicalRect(3800, 1000, 200, 200);
        var clamped = CoordinateSpace.Clamp(region, VirtualScreen);
        Assert.Equal(new PhysicalRect(3800, 1000, 40, 80), clamped);
    }

    [Fact]
    public void Clamp_RegionBeyondLeftEdge_Clips()
    {
        var region = new PhysicalRect(-2000, -50, 200, 200);
        var clamped = CoordinateSpace.Clamp(region, VirtualScreen);
        Assert.Equal(new PhysicalRect(-1920, 0, 120, 150), clamped);
    }
}
