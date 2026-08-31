using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 실제 실행 머신의 Win32 모니터 토폴로지 열거 및 불변식 검증.
/// 특정 하드웨어 구성(3모니터)에 종속되지 않고 단일/다중 모니터 환경 모두에서 유효한 불변식을 단언한다.
/// </summary>
public class MonitorTopologyTests
{
    [Fact]
    public void Enumerate_ReturnsAtLeastOneMonitor_WithSortedPhysicalRects()
    {
        var monitors = MonitorTopology.Enumerate();
        Assert.NotEmpty(monitors);

        // 정확히 하나의 주 모니터가 존재해야 한다.
        Assert.Single(monitors, m => m.IsPrimary);

        // 왼쪽→오른쪽 정렬 계약 검증.
        for (int i = 0; i < monitors.Count - 1; i++)
        {
            Assert.True(
                monitors[i].Bounds.X < monitors[i + 1].Bounds.X ||
                (monitors[i].Bounds.X == monitors[i + 1].Bounds.X && monitors[i].Bounds.Y <= monitors[i + 1].Bounds.Y),
                "모니터 목록이 좌->우 (동일 X좌표 시 상->하) 순으로 정렬되어야 합니다.");
        }

        // 모든 모니터의 크기는 양수여야 한다.
        foreach (var monitor in monitors)
        {
            Assert.True(monitor.Bounds.Width > 0);
            Assert.True(monitor.Bounds.Height > 0);
        }
    }

    [Fact]
    public void WorkArea_FitsInsideBounds_OnAllMonitors()
    {
        // 사용자 요청 18차: 판서 서피스는 작업 표시줄 위로 올라오면 안 된다.
        // 작업 영역은 항상 모니터 전체 영역 내에 포함되어야 한다.
        var monitors = MonitorTopology.Enumerate();
        Assert.NotEmpty(monitors);

        foreach (var monitor in monitors)
        {
            Assert.True(monitor.WorkArea.X >= monitor.Bounds.X, $"WorkArea.X ({monitor.WorkArea.X}) < Bounds.X ({monitor.Bounds.X})");
            Assert.True(monitor.WorkArea.Y >= monitor.Bounds.Y, $"WorkArea.Y ({monitor.WorkArea.Y}) < Bounds.Y ({monitor.Bounds.Y})");
            Assert.True(
                monitor.WorkArea.X + monitor.WorkArea.Width <= monitor.Bounds.X + monitor.Bounds.Width,
                "WorkArea 우측 경계가 Bounds를 초과했습니다.");
            Assert.True(
                monitor.WorkArea.Y + monitor.WorkArea.Height <= monitor.Bounds.Y + monitor.Bounds.Height,
                "WorkArea 하단 경계가 Bounds를 초과했습니다.");
        }
    }

    [Fact]
    public void VirtualScreen_EqualsUnionOfMonitors()
    {
        var monitors = MonitorTopology.Enumerate();
        var union = CoordinateSpace.Union(monitors.Select(m => m.Bounds));
        var vs = MonitorTopology.VirtualScreen();

        Assert.Equal(union, vs);
        Assert.True(vs.Width > 0);
        Assert.True(vs.Height > 0);
    }
}
