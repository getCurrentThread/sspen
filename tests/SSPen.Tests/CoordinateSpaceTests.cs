using System.Windows;
using SSPen.Interop;
using SSPen.Shell;
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

    // ---- 길이 환산: 크기는 올림, 여백은 반올림 (65단계, A1-7) ----
    // 툴바(PlaceToolbar)·토스트(ToastWindow)가 호출 지점에서 쓰던 식을 옮겨 왔으므로 결과가 비트 단위로 같아야 한다.

    /// <summary>Windows가 내놓는 배율 계단 — 100%~350%.</summary>
    private static readonly double[] WindowsScales = [1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0, 3.5];

    /// <summary>
    /// 환산 입력 표본: 레이아웃 반올림이 만드는 <c>n/배율</c> 값(곱에 부동소수 잡음이 끼는 형태), 0.1 간격 소수,
    /// 그리고 실제 호출 지점이 넘기는 두 여백 상수.
    /// </summary>
    private static IEnumerable<double> LengthSamples(double scale)
    {
        for (int n = 0; n <= 600; n++)
        {
            yield return n / scale;
            yield return n * 0.1;
        }
        yield return ToolbarPlacement.RightMargin;
        yield return ToastPlacement.BottomMarginDip;
    }

    [Theory]
    [InlineData(34, 1.25, 43)]
    [InlineData(100.2, 1.5, 151)]
    [InlineData(10.01, 1.0, 11)]
    public void ToPhysicalExtent_FractionalProduct_RoundsUp(double logical, double scale, int expected)
    {
        Assert.Equal(expected, CoordinateSpace.ToPhysicalExtent(logical, scale));
    }

    [Theory]
    [InlineData(34, 1.5, 51)]
    [InlineData(48, 1.25, 60)]
    [InlineData(0, 2.0, 0)]
    public void ToPhysicalExtent_ExactProduct_Unchanged(double logical, double scale, int expected)
    {
        Assert.Equal(expected, CoordinateSpace.ToPhysicalExtent(logical, scale));
    }

    /// <summary>
    /// 특성화: 175%에서 레이아웃 반올림된 29물리px 폭(29/1.75 DIP)을 되돌리면 곱이 29.000000000000004라 30이 된다.
    /// 옮기기 전 식 <c>(int)Math.Ceiling(ActualWidth * dpi)</c>의 결과 그대로이며, 헬퍼가 잡음을 흡수(ε 보정 등)하면
    /// 툴바·토스트 크기가 1px 달라진다. 바꾸려면 동작 변경 단계에서 이 증인과 함께 바꾼다.
    /// </summary>
    [Theory]
    [InlineData(29 / 1.75, 1.75, 30)]
    [InlineData(58 / 1.75, 1.75, 59)]
    [InlineData(61 / 1.75, 1.75, 61)]
    public void ToPhysicalExtent_LayoutRoundedFloatNoise_CeilsRawProduct(double logical, double scale, int expected)
    {
        Assert.Equal(expected, CoordinateSpace.ToPhysicalExtent(logical, scale));
    }

    [Theory]
    [InlineData(12, 1.5, 18)]
    [InlineData(12, 1.25, 15)]
    [InlineData(48, 1.25, 60)]
    [InlineData(12.4, 1.0, 12)]
    [InlineData(12.6, 1.0, 13)]
    public void ToPhysicalLength_NonMidpoint_RoundsToNearest(double logical, double scale, int expected)
    {
        Assert.Equal(expected, CoordinateSpace.ToPhysicalLength(logical, scale));
    }

    /// <summary>특성화: 기본 <c>Math.Round</c>라 중간값은 짝수 쪽으로 간다 (AwayFromZero가 아니다).</summary>
    [Theory]
    [InlineData(2.5, 1.0, 2)]
    [InlineData(3.5, 1.0, 4)]
    [InlineData(5, 1.5, 8)]
    [InlineData(10, 1.25, 12)]
    [InlineData(-2.5, 1.0, -2)]
    public void ToPhysicalLength_Midpoint_UsesBankersRounding(double logical, double scale, int expected)
    {
        Assert.Equal(expected, CoordinateSpace.ToPhysicalLength(logical, scale));
    }

    [Fact]
    public void ToPhysicalExtent_AcrossWindowsScales_MatchesPreStepCeilingFormula()
    {
        foreach (double scale in WindowsScales)
        {
            foreach (double logical in LengthSamples(scale))
            {
                Assert.Equal((int)Math.Ceiling(logical * scale), CoordinateSpace.ToPhysicalExtent(logical, scale));
            }
        }
    }

    [Fact]
    public void ToPhysicalLength_AcrossWindowsScales_MatchesPreStepRoundFormula()
    {
        foreach (double scale in WindowsScales)
        {
            foreach (double logical in LengthSamples(scale))
            {
                Assert.Equal((int)Math.Round(logical * scale), CoordinateSpace.ToPhysicalLength(logical, scale));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToPhysicalExtent_InvalidDpi_Throws(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSpace.ToPhysicalExtent(10, dpi));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToPhysicalLength_InvalidDpi_Throws(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSpace.ToPhysicalLength(10, dpi));
    }

    // ---- PhysicalRect.FromLtrb: RECT 네 변 → 원점+크기 (65단계, A7-8) ----

    [Fact]
    public void FromLtrb_NegativeOrigin_ComputesSize()
    {
        var rect = PhysicalRect.FromLtrb(-1920, 0, 0, 1040);

        Assert.Equal(new PhysicalRect(-1920, 0, 1920, 1040), rect);
    }

    [Fact]
    public void FromLtrb_Degenerate_IsEmpty()
    {
        var rect = PhysicalRect.FromLtrb(5, 5, 5, 9);

        Assert.Equal(new PhysicalRect(5, 5, 0, 4), rect);
        Assert.True(rect.IsEmpty);
    }

    [Fact]
    public void FromLtrb_OfRightAndBottom_RoundTripsRect()
    {
        foreach (var rect in new[] { LeftMonitor, CenterMonitor, new PhysicalRect(-3840, -200, 1920, 1040) })
        {
            Assert.Equal(rect, PhysicalRect.FromLtrb(rect.X, rect.Y, rect.Right, rect.Bottom));
        }
    }
}
