using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceRosterPlan"/>의 증인 (23단계, WI-16, R17). 시동(<c>existing</c> 비어 있음)과 설정 동기화가
/// 같은 diff를 쓰며, 토폴로지에서 사라진 모니터는 닫지 않는다는 오늘의 동작(<c>_Today</c>)을 고정한다.
/// </summary>
public class SurfaceRosterPlanTests
{
    private static MonitorSurfaceInfo Mon(string name, bool primary = false) =>
        new(name, new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), primary);

    private static readonly MonitorSurfaceInfo D1 = Mon(@"\\.\DISPLAY1");
    private static readonly MonitorSurfaceInfo D2 = Mon(@"\\.\DISPLAY2", primary: true);
    private static readonly MonitorSurfaceInfo D3 = Mon(@"\\.\DISPLAY3");

    private static HashSet<string> Disabled(params string[] names) => new(names);

    [Fact]
    public void Build_NoExisting_CreatesEveryEnabledMonitorInTopologyOrder()
    {
        var diff = SurfaceRosterPlan.Build([], [D1, D2, D3], Disabled(D2.DeviceName));

        Assert.Empty(diff.ToClose);
        Assert.Equal([D1, D3], diff.ToCreate);
    }

    [Fact]
    public void Build_DisabledExisting_IsInToClose()
    {
        var diff = SurfaceRosterPlan.Build(
            [D1.DeviceName, D2.DeviceName, D3.DeviceName], [D1, D2, D3], Disabled(D2.DeviceName));

        Assert.Equal([D2.DeviceName], diff.ToClose);
        Assert.Empty(diff.ToCreate);
    }

    [Fact]
    public void Build_EnabledMissing_IsInToCreate()
    {
        var diff = SurfaceRosterPlan.Build([D1.DeviceName, D3.DeviceName], [D1, D2, D3], Disabled());

        Assert.Empty(diff.ToClose);
        Assert.Equal([D2], diff.ToCreate);
    }

    [Fact]
    public void Build_AlreadyPresent_IsNotRecreated()
    {
        var diff = SurfaceRosterPlan.Build(
            [D1.DeviceName, D2.DeviceName, D3.DeviceName], [D1, D2, D3], Disabled());

        Assert.Empty(diff.ToClose);
        Assert.Empty(diff.ToCreate);
    }

    /// <summary>보존이지 승인이 아니다: 토폴로지에서 사라진 모니터의 서피스는 오늘 닫히지 않는다.</summary>
    [Fact]
    public void Build_MonitorGoneFromTopology_IsNotClosed_Today()
    {
        var diff = SurfaceRosterPlan.Build([D1.DeviceName, D2.DeviceName, D3.DeviceName], [D1, D2], Disabled());

        Assert.Empty(diff.ToClose);
        Assert.Empty(diff.ToCreate);
    }

    [Fact]
    public void Build_DisabledAndMissing_IsNeitherClosedNorCreated()
    {
        var diff = SurfaceRosterPlan.Build([D1.DeviceName], [D1, D2, D3], Disabled(D2.DeviceName, D3.DeviceName));

        Assert.Empty(diff.ToClose);
        Assert.Empty(diff.ToCreate);
    }
}
