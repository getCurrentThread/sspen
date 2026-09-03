using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToastPlacement"/>의 증인. 음수 원점 토폴로지(목표 구성: 3×1920×1080, 원점 −1920,0)와
/// 작업 영역보다 큰 토스트를 포함한다 — 두 경우 모두 창의 시작 지점이 화면 안에 남아야 한다.
/// </summary>
public class ToastPlacementTests
{
    private static MonitorSurfaceInfo Monitor(string name, PhysicalRect bounds, bool primary = false) =>
        new(name, bounds, bounds, primary);

    [Fact]
    public void Anchor_CentersHorizontallyAboveTheWorkAreaBottom()
    {
        var (x, y) = ToastPlacement.Anchor(new PhysicalRect(0, 0, 1920, 1040), width: 400, height: 60, bottomMargin: 48);

        Assert.Equal((1920 - 400) / 2, x);
        Assert.Equal(1040 - 48 - 60, y);
    }

    /// <summary>작업 영역은 화면 전체가 아니다 — 작업 표시줄 위에 놓여야 한다.</summary>
    [Fact]
    public void Anchor_UsesTheWorkAreaBottom_NotTheMonitorBottom()
    {
        var workArea = new PhysicalRect(0, 0, 1920, 1040); // 1080 화면에서 40px 작업 표시줄.

        var (_, y) = ToastPlacement.Anchor(workArea, width: 300, height: 50, bottomMargin: 0);

        Assert.Equal(1040 - 50, y);
    }

    /// <summary>음수 원점(왼쪽 보조 모니터)에서도 그 화면 안에 놓인다.</summary>
    [Fact]
    public void Anchor_NegativeOrigin_StaysOnThatMonitor()
    {
        var (x, y) = ToastPlacement.Anchor(new PhysicalRect(-1920, 0, 1920, 1080), width: 400, height: 60, bottomMargin: 48);

        Assert.InRange(x, -1920, -1920 + 1920 - 400);
        Assert.InRange(y, 0, 1080 - 60);
    }

    /// <summary>토스트가 작업 영역보다 크면 잘라내지 않고 좌상단으로 클램프한다 (최소한 시작 지점은 화면 안).</summary>
    [Fact]
    public void Anchor_ToastWiderThanWorkArea_ClampsToTheOrigin()
    {
        var (x, y) = ToastPlacement.Anchor(new PhysicalRect(100, 200, 300, 100), width: 800, height: 400, bottomMargin: 48);

        Assert.Equal(100, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void MonitorFor_CursorInsideASecondaryMonitor_PicksThatOne()
    {
        var monitors = new[]
        {
            Monitor(@"\\.\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080), primary: true),
            Monitor(@"\\.\DISPLAY2", new PhysicalRect(-1920, 0, 1920, 1080)),
        };

        var picked = ToastPlacement.MonitorFor(monitors, cursorX: -500, cursorY: 500);

        Assert.Equal(@"\\.\DISPLAY2", picked.DeviceName);
    }

    /// <summary>모니터 사이 공백에 커서가 있으면 주 화면으로 떨어진다 (알림을 잃지 않는다).</summary>
    [Fact]
    public void MonitorFor_CursorInAGap_FallsBackToPrimary()
    {
        var monitors = new[]
        {
            Monitor(@"\\.\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080), primary: true),
            Monitor(@"\\.\DISPLAY2", new PhysicalRect(3000, 0, 1920, 1080)),
        };

        var picked = ToastPlacement.MonitorFor(monitors, cursorX: 2500, cursorY: 100);

        Assert.Equal(@"\\.\DISPLAY1", picked.DeviceName);
    }

    /// <summary>주 화면 표시가 없는 목록이어도 무너지지 않는다 — 첫 화면을 쓴다.</summary>
    [Fact]
    public void MonitorFor_NoPrimaryFlag_UsesTheFirstMonitor()
    {
        var monitors = new[] { Monitor(@"\\.\DISPLAY9", new PhysicalRect(0, 0, 800, 600)) };

        Assert.Equal(@"\\.\DISPLAY9", ToastPlacement.MonitorFor(monitors, 5000, 5000).DeviceName);
    }
}
