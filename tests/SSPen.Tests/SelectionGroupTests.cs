using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// R1: 그룹 프레임과 그룹 변형 수학의 증인.
///
/// 핵심 증인은 <see cref="ScaleAbout_RotatedElements_KeepsLocalAxesOrthogonal"/>다 —
/// "등방 스케일은 각도가 제각각인 그룹에서도 정확하다"가 성립하지 않으면 R1의 설계 전제가 무너진다.
/// </summary>
public class SelectionGroupTests
{
    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    // ---- 프레임 ----

    [Fact]
    public void Frame_TwoElements_UnionsAxisAlignedBounds()
    {
        var a = Stroke(0, 0, 10, 10);
        var b = Stroke(100, 50, 20, 20);

        var frame = SelectionGroup.Frame([a, b]);

        Assert.NotNull(frame);
        Assert.Equal(Rect.Union(a.TransformedBounds, b.TransformedBounds), frame!.Value);
    }

    [Fact]
    public void Frame_Empty_ReturnsNull() => Assert.Null(SelectionGroup.Frame([]));

    [Fact]
    public void Frame_FadingElement_IsExcluded()
    {
        var solid = Stroke(0, 0, 10, 10);
        var fading = Stroke(500, 500, 10, 10);
        fading.IsFading = true;

        var frame = SelectionGroup.Frame([solid, fading]);

        Assert.Equal(solid.TransformedBounds, frame);
    }

    [Fact]
    public void Frame_OnlyFadingElements_ReturnsNull()
    {
        var fading = Stroke(0, 0, 10, 10);
        fading.IsFading = true;

        Assert.Null(SelectionGroup.Frame([fading]));
    }

    // ---- 핸들 ----

    [Theory]
    [InlineData(GroupHandleKind.TopLeft, GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.TopRight, GroupHandleKind.BottomLeft)]
    [InlineData(GroupHandleKind.BottomRight, GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.BottomLeft, GroupHandleKind.TopRight)]
    public void AnchorCorner_IsDiagonallyOpposite(GroupHandleKind handle, GroupHandleKind expected)
    {
        var frame = new Rect(10, 20, 100, 60);

        Assert.Equal(SelectionGroup.CornerCenter(frame, expected), SelectionGroup.AnchorCorner(frame, handle));
    }

    [Fact]
    public void HitHandle_OnCorner_ReturnsThatCorner()
    {
        var frame = new Rect(10, 20, 100, 60);

        var hit = SelectionGroup.HitHandle(frame, frame.BottomRight, Rect.Empty);

        Assert.Equal(GroupHandleKind.BottomRight, hit);
    }

    [Fact]
    public void HitHandle_OnRotateHandle_WinsOverCorners()
    {
        var frame = new Rect(10, 20, 100, 60);

        var hit = SelectionGroup.HitHandle(frame, SelectionGroup.RotateHandle(frame), Rect.Empty);

        Assert.Equal(GroupHandleKind.Rotate, hit);
    }

    [Fact]
    public void HitHandle_FrameInterior_IsNotAHandle()
    {
        var frame = new Rect(10, 20, 100, 60);

        Assert.Null(SelectionGroup.HitHandle(frame, SelectionGroup.Center(frame), Rect.Empty));
    }

    [Fact]
    public void ScaleFactor_CursorAtGripCorner_IsOne()
    {
        var frame = new Rect(0, 0, 100, 50);

        double factor = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, frame.BottomRight);

        Assert.Equal(1, factor, 9);
    }

    [Fact]
    public void ScaleFactor_CursorAtDoubleDiagonal_IsTwo()
    {
        var frame = new Rect(0, 0, 100, 50);
        // 앵커(좌상 0,0)에서 잡은 모서리(100,50)까지의 대각을 2배 지점까지 끈다.
        double factor = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, new Point(200, 100));

        Assert.Equal(2, factor, 9);
    }

    [Fact]
    public void ScaleFactor_CursorOffAxis_UsesProjectionSoAspectNeverChanges()
    {
        var frame = new Rect(0, 0, 100, 50);
        // 대각과 수직인 방향으로 밀어도 정사영 성분이 그대로면 배율이 변하지 않는다.
        var diagonal = frame.BottomRight - frame.TopLeft;
        var perpendicular = new Vector(-diagonal.Y, diagonal.X);

        double onAxis = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, frame.BottomRight);
        double offAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, frame.BottomRight + perpendicular);

        Assert.Equal(onAxis, offAxis, 9);
    }

    // ---- 그룹 변형 수학 (R1의 설계 전제) ----

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

    // ---- 배율 재단 (D5) ----

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

    // ---- SEL-LIM-5: '그리는 조건'과 '잡히는 조건'은 같은 술어여야 한다 ----

    /// <summary>
    /// 회귀 증인: 모니터에 걸친 선택에서 이 서피스가 요소를 <b>1개만</b> 소유하면 핸들을 그려서도,
    /// 잡아서도 안 된다. 예전에는 렌더가 <c>owned.Count &lt; MinGroupCount</c>라며 요소별 8핸들
    /// 경로로 빠져 핸들을 전부 그렸는데 히트 테스트는 막혀 있어, 회전 핸들 클릭이 빈 곳 분기로
    /// 떨어져 선택 전체가 날아가고 클릭 통과까지 켜졌다.
    /// </summary>
    [Fact]
    public void HandlesGrabbable_SingleOwnedOfCrossMonitorPair_IsFalse() =>
        Assert.False(SelectionGroup.HandlesGrabbable(ownedCount: 1, selectionCount: 2));

    [Fact]
    public void HandlesGrabbable_OwnsWholeSelection_IsTrue()
    {
        Assert.True(SelectionGroup.HandlesGrabbable(1, 1));
        Assert.True(SelectionGroup.HandlesGrabbable(3, 3));
    }

    [Fact]
    public void HandlesGrabbable_NothingOwned_IsFalse()
    {
        Assert.False(SelectionGroup.HandlesGrabbable(0, 0));
        Assert.False(SelectionGroup.HandlesGrabbable(0, 2));
    }

    // ---- 그룹 배율 클램프: 퇴화 축이 그룹 전체를 봉쇄하면 안 된다 ----

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

    // ---- 회전한 그룹 프레임 (제스처 한정 렌더/히트 좌표, SEL-LIM-6) ----
    //
    // 증상: 다중 선택을 회전하면 점선 테두리와 5개 핸들이 시작 위치에 못 박혀 있었다.
    // 원인은 그려지는 프레임이 각도를 담을 수 없는 축 정렬 Rect였다는 것 —
    // 아래 증인들은 GroupFrame이 (a) 각도 0에서 예전과 정확히 같고,
    // (b) 각도가 붙으면 잉크와 같은 강체 운동을 겪으며,
    // (c) 그려지는 좌표와 잡히는 좌표가 끝까지 같은 계산에서 나온다는 것을 고정한다.

    /// <summary>NaN은 범위 어서트를 조용히 통과하므로 좌표 비교 전에 반드시 먼저 배제한다 (R16).</summary>
    private static void AssertPointsEqual(Point expected, Point actual, double tolerance = 1e-7)
    {
        Assert.False(double.IsNaN(actual.X), "X가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.False(double.IsNaN(actual.Y), "Y가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.True(
            Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance,
            $"기대 {expected} / 실제 {actual} (허용오차 {tolerance})");
    }

    /// <summary>프레임 로컬 점을 피벗 기준으로 회전시킨 월드 위치 (테스트가 독립적으로 계산하는 기준값).</summary>
    private static Point RotateAboutPivot(Point p, Point pivot, double degrees) =>
        pivot + TransformMath.RotateVector(p - pivot, degrees);

    // -- 하위 호환: 각도 0이면 수정 이전과 같은 좌표여야 한다 --

    /// <summary>
    /// 각도 0 단락이 실제로 동작해 피벗 왕복 (x−p)+p의 1ulp 표류가 없음을 못박는다.
    /// 허용오차 없이 정확히 같아야 한다 — 회전하지 않는 모든 경우가 수정 이전과 비트 동일이라는 뜻이다.
    /// </summary>
    [Fact]
    public void Corners_ZeroAngle_MatchesAxisAlignedRectCornersExactly()
    {
        var bounds = new Rect(10, 20, 100, 60);

        var corners = SelectionGroup.Corners(new GroupFrame(bounds, 0));

        Assert.Equal(bounds.TopLeft, corners[0]);
        Assert.Equal(bounds.TopRight, corners[1]);
        Assert.Equal(bounds.BottomRight, corners[2]);
        Assert.Equal(bounds.BottomLeft, corners[3]);
    }

    [Theory]
    [InlineData(GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.TopRight)]
    [InlineData(GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.BottomLeft)]
    public void CornerCenter_ZeroAngle_MatchesRectOverload(GroupHandleKind handle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        Assert.Equal(
            SelectionGroup.CornerCenter(bounds, handle),
            SelectionGroup.CornerCenter(new GroupFrame(bounds, 0), handle));
    }

    [Fact]
    public void TopCenter_ZeroAngle_MatchesRectOverload()
    {
        var bounds = new Rect(10, 20, 100, 60);

        Assert.Equal(SelectionGroup.TopCenter(bounds), SelectionGroup.TopCenter(new GroupFrame(bounds, 0)));
    }

    /// <summary>R5 그룹판: 각도 0에서 렌더 오버로드와 히트 오버로드가 같은 클램프 지점을 낸다.</summary>
    [Fact]
    public void RotateHandle_ZeroAngle_MatchesRectOverload_AndClampsIdentically()
    {
        var bounds = new Rect(800, 2, 100, 60); // 화면 최상단 — 회전 핸들이 서피스 밖으로 나간다.
        var surface = new Rect(0, 0, 1920, 1080);
        double reach = TransformMath.HandleScreenSize / 2;

        var fromRect = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(bounds), surface, reach);
        var fromFrame = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(new GroupFrame(bounds, 0)), surface, reach);

        AssertPointsEqual(fromRect, fromFrame);
        Assert.Equal(GroupHandleKind.Rotate, SelectionGroup.HitHandle(bounds, fromFrame, surface));
    }

    // -- 회전 기하 --

    /// <summary>원점이 아니라 <b>프레임 중심</b>을 축으로 돈다. 최빈 오구현(원점 회전)이면 즉시 실패한다.</summary>
    [Fact]
    public void Corners_RotatedFrame_RotateAboutFrameCenter_NotOrigin()
    {
        var bounds = new Rect(300, 200, 100, 60); // 원점에서 멀리 — 원점 회전이면 화면 밖으로 날아간다.
        var frame = new GroupFrame(bounds, 30);
        var pivot = SelectionGroup.Center(bounds);

        var corners = SelectionGroup.Corners(frame);

        AssertPointsEqual(RotateAboutPivot(bounds.TopLeft, pivot, 30), corners[0], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.TopRight, pivot, 30), corners[1], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.BottomRight, pivot, 30), corners[2], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.BottomLeft, pivot, 30), corners[3], 1e-9);
    }

    /// <summary>회전은 강체 운동이다 — 어느 각도에서도 직각과 변 길이가 보존되어야 한다(전단 금지, A3).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(47.5)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-120)]
    public void Corners_RotatedFrame_StaysRigid_AtManyAngles(double angle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        var c = SelectionGroup.Corners(new GroupFrame(bounds, angle));
        var top = c[1] - c[0];
        var right = c[2] - c[1];
        var bottom = c[2] - c[3];
        var left = c[3] - c[0];

        Assert.Equal(0, (top.X * right.X) + (top.Y * right.Y), 9);
        Assert.Equal(bounds.Width, top.Length, 9);
        Assert.Equal(bounds.Width, bottom.Length, 9);
        Assert.Equal(bounds.Height, right.Length, 9);
        Assert.Equal(bounds.Height, left.Length, 9);
    }

    /// <summary>
    /// <b>수정 완료의 정의</b>: 가이드 프레임이 잉크와 <b>같은 (pivot, delta)</b>로 도는가.
    /// 두 가지를 동시에 본다 — (a) 포즈된 4점이 시작 프레임의 강체 이동상이고,
    /// (b) 회전된 잉크의 꼭짓점을 −delta로 되돌리면 전부 시작 프레임 안에 들어온다
    /// (= 기울어진 가이드가 여전히 잉크를 정확히 감싼다).
    ///
    /// <b>하지 말 것</b>: 회전된 잉크 꼭짓점이 포즈된 사각형 "안"인지 볼록 판정으로 단언하기.
    /// 그 점들은 정의상 경계 <b>위</b>에 있고 볼록 판정에 엡실론이 없어 1e-14 잡음만으로 outside가 난다.
    /// </summary>
    [Fact]
    public void Corners_FrameRotatedByDelta_MatchesMembersRotatedByTheSameDelta()
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);
        const double delta = 40;

        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, delta);
        }

        var posed = SelectionGroup.Corners(new GroupFrame(frame0, delta));
        AssertPointsEqual(RotateAboutPivot(frame0.TopLeft, pivot, delta), posed[0], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.TopRight, pivot, delta), posed[1], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.BottomRight, pivot, delta), posed[2], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.BottomLeft, pivot, delta), posed[3], 1e-9);

        var slack = frame0;
        slack.Inflate(1e-6, 1e-6);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            foreach (var corner in element.TransformedCorners())
            {
                var pulledBack = RotateAboutPivot(corner, pivot, -delta);
                Assert.True(
                    slack.Contains(pulledBack),
                    $"−{delta}도로 되돌린 잉크 꼭짓점 {pulledBack}이 시작 프레임 {frame0} 밖이다 — 가이드가 잉크를 감싸지 못한다.");
            }
        }
    }

    /// <summary>
    /// 90도 배수에서는 포즈된 프레임과 마우스 업 이후의 살아있는 축 정렬 합집합이 같다 —
    /// SEL-LIM-6의 "90도 배수에서는 릴리스 스냅이 시각적으로 0"이라는 서술의 증인.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Corners_RotatedBy90Multiples_EqualPostReleaseAxisAlignedUnion(double angle)
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);

        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, angle);
        }

        var live = SelectionGroup.Frame([a, b])!.Value;
        var liveCorners = SelectionGroup.Corners(new GroupFrame(live, 0));
        var posed = SelectionGroup.Corners(new GroupFrame(frame0, angle));

        foreach (var corner in posed)
        {
            Assert.True(
                Array.Exists(liveCorners, other => (other - corner).Length <= 1e-6),
                $"{angle}도에서 포즈된 꼭짓점 {corner}이 릴리스 이후 합집합 {live}의 꼭짓점 집합에 없다.");
        }
    }

    /// <summary>
    /// 프레임이 180도 돌면 회전 핸들은 프레임 <b>아래</b>에 있어야 한다 (R5).
    /// 하드코딩 <c>frame.Top − offset</c>을 죽이는 증인 — 요소별
    /// <c>RotateHandleWorld_UpsideDownElement_SitsBelowCenterNotAboveIt</c>의 그룹판이다.
    /// </summary>
    [Fact]
    public void RotateHandle_FrameAt180Degrees_SitsBelowFrameCenter_NotAbove()
    {
        var bounds = new Rect(10, 20, 100, 60);
        var frame = new GroupFrame(bounds, 180);

        var handle = SelectionGroup.RotateHandle(frame);

        Assert.True(
            handle.Y > SelectionGroup.Center(frame).Y,
            $"180도에서 회전 핸들 {handle}이 중심 {SelectionGroup.Center(frame)} 위에 있다 — 화면 −Y 하드코딩이 남아 있다.");
    }

    /// <summary>회전 핸들은 회전한 상단 변에 <b>수직</b>으로 화면 거리만큼 떨어져 있어야 한다.</summary>
    [Fact]
    public void RotateHandle_RotatedFrame_KeepsScreenOffsetPerpendicularToTopEdge()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);
        var corners = SelectionGroup.Corners(frame);

        var stem = SelectionGroup.RotateHandle(frame) - SelectionGroup.TopCenter(frame);
        var topEdge = corners[1] - corners[0];

        Assert.Equal(TransformMath.RotateHandleScreenOffset, stem.Length, 6);
        Assert.Equal(0, (stem.X * topEdge.X) + (stem.Y * topEdge.Y), 9);
    }

    /// <summary>스템 시작점은 회전한 상단 변의 중점이어야 테두리·스템·핸들이 한 도형으로 보인다.</summary>
    [Fact]
    public void TopCenter_RotatedFrame_IsMidpointOfRotatedTopEdge()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);
        var corners = SelectionGroup.Corners(frame);

        var midpoint = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);

        AssertPointsEqual(midpoint, SelectionGroup.TopCenter(frame), 1e-9);
    }

    // -- 렌더 == 히트 (R5): 그려지는 위치에서 잡혀야 한다 --

    /// <summary>
    /// 각도 축의 "보이지만 잡히지 않는 핸들" 방어선. 예전에 그 결함은 선택과 도구를 통째로 날렸다
    /// (SEL-LIM-5 회귀 서술 참고). 회전 <b>전</b> 축 정렬 위치에서는 잡히지 않아야 한다.
    /// </summary>
    [Theory]
    [InlineData(GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.TopRight)]
    [InlineData(GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.BottomLeft)]
    public void HitHandle_RotatedFrame_GrabsCornerAtDrawnPosition(GroupHandleKind handle)
    {
        var bounds = new Rect(10, 20, 100, 60);
        var frame = new GroupFrame(bounds, 35);

        Assert.Equal(
            handle,
            SelectionGroup.HitHandle(frame, SelectionGroup.CornerCenter(frame, handle), Rect.Empty));
        Assert.NotEqual(
            handle,
            SelectionGroup.HitHandle(frame, SelectionGroup.CornerCenter(bounds, handle), Rect.Empty));
    }

    [Fact]
    public void HitHandle_RotatedFrame_RotateHandleStillWinsOverCorners()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);

        Assert.Equal(
            GroupHandleKind.Rotate,
            SelectionGroup.HitHandle(frame, SelectionGroup.RotateHandle(frame), Rect.Empty));
    }

    /// <summary>클램프된 회전 핸들도 렌더와 히트가 같은 지점이어야 한다 (R5).</summary>
    [Fact]
    public void HitHandle_RotatedFrame_ClampedRotateHandle_IsGrabbableAtClampedSpot()
    {
        var frame = new GroupFrame(new Rect(800, 2, 100, 60), 35);
        var surface = new Rect(0, 0, 1920, 1080);

        var drawn = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(frame), surface, TransformMath.HandleScreenSize / 2);

        Assert.Equal(GroupHandleKind.Rotate, SelectionGroup.HitHandle(frame, drawn, surface));
    }

    // -- 수학 불변 --

    /// <summary><see cref="GroupFrame"/>이 피벗을 별도 필드로 안 실어도 되는 근거.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-120)]
    public void Center_RotatedFrame_IsInvariantUnderFrameAngle(double angle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        AssertPointsEqual(SelectionGroup.Center(bounds), SelectionGroup.Center(new GroupFrame(bounds, angle)));
    }

    [Theory]
    [InlineData(GroupHandleKind.TopLeft, GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.TopRight, GroupHandleKind.BottomLeft)]
    [InlineData(GroupHandleKind.BottomRight, GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.BottomLeft, GroupHandleKind.TopRight)]
    public void AnchorCorner_RotatedFrame_IsStillDiagonallyOpposite(
        GroupHandleKind handle, GroupHandleKind expected)
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);

        var anchor = SelectionGroup.AnchorCorner(frame, handle);
        var grip = SelectionGroup.CornerCenter(frame, handle);

        AssertPointsEqual(SelectionGroup.CornerCenter(frame, expected), anchor, 1e-9);
        AssertPointsEqual(
            frame.Pivot, new Point((anchor.X + grip.X) / 2, (anchor.Y + grip.Y) / 2), 1e-9);
    }

    /// <summary>등방 배율의 정사영 축이 프레임과 함께 돈다 — 본문을 고치지 않아도 되는 이유의 증인.</summary>
    [Fact]
    public void ScaleFactor_RotatedFrame_ProjectsOntoRotatedDiagonal()
    {
        var frame = new GroupFrame(new Rect(0, 0, 100, 50), 35);
        var anchor = SelectionGroup.AnchorCorner(frame, GroupHandleKind.BottomRight);
        var grip = SelectionGroup.CornerCenter(frame, GroupHandleKind.BottomRight);
        var diagonal = grip - anchor;
        var perpendicular = new Vector(-diagonal.Y, diagonal.X);

        double onAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, anchor + (diagonal * 2));
        double offAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, anchor + (diagonal * 2) + perpendicular);

        Assert.Equal(2, onAxis, 9);
        Assert.Equal(onAxis, offAxis, 9);
    }

    // -- 제스처 계약 (컨트롤러에서 순수 코어로 내린 부분) --

    /// <summary>
    /// 밀어 넣는 것은 <b>동결된</b> 크기다. 여기에 살아있는 합집합을 넣도록 바꾸면
    /// "잡은 핸들이 커서 밑에서 빠져나간다"가 전 테스트 초록인 채 부활한다.
    /// </summary>
    [Fact]
    public void GestureFrame_Rotating_FreezesSizeAndCarriesDelta()
    {
        var frozen = new Rect(10, 20, 100, 60);

        var frame = SelectionGroup.GestureFrame(frozen, rotating: true, deltaDegrees: 40);

        Assert.NotNull(frame);
        Assert.Equal(frozen, frame!.Value.Bounds);
        Assert.Equal(40, frame.Value.AngleDegrees, 9);
    }

    /// <summary>등방 스케일·이동은 프레임을 밀지 않는다 — 살아있는 합집합이 그대로 정답이다.</summary>
    [Fact]
    public void GestureFrame_NotRotating_ReturnsNull() =>
        Assert.Null(SelectionGroup.GestureFrame(new Rect(10, 20, 100, 60), rotating: false, deltaDegrees: 40));

    /// <summary>
    /// <see cref="Rect.Empty"/>는 좌표가 ±무한대라 피벗이 NaN이 된다. 도달 불가능하더라도
    /// 타입 경계에서 막는다 — NaN은 범위 어서트를 조용히 통과한다 (R16).
    /// </summary>
    [Fact]
    public void GestureFrame_EmptyFrozenRect_ReturnsNull_NoNaN()
    {
        Assert.Null(SelectionGroup.GestureFrame(Rect.Empty, rotating: true, deltaDegrees: 40));
        Assert.True(
            double.IsNaN(new GroupFrame(Rect.Empty, 40).Pivot.X),
            "가드가 필요한 이유 자체가 사라졌다면 가드도 재검토할 것.");
    }

    /// <summary>
    /// Shift는 <b>증분</b>을 15도 배수로 스냅하고, 가이드와 잉크가 <b>그 값 하나</b>를 공유한다 —
    /// 두 번 계산하거나 프레임에 스냅 전 각을 쓰면 15도 경계마다 가이드가 잉크에서 떨어진다.
    /// </summary>
    [Fact]
    public void RotationDelta_WithShift_SnapsIncrement_AndGuideSharesTheSameNumber()
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), 38); // 38도 → 45도로 스냅

        double delta = SelectionGroup.RotationDelta(pivot, from, to, shift: true);

        Assert.Equal(45, delta, 9);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, delta);
        }

        var posed = SelectionGroup.Corners(new GroupFrame(frame0, delta));
        AssertPointsEqual(RotateAboutPivot(frame0.TopLeft, pivot, delta), posed[0], 1e-9);

        var slack = frame0;
        slack.Inflate(1e-6, 1e-6);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            foreach (var corner in element.TransformedCorners())
            {
                Assert.True(
                    slack.Contains(RotateAboutPivot(corner, pivot, -delta)),
                    $"스냅된 {delta}도에서 가이드와 잉크가 어긋났다.");
            }
        }
    }

    /// <summary>커서가 피벗과 겹치면 0이고 NaN이 아니다 (승격 과정에서 원본 퇴화 방어가 유실되지 않았음).</summary>
    [Fact]
    public void RotationDelta_DegenerateDrag_ReturnsZero()
    {
        var pivot = new Point(50, 50);

        double delta = SelectionGroup.RotationDelta(pivot, pivot, new Point(80, 20), shift: false);

        Assert.False(double.IsNaN(delta), "NaN이면 각도가 요소로 새어 나가 화면에서 증발한다 (R16).");
        Assert.Equal(0, delta, 9);
    }

    // -- 설계 방화벽 --

    /// <summary>
    /// 그룹 프레임의 <b>지속</b> 상태는 각도가 없는 <see cref="Rect"/>여야 한다.
    /// 각도를 여기로 올리려는 시도는 이 증인에서 걸린다.
    /// </summary>
    [Fact]
    public void Frame_ReturnType_IsAngleFreeRect_ByReflection()
    {
        var method = typeof(SelectionGroup).GetMethod(nameof(SelectionGroup.Frame));

        Assert.NotNull(method);
        Assert.True(
            method!.ReturnType == typeof(Rect?),
            "그룹 프레임에 각도를 영속시키려면 원장(TransformDelta)에 각도 자리를 먼저 만들어야 한다 (SEL-LIM-6). "
            + "실행취소가 되돌릴 수 없는 상태를 만들지 말 것.");
    }

    // -- GroupRotateStep: 가이드와 잉크가 어긋날 수 없음을 타입으로 고정 --

    /// <summary>
    /// <b>수정의 배선 증인</b>. 컨트롤러는 이 한 호출의 세 필드를 그대로 쓰므로,
    /// 여기서 각도가 일치하면 화면의 가이드와 잉크가 어긋날 방법이 없다.
    /// 이 단언이 깨지면 "그룹을 회전해도 테두리가 안 도는" 원래 증상이 그대로 돌아온다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(38)]
    [InlineData(90)]
    [InlineData(-137.5)]
    public void RotateStep_GuideAngleAndInkDelta_AreTheSameNumber(double angle)
    {
        var frozen = new Rect(300, 200, 260, 140);
        var pivot = SelectionGroup.Center(frozen);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), angle);

        var step = SelectionGroup.RotateStep(frozen, from, to, shift: false);

        Assert.NotNull(step.Guide);
        Assert.Equal(
            step.DeltaDegrees,
            step.Guide!.Value.AngleDegrees);
        Assert.Equal(angle, step.DeltaDegrees, 9);
    }

    /// <summary>가이드와 잉크는 <b>같은 피벗</b>을 써야 한다 — 동결 프레임의 중심 하나.</summary>
    [Fact]
    public void RotateStep_PivotIsFrozenFrameCenter_AndGuideSharesIt()
    {
        var frozen = new Rect(300, 200, 260, 140);

        var step = SelectionGroup.RotateStep(
            frozen, new Point(600, 270), new Point(430, 500), shift: false);

        AssertPointsEqual(SelectionGroup.Center(frozen), step.Pivot);
        AssertPointsEqual(step.Pivot, step.Guide!.Value.Pivot);
    }

    /// <summary>
    /// 회전 중에는 <b>항상</b> 가이드를 민다. null이 섞이면 창이 살아있는 축 정렬 합집합으로 되돌아가
    /// 잡은 핸들이 커서 밑에서 빠져나간다.
    /// </summary>
    [Fact]
    public void RotateStep_NonEmptyFrozen_AlwaysProducesGuideFrozenAtStartSize()
    {
        var frozen = new Rect(300, 200, 260, 140);

        var step = SelectionGroup.RotateStep(
            frozen, new Point(600, 270), new Point(430, 500), shift: false);

        Assert.NotNull(step.Guide);
        Assert.Equal(frozen, step.Guide!.Value.Bounds);
    }

    /// <summary>Shift 스냅은 가이드와 잉크에 <b>동시에</b> 걸려야 한다 (한쪽만 스냅되면 15도 경계마다 어긋난다).</summary>
    [Fact]
    public void RotateStep_WithShift_SnapsGuideAndInkTogether()
    {
        var frozen = new Rect(300, 200, 260, 140);
        var pivot = SelectionGroup.Center(frozen);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), 38);

        var step = SelectionGroup.RotateStep(frozen, from, to, shift: true);

        Assert.Equal(45, step.DeltaDegrees, 9);
        Assert.Equal(step.DeltaDegrees, step.Guide!.Value.AngleDegrees);
    }

    /// <summary>퇴화 입력에서 NaN이 잉크로 새어 나가지 않는다 (R16).</summary>
    [Fact]
    public void RotateStep_EmptyFrozen_IsInertAndNaNFree()
    {
        var step = SelectionGroup.RotateStep(Rect.Empty, new Point(10, 10), new Point(90, 40), shift: false);

        Assert.Null(step.Guide);
        Assert.Equal(0, step.DeltaDegrees);
        Assert.False(double.IsNaN(step.Pivot.X), "NaN 피벗은 요소를 화면에서 증발시킨다 (R16).");
        Assert.False(double.IsNaN(step.Pivot.Y), "NaN 피벗은 요소를 화면에서 증발시킨다 (R16).");
    }
}
