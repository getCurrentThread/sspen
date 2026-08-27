using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 변형 수학 순수 검증 (SEL-9, MI-1 로컬축). 핵심은 R21 앵커 불변식이며 증인은 **0도와 30도 양쪽**이
/// 필요하다 — 0도에서는 R=I라 보정 누락 오류가 상쇄되어 드러나지 않는다.
/// </summary>
public class TransformMathTests
{
    private const double Tolerance = 1e-9;
    private static readonly Rect Bounds = new(0, 0, 100, 50);

    private static Point AnchorWorld(ElementTransformState state, Rect bounds, HandleKind handle)
    {
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return TransformMath.ToMatrix(state, center).Transform(TransformMath.AnchorLocal(bounds, handle));
    }

    private static Point GripWorld(ElementTransformState state, Rect bounds, HandleKind handle)
    {
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return TransformMath.ToMatrix(state, center).Transform(TransformMath.HandleCenterLocal(bounds, handle));
    }

    private static void AssertPointsEqual(Point expected, Point actual, double tolerance = 1e-7)
    {
        Assert.False(double.IsNaN(actual.X), "X가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.False(double.IsNaN(actual.Y), "Y가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    // ---- ToMatrix: 합성 순서 계약 ----

    [Fact]
    public void ToMatrix_Identity_LeavesPointsUnchanged()
    {
        var m = TransformMath.ToMatrix(ElementTransformState.Identity, new Point(50, 25));

        AssertPointsEqual(new Point(10, 20), m.Transform(new Point(10, 20)));
    }

    [Fact]
    public void ToMatrix_ScaleAppliedBeforeRotation_FollowsLocalAxes()
    {
        // 로컬 X축으로만 2배 늘린 뒤 90도 회전 → 늘어난 축이 월드 Y로 간다 (스케일이 회전보다 먼저).
        var state = new ElementTransformState(2, 1, 90, default);
        var center = new Point(50, 25);

        var m = TransformMath.ToMatrix(state, center);
        var mapped = m.Transform(new Point(150, 25)); // 중심에서 로컬 +X로 100

        // (100,0) → S(2,1) → (200,0) → R(90) → (0,200) → +center
        AssertPointsEqual(new Point(50, 225), mapped);
    }

    [Fact]
    public void ToMatrix_TranslationIsAppliedLast_AsDisplacement()
    {
        var state = new ElementTransformState(1, 1, 0, new Vector(7, -3));

        var m = TransformMath.ToMatrix(state, new Point(50, 25));

        AssertPointsEqual(new Point(17, 17), m.Transform(new Point(10, 20)));
    }

    [Fact]
    public void ToLocal_RoundTripsThroughToMatrix()
    {
        var state = new ElementTransformState(2.5, -1.5, 37, new Vector(11, 4));
        var world = TransformMath.ToMatrix(state, new Point(50, 25)).Transform(new Point(80, 10));

        var local = TransformMath.ToLocal(state, Bounds, world);

        AssertPointsEqual(new Point(80, 10), local, 1e-6);
    }

    // ---- NonDegenerate ----

    [Fact]
    public void NonDegenerate_ZeroHeightRect_ExpandsToMinExtent()
    {
        var expanded = TransformMath.NonDegenerate(new Rect(0, 10, 100, 0), 4);

        Assert.Equal(100, expanded.Width, 9);
        Assert.Equal(4, expanded.Height, 9);
        Assert.Equal(10, expanded.Y + expanded.Height / 2, 9); // 중심 보존
    }

    [Fact]
    public void NonDegenerate_ZeroWidthRect_ExpandsToMinExtent()
    {
        var expanded = TransformMath.NonDegenerate(new Rect(20, 0, 0, 80), 6);

        Assert.Equal(6, expanded.Width, 9);
        Assert.Equal(80, expanded.Height, 9);
        Assert.Equal(20, expanded.X + expanded.Width / 2, 9);
    }

    [Fact]
    public void NonDegenerate_AlreadyLargeRect_IsUnchanged()
    {
        var original = new Rect(3, 4, 50, 60);

        Assert.Equal(original, TransformMath.NonDegenerate(original, 4));
    }

    // ---- 앵커/핸들 로컬 배치 ----

    [Theory]
    [InlineData(HandleKind.TopLeft, 100, 50)]
    [InlineData(HandleKind.TopRight, 0, 50)]
    [InlineData(HandleKind.BottomRight, 0, 0)]
    [InlineData(HandleKind.BottomLeft, 100, 0)]
    public void AnchorLocal_EachCornerHandle_ReturnsOppositeCorner(HandleKind handle, double x, double y)
    {
        AssertPointsEqual(new Point(x, y), TransformMath.AnchorLocal(Bounds, handle));
    }

    [Theory]
    [InlineData(HandleKind.Top, 50, 50)]
    [InlineData(HandleKind.Right, 0, 25)]
    [InlineData(HandleKind.Bottom, 50, 0)]
    [InlineData(HandleKind.Left, 100, 25)]
    public void AnchorLocal_EachSideHandle_ReturnsOppositeEdgeMidpoint(HandleKind handle, double x, double y)
    {
        AssertPointsEqual(new Point(x, y), TransformMath.AnchorLocal(Bounds, handle));
    }

    [Fact]
    public void HandleCenterLocal_AllEightHandles_LieOnLocalBoundsEdges()
    {
        foreach (var handle in TransformMath.SizeHandlesCornersFirst)
        {
            var center = TransformMath.HandleCenterLocal(Bounds, handle);

            bool onVerticalEdge = Math.Abs(center.X - Bounds.Left) < Tolerance
                || Math.Abs(center.X - Bounds.Right) < Tolerance;
            bool onHorizontalEdge = Math.Abs(center.Y - Bounds.Top) < Tolerance
                || Math.Abs(center.Y - Bounds.Bottom) < Tolerance;

            Assert.True(onVerticalEdge || onHorizontalEdge, $"{handle}가 경계 변 위에 있지 않다.");
        }
    }

    [Fact]
    public void HandleCenterLocal_RotateHandle_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TransformMath.HandleCenterLocal(Bounds, HandleKind.Rotate));
    }

    // ---- ScaleLocal: 기본 동작 ----

    [Fact]
    public void ScaleLocal_UnrotatedCornerHandle_MatchesAxisAlignedScale()
    {
        var result = TransformMath.ScaleLocal(
            ElementTransformState.Identity, Bounds, HandleKind.BottomRight, new Point(200, 100));

        Assert.Equal(2, result.ScaleX, 9);
        Assert.Equal(2, result.ScaleY, 9);
        Assert.Equal(0, result.AngleDegrees, 9);
    }

    [Fact]
    public void ScaleLocal_CornerHandleAnisotropic_ScalesBothAxesIndependently()
    {
        var result = TransformMath.ScaleLocal(
            ElementTransformState.Identity, Bounds, HandleKind.BottomRight, new Point(300, 75));

        Assert.Equal(3, result.ScaleX, 9);
        Assert.Equal(1.5, result.ScaleY, 9);
    }

    [Fact]
    public void ScaleLocal_SideHandle_ChangesOneAxisOnly()
    {
        var start = new ElementTransformState(1, 1, 0, default);

        var right = TransformMath.ScaleLocal(start, Bounds, HandleKind.Right, new Point(400, 999));
        Assert.Equal(1, right.ScaleY, 9);
        Assert.NotEqual(1, right.ScaleX, 9);

        var bottom = TransformMath.ScaleLocal(start, Bounds, HandleKind.Bottom, new Point(999, 150));
        Assert.Equal(1, bottom.ScaleX, 9);
        Assert.NotEqual(1, bottom.ScaleY, 9);
    }

    [Fact]
    public void ScaleLocal_RotatedElement_ScalesAlongLocalAxes()
    {
        // 90도 회전 상태: 로컬 +X가 화면 +Y로 간다. 앵커(왼쪽 변 중점)에서 로컬 X 방향으로
        // 원래 폭(100)의 3배인 300만큼 떨어진 지점으로 끌면 ScaleX가 정확히 3이 된다.
        // 앵커가 월드 고정이므로 '스케일 3짜리 상태의 핸들 위치'와는 다른 점이라는 것이 R21의 요점이다.
        var start = new ElementTransformState(1, 1, 90, default);
        var anchorWorld = AnchorWorld(start, Bounds, HandleKind.Right);
        var localXInWorld = TransformMath.RotateVector(new Vector(1, 0), 90);

        var result = TransformMath.ScaleLocal(
            start, Bounds, HandleKind.Right, anchorWorld + localXInWorld * 300);

        Assert.Equal(3, result.ScaleX, 6);
        Assert.Equal(1, result.ScaleY, 9);
        Assert.Equal(90, result.AngleDegrees, 9);

        // 화면상 효과: 세로로 길어지고 가로는 그대로다.
        var scaledBounds = Rect.Transform(Bounds, TransformMath.ToMatrix(result, new Point(50, 25)));
        Assert.Equal(300, scaledBounds.Height, 6);
        Assert.Equal(50, scaledBounds.Width, 6);
    }

    // ---- ScaleLocal: R21 앵커 불변 (0도 / 30도 양쪽) ----

    [Fact]
    public void ScaleLocal_CornerHandle_KeepsAnchorFixed_AtZeroDegrees()
    {
        var start = new ElementTransformState(1, 1, 0, default);
        var before = AnchorWorld(start, Bounds, HandleKind.BottomRight);

        var result = TransformMath.ScaleLocal(start, Bounds, HandleKind.BottomRight, new Point(240, 130));

        AssertPointsEqual(before, AnchorWorld(result, Bounds, HandleKind.BottomRight));
    }

    [Fact]
    public void ScaleLocal_CornerHandle_KeepsAnchorFixed_AtThirtyDegrees()
    {
        // 30도에서는 R != I라 Translation 보정을 빠뜨리면 앵커가 눈에 띄게 밀린다 (R21).
        var start = new ElementTransformState(1, 1, 30, default);
        var before = AnchorWorld(start, Bounds, HandleKind.BottomRight);

        var result = TransformMath.ScaleLocal(start, Bounds, HandleKind.BottomRight, new Point(240, 130));

        AssertPointsEqual(before, AnchorWorld(result, Bounds, HandleKind.BottomRight));
        Assert.NotEqual(start.ScaleX, result.ScaleX, 6);
    }

    [Fact]
    public void ScaleLocal_WithoutPinning_MovesAnchor_OnlyVisibleWhenRotated()
    {
        // R21이 비어 있지 않음을 증명한다: 보정항(6)을 빼면 앵커가 밀린다.
        // 0도에서도 밀리긴 하지만 순수 축방향이라 구현자가 '중심 기준 확대'로 오인하기 쉽고,
        // 30도에서는 빗방향으로 밀려 즉시 이상으로 보인다 — 그래서 증인이 0도/30도 양쪽 필요하다.
        foreach (double angle in new[] { 0.0, 30.0 })
        {
            var start = new ElementTransformState(1, 1, angle, default);
            var before = AnchorWorld(start, Bounds, HandleKind.BottomRight);

            var unpinned = start with { ScaleX = 2, ScaleY = 2 };
            var moved = AnchorWorld(unpinned, Bounds, HandleKind.BottomRight);
            Assert.True((moved - before).Length > 1, $"{angle}도: 보정 없으면 앵커가 밀려야 한다.");

            var pinned = unpinned with
            {
                Translation = TransformMath.PinAnchor(start, unpinned, Bounds, HandleKind.BottomRight),
            };
            AssertPointsEqual(before, AnchorWorld(pinned, Bounds, HandleKind.BottomRight));
        }
    }

    [Fact]
    public void ScaleLocal_SideHandle_KeepsAnchorEdgeFixed_Rotated()
    {
        var start = new ElementTransformState(1.4, 0.8, 47, new Vector(13, -9));
        var before = AnchorWorld(start, Bounds, HandleKind.Left);

        var result = TransformMath.ScaleLocal(start, Bounds, HandleKind.Left, new Point(-60, 40));

        AssertPointsEqual(before, AnchorWorld(result, Bounds, HandleKind.Left));
    }

    [Fact]
    public void ScaleLocal_RotatedCornerDrag_MovesGripOntoCursor()
    {
        // 앵커 고정과 짝을 이루는 반대편 계약: 끌고 있는 핸들은 커서에 정확히 붙는다.
        var start = new ElementTransformState(1, 1, 30, default);
        var cursor = new Point(240, 130);

        var result = TransformMath.ScaleLocal(start, Bounds, HandleKind.BottomRight, cursor);

        AssertPointsEqual(cursor, GripWorld(result, Bounds, HandleKind.BottomRight), 1e-6);
    }

    [Fact]
    public void ScaleLocal_RotatedElement_KeepsFrameRectangular()
    {
        // LD-1의 구조적 주장: A3 표현에서는 전단이 표현 불가능하다. 회전 + 비등방 스케일을 합성해도
        // 인접 변이 직교를 유지해야 한다 (자유 Matrix였다면 R(30)·S(2,1)·R(30)에서 무너지는 지점).
        var start = new ElementTransformState(1, 1, 30, default);
        var scaled = TransformMath.ScaleLocal(start, Bounds, HandleKind.BottomRight, new Point(400, 90));
        var rotatedAgain = TransformMath.Rotate(
            scaled, Bounds, new Point(200, 0), new Point(0, 200), shift: false);

        var m = TransformMath.ToMatrix(rotatedAgain, new Point(50, 25));
        var topLeft = m.Transform(Bounds.TopLeft);
        var topRight = m.Transform(Bounds.TopRight);
        var bottomLeft = m.Transform(Bounds.BottomLeft);

        var edgeX = topRight - topLeft;
        var edgeY = bottomLeft - topLeft;
        double dot = (edgeX.X * edgeY.X) + (edgeX.Y * edgeY.Y);

        Assert.InRange(dot / (edgeX.Length * edgeY.Length), -1e-9, 1e-9);
    }

    // ---- ScaleLocal: 퇴화·부호 방어 ----

    [Fact]
    public void ScaleLocal_DegenerateSpan_KeepsExistingScaleInsteadOfNaN()
    {
        // 폭이 MinScale 미만이면 그 축의 분모가 0에 가까워 0/0 → NaN이 된다 (R16).
        var degenerate = new Rect(10, 0, 0, 50);

        var result = TransformMath.ScaleLocal(
            ElementTransformState.Identity, degenerate, HandleKind.Right, new Point(500, 25));

        Assert.False(double.IsNaN(result.ScaleX));
        Assert.False(double.IsNaN(result.ScaleY));
        Assert.Equal(1, result.ScaleX, 9);
    }

    [Fact]
    public void ScaleLocal_DragPastAnchor_FlipsSignAndKeepsMinimumMagnitude()
    {
        var result = TransformMath.ScaleLocal(
            ElementTransformState.Identity, Bounds, HandleKind.Right, new Point(-500, 25));

        Assert.True(result.ScaleX < 0, "반대편으로 끌면 부호가 뒤집혀야 한다 (R14).");
        Assert.True(Math.Abs(result.ScaleX) >= TransformMath.MinScale);
    }

    [Fact]
    public void ClampMagnitude_PreservesSignAndFloorsAbsoluteValue()
    {
        Assert.Equal(TransformMath.MinScale, TransformMath.ClampMagnitude(0), 9);
        Assert.Equal(-TransformMath.MinScale, TransformMath.ClampMagnitude(-0.0001), 9);
        Assert.Equal(-3, TransformMath.ClampMagnitude(-3), 9);
        Assert.Equal(TransformMath.MinScale, TransformMath.ClampMagnitude(double.NaN), 9);
    }

    // ---- Rotate ----

    [Fact]
    public void Rotate_FreeDrag_AddsSweptAngleToState()
    {
        var start = new ElementTransformState(1, 1, 10, default);
        var pivot = new Point(50, 25);

        var result = TransformMath.Rotate(
            start, Bounds, pivot + new Vector(100, 0), pivot + new Vector(0, 100), shift: false);

        Assert.Equal(100, result.AngleDegrees, 6);
    }

    [Fact]
    public void Rotate_PivotIsLocalBoundsCenterPlusTranslation()
    {
        var start = new ElementTransformState(1, 1, 0, new Vector(200, 300));
        var pivot = new Point(50 + 200, 25 + 300);

        var result = TransformMath.Rotate(
            start, Bounds, pivot + new Vector(50, 0), pivot + new Vector(0, 50), shift: false);

        Assert.Equal(90, result.AngleDegrees, 6);
        Assert.Equal(start.Translation, result.Translation);
    }

    [Theory]
    [InlineData(0, 7, 0)]
    [InlineData(0, 44, 45)]
    [InlineData(0, 98, 105)]
    public void Rotate_WithShift_SnapsResultToFifteenDegreeMultiple(
        double startAngle, double sweepDegrees, double expected)
    {
        var start = new ElementTransformState(1, 1, startAngle, default);
        var pivot = new Point(50, 25);
        double radians = sweepDegrees * Math.PI / 180.0;
        var to = pivot + new Vector(100 * Math.Cos(radians), 100 * Math.Sin(radians));

        var result = TransformMath.Rotate(start, Bounds, pivot + new Vector(100, 0), to, shift: true);

        Assert.Equal(expected, result.AngleDegrees, 6);
    }

    [Fact]
    public void Rotate_CursorOnPivot_LeavesStateUnchanged()
    {
        var start = new ElementTransformState(1, 1, 33, default);
        var pivot = new Point(50, 25);

        Assert.Equal(start, TransformMath.Rotate(start, Bounds, pivot, pivot, shift: false));
    }

    // ---- Translate ----

    [Fact]
    public void Translate_AccumulatesDisplacementOnly()
    {
        var start = new ElementTransformState(2, 3, 45, new Vector(10, 10));

        var result = TransformMath.Translate(start, new Vector(-4, 6));

        Assert.Equal(new Vector(6, 16), result.Translation);
        Assert.Equal(2, result.ScaleX, 9);
        Assert.Equal(3, result.ScaleY, 9);
        Assert.Equal(45, result.AngleDegrees, 9);
    }

    // ---- 회전 핸들 위치와 클램프 ----

    [Fact]
    public void RotateHandleWorld_Unrotated_SitsAboveTopEdgeByScreenOffset()
    {
        var handle = TransformMath.RotateHandleWorld(ElementTransformState.Identity, Bounds, 24);

        AssertPointsEqual(new Point(50, -24), handle);
    }

    [Fact]
    public void RotateHandleWorld_UpsideDownElement_SitsBelowCenterNotAboveIt()
    {
        // 180도 회전이면 '상단' 가정이 깨진다 — 핸들은 화면 아래로 가야 한다 (R5).
        var state = new ElementTransformState(1, 1, 180, default);

        var handle = TransformMath.RotateHandleWorld(state, Bounds, 24);

        Assert.True(handle.Y > 25, "180도 회전 시 회전 핸들이 요소 중심보다 아래에 있어야 한다.");
    }

    [Fact]
    public void RotateHandleWorld_ScaledElement_KeepsConstantScreenOffset()
    {
        var scaled = new ElementTransformState(5, 5, 0, default);

        var top = TransformMath.TopCenterWorld(scaled, Bounds);
        var handle = TransformMath.RotateHandleWorld(scaled, Bounds, 24);

        Assert.Equal(24, (handle - top).Length, 6);
    }

    [Fact]
    public void ClampRotateHandle_OutsideSurface_PullsInsideByInset()
    {
        var surface = new Rect(0, 0, 1920, 1080);

        var clamped = TransformMath.ClampRotateHandle(new Point(500, -40), surface, 4);

        Assert.Equal(500, clamped.X, 9);
        Assert.Equal(4, clamped.Y, 9);
    }

    [Fact]
    public void ClampRotateHandle_EmptySurface_LeavesHandleUntouched()
    {
        Assert.Equal(new Point(-999, -999), TransformMath.ClampRotateHandle(new Point(-999, -999), Rect.Empty, 4));
    }

    // ---- HitHandle ----

    [Fact]
    public void HitHandle_OnRotateHandle_ReturnsRotate()
    {
        var hit = TransformMath.HitHandle(
            ElementTransformState.Identity, Bounds, new Point(50, -24), Rect.Empty);

        Assert.Equal(HandleKind.Rotate, hit);
    }

    [Fact]
    public void HitHandle_OnCornerHandle_ReturnsThatCorner()
    {
        var hit = TransformMath.HitHandle(
            ElementTransformState.Identity, Bounds, new Point(100, 50), Rect.Empty);

        Assert.Equal(HandleKind.BottomRight, hit);
    }

    [Fact]
    public void HitHandle_RotatedElement_MatchesVisualHandlePosition()
    {
        // MI-1 핵심 체감: 회전 상태에서도 화면에 보이는 핸들 위치에서 잡혀야 한다.
        var state = new ElementTransformState(1, 1, 30, default);
        var visual = GripWorld(state, Bounds, HandleKind.TopRight);

        Assert.Equal(HandleKind.TopRight, TransformMath.HitHandle(state, Bounds, visual, Rect.Empty));
    }

    [Fact]
    public void HitHandle_ScaledElement_KeepsConstantScreenReach()
    {
        // 로컬 비교이지만 허용 반경은 배율로 나누므로 화면 히트 크기가 배율과 무관하게 일정하다.
        var state = new ElementTransformState(4, 4, 0, default);
        var corner = GripWorld(state, Bounds, HandleKind.BottomRight);

        Assert.Equal(
            HandleKind.BottomRight,
            TransformMath.HitHandle(state, Bounds, corner + new Vector(3, 0), Rect.Empty));
        Assert.Null(TransformMath.HitHandle(state, Bounds, corner + new Vector(9, 0), Rect.Empty));
    }

    [Fact]
    public void HitHandle_EmptySpace_ReturnsNull()
    {
        Assert.Null(TransformMath.HitHandle(
            ElementTransformState.Identity, Bounds, new Point(50, 25), Rect.Empty));
    }

    [Fact]
    public void HitHandle_ClampedRotateHandle_IsGrabbableAtClampedPosition()
    {
        // 요소가 화면 최상단이면 회전 핸들이 화면 밖으로 나간다 — 클램프된 위치에서 잡혀야 한다 (R5).
        var surface = new Rect(0, 0, 1920, 1080);
        var expected = TransformMath.ClampRotateHandle(
            TransformMath.RotateHandleWorld(ElementTransformState.Identity, Bounds), surface, 4);

        Assert.Equal(HandleKind.Rotate, TransformMath.HitHandle(
            ElementTransformState.Identity, Bounds, expected, surface));
    }
}
