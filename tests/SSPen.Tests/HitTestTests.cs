using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-7: 지우개 히트테스트 (요소 전체 삭제; 드래그 삭제도 이동마다 동일 지오메트리 재사용).</summary>
public class HitTestTests
{
    private static StrokeElement Stroke(double thickness, params Point[] points) =>
        new(points, Colors.Black, thickness, isHighlighter: false);

    [Fact]
    public void Stroke_PointOnSegment_Hits()
    {
        var stroke = Stroke(4, new Point(0, 0), new Point(100, 0));
        Assert.True(stroke.HitTest(new Point(50, 0), tolerance: 2));
    }

    [Fact]
    public void Stroke_WithinToleranceAndHalfThickness_Hits()
    {
        var stroke = Stroke(4, new Point(0, 0), new Point(100, 0));
        // 허용 오차 2 + 굵기/2 = 4 → 거리 3.9는 명중.
        Assert.True(stroke.HitTest(new Point(50, 3.9), tolerance: 2));
        Assert.False(stroke.HitTest(new Point(50, 4.1), tolerance: 2));
    }

    [Fact]
    public void Stroke_SinglePoint_UsesPointDistance()
    {
        var dot = Stroke(6, new Point(10, 10));
        Assert.True(dot.HitTest(new Point(12, 10), tolerance: 1));
        Assert.False(dot.HitTest(new Point(20, 10), tolerance: 1));
    }

    [Fact]
    public void Document_ClickBetweenTwoStrokes_DeletesNearestOnly()
    {
        var document = new AnnotationDocument("TEST");
        var near = Stroke(2, new Point(0, 10), new Point(100, 10));
        var far = Stroke(2, new Point(0, 20), new Point(100, 20));
        document.Add(near);
        document.Add(far);

        // (50, 13): near까지 3, far까지 7 — 둘 다 허용 오차 안이어도 가까운 것만.
        var hit = document.HitTestNearest(new Point(50, 13), tolerance: 8);
        Assert.Same(near, hit);
    }

    [Fact]
    public void Document_Tie_PrefersTopmost()
    {
        var document = new AnnotationDocument("TEST");
        var below = Stroke(2, new Point(0, 10), new Point(100, 10));
        var above = Stroke(2, new Point(0, 10), new Point(100, 10));
        document.Add(below);
        document.Add(above);
        Assert.Same(above, document.HitTestNearest(new Point(50, 10), tolerance: 4));
    }

    [Fact]
    public void Document_NoElementInTolerance_ReturnsNull()
    {
        var document = new AnnotationDocument("TEST");
        document.Add(Stroke(2, new Point(0, 0), new Point(10, 0)));
        Assert.Null(document.HitTestNearest(new Point(500, 500), tolerance: 6));
    }

    [Fact]
    public void Shape_Rectangle_OutlineOnly_InteriorMisses()
    {
        var rect = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 100), Colors.Red, 2);
        Assert.True(rect.HitTest(new Point(0, 50), tolerance: 2));   // 왼쪽 변
        Assert.True(rect.HitTest(new Point(50, 100), tolerance: 2)); // 아래 변
        Assert.False(rect.HitTest(new Point(50, 50), tolerance: 2)); // 내부 (채우기 없음)
    }

    [Fact]
    public void Shape_Ellipse_OutlineOnly_CenterMisses()
    {
        var ellipse = new ShapeElement(ShapeKind.Ellipse, new Point(0, 0), new Point(200, 100), Colors.Red, 2);
        Assert.True(ellipse.HitTest(new Point(200, 50), tolerance: 3));  // 오른쪽 끝점
        Assert.True(ellipse.HitTest(new Point(100, 0), tolerance: 3));   // 위 끝점
        Assert.False(ellipse.HitTest(new Point(100, 50), tolerance: 3)); // 중심
    }

    [Fact]
    public void Shape_Line_SegmentDistance()
    {
        var line = new ShapeElement(ShapeKind.Line, new Point(0, 0), new Point(100, 100), Colors.Red, 2);
        Assert.True(line.HitTest(new Point(50, 50), tolerance: 1));
        Assert.False(line.HitTest(new Point(80, 20), tolerance: 1));
    }

    [Fact]
    public void Text_BoundingBoxHit()
    {
        var text = new TextElement(new Point(10, 10), "안녕", Colors.Black, 24, new Size(48, 30));
        Assert.True(text.HitTest(new Point(30, 25), tolerance: 0));  // 상자 내부
        Assert.True(text.HitTest(new Point(60, 25), tolerance: 3));  // 상자 오른쪽 2px 바깥
        Assert.False(text.HitTest(new Point(100, 100), tolerance: 3));
    }

    // ---- 변형 인지 히트테스트 (SEL-2, SEL-AC-8, ARCH-19 화면 공간 통일) ----

    [Fact]
    public void ScreenDistanceTo_IdentityTransform_MatchesLegacyModelDistance()
    {
        var stroke = Stroke(4, new Point(0, 0), new Point(100, 0));

        // 변형이 없으면 MeanScale == 1이라 변형 도입 이전 계산과 완전히 동일하다.
        Assert.Equal(5, stroke.ScreenDistanceTo(new Point(50, 5)), 9);
    }

    [Fact]
    public void HitTest_TranslatedElement_HitsAtVisiblePositionNotOriginal()
    {
        var stroke = Stroke(4, new Point(0, 0), new Point(100, 0));
        stroke.TransformState = TransformMath.Translate(stroke.TransformState, new Vector(0, 200));

        Assert.False(stroke.HitTest(new Point(50, 0), tolerance: 2));
        Assert.True(stroke.HitTest(new Point(50, 200), tolerance: 2));
    }

    [Fact]
    public void HitTest_RotatedElement_HitsAtVisiblePositionNotOriginal()
    {
        // 원점 중심 수평 획을 90도 돌리면 화면상 세로가 된다.
        var stroke = Stroke(4, new Point(-50, 0), new Point(50, 0));
        stroke.TransformState = stroke.TransformState with { AngleDegrees = 90 };

        Assert.True(stroke.HitTest(new Point(0, 40), tolerance: 2));
        Assert.False(stroke.HitTest(new Point(40, 0), tolerance: 2));
    }

    [Fact]
    public void ScreenDistanceTo_ScaledElement_MatchesVisualRadius()
    {
        // 3배 확대하면 화면상 5px 떨어진 점의 화면 거리도 5여야 한다 (모델 거리는 5/3).
        var stroke = Stroke(4, new Point(-50, 0), new Point(50, 0));
        stroke.TransformState = new ElementTransformState(3, 3, 0, default);

        Assert.Equal(5, stroke.ScreenDistanceTo(new Point(0, 5)), 6);
    }

    [Fact]
    public void ScreenDistanceTo_ThicknessTermScalesWithElement()
    {
        // 굵기 8을 3배 확대하면 화면상 굵기가 24가 되므로 명중 반경도 함께 커진다.
        var stroke = Stroke(8, new Point(-50, 0), new Point(50, 0));
        stroke.TransformState = new ElementTransformState(3, 3, 0, default);

        // 허용 오차 0 + 화면 굵기 24/2 = 12.
        Assert.True(stroke.HitTest(new Point(0, 11.9), tolerance: 0));
        Assert.False(stroke.HitTest(new Point(0, 12.1), tolerance: 0));
    }

    [Fact]
    public void HitTestNearest_TransformedAndUntransformed_PicksVisuallyNearest()
    {
        // ARCH-19: 모델 공간 값을 비교하면 확대된 획의 모델 거리 5(화면 15)가
        // 변형 없는 획의 모델 거리 8(화면 8)을 이겨 화면상 더 먼 요소가 지워진다.
        var document = new AnnotationDocument("m");

        var scaled = Stroke(2, new Point(-50, 0), new Point(50, 0));
        scaled.TransformState = new ElementTransformState(3, 3, 0, default);
        document.Add(scaled);

        var plain = Stroke(2, new Point(-50, 8), new Point(50, 8));
        document.Add(plain);

        // 커서 (0, 8): scaled까지 화면 거리 8, plain까지 화면 거리 0.
        Assert.Same(plain, document.HitTestNearest(new Point(0, 8), tolerance: 20));
    }

    [Fact]
    public void HitTest_TransformedElement_ErasesAtVisiblePosition_ThroughDocument()
    {
        // 지우개는 HitTestNearest를 공유하므로 변형 후에도 화면에 보이는 위치에서 명중해야 한다.
        var document = new AnnotationDocument("m");
        var stroke = Stroke(4, new Point(0, 0), new Point(100, 0));
        document.Add(stroke);

        stroke.TransformState = TransformMath.Translate(stroke.TransformState, new Vector(300, 300));

        Assert.Null(document.HitTestNearest(new Point(50, 0), tolerance: 6));
        Assert.Same(stroke, document.HitTestNearest(new Point(350, 300), tolerance: 6));
    }
}
