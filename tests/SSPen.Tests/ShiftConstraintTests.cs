using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-9: Shift 제약 — 15도 스냅(선·화살표), 정사각형/정원 정규화 (음수 방향 드래그 포함).</summary>
public class ShiftConstraintTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void SnapAngle_NearHorizontal_SnapsToZeroDegrees()
    {
        var snapped = ShiftConstraints.SnapAngle(new Point(0, 0), new Point(100, 5));
        Assert.Equal(0, Math.Atan2(snapped.Y, snapped.X), 3);
        // 크기 유지
        double length = Math.Sqrt(100 * 100 + 5 * 5);
        Assert.Equal(length, snapped.X, 3);
    }

    [Fact]
    public void SnapAngle_22Degrees_SnapsTo15()
    {
        double angle = 22 * Math.PI / 180;
        var end = new Point(100 * Math.Cos(angle), 100 * Math.Sin(angle));
        var snapped = ShiftConstraints.SnapAngle(new Point(0, 0), end);
        double resultDegrees = Math.Atan2(snapped.Y, snapped.X) * 180 / Math.PI;
        Assert.Equal(15, resultDegrees, 3);
    }

    [Fact]
    public void SnapAngle_23Degrees_SnapsTo30()
    {
        double angle = 23 * Math.PI / 180;
        var end = new Point(100 * Math.Cos(angle), 100 * Math.Sin(angle));
        var snapped = ShiftConstraints.SnapAngle(new Point(0, 0), end);
        double resultDegrees = Math.Atan2(snapped.Y, snapped.X) * 180 / Math.PI;
        Assert.Equal(30, resultDegrees, 3);
    }

    [Fact]
    public void SnapAngle_NegativeQuadrant_SnapsCorrectly()
    {
        // 시작점이 원점이 아니어도, 3사분면 드래그도 동작.
        var start = new Point(50, 50);
        double angle = -99 * Math.PI / 180;
        var end = new Point(start.X + 80 * Math.Cos(angle), start.Y + 80 * Math.Sin(angle));
        var snapped = ShiftConstraints.SnapAngle(start, end);
        double resultDegrees = Math.Atan2(snapped.Y - start.Y, snapped.X - start.X) * 180 / Math.PI;
        Assert.Equal(-105, resultDegrees, 3); // -99도는 -105에 더 가깝다 (6 < 9)
    }

    [Fact]
    public void SnapAngle_ZeroLength_ReturnsEnd()
    {
        var p = new Point(10, 10);
        Assert.Equal(p, ShiftConstraints.SnapAngle(p, p));
    }

    [Theory]
    [InlineData(100, 40, 100, 100)]    // 우하 드래그 → 큰 축 기준 정사각형
    [InlineData(-100, 40, -100, 100)]  // 좌하
    [InlineData(-30, -90, -90, -90)]   // 좌상
    [InlineData(70, -20, 70, -70)]     // 우상
    public void NormalizeSquare_PreservesDragDirection(double dx, double dy, double expectedDx, double expectedDy)
    {
        var start = new Point(200, 200);
        var end = new Point(start.X + dx, start.Y + dy);
        var normalized = ShiftConstraints.NormalizeSquare(start, end);
        Assert.Equal(expectedDx, normalized.X - start.X, Tolerance);
        Assert.Equal(expectedDy, normalized.Y - start.Y, Tolerance);
    }

    [Fact]
    public void Apply_RoutesByShapeKind()
    {
        var start = new Point(0, 0);
        var end = new Point(100, 30);

        var line = ShiftConstraints.Apply(ShapeKind.Line, start, end);
        var arrow = ShiftConstraints.Apply(ShapeKind.Arrow, start, end);
        Assert.Equal(line, arrow);

        var square = ShiftConstraints.Apply(ShapeKind.Rectangle, start, end);
        Assert.Equal(100, square.X, Tolerance);
        Assert.Equal(100, square.Y, Tolerance);

        var circle = ShiftConstraints.Apply(ShapeKind.Ellipse, start, end);
        Assert.Equal(square, circle);
    }

    // ---- SnapDegrees (X1): 회전 변형이 필요로 하는 각도 함수. SnapAngle도 이것을 경유한다. ----

    [Theory]
    [InlineData(7, 0)]
    [InlineData(8, 15)]
    [InlineData(22, 15)]
    [InlineData(23, 30)]
    [InlineData(44, 45)]
    [InlineData(359, 360)]
    public void SnapDegrees_AtBoundary_RoundsToNearestStep(double input, double expected)
    {
        Assert.Equal(expected, ShiftConstraints.SnapDegrees(input), Tolerance);
    }

    /// <summary>
    /// 정확히 반 칸은 **바깥쪽으로** 붙어야 한다. <c>Math.Round</c> 기본값(은행가 반올림)은
    /// 7.5/15 = 0.5를 짝수인 0으로 내려 **0도로 스냅**해 버렸다. 7과 8만 테스트하면
    /// 정확히 이 지점을 비켜가므로 결함이 숨는다 — 반 칸을 명시적으로 고정한다.
    /// </summary>
    [Theory]
    [InlineData(7.5, 15)]
    [InlineData(22.5, 30)]
    [InlineData(37.5, 45)]
    [InlineData(-7.5, -15)]
    [InlineData(-22.5, -30)]
    public void SnapDegrees_AtExactMidpoint_RoundsAwayFromZero(double input, double expected)
    {
        Assert.Equal(expected, ShiftConstraints.SnapDegrees(input), Tolerance);
    }

    [Theory]
    [InlineData(-7, 0)]
    [InlineData(-8, -15)]
    [InlineData(-99, -105)]
    [InlineData(-23, -30)]
    public void SnapDegrees_NegativeAngle_SnapsSymmetrically(double input, double expected)
    {
        Assert.Equal(expected, ShiftConstraints.SnapDegrees(input), Tolerance);
    }

    [Fact]
    public void SnapDegrees_CustomStep_UsesThatStep()
    {
        Assert.Equal(90, ShiftConstraints.SnapDegrees(80, 90), Tolerance);
        Assert.Equal(0, ShiftConstraints.SnapDegrees(40, 90), Tolerance);
    }

    [Fact]
    public void SnapDegrees_NonPositiveStep_ReturnsInputUnchanged()
    {
        Assert.Equal(37.5, ShiftConstraints.SnapDegrees(37.5, 0), Tolerance);
        Assert.Equal(37.5, ShiftConstraints.SnapDegrees(37.5, -15), Tolerance);
    }
}
