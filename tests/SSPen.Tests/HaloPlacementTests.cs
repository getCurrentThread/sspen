using System.Windows;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HaloPlacement"/>의 증인 (42단계, ARCH-3, AGENTS L17). 작업 영역(WorkArea) 포함 판정과 음수 원점·혼합 DPI 로컬 좌표를
/// VirtualTopologySimulationTests 스타일로 고정한다 — 창 코드(UpdateHalo)에는 이전까지 헤드리스 증인이 없었다.
/// </summary>
public class HaloPlacementTests
{
    private static readonly PhysicalRect WorkArea = new(0, 0, 1920, 1040); // 작업 표시줄 40px 제외

    [Fact]
    public void IsVisible_InsideMonitorButInTaskbarBand_IsHidden()
    {
        Assert.True(HaloPlacement.IsVisible(true, true, WorkArea, 100, 1000));
        Assert.False(HaloPlacement.IsVisible(true, true, WorkArea, 100, 1060)); // rcMonitor 안이지만 rcWork 밖
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void IsVisible_HaloOffOrSurfacesHidden_IsHidden(bool haloActive, bool surfacesVisible) =>
        Assert.False(HaloPlacement.IsVisible(haloActive, surfacesVisible, WorkArea, 100, 100));

    [Fact]
    public void IsVisible_OutsideThisMonitor_IsHidden() =>
        Assert.False(HaloPlacement.IsVisible(true, true, WorkArea, 2000, 100));

    [Fact]
    public void TopLeft_NegativeOriginMonitor_UsesLocalCoordinates()
    {
        var left = new PhysicalRect(-1920, 0, 1920, 1040);

        var topLeft = HaloPlacement.TopLeft(left, physicalX: -1900, physicalY: 30, dpiScale: 1.0);

        Assert.Equal(new Point(20 - 20, 30 - 20), topLeft);
    }

    [Fact]
    public void TopLeft_150Percent_DividesByScale()
    {
        var topLeft = HaloPlacement.TopLeft(WorkArea, physicalX: 300, physicalY: 150, dpiScale: 1.5);

        Assert.Equal(new Point(200 - 20, 100 - 20), topLeft);
    }

    [Fact]
    public void Diameter_Is40_Today() => Assert.Equal(40, HaloPlacement.Diameter);
}
