using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// WI-9류: 화살촉 순수 기하 검증 (<see cref="ShapeGeometry.ArrowHead"/>) — 영길이/길이 clamp/좌우 대칭.
/// 21단계에서 <c>ArrowGeometryTests</c>를 대상 타입 1:1 이름으로 개명했다. 마지막 두 증인은 ARCH-16
/// "렌더 날개점 == 경계 날개점"이 함수 하나로 성립함을 고정한다.
/// </summary>
public class ShapeGeometryTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void ArrowHead_ZeroLength_ReturnsEndTwice()
    {
        var end = new Point(50, 50);
        var (h1, h2) = ShapeGeometry.ArrowHead(end, end);
        Assert.Equal(end, h1);
        Assert.Equal(end, h2);
    }

    [Fact]
    public void ArrowHead_ShortVector_ClampsToMinimumHeadLength()
    {
        // 길이 10 * 0.25 = 2.5 → 최소 8로 clamp.
        var start = new Point(0, 0);
        var end = new Point(10, 0);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);
        double dist1 = (end - h1).Length;
        double dist2 = (end - h2).Length;
        Assert.Equal(8, dist1, 3);
        Assert.Equal(8, dist2, 3);
    }

    [Fact]
    public void ArrowHead_LongVector_ClampsToMaximumHeadLength()
    {
        // 길이 200 * 0.25 = 50 → 최대 24로 clamp.
        var start = new Point(0, 0);
        var end = new Point(200, 0);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);
        double dist1 = (end - h1).Length;
        double dist2 = (end - h2).Length;
        Assert.Equal(24, dist1, 3);
        Assert.Equal(24, dist2, 3);
    }

    [Fact]
    public void ArrowHead_MidLength_UsesQuarterOfLength()
    {
        // 길이 60 * 0.25 = 15 → clamp 범위(8..24) 내부라 그대로 사용.
        var start = new Point(0, 0);
        var end = new Point(60, 0);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);
        double dist1 = (end - h1).Length;
        double dist2 = (end - h2).Length;
        Assert.Equal(15, dist1, 3);
        Assert.Equal(15, dist2, 3);
    }

    [Fact]
    public void ArrowHead_HorizontalVector_IsSymmetricAroundAxis()
    {
        var start = new Point(0, 0);
        var end = new Point(100, 0);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);

        // 수평 화살표: 두 날개점은 x축에 대해 y가 서로 반대 부호로 대칭.
        Assert.Equal(h1.X, h2.X, 3);
        Assert.Equal(-h1.Y, h2.Y, 3);
        Assert.NotEqual(0, h1.Y, 3);
    }

    [Fact]
    public void ArrowHead_DiagonalVector_WingsEquidistantFromEnd()
    {
        var start = new Point(0, 0);
        var end = new Point(100, 100);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);

        double dist1 = (end - h1).Length;
        double dist2 = (end - h2).Length;
        Assert.Equal(dist1, dist2, Tolerance);

        // 두 날개점 사이 중점이 원래 방향선(start→end 연장) 위, 즉 end에서 시작 방향으로 되짚어간 지점과 일치.
        var mid = new Point((h1.X + h2.X) / 2, (h1.Y + h2.Y) / 2);
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var expectedMidDir = new Vector(Math.Cos(angle), Math.Sin(angle));
        var actualMidDir = mid - end;
        actualMidDir.Normalize();
        Assert.Equal(-expectedMidDir.X, actualMidDir.X, 3);
        Assert.Equal(-expectedMidDir.Y, actualMidDir.Y, 3);
    }

    [Fact]
    public void ArrowHead_SpreadAngle_Is25DegreesFromShaft()
    {
        var start = new Point(0, 0);
        var end = new Point(100, 0);
        var (h1, h2) = ShapeGeometry.ArrowHead(start, end);

        // 화살 축(end→start 방향)과 각 날개(end→h) 사이 각도가 ±25도(스펙 spread = PI/7).
        var shaftDir = start - end;
        shaftDir.Normalize();
        var wing1Dir = h1 - end;
        wing1Dir.Normalize();
        var wing2Dir = h2 - end;
        wing2Dir.Normalize();

        double angle1 = Math.Acos(Math.Clamp(Vector.Multiply(shaftDir, wing1Dir), -1, 1)) * 180 / Math.PI;
        double angle2 = Math.Acos(Math.Clamp(Vector.Multiply(shaftDir, wing2Dir), -1, 1)) * 180 / Math.PI;
        Assert.Equal(180.0 / 7, angle1, 1);
        Assert.Equal(180.0 / 7, angle2, 1);
    }

    // ---- ARCH-16: 렌더와 경계는 같은 함수를 쓴다 (21단계) ----

    [Fact]
    public void ArrowVisualWings_LieInsideShapeElementLocalBounds()
    {
        var start = new Point(20, 30);
        var end = new Point(140, 90);
        var arrow = new ShapeElement(ShapeKind.Arrow, start, end, System.Windows.Media.Colors.Red, 4);

        var (wing1, wing2) = ShapeGeometry.ArrowHead(start, end);

        // 팩토리가 그리는 날개점(같은 함수)이 모델의 로컬 경계 안에 있다 — 마퀴가 촉을 놓치지 않는다.
        Assert.True(arrow.LocalBounds.Contains(wing1));
        Assert.True(arrow.LocalBounds.Contains(wing2));
    }

    /// <summary>
    /// 87단계(A4-1): 렌더와 히트가 같은 날개 — 날개 선분 중점마다 요소가 맞고, 촉 바깥의 빈 곳은 맞지 않는다
    /// (<c>TableGeometryTests.Dividers_MidpointOfEverySegment_HitsTableElement</c>와 같은 형태). 짧은 화살표는
    /// 촉 길이가 최소 8로 clamp되는 경우다.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 200, 0)]
    [InlineData(20, 30, 140, 90)]
    [InlineData(0, 0, 10, 0)]
    public void ArrowWings_MidpointOfEverySegment_HitsShapeElement(double sx, double sy, double ex, double ey)
    {
        var start = new Point(sx, sy);
        var end = new Point(ex, ey);
        var arrow = new ShapeElement(ShapeKind.Arrow, start, end, System.Windows.Media.Colors.Red, 2);

        var (wing1, wing2) = ShapeGeometry.ArrowHead(start, end);
        foreach (var wing in new[] { wing1, wing2 })
        {
            var mid = new Point((end.X + wing.X) / 2, (end.Y + wing.Y) / 2);
            Assert.True(arrow.HitTest(mid, tolerance: 0.5), $"날개 중점 {mid}");
        }
    }

    [Fact]
    public void ArrowWings_PointFarOutsideHead_Misses()
    {
        var arrow = new ShapeElement(ShapeKind.Arrow, new Point(0, 0), new Point(200, 0), System.Windows.Media.Colors.Red, 2);

        // 날개 끝 (≈178.3, ±10.4)보다 한참 바깥 — 축에서도 날개에서도 20px 넘게 떨어져 있다.
        Assert.False(arrow.HitTest(new Point(170, 35), tolerance: 2));
    }

    [Fact]
    public void ArrowHead_LivesOnlyInShapeGeometry_ByReflection()
    {
        Assert.Null(typeof(AnnotationVisualFactory).GetMethod("ArrowHead"));
        Assert.NotNull(typeof(ShapeGeometry).GetMethod("ArrowHead"));
    }
}
