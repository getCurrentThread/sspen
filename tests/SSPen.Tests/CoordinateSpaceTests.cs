using System.Windows;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-2: 좌표 이음새 — 음수 원점 (-1920,0)이 핵심 회귀 지점 (R2/R3).</summary>
public class CoordinateSpaceTests
{
    [Fact]
    public void ToLogical_At100Dpi_IsIdentity_IncludingNegativeOrigin()
    {
        var p = CoordinateSpace.ToLogical(-1920, 0, 1.0);
        Assert.Equal(-1920, p.X);
        Assert.Equal(0, p.Y);
    }

    [Fact]
    public void RoundTrip_At100Dpi_PreservesNegativeRect()
    {
        var physical = new PhysicalRect(-1920, 0, 1920, 1080);
        var logical = CoordinateSpace.ToLogical(physical, 1.0);
        var back = CoordinateSpace.ToPhysical(logical, 1.0);
        Assert.Equal(physical, back);
    }

    [Fact]
    public void RoundTrip_At150Dpi_PreservesNegativeRect()
    {
        var physical = new PhysicalRect(-1920, 0, 1920, 1080);
        var logical = CoordinateSpace.ToLogical(physical, 1.5);
        Assert.Equal(-1280, logical.X);
        Assert.Equal(1280, logical.Width, 3);
        var back = CoordinateSpace.ToPhysical(logical, 1.5);
        Assert.Equal(physical, back);
    }

    [Fact]
    public void ToPhysical_NegativeLogicalPoint_RoundsCorrectly()
    {
        var (x, y) = CoordinateSpace.ToPhysical(new Point(-1279.6, 719.5), 1.5);
        Assert.Equal(-1919, x);
        Assert.Equal(1079, y);
    }

    [Fact]
    public void Union_ThreeMonitorTopology_YieldsVirtualScreen()
    {
        // 대상 환경: DISPLAY1(-1920,0), DISPLAY3(0,0), DISPLAY2(1920,0), 전부 1920x1080.
        var union = CoordinateSpace.Union(
        [
            new PhysicalRect(-1920, 0, 1920, 1080),
            new PhysicalRect(0, 0, 1920, 1080),
            new PhysicalRect(1920, 0, 1920, 1080),
        ]);
        Assert.Equal(new PhysicalRect(-1920, 0, 5760, 1080), union);
    }

    [Fact]
    public void Union_Empty_YieldsEmptyRect()
    {
        Assert.Equal(new PhysicalRect(0, 0, 0, 0), CoordinateSpace.Union([]));
    }

    [Fact]
    public void Clamp_RegionSpanningSeam_ClipsToMonitor()
    {
        var leftMonitor = new PhysicalRect(-1920, 0, 1920, 1080);
        var acrossSeam = new PhysicalRect(-100, 100, 300, 200);
        var clamped = CoordinateSpace.Clamp(acrossSeam, leftMonitor);
        Assert.Equal(new PhysicalRect(-100, 100, 100, 200), clamped);
    }

    [Fact]
    public void Clamp_DisjointRegion_IsEmpty()
    {
        var leftMonitor = new PhysicalRect(-1920, 0, 1920, 1080);
        var offScreen = new PhysicalRect(4000, 0, 100, 100);
        Assert.True(CoordinateSpace.Clamp(offScreen, leftMonitor).IsEmpty);
    }

    [Fact]
    public void Contains_NegativeCoordinates()
    {
        var left = new PhysicalRect(-1920, 0, 1920, 1080);
        Assert.True(left.Contains(-1920, 0));
        Assert.True(left.Contains(-1, 1079));
        Assert.False(left.Contains(0, 0)); // 오른쪽 경계는 배타
        Assert.False(left.Contains(-1921, 0));
    }

    // ---- Rebase: 서피스 간 점 사상 (SEL-14, ARCH-20) ----

    private static readonly PhysicalRect LeftMonitor = new(-1920, 0, 1920, 1080);
    private static readonly PhysicalRect CenterMonitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Rebase_SameDpi_IsTranslationOnly()
    {
        // 가운데 모니터 논리 (100,200) = 물리 (100,200) → 왼쪽 모니터 논리 (2020,200).
        var rebased = CoordinateSpace.Rebase(new Point(100, 200), CenterMonitor, 1.0, LeftMonitor, 1.0);

        Assert.Equal(2020, rebased.X, 9);
        Assert.Equal(200, rebased.Y, 9);
    }

    [Fact]
    public void Rebase_DifferentDpi_PreservesPhysicalPosition()
    {
        var source = new Point(300, 400);

        var rebased = CoordinateSpace.Rebase(source, CenterMonitor, 1.0, LeftMonitor, 1.5);

        // 두 좌표계가 가리키는 물리 픽셀이 같아야 한다 — 이것이 Rebase의 정의다.
        double sourcePhysicalX = CenterMonitor.X + source.X * 1.0;
        double targetPhysicalX = LeftMonitor.X + rebased.X * 1.5;
        Assert.Equal(sourcePhysicalX, targetPhysicalX, 1e-6);
    }

    [Fact]
    public void Rebase_SameMonitor_IsIdentity()
    {
        var source = new Point(640, 360);

        var rebased = CoordinateSpace.Rebase(source, CenterMonitor, 1.0, CenterMonitor, 1.0);

        Assert.Equal(source.X, rebased.X, 9);
        Assert.Equal(source.Y, rebased.Y, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidDpi_Throws(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSpace.ToLogical(0, 0, dpi));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void Rebase_InvalidDpi_Throws(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSpace.Rebase(new Point(0, 0), CenterMonitor, dpi, LeftMonitor, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSpace.Rebase(new Point(0, 0), CenterMonitor, 1.0, LeftMonitor, dpi));
    }
}
