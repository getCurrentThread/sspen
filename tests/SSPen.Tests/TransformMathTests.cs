using System.Windows;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// 변형 수학 순수 검증 (SEL-9, MI-1 로컬축). 핵심은 R21 앵커 불변식이며 증인은 **0도와 30도 양쪽**이
/// 필요하다 — 0도에서는 R=I라 보정 누락 오류가 상쇄되어 드러나지 않는다.
///
/// 그룹 변형 수학(ScaleAbout/RotateAbout, R1)과 배율 재단(ClampGroupFactor/ClampMagnitude, D5)은 대상 타입이
/// <see cref="TransformMath"/>라 리팩터링 19단계에서 SelectionGroupTests로부터 글자 그대로 옮겨 왔다 —
/// R1의 핵심 증인은 <see cref="ScaleAbout_RotatedElements_KeepsLocalAxesOrthogonal"/>다:
/// "등방 스케일은 각도가 제각각인 그룹에서도 정확하다"가 성립하지 않으면 R1의 설계 전제가 무너진다.
/// 적대적/경계 케이스는 <see cref="TransformMathRedTeamTests"/>에 있고, 헬퍼 <c>AnchorWorld</c>/
/// <c>AssertPointsEqual</c>은 <see cref="TestGeometry"/>로 승격했다 (<c>GripWorld</c>는 이 파일만 쓴다).
/// </summary>
public class TransformMathTests
{
    private const double Tolerance = 1e-9;
    private static readonly Rect Bounds = new(0, 0, 100, 50);

    private static Point GripWorld(ElementTransformState state, Rect bounds, HandleKind handle)
    {
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return TransformMath.ToMatrix(state, center).Transform(TransformMath.HandleCenterLocal(bounds, handle));
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

    // ---- ScaleAbout / RotateAbout: 그룹 변형 수학 (R1의 설계 전제) — 리팩터링 19단계에서 SelectionGroupTests로부터 이동 ----

    /// <summary>
    /// 그룹 등방 스케일은 회전각이 제각각이어도 로컬 두 축의 직교를 보존한다 —
    /// 즉 전단이 생기지 않는다. 이것이 "측면 핸들 없이 모서리 4개만"이라는 결정의 근거다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(47.5)]
    [InlineData(-120)]
    public void ScaleAbout_RotatedElements_KeepsLocalAxesOrthogonal(double angle)
    {
        var element = Stroke(0, 0, 40, 20);
        var bounds = element.LocalBounds;
        var start = new ElementTransformState(1.4, 0.6, angle, new Vector(17, -9));

        var scaled = TransformMath.ScaleAbout(start, bounds, new Point(200, 150), 2.5);
        var matrix = TransformMath.ToMatrix(scaled, new Point(
            bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)));

        // 행벡터 규약에서 선형부의 두 행이 직교하면 전단이 없다.
        double dot = (matrix.M11 * matrix.M21) + (matrix.M12 * matrix.M22);
        Assert.Equal(0, dot, 9);
    }

    [Fact]
    public void ScaleAbout_PivotPointIsFixed()
    {
        var element = Stroke(0, 0, 40, 20);
        var bounds = element.LocalBounds;
        var pivot = new Point(300, 200);
        var start = new ElementTransformState(1, 1, 25, new Vector(50, 30));

        var scaled = TransformMath.ScaleAbout(start, bounds, pivot, 3);

        // 피벗에 있던 월드 점은 그대로여야 한다. 요소 로컬 점 하나를 골라 월드로 올린 뒤
        // 그 점이 피벗 기준 정확히 3배 멀어졌는지 본다.
        var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var probe = new Point(bounds.Left, bounds.Top);
        var before = TransformMath.ToMatrix(start, center).Transform(probe);
        var after = TransformMath.ToMatrix(scaled, center).Transform(probe);

        Assert.Equal(pivot.X + ((before.X - pivot.X) * 3), after.X, 6);
        Assert.Equal(pivot.Y + ((before.Y - pivot.Y) * 3), after.Y, 6);
    }

    [Fact]
    public void RotateAbout_PivotPointIsFixedAndAngleAccumulates()
    {
        var element = Stroke(0, 0, 40, 20);
        var bounds = element.LocalBounds;
        var pivot = new Point(100, 100);
        var start = new ElementTransformState(1, 1, 10, new Vector(60, 0));

        var rotated = TransformMath.RotateAbout(start, bounds, pivot, 90);

        Assert.Equal(100, rotated.AngleDegrees, 9);

        var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var probe = new Point(bounds.Left, bounds.Top);
        var before = TransformMath.ToMatrix(start, center).Transform(probe);
        var after = TransformMath.ToMatrix(rotated, center).Transform(probe);

        var expected = TransformMath.RotateVector(before - pivot, 90) + pivot;
        Assert.Equal(expected.X, after.X, 6);
        Assert.Equal(expected.Y, after.Y, 6);
    }

    [Fact]
    public void RotateAbout_FullTurn_ReturnsToStartPosition()
    {
        var element = Stroke(0, 0, 40, 20);
        var bounds = element.LocalBounds;
        var pivot = new Point(-40, 220);
        var start = new ElementTransformState(2, 0.5, 33, new Vector(11, 7));

        var quarter = TransformMath.RotateAbout(start, bounds, pivot, 90);
        var half = TransformMath.RotateAbout(quarter, bounds, pivot, 90);
        var threeQuarter = TransformMath.RotateAbout(half, bounds, pivot, 90);
        var full = TransformMath.RotateAbout(threeQuarter, bounds, pivot, 90);

        Assert.Equal(start.Translation.X, full.Translation.X, 6);
        Assert.Equal(start.Translation.Y, full.Translation.Y, 6);
        Assert.Equal(start.AngleDegrees + 360, full.AngleDegrees, 6);
    }

    // ---- ClampGroupFactor / ClampMagnitude: 배율 재단 (D5) — 리팩터링 19단계에서 SelectionGroupTests로부터 이동 ----

    [Fact]
    public void ClampGroupFactor_WithinLimits_PassesThrough()
    {
        var states = new[] { ElementTransformState.Identity, ElementTransformState.Identity with { ScaleX = 2 } };

        Assert.Equal(3, TransformMath.ClampGroupFactor(3, states), 9);
    }

    [Fact]
    public void ClampGroupFactor_LimitedByLargestMember_NotByAverage()
    {
        // 가장 큰 요소가 상한에 먼저 닿으면 그룹 전체가 거기서 멈춘다 — 그래야 그룹이 찢어지지 않는다.
        var states = new[]
        {
            ElementTransformState.Identity,
            ElementTransformState.Identity with { ScaleX = TransformMath.MaxScale / 2 },
        };

        Assert.Equal(2, TransformMath.ClampGroupFactor(1000, states), 9);
    }

    [Fact]
    public void ClampGroupFactor_ShrinkIsLimitedBySmallestMember()
    {
        var states = new[] { ElementTransformState.Identity with { ScaleX = TransformMath.MinScale * 2, ScaleY = 1 } };

        Assert.Equal(0.5, TransformMath.ClampGroupFactor(0.0001, states), 9);
    }

    [Fact]
    public void ClampGroupFactor_NaN_IsNeutral() =>
        Assert.Equal(1, TransformMath.ClampGroupFactor(double.NaN, [ElementTransformState.Identity]), 9);

    [Fact]
    public void ClampGroupFactor_NoStates_IsNeutral() =>
        Assert.Equal(1, TransformMath.ClampGroupFactor(5, []), 9);

    [Fact]
    public void ClampMagnitude_AboveCeiling_IsCapped() =>
        Assert.Equal(TransformMath.MaxScale, TransformMath.ClampMagnitude(10_000), 9);

    [Fact]
    public void ClampMagnitude_NegativeAboveCeiling_KeepsSign() =>
        Assert.Equal(-TransformMath.MaxScale, TransformMath.ClampMagnitude(-10_000), 9);

    // ---- ClampGroupFactor: 퇴화 축이 그룹 전체를 봉쇄하면 안 된다 — 리팩터링 19단계에서 SelectionGroupTests로부터 이동 ----

    /// <summary>
    /// 회귀 증인: 요소 하나의 한 축이 이미 <c>MinScale</c>이면 예전에는 하한이 1로 올라가
    /// <b>그룹 전체의 축소가 조용히 완전 봉쇄</b>됐다 (측면 핸들로 요소 하나를 납작하게 만든 순간
    /// 그룹이 영원히 안 줄어드는 증상).
    /// </summary>
    [Fact]
    public void ClampGroupFactor_MemberAxisAtFloor_StillAllowsShrink()
    {
        var flat = ElementTransformState.Identity with { ScaleX = TransformMath.MinScale, ScaleY = 1 };
        var normal = ElementTransformState.Identity;

        Assert.Equal(0.5, TransformMath.ClampGroupFactor(0.5, [flat, normal]), 9);
    }

    [Fact]
    public void ClampGroupFactor_MemberAtCeiling_StillAllowsGrowthOfNoneButShrinkOfAll()
    {
        var maxed = ElementTransformState.Identity with
        {
            ScaleX = TransformMath.MaxScale,
            ScaleY = TransformMath.MaxScale,
        };

        Assert.Equal(1, TransformMath.ClampGroupFactor(4, [maxed]), 9);
        Assert.Equal(0.5, TransformMath.ClampGroupFactor(0.5, [maxed]), 9);
    }

    /// <summary>
    /// 회귀 증인: 구성원이 <b>전부</b> 바닥에 닿으면 하한을 올릴 근거가 하나도 없어 lower가 0으로
    /// 남는데, 그때 커서를 앵커에 얹으면 factor 0이 통과한다. f=0은 비가역이라 선택 요소들의 중심이
    /// 피벗 한 점으로 합쳐지고 최대 배율로 되키워도 0×f = 0이라 제스처로는 영영 복구되지 않는다.
    /// </summary>
    [Fact]
    public void ClampGroupFactor_AllMembersAtFloor_NeverCollapsesToZero()
    {
        var floored = ElementTransformState.Identity with
        {
            ScaleX = TransformMath.MinScale,
            ScaleY = TransformMath.MinScale,
        };
        var states = new[] { floored, floored };

        Assert.True(TransformMath.ClampGroupFactor(0, states) > 0);
        Assert.True(TransformMath.ClampGroupFactor(-5, states) > 0);
        Assert.True(TransformMath.ClampGroupFactor(0.5, states) > 0);
    }

    /// <summary>배율 0은 어떤 상태 조합에서도 통과하지 못한다 — 사상이 항상 가역이라는 증인.</summary>
    [Fact]
    public void ClampGroupFactor_ZeroOrNegative_IsAlwaysPositive()
    {
        var mixed = new[]
        {
            ElementTransformState.Identity,
            ElementTransformState.Identity with { ScaleX = TransformMath.MinScale, ScaleY = 4 },
        };

        Assert.True(TransformMath.ClampGroupFactor(0, mixed) > 0);
        Assert.True(TransformMath.ClampGroupFactor(-1, mixed) > 0);
    }

    /// <summary>혼합 DPI 이관 뒤 흔한 상태(등방 배율 여럿)에서 factor 1은 반드시 1로 통과해야 한다.</summary>
    [Fact]
    public void ClampGroupFactor_IdentityFactor_PassesThrough()
    {
        var states = new[]
        {
            ElementTransformState.Identity with { ScaleX = 0.5, ScaleY = 0.5 },
            ElementTransformState.Identity with { ScaleX = 3, ScaleY = 3 },
        };

        Assert.Equal(1, TransformMath.ClampGroupFactor(1, states), 9);
    }
}
