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
}
