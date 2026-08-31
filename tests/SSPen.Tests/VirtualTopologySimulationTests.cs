using System.Windows;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 가상 모니터 토폴로지 및 Mixed-DPI 시뮬레이션 테스트.
/// 물리 머신 구성에 구애받지 않고 헤드리스 환경에서 다중 모니터, 음수 원점, 혼합 DPI 시나리오를 100% 결정론적으로 검증한다.
/// </summary>
public class VirtualTopologySimulationTests : IDisposable
{
    public VirtualTopologySimulationTests()
    {
        MonitorTopology.ResetProviderForTesting();
    }

    public void Dispose()
    {
        MonitorTopology.ResetProviderForTesting();
    }

    [Fact]
    public void SingleMonitor_WithHighDpi_CalculatesBoundsAndVirtualScreen()
    {
        // 시나리오 1: 단일 고해상도 노트북 모니터 (1920x1080 @ 125% DPI)
        var monitor = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY1",
            Bounds: new PhysicalRect(0, 0, 1920, 1080),
            WorkArea: new PhysicalRect(0, 0, 1920, 1032), // 작업표시줄 제외
            IsPrimary: true);

        MonitorTopology.SetProviderForTesting(() => [monitor]);

        var monitors = MonitorTopology.Enumerate();
        Assert.Single(monitors);
        Assert.True(monitors[0].IsPrimary);
        Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), monitors[0].Bounds);
        Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), MonitorTopology.VirtualScreen());

        // 125% DPI 논리 변환 단언
        var logicalWork = CoordinateSpace.ToLogical(monitors[0].WorkArea, 1.25);
        Assert.Equal(1920 / 1.25, logicalWork.Width);
        Assert.Equal(1032 / 1.25, logicalWork.Height);
    }

    [Fact]
    public void DualMonitor_MixedDpi_RebaseMaintainsPhysicalPosition()
    {
        // 시나리오 2: 혼합 DPI 듀얼 모니터
        // 주 모니터: 4K (3840x2160 @ 150% DPI, 0,0)
        // 부 모니터: FHD (1920x1080 @ 100% DPI, 3840,0)
        var primary = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY1",
            Bounds: new PhysicalRect(0, 0, 3840, 2160),
            WorkArea: new PhysicalRect(0, 0, 3840, 2112),
            IsPrimary: true);

        var secondary = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY2",
            Bounds: new PhysicalRect(3840, 0, 1920, 1080),
            WorkArea: new PhysicalRect(3840, 0, 1920, 1040),
            IsPrimary: false);

        MonitorTopology.SetProviderForTesting(() => [primary, secondary]);

        var vs = MonitorTopology.VirtualScreen();
        Assert.Equal(new PhysicalRect(0, 0, 5760, 2160), vs);

        // 주 모니터(150% DPI)의 중앙 점 (논리 좌표 1280, 720 = 물리 1920, 1080)
        var sourceLogical = new Point(1280, 720);
        var rebasedOnSecondary = CoordinateSpace.Rebase(
            sourceLogical,
            primary.Bounds,
            sourceDpi: 1.5,
            secondary.Bounds,
            targetDpi: 1.0);

        // 부 모니터(100% DPI) 기준에서는 물리 1920 - 3840 = -1920 위치가 되어야 함
        Assert.Equal(-1920, rebasedOnSecondary.X);
        Assert.Equal(1080, rebasedOnSecondary.Y);
    }

    [Fact]
    public void TripleMonitor_WithNegativeOrigin_SortsLeftToRight()
    {
        // 시나리오 3: 음수 원점을 포함한 3모니터 환경 (비정렬 주입 시에도 좌->우 정렬 보장)
        var left = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY2",
            Bounds: new PhysicalRect(-1920, 0, 1920, 1080),
            WorkArea: new PhysicalRect(-1920, 0, 1920, 1080),
            IsPrimary: false);

        var center = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY1",
            Bounds: new PhysicalRect(0, 0, 1920, 1080),
            WorkArea: new PhysicalRect(0, 0, 1920, 1032),
            IsPrimary: true);

        var right = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY3",
            Bounds: new PhysicalRect(1920, 0, 1920, 1080),
            WorkArea: new PhysicalRect(1920, 0, 1920, 1080),
            IsPrimary: false);

        // 역순으로 주입
        MonitorTopology.SetProviderForTesting(() => [right, center, left]);

        var monitors = MonitorTopology.Enumerate();
        Assert.Equal(3, monitors.Count);

        var vs = MonitorTopology.VirtualScreen();
        Assert.Equal(new PhysicalRect(-1920, 0, 5760, 1080), vs);
    }

    [Fact]
    public void VerticalPivotMonitor_CalculatesUnionAndBoundsCorrectly()
    {
        // 시나리오 4: 세로 피벗 모니터가 결합된 환경
        // 주 모니터: 가로 FHD (1920x1080, 0,0)
        // 세로 모니터: 피벗 FHD (1080x1920, 1920,-420)
        var primary = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY1",
            Bounds: new PhysicalRect(0, 0, 1920, 1080),
            WorkArea: new PhysicalRect(0, 0, 1920, 1032),
            IsPrimary: true);

        var vertical = new MonitorSurfaceInfo(
            DeviceName: @"\\.\DISPLAY2",
            Bounds: new PhysicalRect(1920, -420, 1080, 1920),
            WorkArea: new PhysicalRect(1920, -420, 1080, 1920),
            IsPrimary: false);

        MonitorTopology.SetProviderForTesting(() => [primary, vertical]);

        var vs = MonitorTopology.VirtualScreen();
        Assert.Equal(0, vs.X);
        Assert.Equal(-420, vs.Y);
        Assert.Equal(3000, vs.Width); // 1920 + 1080
        Assert.Equal(1920, vs.Height); // max(1080, -420 + 1920 = 1500) -> Y: -420부터 시작하므로 (-420 ~ 1500) = 1920
    }
}
