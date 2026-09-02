using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToolbarPlacement"/>의 증인 (34단계, AC-21/CRIT-17). E2E 시동은 툴바 위치를 단언하지 않았으므로 이 표가 유일한 증인이다.
/// </summary>
public class ToolbarPlacementTests
{
    private static readonly PhysicalRect Primary = new(0, 0, 1920, 1080);

    [Fact]
    public void Initial_NoSavedPosition_RightEdgeVerticalCenter()
    {
        var (left, top) = ToolbarPlacement.Initial(null, null, Primary);

        Assert.Equal(1920 - 34 - 12, left);
        Assert.Equal((1080 - 524) / 2.0, top);
    }

    [Fact]
    public void Initial_NegativeOriginMonitor_OffsetsFromItsOrigin()
    {
        var (left, top) = ToolbarPlacement.Initial(null, null, new PhysicalRect(-1920, 0, 1920, 1080));

        Assert.Equal(-1920 + 1920 - 46, left);
        Assert.Equal(278, top);
    }

    [Theory]
    [InlineData(100.0, null)]
    [InlineData(null, 200.0)]
    public void Initial_OnlyOneSaved_UsesDefault(double? savedLeft, double? savedTop)
    {
        var (left, top) = ToolbarPlacement.Initial(savedLeft, savedTop, Primary);

        Assert.Equal(1874, left);
        Assert.Equal(278, top);
    }

    [Fact]
    public void Initial_BothSaved_Restores()
    {
        var (left, top) = ToolbarPlacement.Initial(123.5, -40, Primary);

        Assert.Equal(123.5, left);
        Assert.Equal(-40, top);
    }

    /// <summary>CRIT-17: 스트립 높이가 틀어지면 툴바가 중앙에서 밀린다. 34·12는 다른 두 양이라 46으로 합치지 않는다.</summary>
    [Fact]
    public void Constants_AreTheMeasuredStripToday()
    {
        Assert.Equal(524, ToolbarPlacement.StripHeight);
        Assert.Equal(34, ToolbarPlacement.StripWidth);
        Assert.Equal(12, ToolbarPlacement.RightMargin);
    }
}
