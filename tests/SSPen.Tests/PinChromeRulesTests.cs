using SSPen.Pin;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="PinChromeRules"/>의 증인 (AC-14..18). 핵심은 두 표식의 성격 차이다 —
/// 호버 크롬은 조건부, 통과 표식은 상시. 통과 중에는 창이 마우스를 받지 못해 호버가 정의상 성립하지 않는다.
/// </summary>
public class PinChromeRulesTests
{
    private const double Wide = 400;
    private const double Tall = 300;

    [Fact]
    public void Resolve_HoveredAndInteractive_ShowsChrome()
    {
        var state = PinChromeRules.Resolve(mouseOver: true, clickThrough: false, scale: 1.0, Wide, Tall);

        Assert.True(state.ShowChrome);
        Assert.False(state.ShowClickThroughBadge);
    }

    [Fact]
    public void Resolve_NotHovered_ShowsNeither()
    {
        var state = PinChromeRules.Resolve(mouseOver: false, clickThrough: false, scale: 1.0, Wide, Tall);

        Assert.False(state.ShowChrome);
        Assert.False(state.ShowClickThroughBadge);
    }

    /// <summary>
    /// 통과 중에는 크롬을 그리지 않고 상시 표식만 남긴다. 그려 봐야 누를 수 없고,
    /// 예전처럼 표식이 없으면 되찾는 제스처를 모르는 사용자에게는 닫을 수도 옮길 수도 없는 창이 된다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_ClickThrough_HidesChromeAndShowsTheBadge(bool mouseOver)
    {
        var state = PinChromeRules.Resolve(mouseOver, clickThrough: true, scale: 1.0, Wide, Tall);

        Assert.False(state.ShowChrome);
        Assert.True(state.ShowClickThroughBadge);
    }

    /// <summary>작은 핀은 자기 크롬에 통째로 덮인다 — 그럴 바에는 그림을 보여 준다.</summary>
    [Theory]
    [InlineData(40, 300)]
    [InlineData(400, 24)]
    [InlineData(40, 24)]
    public void Resolve_BelowMinimumFootprint_HidesChrome(double width, double height)
    {
        var state = PinChromeRules.Resolve(mouseOver: true, clickThrough: false, scale: 1.0, width, height);

        Assert.False(state.ShowChrome);
    }

    /// <summary>경계값은 크롬을 보인다 (최소 크기는 '이상').</summary>
    [Fact]
    public void Resolve_ExactlyAtTheMinimumFootprint_ShowsChrome()
    {
        var state = PinChromeRules.Resolve(
            mouseOver: true, clickThrough: false, scale: 1.0,
            PinChromeRules.MinChromeWidth, PinChromeRules.MinChromeHeight);

        Assert.True(state.ShowChrome);
    }

    /// <summary>지금 몇 배로 보고 있는지는 화면 어디에도 없던 정보다.</summary>
    [Theory]
    [InlineData(1.0, "100%")]
    [InlineData(0.1, "10%")]
    [InlineData(1.256, "126%")]
    [InlineData(8.0, "800%")]
    public void FormatZoom_RoundsToWholePercent(double scale, string expected)
    {
        Assert.Equal(expected, PinChromeRules.FormatZoom(scale));
        Assert.Equal(expected, PinChromeRules.Resolve(true, false, scale, Wide, Tall).ZoomPercent);
    }
}
