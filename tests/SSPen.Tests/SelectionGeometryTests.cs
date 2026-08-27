using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 마퀴 교차와 선택 힌트 판정 (SEL-8, SEL-B-1, f8, f9).
/// 핵심 계약: 마퀴는 **축 정렬 경계 상자** 교차이며 MI-1이 핸들을 로컬축으로 바꾼 뒤에도 불변이다.
/// </summary>
public class SelectionGeometryTests
{
    private static StrokeElement Stroke(params Point[] points) =>
        new(points, Colors.Black, thickness: 2, isHighlighter: false);

    private static StrokeElement Fading(params Point[] points)
    {
        var stroke = Stroke(points);
        stroke.IsFading = true;
        return stroke;
    }

    [Fact]
    public void Intersects_MarqueeOverlapsBounds_ReturnsTrue()
    {
        var stroke = Stroke(new Point(10, 10), new Point(90, 90));

        Assert.True(SelectionGeometry.Intersects(new Rect(50, 50, 200, 200), stroke));
    }

    [Fact]
    public void Intersects_MarqueeDisjointFromBounds_ReturnsFalse()
    {
        var stroke = Stroke(new Point(10, 10), new Point(90, 90));

        Assert.False(SelectionGeometry.Intersects(new Rect(500, 500, 100, 100), stroke));
    }

    [Fact]
    public void Intersects_MarqueeTouchesEdgeExactly_ReturnsTrue()
    {
        var stroke = Stroke(new Point(0, 0), new Point(100, 100));
        var bounds = stroke.TransformedBounds;

        var touching = new Rect(bounds.Right, bounds.Top, 50, 50);

        Assert.True(SelectionGeometry.Intersects(touching, stroke));
    }

    [Fact]
    public void Intersects_MarqueeOverlapsBoundsWithoutTouchingInk_ReturnsTrue()
    {
        // SEL-B-1 핵심: 화면을 가로지르는 긴 대각선 획은 잉크를 스치지 않아도 경계가 겹치면 선택된다.
        var diagonal = Stroke(new Point(0, 0), new Point(1000, 1000));

        // 좌하단 구석 — 대각선 잉크에서 멀지만 경계 상자 안이다.
        var marquee = new Rect(20, 900, 60, 60);

        Assert.True(SelectionGeometry.Intersects(marquee, diagonal));
        Assert.False(diagonal.HitTest(new Point(50, 930), tolerance: 6));
    }

    [Fact]
    public void Intersects_FadingElement_ReturnsFalse()
    {
        var fading = Fading(new Point(10, 10), new Point(90, 90));

        Assert.False(SelectionGeometry.Intersects(new Rect(0, 0, 500, 500), fading));
    }

    [Fact]
    public void Intersects_TransformedElement_UsesPostTransformBounds()
    {
        var stroke = Stroke(new Point(0, 0), new Point(50, 50));
        var marquee = new Rect(400, 400, 100, 100);

        Assert.False(SelectionGeometry.Intersects(marquee, stroke));

        stroke.TransformState = TransformMath.Translate(stroke.TransformState, new Vector(420, 420));

        Assert.True(SelectionGeometry.Intersects(marquee, stroke));
    }

    [Fact]
    public void Intersects_RotatedElement_UsesAxisAlignedBoundsNotObb()
    {
        // MI-1 이후에도 SEL-B-1 불변: 회전한 요소의 판정 상자는 잉크보다 넉넉한 축 정렬 상자다.
        // OBB(SAT) 교차였다면 이 마퀴는 빗나간다.
        var stroke = Stroke(new Point(-100, 0), new Point(100, 0));
        stroke.TransformState = stroke.TransformState with { AngleDegrees = 45 };

        var axisAligned = stroke.TransformedBounds;
        Assert.True(axisAligned.Height > 100, "45도 회전이면 축 정렬 경계의 높이가 크게 늘어난다.");

        // 회전한 잉크에서 멀리 떨어진 좌상단 구석이지만 축 정렬 상자 안이다.
        var marquee = new Rect(axisAligned.Left + 1, axisAligned.Top + 1, 4, 4);

        Assert.True(SelectionGeometry.Intersects(marquee, stroke));
    }

    [Fact]
    public void HitMarquee_MultipleElements_PreservesDocumentOrder()
    {
        var first = Stroke(new Point(0, 0), new Point(10, 10));
        var second = Stroke(new Point(20, 20), new Point(30, 30));
        var third = Stroke(new Point(40, 40), new Point(50, 50));
        var outside = Stroke(new Point(900, 900), new Point(910, 910));

        var hits = SelectionGeometry.HitMarquee(
            [first, second, outside, third], new Rect(0, 0, 200, 200));

        Assert.Equal([first, second, third], hits);
    }

    [Fact]
    public void HitMarquee_EmptyMarquee_SelectsNothing()
    {
        var stroke = Stroke(new Point(0, 0), new Point(10, 10));

        Assert.Empty(SelectionGeometry.HitMarquee([stroke], Rect.Empty));
    }

    [Fact]
    public void HitTopmost_OverlappingElements_ReturnsLastAdded()
    {
        var below = Stroke(new Point(0, 10), new Point(100, 10));
        var above = Stroke(new Point(0, 10), new Point(100, 10));

        Assert.Same(above, SelectionGeometry.HitTopmost([below, above], new Point(50, 10), tolerance: 4));
    }

    [Fact]
    public void HitTopmost_FadingElementUnderCursor_ReturnsNull()
    {
        var fading = Fading(new Point(0, 10), new Point(100, 10));

        Assert.Null(SelectionGeometry.HitTopmost([fading], new Point(50, 10), tolerance: 4));
    }

    [Fact]
    public void HitTopmost_FadingOnTopOfSolid_SkipsFadingAndReturnsSolid()
    {
        var solid = Stroke(new Point(0, 10), new Point(100, 10));
        var fading = Fading(new Point(0, 10), new Point(100, 10));

        Assert.Same(solid, SelectionGeometry.HitTopmost([solid, fading], new Point(50, 10), tolerance: 4));
    }

    [Fact]
    public void HitTopmost_EmptySpace_ReturnsNull()
    {
        var stroke = Stroke(new Point(0, 0), new Point(10, 0));

        Assert.Null(SelectionGeometry.HitTopmost([stroke], new Point(500, 500), tolerance: 6));
    }

    [Fact]
    public void HitTopmost_TransformedElement_HitsAtVisiblePosition()
    {
        var stroke = Stroke(new Point(0, 0), new Point(100, 0));
        stroke.TransformState = TransformMath.Translate(stroke.TransformState, new Vector(0, 300));

        Assert.Null(SelectionGeometry.HitTopmost([stroke], new Point(50, 0), tolerance: 4));
        Assert.Same(stroke, SelectionGeometry.HitTopmost([stroke], new Point(50, 300), tolerance: 4));
    }
}
