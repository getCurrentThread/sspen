using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 장식 레이아웃 순수 검증 (SEL-10, MI-1). 점선 경계와 8핸들은 축 정렬 <see cref="Rect"/>가 아니라
/// **로컬 프레임 4점(OBB)** 위에 올라간다. 좌표 정확성만 다루며 렌더는 통합 테스트가 본다.
/// </summary>
public class DecorationLayoutTests
{
    private static ShapeElement Rectangle() =>
        new(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 50), Colors.Red, thickness: 2);

    private static void AssertPointsEqual(Point expected, Point actual, double tolerance = 1e-6)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    [Fact]
    public void Corners_UnrotatedElement_MatchesAxisAlignedRect()
    {
        var element = Rectangle();
        var bounds = element.LocalBounds;

        var corners = element.TransformedCorners();

        AssertPointsEqual(bounds.TopLeft, corners[0]);
        AssertPointsEqual(bounds.TopRight, corners[1]);
        AssertPointsEqual(bounds.BottomRight, corners[2]);
        AssertPointsEqual(bounds.BottomLeft, corners[3]);
    }

    [Fact]
    public void Corners_Order_IsClockwiseContract()
    {
        // 계약: 좌상 → 우상 → 우하 → 좌하. 회전해도 순환 순서는 유지된다.
        var element = Rectangle();
        element.TransformState = element.TransformState with { AngleDegrees = 90 };

        var corners = element.TransformedCorners();
        var m = element.TransformMatrix;
        var bounds = element.LocalBounds;

        AssertPointsEqual(m.Transform(bounds.TopLeft), corners[0]);
        AssertPointsEqual(m.Transform(bounds.TopRight), corners[1]);
        AssertPointsEqual(m.Transform(bounds.BottomRight), corners[2]);
        AssertPointsEqual(m.Transform(bounds.BottomLeft), corners[3]);
    }

    [Fact]
    public void Corners_RotatedElement_FollowLocalFrame()
    {
        var element = Rectangle();
        element.TransformState = element.TransformState with { AngleDegrees = 90 };

        var corners = element.TransformedCorners();

        // 로컬 상단 변(좌상→우상)이 90도 회전 후 화면에서는 세로 방향이 된다.
        var topEdge = corners[1] - corners[0];
        Assert.InRange(Math.Abs(topEdge.X), 0, 1e-6);
        Assert.True(Math.Abs(topEdge.Y) > 1);

        // 축 정렬 경계와 다르다는 것이 이 계약의 요점이다.
        Assert.NotEqual(element.TransformedBounds.TopLeft, corners[0]);
    }

    [Fact]
    public void HandlePositions_RotatedElement_LieOnRotatedEdges()
    {
        var element = Rectangle();
        element.TransformState = element.TransformState with { AngleDegrees = 30 };
        var bounds = element.LocalBounds;
        var m = element.TransformMatrix;
        var corners = element.TransformedCorners();

        var top = m.Transform(TransformMath.HandleCenterLocal(bounds, HandleKind.Top));

        // 상단 변 핸들은 회전된 상단 변(corners[0] → corners[1])의 중점이어야 한다.
        AssertPointsEqual(new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2), top);
    }

    [Fact]
    public void HandlePositions_AllEightHandles_MapOntoFramePerimeter()
    {
        var element = Rectangle();
        element.TransformState = new ElementTransformState(2, 0.5, 63, new Vector(40, -20));
        var bounds = element.LocalBounds;
        var m = element.TransformMatrix;

        foreach (var handle in TransformMath.SizeHandlesCornersFirst)
        {
            var local = TransformMath.HandleCenterLocal(bounds, handle);
            var world = m.Transform(local);

            // 역사상하면 다시 로컬 경계 위로 돌아와야 한다 (렌더와 힌트가 같은 프레임을 쓴다는 증거).
            var roundTrip = TransformMath.ToLocal(element.TransformState, bounds, world);
            AssertPointsEqual(local, roundTrip, 1e-6);
        }
    }

    [Fact]
    public void RotateHandle_FollowsLocalTopEdge_NotWorldTop()
    {
        var element = Rectangle();
        element.TransformState = element.TransformState with { AngleDegrees = 90 };
        var bounds = element.LocalBounds;

        var top = TransformMath.TopCenterWorld(element.TransformState, bounds);
        var handle = TransformMath.RotateHandleWorld(element.TransformState, bounds);

        // 90도 회전이면 로컬 '위'가 화면 오른쪽이므로 핸들은 상단 변 중점의 +X 방향에 있다.
        Assert.True(handle.X > top.X + 1, "회전 핸들이 로컬 상단 변 바깥을 따라가야 한다.");
        Assert.InRange(Math.Abs(handle.Y - top.Y), 0, 1e-6);
    }

    [Fact]
    public void Corners_TranslatedElement_ShiftByDisplacementOnly()
    {
        var element = Rectangle();
        var before = element.TransformedCorners();

        element.TransformState = TransformMath.Translate(element.TransformState, new Vector(15, -25));
        var after = element.TransformedCorners();

        for (int i = 0; i < 4; i++)
        {
            AssertPointsEqual(new Point(before[i].X + 15, before[i].Y - 25), after[i]);
        }
    }
}
