using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// R2/R5/R6/R7: 해제 판정·관대 히트·휠 세션의 증인.
/// 전부 순수 함수 또는 주입 시계 위에서 돌아가므로 헤드리스로 검증된다.
/// </summary>
public class SelectionGestureRuleTests
{
    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    // ---- R2: 제자리 클릭 vs 드래그 ----

    [Fact]
    public void IsStationaryClick_SamePoint_IsTrue() =>
        Assert.True(SelectionGestureRules.IsStationaryClick(new Point(10, 10), new Point(10, 10)));

    [Fact]
    public void IsStationaryClick_JustUnderThreshold_IsTrue() =>
        Assert.True(SelectionGestureRules.IsStationaryClick(new Point(0, 0), new Point(2, 0)));

    [Fact]
    public void IsStationaryClick_AtThreshold_IsDrag() =>
        Assert.False(SelectionGestureRules.IsStationaryClick(new Point(0, 0), new Point(3, 0)));

    // ---- R5: 클릭 통과 전환은 '선택이 있었던 제자리 클릭'에만 ----

    [Fact]
    public void ShouldEngageClickThrough_StationaryClickWithSelection_IsTrue() =>
        Assert.True(SelectionGestureRules.ShouldEngageClickThrough(true, new Point(5, 5), new Point(5, 6)));

    /// <summary>
    /// 아무것도 안 고른 상태의 빈 클릭까지 통과로 흡수하면, 선택 도구를 켜자마자 도구가 해제되어
    /// 아무것도 고를 수 없게 된다 — 이 증인이 그 회귀를 막는다.
    /// </summary>
    [Fact]
    public void ShouldEngageClickThrough_NoPriorSelection_IsFalse() =>
        Assert.False(SelectionGestureRules.ShouldEngageClickThrough(false, new Point(5, 5), new Point(5, 5)));

    /// <summary>드래그는 마퀴다 — 여기서 통과가 켜지면 마퀴 선택이 통째로 불가능해진다.</summary>
    [Fact]
    public void ShouldEngageClickThrough_Drag_IsFalse() =>
        Assert.False(SelectionGestureRules.ShouldEngageClickThrough(true, new Point(0, 0), new Point(80, 40)));

    // ---- R7: 하이브리드 고정점 ----

    [Fact]
    public void WheelPivot_CursorInsideFrame_UsesCursor()
    {
        var frame = new Rect(0, 0, 100, 100);

        Assert.Equal(new Point(20, 30), SelectionGestureRules.WheelPivot(frame, new Point(20, 30)));
    }

    [Fact]
    public void WheelPivot_CursorOutsideFrame_UsesFrameCenter()
    {
        var frame = new Rect(0, 0, 100, 100);

        Assert.Equal(new Point(50, 50), SelectionGestureRules.WheelPivot(frame, new Point(500, 500)));
    }

    // ---- R7: 휠 세션 (원장 1항목 계약) ----

    [Fact]
    public void WheelSession_Steps_AccumulateMultiplicatively()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);

        session.Step(1, now);
        double factor = session.Step(1, now);

        Assert.Equal(WheelScaleSession.NotchFactor * WheelScaleSession.NotchFactor, factor, 9);
    }

    [Fact]
    public void WheelSession_NegativeNotch_Shrinks()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);

        Assert.Equal(1 / WheelScaleSession.NotchFactor, session.Step(-1, now), 9);
    }

    /// <summary>재진입 Begin은 고정점을 갈아치우지 않는다 — 갈아치우면 휠 도중 선택이 표류한다.</summary>
    [Fact]
    public void WheelSession_BeginWhileActive_KeepsFrozenPivot()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(10, 10), now);
        session.Step(1, now);

        session.Begin(new Point(999, 999), now);

        Assert.Equal(new Point(10, 10), session.Pivot);
        Assert.Equal(WheelScaleSession.NotchFactor, session.Factor, 9);
    }

    [Fact]
    public void WheelSession_BeforeIdleTimeout_IsNotDue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);
        session.Step(1, now);

        Assert.False(session.DueToCommit(now + WheelScaleSession.IdleTimeout - TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void WheelSession_AfterIdleTimeout_IsDue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);
        session.Step(1, now);

        Assert.True(session.DueToCommit(now + WheelScaleSession.IdleTimeout));
    }

    [Fact]
    public void WheelSession_Inactive_IsNeverDue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(new WheelScaleSession().DueToCommit(now.AddHours(1)));
    }

    [Fact]
    public void WheelSession_End_ResetsFactor()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);
        session.Step(3, now);

        session.End();

        Assert.False(session.Active);
        Assert.Equal(1, session.Factor, 9);
    }

    // ---- R6: 관대 히트 ----

    /// <summary>
    /// 선택/지우개 허용 오차가 오늘 같은 값(6px)이라는 사실을 <b>보이게</b> 고정한다 — 감각 통일이 의도이기 때문이다.
    /// 두 경로의 랭킹 규칙은 의도적으로 다르므로(지우개는 '가장 가까운 것', 선택은 '가장 위 → 면적 최소')
    /// 상수를 하나로 합치면 안 된다. 값을 <b>일부러</b> 갈라놓는 날이 오면 이 테스트는 고칠 것이 아니라
    /// 지우는 것이 맞다.
    /// </summary>
    [Fact]
    public void SelectAndEraseTolerance_AreEqualToday()
    {
        Assert.Equal(6d, SelectionGestureRules.SelectHitTolerancePixels);
        Assert.Equal(6d, SelectionGestureRules.EraseHitTolerancePixels);
    }

    /// <summary>사각형 도형은 외곽선만 명중이었다 — 내부 클릭이 이제 잡혀야 한다.</summary>
    [Fact]
    public void HitForSelect_InsideRectangleShape_IsHit()
    {
        var rectangle = new ShapeElement(
            ShapeKind.Rectangle, new Point(0, 0), new Point(200, 100), Colors.Red, 2);

        Assert.Same(rectangle, SelectionGeometry.HitForSelect([rectangle], new Point(100, 50), 6));
        // 지우개가 쓰는 정확 히트는 그대로 미명중이어야 한다 (공유 API를 건드리지 않았다는 증인).
        Assert.Null(SelectionGeometry.HitTopmost([rectangle], new Point(100, 50), 6));
    }

    /// <summary>잉크 실선 정확 히트가 경계 상자 히트를 이긴다.</summary>
    [Fact]
    public void HitForSelect_ExactInkHit_WinsOverLargerBoxContainingIt()
    {
        var big = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(400, 400), Colors.Red, 2);
        var line = Stroke(100, 100, 100, 0);

        // 획 위의 점: 큰 사각형의 경계 상자 안이지만 획 정확 히트가 이겨야 한다.
        Assert.Same(line, SelectionGeometry.HitForSelect([big, line], new Point(150, 100), 6));
    }

    /// <summary>
    /// 경계 상자 히트끼리는 **면적이 작은 쪽**이 이긴다. 이게 없으면 화면을 가로지르는 대각선 획 하나가
    /// 화면 전체의 클릭 표적이 되어 그 아래 어떤 요소도 고를 수 없다.
    /// </summary>
    [Fact]
    public void HitForSelect_OverlappingBoxes_SmallestAreaWins()
    {
        var huge = Stroke(0, 0, 1000, 1000);
        var small = new ShapeElement(ShapeKind.Rectangle, new Point(400, 400), new Point(460, 440), Colors.Red, 2);

        Assert.Same(small, SelectionGeometry.HitForSelect([huge, small], new Point(430, 420), 6));
    }

    [Fact]
    public void HitForSelect_FadingElement_IsNeverHit()
    {
        var fading = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(200, 100), Colors.Red, 2)
        {
            IsFading = true,
        };

        Assert.Null(SelectionGeometry.HitForSelect([fading], new Point(100, 50), 6));
    }

    [Fact]
    public void HitForSelect_FarOutside_IsNull()
    {
        var rectangle = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(50, 50), Colors.Red, 2);

        Assert.Null(SelectionGeometry.HitForSelect([rectangle], new Point(900, 900), 6));
    }

    // ---- R6: 선택 프레임 내부 판정 ----

    [Fact]
    public void ContainsInFrame_InsideUnrotatedElement_IsTrue()
    {
        var rectangle = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 60), Colors.Red, 2);

        Assert.True(SelectionGeometry.ContainsInFrame(rectangle, new Point(50, 30)));
    }

    [Fact]
    public void ContainsInFrame_Outside_IsFalse()
    {
        var rectangle = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 60), Colors.Red, 2);

        Assert.False(SelectionGeometry.ContainsInFrame(rectangle, new Point(500, 30)));
    }

    /// <summary>
    /// 45도 회전한 요소에서 **축 정렬 상자에는 들어가지만 OBB 밖**인 모서리 지점은 미명중이어야 한다 —
    /// 화면에 그려진 점선 경계와 잡히는 영역이 일치한다는 증인.
    /// </summary>
    [Fact]
    public void ContainsInFrame_RotatedElement_UsesObbNotAxisAlignedBox()
    {
        var rectangle = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 100), Colors.Red, 2)
        {
            TransformState = ElementTransformState.Identity with { AngleDegrees = 45 },
        };
        var corner = rectangle.TransformedBounds.TopLeft + new Vector(2, 2);

        Assert.True(rectangle.TransformedBounds.Contains(corner));
        Assert.False(SelectionGeometry.ContainsInFrame(rectangle, corner));
    }

    // ---- R6: 프레임 내부 이동이 미선택 요소를 가리면 안 된다 ----

    [Fact]
    public void ShouldMoveFromFrameInterior_EmptySpotInsideFrame_Moves() =>
        Assert.True(SelectionGestureRules.ShouldMoveFromFrameInterior(
            insideFrame: true, hitExists: false, hitIsSelected: false));

    [Fact]
    public void ShouldMoveFromFrameInterior_OnAlreadySelectedElement_Moves() =>
        Assert.True(SelectionGestureRules.ShouldMoveFromFrameInterior(
            insideFrame: true, hitExists: true, hitIsSelected: true));

    /// <summary>
    /// 회귀 증인: 예전에는 프레임 내부를 무조건 이동으로 먹어서, 화면을 가로지르는 대각선 획 하나만
    /// 골라도 그 축 정렬 프레임이 화면 대부분을 덮어 그 아래 어떤 요소도 다시는 고를 수 없었다.
    /// </summary>
    [Fact]
    public void ShouldMoveFromFrameInterior_OnUnselectedElementInsideFrame_YieldsToSelection() =>
        Assert.False(SelectionGestureRules.ShouldMoveFromFrameInterior(
            insideFrame: true, hitExists: true, hitIsSelected: false));

    [Fact]
    public void ShouldMoveFromFrameInterior_OutsideFrame_NeverMoves()
    {
        Assert.False(SelectionGestureRules.ShouldMoveFromFrameInterior(false, false, false));
        Assert.False(SelectionGestureRules.ShouldMoveFromFrameInterior(false, true, true));
    }

    // ---- R7: 누적 배율 되먹임 (데드존 제거) ----

    /// <summary>
    /// 회귀 증인: 클램프 결과를 세션에 되먹이지 않으면 <c>Factor</c>가 한계를 모른 채 계속 커져,
    /// 천장에서 20노치를 더 굴린 뒤에는 20노치를 되굴려야 비로소 반응이 돌아오는 데드존이 생긴다.
    /// </summary>
    [Fact]
    public void WheelSession_ClampedFactorFedBack_ReactsToFirstReverseNotch()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var states = new[] { ElementTransformState.Identity };
        var session = new WheelScaleSession();
        session.Begin(new Point(0, 0), now);

        // 천장(MaxScale)을 한참 넘겨 굴린다 — 매 노치마다 호출부처럼 클램프 결과를 되먹인다.
        for (int i = 0; i < 80; i++)
        {
            session.SetFactor(TransformMath.ClampGroupFactor(session.Step(1, now), states));
        }
        Assert.Equal(TransformMath.MaxScale, session.Factor, 9);

        double afterOneNotchDown = TransformMath.ClampGroupFactor(session.Step(-1, now), states);

        Assert.True(afterOneNotchDown < TransformMath.MaxScale);
    }

    [Fact]
    public void WheelSession_SetFactorWhileInactive_IsIgnored()
    {
        var session = new WheelScaleSession();

        session.SetFactor(42);

        Assert.Equal(1, session.Factor, 9);
    }
}
