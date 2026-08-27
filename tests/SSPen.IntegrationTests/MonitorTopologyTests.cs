using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 머신 바운드 (프리모템 2): 대상 PC 토폴로지 — 3모니터, 가상 스크린 5760x1080 원점 (-1920,0).
/// </summary>
public class MonitorTopologyTests
{
    [Fact]
    public void Enumerate_ReturnsThreeMonitors_WithExpectedPhysicalRects()
    {
        var monitors = MonitorTopology.Enumerate();
        Assert.Equal(3, monitors.Count);

        // 왼쪽→오른쪽 정렬 계약.
        Assert.Equal(new PhysicalRect(-1920, 0, 1920, 1080), monitors[0].Bounds);
        Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), monitors[1].Bounds);
        Assert.Equal(new PhysicalRect(1920, 0, 1920, 1080), monitors[2].Bounds);

        // 주 모니터는 (0,0).
        Assert.True(monitors[1].IsPrimary);
    }

    [Fact]
    public void WorkArea_ExcludesTaskbar_OnPrimaryMonitor()
    {
        // 사용자 요청 18차: 판서 서피스는 작업 표시줄 위로 올라오면 안 된다.
        // 작업 영역은 모니터 전체 안에 들어있고, 작업 표시줄이 있는 모니터에서는 더 작다.
        var monitors = MonitorTopology.Enumerate();
        var primary = monitors.Single(m => m.IsPrimary);

        Assert.True(primary.WorkArea.Height < primary.Bounds.Height,
            $"주 모니터 작업 영역({primary.WorkArea})이 전체({primary.Bounds})와 같다 — 작업 표시줄이 반영되지 않았다.");

        foreach (var monitor in monitors)
        {
            Assert.True(monitor.WorkArea.X >= monitor.Bounds.X);
            Assert.True(monitor.WorkArea.Y >= monitor.Bounds.Y);
            Assert.True(
                monitor.WorkArea.X + monitor.WorkArea.Width <= monitor.Bounds.X + monitor.Bounds.Width);
            Assert.True(
                monitor.WorkArea.Y + monitor.WorkArea.Height <= monitor.Bounds.Y + monitor.Bounds.Height);
        }
    }

    [Fact]
    public void VirtualScreen_MatchesSpecTopology()
    {
        var vs = MonitorTopology.VirtualScreen();
        Assert.Equal(new PhysicalRect(-1920, 0, 5760, 1080), vs);
    }

    [Fact]
    public void VirtualScreen_EqualsUnionOfMonitors()
    {
        var union = CoordinateSpace.Union(MonitorTopology.Enumerate().Select(m => m.Bounds));
        Assert.Equal(MonitorTopology.VirtualScreen(), union);
    }
}
